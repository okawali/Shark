using Microsoft.Extensions.Logging;
using Shark.Crypto;
using Shark.Data;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;

namespace Shark
{
    public interface ISharkClient : ISocketClient
    {
        ICryptoHelper CryptoHelper { get; }
        ISharkServer Server { get; }
        IDictionary<Guid, ISocketClient> HttpClients {get;}
        ILogger Logger { get; }
        ILoggerFactory LoggerFactory { get; }

        Task<BlockData> ReadBlock();
        Task WriteBlock(BlockData block);
        Task<ISocketClient> ConnectTo(IPAddress address, int port);
        Task<ISocketClient> ConnectTo(string address, int port);
        Task<ISocketClient> ConnectTo(IPEndPoint endPoint);
        ICryptoHelper GenerateCryptoHelper(byte[] passowrd);
        void EncryptBlock(ref BlockData block);
        void DeccryptBlock(ref BlockData block);
    }
}
