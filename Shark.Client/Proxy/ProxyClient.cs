using Microsoft.Extensions.Logging;
using Shark.Constants;
using Shark.Data;
using Shark.Net;
using Shark.Net.Client;
using Shark.Utils;
using System;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;

namespace Shark.Client.Proxy
{
    public abstract class ProxyClient(IProxyServer server, ISharkClient shark) : IProxyClient
    {
        private readonly TaskCompletionSource<bool> _connectionCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Id { get; private set; } = RandomIdGenerator.NewId();
        public bool Disposed { get; private set; } = false;
        public IProxyServer Server { get; private set; } = server;
        public abstract ILogger Logger { get; }

        public ISharkClient Shark { private set; get; } = shark;
        public abstract IServiceProvider ServiceProvider { get; }

        public abstract event Action<ISocketClient> RemoteDisconnected;
        public abstract Task<bool> ProcessSharkData(BlockData block);
        public abstract Task<HostData> StartAndProcessRequest();


        public abstract ValueTask<int> ReadAsync(Memory<byte> buffer);
        public abstract ValueTask WriteAsync(ReadOnlyMemory<byte> buffer);
        public abstract Task FlushAsync();

        protected async Task ForwardData(PipeReader reader, int initialNumber = 0)
        {
            int dataNumber = initialNumber;
            while (true)
            {
                var read = await reader.ReadAsync();
                var buffer = read.Buffer;
                var consumed = buffer.Start;
                BlockData block;
                try
                {
                    if (read.IsCanceled)
                    {
                        throw new OperationCanceledException();
                    }

                    if (buffer.IsEmpty)
                    {
                        consumed = buffer.End;
                        if (read.IsCompleted)
                        {
                            return;
                        }
                        continue;
                    }

                    // TCP frames can follow segment boundaries instead of copying segments together.
                    ReadOnlyMemory<byte> plaintext = default;
                    foreach (var segment in buffer)
                    {
                        if (!segment.IsEmpty)
                        {
                            plaintext = segment[..Math.Min(segment.Length, PipeConstants.BUFFER_SIZE)];
                            break;
                        }
                    }

                    block = new BlockData { Id = Id, BlockNumber = dataNumber++, Type = BlockType.DATA };
                    Shark.EncryptBlock(ref block, plaintext.Span);
                    consumed = buffer.GetPosition(plaintext.Length);
                }
                finally
                {
                    // Only independently owned output survives beyond this point.
                    reader.AdvanceTo(consumed);
                }

                await Shark.WriteBlock(block);
            }
        }

        internal Task<bool> WaitForConnection(TimeSpan timeout, CancellationToken token)
        {
            return _connectionCompletion.Task.WaitAsync(timeout, token);
        }

        protected void CompleteConnection(bool success)
        {
            _connectionCompletion.TrySetResult(success);
        }

        #region IDisposable Support
        protected virtual void Dispose(bool disposing)
        {
            if (!Disposed)
            {
                if (disposing)
                {
                    _connectionCompletion.TrySetResult(false);
                }

                // free unmanaged resources (unmanaged objects) and override a finalizer below.
                // set large fields to null.
                Disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~ProxyClient()
        {
            Dispose(false);
        }
        #endregion
    }
}
