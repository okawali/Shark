using Norgerman.Cryptography.Scrypt;
using Shark.Constants;
using Shark.Crypto;
using Shark.Data;
using System;
using System.Threading.Tasks;

namespace Shark
{
    abstract class SharkClient : IDisposable
    {
        public Guid Id
        {
            get;
            private set;
        }

        public SharkServer Server
        {
            get;
            private set;
        }

        public ICryptoHelper CryptoHelper
        {
            get;
            private set;
        }

        public bool Disposed
        {
            get;
            protected set;
        }

        public SharkClient(SharkServer server)
        {
            Id = Guid.NewGuid();
            Server = server;
        }

        public void GenerateCryptoHelper(byte[] passowrd)
        {
            var iv = ScryptUtil.Scrypt(passowrd, Id.ToByteArray(), 256, 8, 16, 16);
            var key = ScryptUtil.Scrypt(passowrd, iv, 512, 8, 16, 32);
            CryptoHelper = new AesHelper(key, iv);
        }

        public async Task<BlockData> ReadBlock()
        {
            var block = await ReadHeader();
            if (block.Type != BlockType.INVALID)
            {
                block.Data = await ReadData(block.Length);
            }
            block.Check();
            block.CryptoHelper = CryptoHelper;
            return block;
        }

        public async Task WriteBlock(BlockData block)
        {
            var header = block.GenerateHeader();
            await WriteAsync(header, 0, header.Length);

            if ((block.Data?.Length ?? 0) != 0)
            {
                await WriteAsync(block.Data, 0, block.Data.Length);
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

        public abstract Task<bool> Avaliable { get; }
        public abstract Task<int> ReadAsync(byte[] buffer, int offset, int count);
        public abstract Task WriteAsync(byte[] buffer, int offset, int count);
        public abstract Task CloseAsync();
        public abstract void Dispose();
    }
}
