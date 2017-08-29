using Microsoft.Extensions.Logging;
using Norgerman.Cryptography.Scrypt;
using Shark.Constants;
using Shark.Crypto;
using Shark.Data;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;

namespace Shark.Net
{
    public abstract class SharkClient : ISharkClient
    {
        public Guid Id { get; private set; }
        public ISharkServer Server { get; private set; }
        public ICryptoHelper CryptoHelper { get; private set; }
        public IDictionary<Guid, ISocketClient> HttpClients { get; private set; }
        public bool Disposed => _disposed;

        public abstract bool CanWrite { get; }
        public abstract ILogger Logger { get; }
        public abstract Task<bool> Avaliable { get; }

        private bool _disposed = false;

        public SharkClient(SharkServer server)
        {
            Id = Guid.NewGuid();
            Server = server;
            HttpClients = new Dictionary<Guid, ISocketClient>();
        }

        public virtual ICryptoHelper GenerateCryptoHelper(byte[] passowrd)
        {
            var iv = ScryptUtil.Scrypt(passowrd, Id.ToByteArray(), 256, 8, 16, 16);
            var key = ScryptUtil.Scrypt(passowrd, iv, 512, 8, 16, 32);
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
            return block;
        }

        public virtual async Task WriteBlock(BlockData block)
        {
            var header = block.GenerateHeader();
            await WriteAsync(header, 0, header.Length);

            if ((block.Data?.Length ?? 0) != 0)
            {
                await WriteAsync(block.Data, 0, block.Data.Length);
            }
            await FlushAsync();
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


        private async Task<BlockData> ReadHeader()
        {
            var header = new byte[BlockData.HEADER_SIZE];
            var needRead = BlockData.HEADER_SIZE;
            var readed = 0;
            while (await Avaliable)
            {
                readed += await ReadAsync(header, readed, needRead - readed);

                if (needRead == readed)
                {
                    break;
                }
            }

            var valid = BlockData.TryParseHeader(header, out var block);
            if (!valid)
            {
                block.Type = BlockType.INVALID;
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
            var readed = 0;

            while (await Avaliable)
            {
                readed += await ReadAsync(data, readed, length - readed);

                if (length == readed)
                {
                    break;
                }
            }

            return data;
        }


        public Task<ISocketClient> ConnectTo(IPAddress address, int port, Guid? id = null)
        {
            return ConnectTo(new IPEndPoint(address, port));
        }

        virtual async public Task<ISocketClient> ConnectTo(string address, int port, Guid? id = null)
        {
            if (IPAddress.TryParse(address, out var ip))
            {
                return await ConnectTo(ip, port);
            }
            else
            {
                var addressList = await Dns.GetHostAddressesAsync(address);
                return await ConnectTo(addressList[0], port);
            }
        }

        #region IDisposable Support
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    Server.RemoveClient(Id);
                }
                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }
        #endregion

        public abstract Task<int> ReadAsync(byte[] buffer, int offset, int count);
        public abstract Task WriteAsync(byte[] buffer, int offset, int count);
        public abstract Task CloseAsync();
        public abstract Task<ISocketClient> ConnectTo(IPEndPoint endPoint, Guid? id = null);
        public abstract Task FlushAsync();
    }
}
