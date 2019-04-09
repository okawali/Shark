using Microsoft.Extensions.DependencyInjection;

namespace Shark.Plugins
{
    public interface IPlugin
    {
        void Configure(IServiceCollection services);
    }
}
