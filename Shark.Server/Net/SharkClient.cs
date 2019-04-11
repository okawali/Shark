using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Shark.Constants;
using Shark.Data;
using Shark.Net;
using Shark.Net.Server;
using Shark.Security.Authentication;
using Shark.Security.Crypto;
using Shark.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Shark.Server.Net
{
    public abstract class SharkClient : ISharkClient
    {
        public int Id { get; private set; }
        public ISharkServer Server { get; private set; }
        public IDictionary<int, ISocketClient> RemoteClients { get; private set; }
        public ConcurrentQueue<int> DisconnectQueue { get; private set; }
        public bool CanRead { get; protected set; }
        public abstract ILogger Logger { get; }
        public bool Disposed { get; private set; } = false;
        public bool Initialized => true;

        public abstract ICryptor Cryptor { get; }
        public abstract IServiceProvider ServiceProvider { get; }
        protected abstract IAuthenticator Authenticator { get; }

        public abstract event Action<ISocketClient> RemoteDisconnected;

        private SemaphoreSlim _writeSemaphore;
        private Timer _timer;

        public SharkClient(SharkServer server)
        {
            Id = RandomIdGenerator.NewId();
            Server = server;
            RemoteClients = new ConcurrentDictionary<int, ISocketClient>();
            CanRead = true;
            _writeSemaphore = new SemaphoreSlim(1, 1);
            DisconnectQueue = new ConcurrentQueue<int>();
            _timer = new Timer(OnTimeOut, null, 2000, 2000);
        }

        public virtual async Task<BlockData> ReadBlock()
        {
            var block = await ReadHeader();
            if (block.IsValid)
            {
                block.Data = await ReadData(block.Length);
            }
            block.Check();
            Logger.LogDebug("Receive {0}", block);
            return block;
        }

        public virtual async Task WriteBlock(BlockData block)
        {
            await _writeSemaphore.WaitAsync();
            try
            {
                var header = block.GenerateHeader().ToArray();

                await WriteAsync(header);
                await WriteAsync(block.Data);

                await FlushAsync();

                Logger.LogDebug("Write {0}", block);
            }
            finally
            {
                _writeSemaphore.Release();
            }
        }

        public void EncryptBlock(ref BlockData block)
        {
            block.Data = Cryptor?.Encrypt(block.Data.Span) ?? block.Data;
        }

        public void DecryptBlock(ref BlockData block)
        {
            if (block.IsValid)
            {
                block.Data = Cryptor?.Decrypt(block.Data.Span) ?? block.Data;
            }
        }


        public int ChangeId(int id)
        {
            var oldId = Id;
            Server.RemoveClient(this);
            Id = id;
            Server.Clients.Add(id, this);
            return oldId;
        }

        private async Task<BlockData> ReadHeader()
        {
            var header = new byte[BlockData.HEADER_SIZE];
            var needRead = BlockData.HEADER_SIZE;
            var totalRead = 0;
            var readed = 0;
            while ((readed = await ReadAsync(new Memory<byte>(header, totalRead, needRead - totalRead))) != 0)
            {
                totalRead += readed;

                if (needRead == totalRead)
                {
                    break;
                }
            }

            var valid = BlockData.TryParseHeader(new ReadOnlySpan<byte>(header, 0, totalRead), out var block);

            if (!valid)
            {
                block.Type = BlockType.INVALID;
                throw new SharkException("Parse block failed, header cannot be parsed!");
            }
            return block;
        }

        private async Task<byte[]> ReadData(int length)
        {
            if (length == 0)
            {
                return Array.Empty<byte>();
            }

            var data = new byte[length];
            var totalReaded = 0;
            var readed = 0;

            while ((readed = await ReadAsync(new Memory<byte>(data, totalReaded, length - totalReaded))) != 0)
            {
                totalReaded += readed;

                if (length == totalReaded)
                {
                    break;
                }
            }

            return data;
        }

        public async Task Disconnect(List<int> ids)
        {
            var block = new BlockData() { Id = 0, Type = BlockType.DISCONNECT };
            var data = JsonConvert.SerializeObject(ids);
            block.Data = Encoding.UTF8.GetBytes(data);
            EncryptBlock(ref block);
            Logger.LogDebug("Disconnet {0}", data);
            await WriteBlock(block);
        }

        private async void OnTimeOut(object state)
        {
            List<int> ids = new List<int>();
            for (int i = 0; i < 256; i++)
            {
                if (DisconnectQueue.TryDequeue(out var id))
                {
                    ids.Add(id);
                }
                else
                {
                    break;
                }
            }

            if (ids.Count > 0)
            {
                try
                {
                    await Disconnect(ids);
                }
                catch
                {
                    _timer.Change(Timeout.Infinite, Timeout.Infinite);
                }
            }
        }


        public virtual Task<ISocketClient> ConnectTo(IPAddress address, int port, RemoteType type = RemoteType.Tcp, int? id = null)
        {
            return ConnectTo(new IPEndPoint(address, port), type, id);
        }

        public virtual async Task<ISocketClient> ConnectTo(string address, int port, RemoteType type = RemoteType.Tcp, int? id = null)
        {
            if (type == RemoteType.Udp)
            {
                // ignore the configuration
                return await ConnectTo(IPAddress.Any, 0, type, id);
            }

            if (IPAddress.TryParse(address, out var ip))
            {
                return await ConnectTo(ip, port, type, id);
            }
            else
            {
                var addressList = await Dns.GetHostAddressesAsync(address);
                foreach (var addr in addressList)
                {
                    try
                    {
                        return await ConnectTo(addr, port, type, id);
                    }
                    catch (Exception e)
                    {
                        Logger.LogWarning(e, $"Failed to connected to address {addr}, trying next");
                    }
                }
                throw new ArgumentException($"Address {address}:{port}/{type} cannot connect", nameof(address));
            }
        }


        public void RemoveRemoteClient(int id)
        {
            RemoteClients.Remove(id);
        }

        public async Task Auth()
        {
            var block = await ReadBlock();
            if (block.Type == BlockType.HAND_SHAKE)
            {
                var resp = Authenticator.ValidateChallenge(block.Data.Span);

                if (block.Id != 0)
                {
                    ChangeId(block.Id);
                }

                block = new BlockData() { Id = Id, Type = BlockType.HAND_SHAKE, Data = resp };
                await WriteBlock(block);

                block = await ReadBlock();

                ConfigureCryptor(block.Data.Span);
            }
            else if (block.Type == BlockType.FAST_CONNECT)
            {
                var data = block.Data;
                var (id, challenge, password, encryptedData) = FastConnectUtils.ParseFactConnectData(data);
                ConfigureCryptor(password.Span);
                block.Data = encryptedData.ToArray();
                DecryptBlock(ref block);

                if (id != 0)
                {
                    ChangeId(id);
                }

#pragma warning disable CS4014 // no wait the http connecting
                this.ProcessConnect(block, true, Authenticator.ValidateChallenge(challenge.Span));
#pragma warning restore CS4014
            }
        }

        #region IDisposable Support
        protected virtual void Dispose(bool disposing)
        {
            if (!Disposed)
            {
                Server.RemoveClient(this);
                if (disposing)
                {
                    // dispose managed state (managed objects).
                    foreach (var http in RemoteClients)
                    {
                        http.Value.Dispose();
                    }
                    RemoteClients.Clear();
                    _timer.Dispose();
                    _writeSemaphore.Dispose();
                    _timer = null;
                    _writeSemaphore = null;
                    RemoteClients = null;
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

        ~SharkClient()
        {
            Dispose(false);
        }
        #endregion

        public abstract ValueTask<int> ReadAsync(Memory<byte> buffer);
        public abstract ValueTask WriteAsync(ReadOnlyMemory<byte> buffer);
        public abstract Task<ISocketClient> ConnectTo(IPEndPoint endPoint, RemoteType type = RemoteType.Tcp, int? id = null);
        public abstract Task FlushAsync();
        public abstract void ConfigureCryptor(ReadOnlySpan<byte> password);

        #region
        public Task<BlockData> FastConnect(int id, HostData hostData)
        {
            throw new NotImplementedException();
        }

        public Task ProxyTo(int id, HostData hostData)
        {
            throw new NotImplementedException();
        }
        #endregion
    }
}
