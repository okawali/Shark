using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shark.Net;
using Shark.Utils;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Shark.Server.Net.Internal
{
    internal class DefaultSocketClient : ISocketClient
    {
        private readonly CancellationTokenSource _lifetimeCancellation;
        private readonly Lock _syncRoot = new();
        private TcpClient _tcp;
        private NetworkStream _stream;

        public event Action<ISocketClient> RemoteDisconnected;
        public int Id { get; }
        public bool Disposed { get; private set; }
        public ILogger Logger { get; }
        public IServiceProvider ServiceProvider { get; }

        public DefaultSocketClient(
            TcpClient tcp,
            int? id,
            CancellationToken lifetimeToken,
            IServiceProvider serviceProvider,
            ILogger<DefaultSocketClient> logger)
        {
            _tcp = tcp;
            _stream = _tcp.GetStream();
            Id = id ?? RandomIdGenerator.NewId();
            Logger = logger;
            ServiceProvider = serviceProvider;
            _lifetimeCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken);
        }

        public Task FlushAsync()
        {
            return _stream.FlushAsync(_lifetimeCancellation.Token);
        }

        public async ValueTask<int> ReadAsync(Memory<byte> buffer)
        {
            var read = await _stream.ReadAsync(buffer, _lifetimeCancellation.Token);
            if (read == 0)
            {
                CloseConnection();
            }
            return read;
        }

        public ValueTask WriteAsync(ReadOnlyMemory<byte> buffer)
        {
            return _stream.WriteAsync(buffer, _lifetimeCancellation.Token);
        }

        private void CloseConnection()
        {
            try
            {
                _lifetimeCancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            if (Logger.IsEnabled(LogLevel.Information))
            {
                Logger.LogInformation("Socket no data to read, closed {Id}", Id);
            }
            RemoteDisconnected?.Invoke(this);
        }

        public static async Task<ISocketClient> ConnectTo(
            IServiceProvider serviceProvider,
            IPEndPoint endPoint,
            int? id,
            CancellationToken connectToken,
            CancellationToken lifetimeToken,
            TimeSpan timeout)
        {
            var tcp = new TcpClient(AddressFamily.InterNetworkV6);
            tcp.Client.DualMode = true;
            using var operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                connectToken,
                lifetimeToken);
            operationCancellation.CancelAfter(timeout);

            try
            {
                await tcp.ConnectAsync(endPoint.Address, endPoint.Port, operationCancellation.Token);
            }
            catch (OperationCanceledException ex) when (!lifetimeToken.IsCancellationRequested)
            {
                tcp.Dispose();
                throw new SharkException($"Connecting to {endPoint} timed out after {timeout}", ex);
            }
            catch
            {
                tcp.Dispose();
                throw;
            }

            return ActivatorUtilities.CreateInstance<DefaultSocketClient>(
                serviceProvider,
                tcp,
                id,
                lifetimeToken);
        }

        #region IDisposable Support
        protected virtual void Dispose(bool disposing)
        {
            lock (_syncRoot)
            {
                if (Disposed)
                {
                    return;
                }

                if (disposing)
                {
                    try
                    {
                        _lifetimeCancellation.Cancel();
                    }
                    catch (ObjectDisposedException)
                    {
                    }

                    try
                    {
                        _tcp.Client.Shutdown(SocketShutdown.Both);
                    }
                    catch (Exception)
                    {
                    }

                    _stream?.Dispose();
                    _tcp?.Dispose();
                    _lifetimeCancellation.Dispose();
                    RemoteDisconnected = null;
                    _stream = null;
                    _tcp = null;
                }

                Disposed = true;
            }
        }

        ~DefaultSocketClient()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        #endregion
    }
}
