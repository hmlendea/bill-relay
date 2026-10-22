using System;
using System.IO;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using NuciLog;
using NuciLog.Configuration;

using NuciNotifications.Client;
using NuciNotifications.Client.Configuration;

using BillRelay.Configuration;
using BillRelay.Services;
using BillRelay.Services.Attachments;

using ILogger = NuciLog.Core.ILogger;

namespace BillRelay
{
    public static class Program
    {
        public static void Main(string[] arguments)
        {
            HostApplicationBuilder builder = Host.CreateApplicationBuilder(arguments);
            builder.Configuration
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
                .AddEnvironmentVariables();

            ConfigureServices(builder);
            builder.Logging.ClearProviders();
            using IHost host = builder.Build();
            host.Run();
        }

        private static void ConfigureServices(HostApplicationBuilder builder)
        {
            NuciLoggerSettings loggingSettings = new();
            builder.Configuration.GetSection(nameof(NuciLoggerSettings)).Bind(loggingSettings);
            builder.Services.AddSingleton(loggingSettings);
            builder.Services.AddSingleton<ILogger, NuciLogger>();
            builder.Services.Configure<BillRelaySettings>(builder.Configuration);
            builder.Services.AddSingleton<IAttachmentProcessor, AttachmentProcessor>();
            builder.Services.AddSingleton<IAttachmentValidator, AttachmentValidator>();
            builder.Services.AddSingleton<IStateStore, FileStateStore>();
            NuciNotificationsSettings notificationSettings = new();
            builder.Configuration
                .GetSection(nameof(NuciNotificationsSettings))
                .Bind(notificationSettings);
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