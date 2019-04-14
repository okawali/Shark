using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

namespace Shark.Plugins
{
    public interface IPlugin
    {
        void Configure(IServiceCollection services, IConfiguration configuration);
    }
}
