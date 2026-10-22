using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using BillRelay.Configuration;
using BillRelay.Services;

namespace BillRelay
{
    public static class Program
    {
        public static async Task Main(string[] arguments)
        {
            HostApplicationBuilder builder = Host.CreateApplicationBuilder(arguments);

            builder.Configuration
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
                .AddEnvironmentVariables();

            builder.Services.Configure<BillRelaySettings>(builder.Configuration);
            builder.Services.AddSingleton<IAttachmentProcessor, AttachmentProcessor>();
            builder.Services.AddSingleton<IStateStore, FileStateStore>();
            builder.Services.AddHostedService<BillRelayWorker>();

            builder.Logging.ClearProviders();
            builder.Logging.AddConsole();
            builder.Logging.AddDebug();
            builder.Logging.SetMinimumLevel(LogLevel.Information);

            IHost host = builder.Build();
            await host.RunAsync();
        }
    }
}