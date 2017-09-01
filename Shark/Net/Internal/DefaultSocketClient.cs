﻿using Microsoft.Extensions.Logging;
using Shark.Logging;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Shark.Net.Internal
{
    class DefaultSocketClient : ISocketClient
    {
        public event Action<ISocketClient> RemoteDisconnected;
        public Guid Id { get; private set; }
        public bool Disposed { get; private set; }

        public ILogger Logger
        {
            get
            {
                if (_logger == null)
                {
                    _logger = LoggerManager.LoggerFactory.CreateLogger<DefaultSocketClient>();
                }
                return _logger;
            }
        }

        private ILogger _logger;
        private TcpClient _tcp;
        private NetworkStream _stream;

        public void Dispose()
        {
            if (!Disposed)
            {
                _tcp.Client.Shutdown(SocketShutdown.Both);
                _tcp.Client.Disconnect(false);
                _stream.Dispose();
                _tcp.Dispose();
                RemoteDisconnected = null;
                Disposed = true;
            }
        }

        public DefaultSocketClient(TcpClient tcp, Guid? id = null)
        {
            _tcp = tcp;
            _stream = _tcp.GetStream();

            if (id != null)
            {
                Id = id.Value;
            }
            else
            {
                Id = Guid.NewGuid();
            }
        }

        public Task FlushAsync()
        {
            return _stream.FlushAsync();
        }

        public async Task<int> ReadAsync(byte[] buffer, int offset, int count)
        {
            var readed = await _stream.ReadAsync(buffer, offset, count);
            if (readed == 0)
            {
                CloseConnetion();
            }

            return readed;
        }

        public Task WriteAsync(byte[] buffer, int offset, int count)
        {
            return _stream.WriteAsync(buffer, offset, count);
        }

        private void CloseConnetion()
        {
            _tcp.Client.Shutdown(SocketShutdown.Receive);
            _tcp.Client.Disconnect(false);
            Logger.LogInformation("Socket no data to read, closed {0}", Id);
            RemoteDisconnected?.Invoke(this);
        }

        public static async Task<ISocketClient> ConnectTo(IPEndPoint endPoint, Guid? id = null)
        {
            var tcp = new TcpClient();
            await tcp.ConnectAsync(endPoint.Address, endPoint.Port);
            return new DefaultSocketClient(tcp, id);
        }
    }
}