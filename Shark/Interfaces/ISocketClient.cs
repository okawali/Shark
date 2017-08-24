using System;
using System.Threading.Tasks;

namespace Shark
{
    public interface ISocketClient : IDisposable
    {
        bool Disposed { get; }
        bool CanWrite { get; }
        Task<bool> Avaliable { get; }
        Guid Id { get; }
        Task<int> ReadAsync(byte[] buffer, int offset, int count);
        Task WriteAsync(byte[] buffer, int offset, int count);
        Task CloseAsync();
    }
}
