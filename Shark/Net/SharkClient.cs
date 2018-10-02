using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Norgerman.Cryptography.Scrypt;
using Shark.Constants;
using Shark.Crypto;
using Shark.Data;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Shark.Net
{
    public abstract class SharkClient : ISharkClient
    {
        public Guid Id { get; private set; }
        public ISharkServer Server { get; private set; }
        public ICryptoHelper CryptoHelper { get; private set; }
        public IDictionary<Guid, ISocketClient> HttpClients { get; private set; }
        public ConcurrentQueue<Guid> DisconnectQueue { get; private set; }
        public bool CanRead { get; protected set; }
        public abstract ILogger Logger { get; }
        public bool Disposed => _disposed;
        public abstract event Action<ISocketClient> RemoteDisconnected;

        private bool _disposed = false;
        private SemaphoreSlim _writeSemaphore;
        private Timer _timer;

        public SharkClient(SharkServer server)
        {
            Id = Guid.NewGuid();
            Server = server;
            HttpClients = new ConcurrentDictionary<Guid, ISocketClient>();
            CanRead = true;
            _writeSemaphore = new SemaphoreSlim(1, 1);
            DisconnectQueue = new ConcurrentQueue<Guid>();
            _timer = new Timer(OnTimeOut, null, 2000, 2000);
        }

        public virtual ICryptoHelper GenerateCryptoHelper(byte[] password)
        {
            var iv = ScryptUtil.Scrypt(password, password, 256, 8, 16, 16);
            var key = ScryptUtil.Scrypt(password, iv, 512, 8, 16, 32);
            CryptoHelper = new AesHelper(key, iv);
            return CryptoHelper;
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
                var header = block.GenerateHeader();
                await WriteAsync(header, 0, header.Length);

                if ((block.Data?.Length ?? 0) != 0)
                {
                    await WriteAsync(block.Data, 0, block.Data.Length);
                }
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
            if (block.Data != null)
            {
                block.Data = CryptoHelper?.EncryptSingleBlock(block.Data, 0, block.Data.Length) ?? block.Data;
                block.Length = block.Data.Length;
            }
        }

        public void DecryptBlock(ref BlockData block)
        {
            if (block.Data != null && block.IsValid)
            {
                block.Data = CryptoHelper?.DecryptSingleBlock(block.Data, 0, block.Data.Length) ?? block.Data;
                block.Length = block.Data.Length;
            }
        }


        public Guid ChangeId(Guid id)
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
            while ((readed = await ReadAsync(header, totalRead, needRead - totalRead)) != 0)
            {
                totalRead += readed;

                if (needRead == totalRead)
                {
                    break;
                }
            }
            var valid = BlockData.TryParseHeader(header, out var block);
            if (!valid)
            {
                block.Type = BlockType.INVALID;
                throw new SharkException("Invalid Header");
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

            while ((readed = await ReadAsync(data, totalReaded, length - totalReaded)) != 0)
            {
                totalReaded += readed;

                if (length == totalReaded)
                {
                    break;
                }
            }

            return data;
        }

        public async Task Disconnect(List<Guid> ids)
        {
            var block = new BlockData() { Id = Guid.Empty, Type = BlockType.DISCONNECT };
            var data = JsonConvert.SerializeObject(ids);
            block.Data = Encoding.UTF8.GetBytes(data);
            EncryptBlock(ref block);
            block.BodyCrc32 = block.ComputeCrc();
            Logger.LogDebug("Disconnet {0}", data);
            await WriteBlock(block);
        }

        private async void OnTimeOut(object state)
        {
            List<Guid> ids = new List<Guid>();
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


        public virtual Task<ISocketClient> ConnectTo(IPAddress address, int port, Guid? id = null)
        {
            return ConnectTo(new IPEndPoint(address, port), id);
        }

        public virtual async Task<ISocketClient> ConnectTo(string address, int port, Guid? id = null)
        {
            if (IPAddress.TryParse(address, out var ip))
            {
                return await ConnectTo(ip, port);
            }
            else
            {
                var addressList = await Dns.GetHostAddressesAsync(address);
                foreach (var addr in addressList)
                {
                    try
                    {
                        return await ConnectTo(addr, port, id);
                    }
                    catch (Exception e)
                    {
                        Logger.LogWarning(e, $"Failed to connected to address {addr}, trying next");
                    }
                }
                throw new ArgumentException($"Address {address} cannot connect", nameof(address));
            }
        }


        public void RemoveHttpClient(Guid id)
        {
            HttpClients.Remove(id);
        }

        public void RemoveHttpClient(ISocketClient client)
        {
            RemoveHttpClient(client.Id);
        }

        #region IDisposable Support
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // dispose managed state (managed objects).
                    foreach (var http in HttpClients)
                    {
                        http.Value.Dispose();
                    }
                    HttpClients.Clear();
                    _timer.Dispose();
                    _writeSemaphore.Dispose();
                    _timer = null;
                    _writeSemaphore = null;
                    HttpClients = null;
                }

                // free unmanaged resources (unmanaged objects) and override a finalizer below.
                // set large fields to null.
                _disposed = true;
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

        public abstract Task<int> ReadAsync(byte[] buffer, int offset, int count);
        public abstract Task WriteAsync(byte[] buffer, int offset, int count);
        public abstract Task<ISocketClient> ConnectTo(IPEndPoint endPoint, Guid? id = null);
        public abstract Task FlushAsync();
    }
}
