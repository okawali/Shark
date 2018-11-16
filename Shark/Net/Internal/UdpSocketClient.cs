using Microsoft.Extensions.Logging;
using Shark.Logging;
using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using static Shark.Constants.PipeConstants;

namespace Shark.Net.Internal
{
    internal class UdpSocketClient : ISocketClient
    {
        public bool Disposed { get; private set; }
        public Guid Id { get; private set; }
        public ILogger Logger
        {
            get
            {
                if (_logger == null)
                {
                    _logger = LoggerManager.LoggerFactory.CreateLogger<UdpSocketClient>();
                }
                return _logger;
            }
        }
#pragma warning disable CS0067
        public event Action<ISocketClient> RemoteDisconnected;
#pragma warning restore CS0067
        private ILogger _logger;
        private readonly object _syncRoot;
        private readonly UdpClient _udp;
        private readonly byte[] _writeBuffer;
        private readonly Pipe _pipe;

        public UdpSocketClient(UdpClient udp, Guid? id = null)
        {
            _udp = udp;

            if (id != null)
            {
                Id = id.Value;
            }
            else
            {
                Id = Guid.NewGuid();
            }
            _syncRoot = new object();
            _writeBuffer = new byte[1500];
            _pipe = new Pipe(DefaultPipeOptions);
        }

        protected async void StartRead()
        {
            var writer = _pipe.Writer;
            try
            {
                while (true)
                {
                    var memory = writer.GetMemory(BUFFER_SIZE);
                    var received = await _udp.ReceiveAsync();
                    var readed = received.Buffer.Length;
                    if (readed == 0)
                    {
                        break;
                    }
                    received.Buffer.CopyTo(memory);

                    writer.Advance(readed);

                    var flushResult = await writer.FlushAsync();

                    if (flushResult.IsCompleted)
                    {
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Remote read failed");
            }
            finally
            {
                writer.Complete();
            }
        }

        public Task FlushAsync()
        {
            return Task.FromResult(0);
        }

        public async Task<int> ReadAsync(byte[] buffer, int offset, int count)
        {
            var reader = _pipe.Reader;
            var readed = await reader.ReadAsync();
            var len = (int)Math.Min(readed.Buffer.Length, count);
            var used = readed.Buffer.Slice(0, len);
            if (used.Length == 0)
            {
                if (readed.IsCompleted)
                {
                    reader.Complete();
                }
            }

            used.CopyTo(new Span<byte>(buffer, offset, count));
            reader.AdvanceTo(used.End);

            return len;
        }

        public async Task WriteAsync(byte[] buffer, int offset, int count)
        {
            var sended = 0;
            while (sended < count)
            {
                Buffer.BlockCopy(buffer, offset, _writeBuffer, 0, count - sended);
                sended += await _udp.SendAsync(_writeBuffer, count - sended);
            }
        }

        #region IDisposable Support
        protected virtual void Dispose(bool disposing)
        {
            lock (_syncRoot)
            {
                if (!Disposed)
                {
                    if (disposing)
                    {
                        _udp.Dispose();
                    }

                    Disposed = true;
                }
            }
        }
        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
            // TODO: uncomment the following line if the finalizer is overridden above.
            // GC.SuppressFinalize(this);
        }
        #endregion

        public static Task<ISocketClient> ConnectTo(IPEndPoint endPoint, Guid? id = null)
        {
            var udp = new UdpClient(AddressFamily.InterNetworkV6);
            udp.Client.DualMode = true;
            try
            {
                udp.Connect(endPoint);
            }
            catch (Exception)
            {
                udp.Dispose();
                throw;
            }
            var socketClient = new UdpSocketClient(udp, id);
            socketClient.StartRead();

            return Task.FromResult<ISocketClient>(socketClient);
        }
    }
}
