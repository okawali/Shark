using System;
using System.Threading.Tasks;
using System.IO;
using NetUV.Core.Handles;
using NetUV.Core.Buffers;

namespace Shark.Server.Internal
{
    class UvClient : SharkClient
    {
        private Tcp _tcp;
        private MemoryStream _memStream = new MemoryStream();
        private int _state = 0;
        private Exception _exception = null;
        private TaskCompletionSource<int> _taskCompletion = new TaskCompletionSource<int>();

        internal UvClient(Tcp tcp, UvServer server)
            : base(server)
        {
            _tcp = tcp;
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

            _tcp.OnRead(OnAccept, OnError, OnCompleted);

            return taskCompletion.Task;
        }

        public override void Dispose()
        {
            if (!Disposed)
            {
                _tcp.CloseHandle(handle => handle.Dispose());
                Disposed = true;
                _memStream.Dispose();
                Server.RemoveClient(Id);
            }
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count)
        {
            if (Disposed)
            {
                throw new ObjectDisposedException(nameof(UvClient));
            }

            await _taskCompletion.Task;

            switch (_state)
            {
                case 1:
                case 2:
                    return await _memStream.ReadAsync(buffer, 0, count);
                case -1:
                    return 0;
                default:
                    return 0;
            }
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
                if (excetion != null)
                {
                    taskCompletion.SetException(excetion);
                    return;
                }

                taskCompletion.SetResult(0);
            });

            return taskCompletion.Task;
        }

        private void OnAccept(Tcp tcp, ReadableBuffer readableBuffer)
        {
            var buffer = new byte[readableBuffer.Count];
            readableBuffer.ReadBytes(buffer, buffer.Length);
            _state = 1;
            _taskCompletion.TrySetResult(1);
            _memStream.Write(buffer, 0, buffer.Length);
        }

        private void OnError(Tcp tcp, Exception exception)
        {
            _state = -1;
            _taskCompletion.TrySetException(exception);
            _exception = exception;
        }

        private void OnCompleted(Tcp tcp)
        {
            _state = 2;
            _taskCompletion.TrySetResult(2);
        }
    }
}
