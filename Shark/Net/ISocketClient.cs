using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace Shark.Net
{
    public interface ISocketClient : IDisposable
    {
        bool Disposed { get; }
        bool CanWrite { get; }
        bool CanRead { get; }
        Guid Id { get; }
        ILogger Logger { get; }

        Task<int> ReadAsync(byte[] buffer, int offset, int count);
        Task WriteAsync(byte[] buffer, int offset, int count);
        Task FlushAsync();
        Task CloseAsync();
    }
}
