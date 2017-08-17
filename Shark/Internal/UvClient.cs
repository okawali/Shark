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
        private int _state = 0;
        private Exception _exception = null;
        private TaskCompletionSource<int> _taskCompletion = new TaskCompletionSource<int>();
        private Queue<ReadableBuffer> _bufferQuene = new Queue<ReadableBuffer>();

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
                    {
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
                        return readedCount;
                    }
                case -1:
                    throw _exception;
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
            //var buffer = new byte[readableBuffer.Count];
            //readableBuffer.ReadBytes(buffer, buffer.Length);

            //var buffer = Encoding.UTF8.GetBytes(readableBuffer.ReadString(Encoding.UTF8));

            _bufferQuene.Enqueue(readableBuffer);

            if (_state == 0)
            {
                _state = 1;
                _taskCompletion.TrySetResult(1);
            }
        }

        private void OnError(Tcp tcp, Exception exception)
        {
            _exception = exception;
            if (_state == 0)
            {
                _taskCompletion.TrySetException(exception);
            }
        }

        private void OnCompleted(Tcp tcp)
        {
            _state = 2;

            if (_state == 0)
            {
                _taskCompletion.TrySetResult(2);
            }
        }
    }
}
