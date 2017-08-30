using Microsoft.Extensions.Logging;
using NetUV.Core.Buffers;
using NetUV.Core.Handles;
using Shark.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;

namespace Shark.Net.Internal
{
    sealed internal class UvSocketClient : ISocketClient
    {
        private const int DEFAULT_BUFFER_SIZE = 1024;

        public Guid Id { get; private set; }
        public bool Disposed { get; private set; }
        public bool CanWrite { get; private set; }
        public bool CanRead => _canRead || _bufferQuene.Count > 0;

        private bool _canRead;

        public ILogger Logger
        {
            get
            {
                if (_logger == null)
                {
                    _logger = LoggerManager.LoggerFactory.CreateLogger<UvSocketClient>();
                }
                return _logger;
            }
        }

        private ILogger _logger;
        private Tcp _tcp;
        private Loop _loop;
        private Queue<MemoryStream> _bufferQuene = new Queue<MemoryStream>();
        private TaskCompletionSource<bool> _avaliableTaskCompletion = new TaskCompletionSource<bool>();
        private TaskCompletionSource<bool> _completeTaskCompletion = new TaskCompletionSource<bool>();
        private ConcurrentQueue<TaskCompletionSource<int>> _unCompletedWriteTasks = new ConcurrentQueue<TaskCompletionSource<int>>();

        internal UvSocketClient(Tcp tcp, Guid? id = null, Loop loop = null)
        {
            _tcp = tcp;
            _tcp.AddReference();
            if (id != null)
            {
                Id = id.Value;
            }
            else
            {
                Id = Guid.NewGuid();
            }
            _tcp.OnRead(OnAccept, OnError, OnCompleted);
            CanWrite = true;
            _canRead = true;
            _loop = loop;
        }

        public async Task<int> ReadAsync(byte[] buffer, int offset, int count)
        {
            if (Disposed)
            {
                throw new ObjectDisposedException(nameof(UvSharkClient));
            }

            if (count <= 0)
            {
                throw new ArgumentException($"{nameof(count)} must > 0");
            }

            var readedCount = 0;

            while (readedCount < count)
            {
                var task = await Task.WhenAny(_avaliableTaskCompletion.Task, _completeTaskCompletion.Task);

                if (task == _completeTaskCompletion.Task)
                {
                    return readedCount;
                }

                if (_bufferQuene.TryPeek(out var data))
                {
                    readedCount += data.Read(buffer, offset + readedCount, count - readedCount);

                    if (data.Position == data.Length)
                    {
                        if (_bufferQuene.TryDequeue(out data))
                        {
                            data.Dispose();
                        }
                    }

                    if (_bufferQuene.Count == 0)
                    {
                        _avaliableTaskCompletion = new TaskCompletionSource<bool>();
                        return readedCount;
                    }
                }
            }

            return readedCount;
        }

        public Task WriteAsync(byte[] buffer, int offset, int count)
        {
            if (Disposed)
            {
                throw new ObjectDisposedException(nameof(UvSharkClient));
            }

            if (!CanWrite)
            {
                throw new IOException($"{nameof(UvSharkClient)} is not writable, remote closed");
            }

            TaskCompletionSource<int> taskCompletion = new TaskCompletionSource<int>();
            var copyedBuffer = new byte[count];
            Buffer.BlockCopy(buffer, offset, copyedBuffer, 0, count);
            var writableBuffer = WritableBuffer.From(copyedBuffer);
            _unCompletedWriteTasks.Enqueue(taskCompletion);

            _tcp.QueueWriteStream(writableBuffer, (handle, excetion) =>
            {
                try
                {
                    if (_unCompletedWriteTasks.TryDequeue(out var item))
                    {
                        if (excetion != null)
                        {
                            item.SetException(excetion);
                            return;
                        }
                        item.SetResult(0);
                    }
                }
                finally
                {
                    writableBuffer.Dispose();
                }
            });

            return taskCompletion.Task;
        }

        public Task FlushAsync()
        {
            return Task.FromResult(0);
        }

        public Task CloseAsync()
        {
            if (Disposed)
            {
                throw new ObjectDisposedException(nameof(UvSharkClient));
            }

            _canRead = false;
            CanWrite = false;
            _completeTaskCompletion.TrySetResult(false);

            if (_loop != null)
            {
                _loop.Stop();
                _tcp.CloseHandle();
                return Task.FromResult(0);
            }

            TaskCompletionSource<int> taskCompletion = new TaskCompletionSource<int>();

            _tcp.CloseHandle(handle =>
            {
                handle.Dispose();
                taskCompletion.SetResult(0);
            });

            return taskCompletion.Task;
        }

        public void Dispose()
        {
            if (!Disposed)
            {
                _tcp.CloseHandle(handle => handle.Dispose());
                _loop?.Dispose();
                _tcp.RemoveReference();
                _completeTaskCompletion.TrySetResult(false);

                while (_bufferQuene.TryDequeue(out var item))
                {
                    item.Dispose();
                }

                _tcp = null;
                _canRead = false;
                CanWrite = false;
                Disposed = true;
            }
        }

        private void OnAccept(Tcp tcp, ReadableBuffer readableBuffer)
        {
            using (readableBuffer)
            {
                if (readableBuffer.Count > 0)
                {
                    var buffer = new byte[readableBuffer.Count];
                    readableBuffer.ReadBytes(buffer, readableBuffer.Count);
                    _bufferQuene.Enqueue(new MemoryStream(buffer));
                    _avaliableTaskCompletion.TrySetResult(true);
                }
            }
        }

        private void OnError(Tcp tcp, Exception exception)
        {
            _completeTaskCompletion.TrySetException(exception);
        }

        private void OnCompleted(Tcp tcp)
        {
            CanWrite = false;
            _canRead = false;
            Logger.LogInformation("Remote closed");
            _completeTaskCompletion.TrySetResult(false);

            while (_unCompletedWriteTasks.TryDequeue(out var item))
            {
                item.TrySetException(new IOException($"{nameof(UvSharkClient)} is not writable, remote closed"));
            }
        }

        private async Task<bool> GenerateAvaliableTask()
        {
            return await await Task.WhenAny(_avaliableTaskCompletion.Task, _completeTaskCompletion.Task);
        }

        public static Task<ISocketClient> ConnectTo(IPEndPoint endPoint, Guid? id = null)
        {
            var completionSource = new TaskCompletionSource<ISocketClient>();
            Task.Factory.StartNew(() => 
            {
                var loop = new Loop();
                loop.CreateTcp()
                    .NoDelay(true)
                    .ConnectTo(endPoint, (tcp, e) =>
                    {
                        if (e != null)
                        {
                            completionSource.SetException(e);
                        }
                        else
                        {
                            completionSource.SetResult(new UvSocketClient(tcp, id, loop));
                        }
                    });
                loop.RunDefault();
            }, TaskCreationOptions.LongRunning);
            return completionSource.Task;
        }
    }
}