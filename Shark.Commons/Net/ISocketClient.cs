using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace Shark.Net
{
    public interface ISocketClient : IDisposable
    {
        IServiceProvider ServiceProvider { get; }
        bool Disposed { get; }
        int Id { get; }
        ILogger Logger { get; }
        event Action<ISocketClient> RemoteDisconnected;

        ValueTask<int> ReadAsync(Memory<byte> buffer);
        ValueTask WriteAsync(ReadOnlyMemory<byte> buffer);
        Task FlushAsync();
    }
}
