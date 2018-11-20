using Shark.Crypto;
using Shark.Data;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;

namespace Shark.Net
{
    public interface ISharkClient : ISocketClient
    {
        ICryptoHelper CryptoHelper { get; }
        ISharkServer Server { get; }
        IDictionary<Guid, ISocketClient> RemoteClients { get; }
        ConcurrentQueue<Guid> DisconnectQueue { get; }
        bool CanRead { get; }

        Task<BlockData> ReadBlock();
        Task WriteBlock(BlockData block);
        Task<ISocketClient> ConnectTo(IPAddress address, int port, RemoteType type = RemoteType.Tcp, Guid? id = null);
        Task<ISocketClient> ConnectTo(string address, int port, RemoteType type = RemoteType.Tcp, Guid? id = null);
        Task<ISocketClient> ConnectTo(IPEndPoint endPoint, RemoteType type = RemoteType.Tcp, Guid ? id = null);
        ICryptoHelper GenerateCryptoHelper(byte[] password);
        Guid ChangeId(Guid id);
        void EncryptBlock(ref BlockData block);
        void DecryptBlock(ref BlockData block);
        void RemoveRemoteClient(Guid id);
        void RemoveRemoteClient(ISocketClient client);
    }
}
