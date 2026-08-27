using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shark.Net.Client;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Shark.Client
{
    class Worker(IServiceProvider services) : BackgroundService
    {
        private readonly IServiceProvider _services = services;

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            return _services.GetRequiredService<IProxyServer>().Start(stoppingToken);
        }
    }
}
