using Shark.Security.Crypto;
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
        IDictionary<int, ISocketClient> RemoteClients { get; }
        bool CanRead { get; }
        bool Initialized { get; }

        int ChangeId(int id);

        Task<ISocketClient> ConnectTo(int id, IPAddress address, int port, RemoteType type = RemoteType.Tcp);
        Task<ISocketClient> ConnectTo(int id, string address, int port, RemoteType type = RemoteType.Tcp);
        Task<ISocketClient> ConnectTo(int id, IPEndPoint endPoint, RemoteType type = RemoteType.Tcp);

        ISymmetricCipher Cipher { get; }
        ConcurrentQueue<int> DisconnectQueue { get; }

        Task<BlockData> ReadBlock();
        Task WriteBlock(BlockData block);
        Task Auth();
        Task<BlockData> FastConnect(int id, HostData hostData);
        Task ProxyTo(int id, HostData hostData);

        void ConfigureCipher(ReadOnlySpan<byte> password);
        void EncryptBlock(ref BlockData block);
        void EncryptBlock(ref BlockData block, ReadOnlySpan<byte> plaintext);

        void DecryptBlock(ref BlockData block);
        void DecryptBlock(ref BlockData block, ReadOnlySpan<byte> ciphertext);
        void RemoveRemoteClient(int id);
    }
}
