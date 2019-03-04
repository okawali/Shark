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

        Task<int> ReadAsync(byte[] buffer, int offset, int count);
        Task WriteAsync(byte[] buffer, int offset, int count);
        Task FlushAsync();
    }
}
