using Shark.Data;
using System.Threading.Tasks;

namespace Shark.Net.Client
{
    public interface IProxyClient : ISocketClient
    {
        IProxyServer Server { get; }
        ISharkClient Shark { get; }

        Task<HostData> StartAndProcessRequest();
        Task<bool> ProcessSharkData(BlockData block);
    }
}
