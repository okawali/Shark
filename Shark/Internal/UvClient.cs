using NetUV.Core.Buffers;
using NetUV.Core.Handles;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Shark.Internal
{
    class UvClient : SharkClient
    {
        private const int DEFAULT_BUFFER_SIZE = 1024;

        private Tcp _tcp;
        private Queue<MemoryStream> _bufferQuene = new Queue<MemoryStream>();
        private TaskCompletionSource<bool> _avaliableTaskCompletion = new TaskCompletionSource<bool>();
        private TaskCompletionSource<bool> _completeTaskCompletion = new TaskCompletionSource<bool>();

        public override async Task<bool> Avaliable()
        {
            return await await Task.WhenAny(_avaliableTaskCompletion.Task, _completeTaskCompletion.Task);
        }

        internal UvClient(Tcp tcp, UvServer server)
            : base(server)
        {
            _tcp = tcp;
            _tcp.OnRead(OnAccept, OnError, OnCompleted);
            _tcp.AddReference();
        }

        public override Task CloseAsync()
        {
            if (Disposed)
            {
                throw new ObjectDisposedException(nameof(UvClient));
            }

            TaskCompletionSource<int> taskCompletion = new TaskCompletionSource<int>();

            _tcp.CloseHandle(handle =>
            {
                handle.Dispose();
                taskCompletion.SetResult(0);
            });

            return taskCompletion.Task;
        }

        public override void Dispose()
        {
            if (!Disposed)
            {
                _tcp.CloseHandle(handle => handle.Dispose());
                Disposed = true;
                Server.RemoveClient(Id);
                _tcp.RemoveReference();
                _tcp = null;
            }
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count)
        {
            if (Disposed)
            {
                throw new ObjectDisposedException(nameof(UvClient));
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

        public override Task WriteAsync(byte[] buffer, int offset, int count)
        {
            if (Disposed)
            {
                throw new ObjectDisposedException(nameof(UvClient));
            }

            if (!CanWrite)
            {
                throw new IOException($"{nameof(UvClient)} is not writable, remote closed");
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
            }
        }

        private void OnError(Tcp tcp, Exception exception)
        {
            _completeTaskCompletion.SetException(exception);
        }

        private void OnCompleted(Tcp tcp)
        {
            CanWrite = false;
            _completeTaskCompletion.SetResult(false);
        }
    }
}
