using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using BillRelay.Configuration;
using BillRelay.Services;
using NuciNotifications.Client;
using NuciNotifications.Client.Configuration;

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
            NuciNotificationsSettings notificationSettings = new();
            builder.Configuration.GetSection("notifications").Bind(notificationSettings);
            ValidateNotifications(notificationSettings);
            builder.Services.AddSingleton<INuciNotificationsClient>(
                new NuciNotificationsClient(notificationSettings));
            builder.Services.AddSingleton<IBillSender, BillSender>();
            builder.Services.AddSingleton<IBillDelivery, BillDelivery>();
            builder.Services.AddHostedService<BillRelayWorker>();

            builder.Logging.ClearProviders();
            builder.Logging.AddConsole();
            builder.Logging.AddDebug();
            builder.Logging.SetMinimumLevel(LogLevel.Information);

            IHost host = builder.Build();
            await host.RunAsync();
        }

        private static void ValidateNotifications(NuciNotificationsSettings settings)
        {
            if (!Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out Uri? endpoint) ||
                !string.Equals(endpoint.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Notifications require an absolute HTTPS base URL.");
            }

            if (string.IsNullOrWhiteSpace(settings.ApiKey) ||
                string.IsNullOrWhiteSpace(settings.HmacSharedSecretKey))
            {
                throw new InvalidOperationException("Notification API credentials must be configured.");
            }
        }
    }
}