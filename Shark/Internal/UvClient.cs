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

        public override Task<int> ReadAsync(byte[] buffer, int offset, int length)
        {
            if (Disposed)
            {
                throw new ObjectDisposedException(nameof(UvClient));
            }

            TaskCompletionSource<int> taskCompletion = new TaskCompletionSource<int>();

            _tcp.OnRead((handle, readableBuffer) =>
            {
                int readableLength = (int)_memStream.Length + readableBuffer.Count;
                int readedLength = Math.Min(length, readableLength);
                var readedBytes = new byte[readedLength];

                var readedFromMem = _memStream.Read(readedBytes, 0, readedLength);
                var leftLength = readedLength - readedFromMem;

                Buffer.BlockCopy(readedBytes, 0, buffer, offset, readedFromMem);
                offset += readedFromMem;
                if (leftLength > 0)
                {
                    readableBuffer.ReadBytes(readedBytes, leftLength);
                    Buffer.BlockCopy(readedBytes, 0, buffer, offset, leftLength);
                }

                if (readableBuffer.Count > 0)
                {
                    var leftbytes = new byte[readableBuffer.Count];
                    readableBuffer.ReadBytes(leftbytes, readableBuffer.Count);
                    _memStream.Write(leftbytes, 0, leftbytes.Length);
                }

                taskCompletion.SetResult(readedLength);
            }, (handle, exception) => 
            {
                taskCompletion.SetException(exception);
            });

            return taskCompletion.Task;
        }

        public override Task WriteAsync(byte[] buffer, int offset, int length)
        {
            if (Disposed)
            {
                throw new ObjectDisposedException(nameof(UvClient));
            }

            TaskCompletionSource<int> taskCompletion = new TaskCompletionSource<int>();
            var copyedBuffer = new byte[length];
            Buffer.BlockCopy(buffer, offset, copyedBuffer, 0, length);
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
    }
}
