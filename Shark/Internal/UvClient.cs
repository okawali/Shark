using NetUV.Core.Buffers;
using NetUV.Core.Handles;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Shark.Internal
{
    class UvClient : SharkClient
    {
        private Tcp _tcp;
        private Queue<ReadableBuffer> _bufferQuene = new Queue<ReadableBuffer>();
        private TaskCompletionSource<bool> _avaliableTaskCompletion = new TaskCompletionSource<bool>();

        public override Task<bool> Avaliable
        {
            get
            {
                return _avaliableTaskCompletion.Task;
            }
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

            int readedCount = 0;
            while (readedCount < count)
            {
                if (_bufferQuene.TryPeek(out var data))
                {
                    bool dequeued = false;
                    if (data.Count <= count - readedCount)
                    {
                        dequeued = _bufferQuene.TryDequeue(out data);
                    }
                    var currentRead = Math.Min(count - readedCount, data.Count);

                    //because of a bug in netuv
                    for (var i = 0; i < currentRead; i++)
                    {
                        buffer[readedCount + i] = data.ReadByte();
                    }
                    readedCount += currentRead;

                    if (dequeued)
                    {
                        data.Dispose();
                    }
                }
                else
                {
                    break;
                }
            }

            if (_bufferQuene.Count == 0
                && _avaliableTaskCompletion.Task.IsCompletedSuccessfully
                && _avaliableTaskCompletion.Task.Result)
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
            _bufferQuene.Enqueue(readableBuffer);

            _avaliableTaskCompletion.TrySetResult(true);
        }

        private void OnError(Tcp tcp, Exception exception)
        {
            if (_avaliableTaskCompletion.Task.IsCompleted)
            {
                _avaliableTaskCompletion = new TaskCompletionSource<bool>();
            }

            _avaliableTaskCompletion.SetException(exception);
        }

        private void OnCompleted(Tcp tcp)
        {
            if (_avaliableTaskCompletion.Task.IsCompleted)
            {
                _avaliableTaskCompletion = new TaskCompletionSource<bool>();
            }

            _avaliableTaskCompletion.SetResult(false);
        }
    }
}
