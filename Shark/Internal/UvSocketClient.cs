using NetUV.Core.Buffers;
using NetUV.Core.Handles;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Shark.Internal
{
    sealed internal class UvSocketClient : ISocketClient
    {
        private const int DEFAULT_BUFFER_SIZE = 1024;

        public Guid Id { get; private set; }
        public bool Disposed { get; private set; }
        public bool CanWrite { get; private set; }

        private Tcp _tcp;
        private Loop _loop;
        private int _readTimeout;
        private System.Threading.Timer _readTimer;
        private Queue<MemoryStream> _bufferQuene = new Queue<MemoryStream>();
        private TaskCompletionSource<bool> _avaliableTaskCompletion = new TaskCompletionSource<bool>();
        private TaskCompletionSource<bool> _completeTaskCompletion = new TaskCompletionSource<bool>();

        public async Task<bool> Avaliable()
        {
            return await await Task.WhenAny(_avaliableTaskCompletion.Task, _completeTaskCompletion.Task);
        }

        internal UvSocketClient(Tcp tcp, Guid? id = null, Loop loop = null, int readTimeout = Timeout.Infinite)
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
            _loop = loop;
            _readTimeout = readTimeout;
            if (_readTimeout != Timeout.Infinite)
            {
                _readTimer = new System.Threading.Timer(OnReadTimeout, null, _readTimeout, Timeout.Infinite);
            }
        }

        public Task CloseAsync()
        {
            if (Disposed)
            {
                throw new ObjectDisposedException(nameof(UvSharkClient));
            }

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
                _readTimer?.Dispose();
                _tcp.RemoveReference();
                _tcp = null;
                Disposed = true;
            }
        }

        public Task<int> ReadAsync(byte[] buffer, int offset, int count)
        {
            if (Disposed)
            {
                throw new ObjectDisposedException(nameof(UvSharkClient));
            }

            var readedCount = 0;
            var tmpBuffer = new byte[DEFAULT_BUFFER_SIZE];
            while (readedCount < count)
            {
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
                }
                else
                {
                    break;
                }
            }

            if (_bufferQuene.Count == 0)
            {
                _avaliableTaskCompletion = new TaskCompletionSource<bool>();
            }

            return Task.FromResult(readedCount);
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

            _tcp.QueueWriteStream(writableBuffer, (handle, excetion) =>
            {
                try
                {
                    if (excetion != null)
                    {
                        taskCompletion.SetException(excetion);
                        return;
                    }

                    taskCompletion.SetResult(0);
                }
                finally
                {
                    writableBuffer.Dispose();
                }
            });

            return taskCompletion.Task;
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
                _readTimer?.Change(_readTimeout, Timeout.Infinite);
            }
        }

        private void OnError(Tcp tcp, Exception exception)
        {
            _completeTaskCompletion.TrySetException(exception);
        }

        private void OnCompleted(Tcp tcp)
        {
            CanWrite = false;
            _completeTaskCompletion.TrySetResult(false);
        }

        private void OnReadTimeout(object state)
        {
            _completeTaskCompletion.TrySetResult(false);
        }

        public static Task<ISocketClient> ConnectTo(IPEndPoint endPoint)
        {
            var completionSource = new TaskCompletionSource<ISocketClient>();
            
            Task.Run(() => 
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
                            completionSource.SetResult(new UvSocketClient(tcp, null, loop, 5000));
                        }
                    });
                loop.RunDefault();
            });
            return completionSource.Task;
        }
    }
}