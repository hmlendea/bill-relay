using System;
using System.IO;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using NuciNotifications.Client;
using NuciNotifications.Client.Configuration;

using BillRelay.Configuration;
using BillRelay.Services;

namespace BillRelay
{
    public static class Program
    {
        public static void Main(string[] arguments)
        {
            HostApplicationBuilder builder = Host.CreateApplicationBuilder(arguments);
            builder.Configuration
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
                .AddEnvironmentVariables();

            ConfigureServices(builder);
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole();
            builder.Logging.AddDebug();
            builder.Logging.SetMinimumLevel(LogLevel.Information);
            using IHost host = builder.Build();
            host.Run();
        }

        private static void ConfigureServices(HostApplicationBuilder builder)
        {
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