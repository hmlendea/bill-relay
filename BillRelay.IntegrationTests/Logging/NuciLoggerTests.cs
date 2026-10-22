using System;
using System.IO;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using NuciLog;
using NuciLog.Configuration;
using NuciLog.Core;

using NUnit.Framework;

using BillRelay.Logging;

namespace BillRelay.IntegrationTests.Logging
{
    [TestFixture]
    public sealed class NuciLoggerTests
    {
        [Test]
        public void GivenLoggingConfiguration_WhenResolvingTheLogger_ThenNuciLogUsesTheConfiguredSettings()
        {
            string settingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            IConfigurationRoot configuration = new ConfigurationBuilder()
                .AddJsonFile(settingsPath)
                .Build();
            NuciLoggerSettings settings = new();
            configuration.GetSection(nameof(NuciLoggerSettings)).Bind(settings);
            ServiceCollection services = new();
            services.AddSingleton(settings);
            services.AddSingleton<ILogger, NuciLogger>();
            using ServiceProvider provider = services.BuildServiceProvider();

            Assert.That(settings.MinimumLevel, Is.EqualTo(LogLevel.Info));
            Assert.That(settings.IsFileOutputEnabled, Is.False);
            Assert.That(provider.GetRequiredService<ILogger>(), Is.TypeOf<NuciLogger>());
            Assert.That(
                provider.GetRequiredService<ILogger>(),
                Is.SameAs(provider.GetRequiredService<ILogger>()));
        }

        [TestCase("John Doe.pdf")]
        [TestCase("DummyUser.pdf")]
        public void GivenFileDetails_WhenWritingALog_ThenTheValuesArePreserved(string fileName)
        {
            string logPath = Path.GetTempFileName();

            try
            {
                VerifyMaskedOutput(logPath, fileName);
            }
            finally
            {
                File.Delete(logPath);
            }
        }

        private static void VerifyMaskedOutput(string logPath, string fileName)
        {
            NuciLoggerSettings settings = new()
            {
                LogFilePath = logPath,
                IsFileOutputEnabled = true,
                MinimumLevel = LogLevel.Debug
            };
            NuciLogger logger = new(settings);
            logger.Info(
                BillRelayOperation.SaveAttachment,
                OperationStatus.Success,
                new LogInfo(BillRelayInfoKey.FileName, fileName),
                new LogInfo(BillRelayInfoKey.FilePath, Path.Combine("bills", fileName)),
                new LogInfo(BillRelayInfoKey.StateFilePath, Path.Combine("state", fileName)),
                new LogInfo(BillRelayInfoKey.AttachmentId, "attachment42"));
            string output = File.ReadAllText(logPath);

            Assert.That(output, Does.Contain("SaveAttachment"));
            Assert.That(output, Does.Contain("SUCCESS"));
            Assert.That(output, Does.Contain("attachment42"));
            Assert.That(output, Does.Contain(fileName));
            Assert.That(output, Does.Contain("FileName"));
            Assert.That(output, Does.Contain("FilePath"));
            Assert.That(output, Does.Contain("StateFilePath"));
        }
    }
}