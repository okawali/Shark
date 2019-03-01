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
        IDictionary<Guid, ISocketClient> RemoteClients { get; }
        bool CanRead { get; }
        bool Initialized { get; }

        Guid ChangeId(Guid id);

        Task<ISocketClient> ConnectTo(IPAddress address, int port, RemoteType type = RemoteType.Tcp, Guid? id = null);
        Task<ISocketClient> ConnectTo(string address, int port, RemoteType type = RemoteType.Tcp, Guid? id = null);
        Task<ISocketClient> ConnectTo(IPEndPoint endPoint, RemoteType type = RemoteType.Tcp, Guid? id = null);

        ICrypter Crypter { get; }
        ConcurrentQueue<Guid> DisconnectQueue { get; }

        Task<BlockData> ReadBlock();
        Task WriteBlock(BlockData block);
        Task Auth();
        Task<BlockData> FastConnect(Guid id, HostData hostData);
        Task ProxyTo(Guid id, HostData hostData);

        void ConfigureCrypter(byte[] password);
        void EncryptBlock(ref BlockData block);
        void DecryptBlock(ref BlockData block);
        void RemoveRemoteClient(Guid id);
    }
}
