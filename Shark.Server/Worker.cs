using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shark.Net.Server;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Shark.Server
{
    class Worker : BackgroundService
    {
        private readonly IServiceProvider _services;

        public Worker(IServiceProvider services)
        {
            _services = services;
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            return _services.GetRequiredService<ISharkServer>()
                 .OnClientConnected(async client =>
                 {
                     try
                     {
                         await client.Auth();
                         await client.RunSharkLoop();
                     }
                     catch (Exception e)
                     {

                         client.Logger.LogError(e, "Shark clinet errored");
                     }
                     finally
                     {
                         client.Dispose();
                     }
                 })
                .Start(stoppingToken);
        }
    }
}
