using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

using Microsoft.Extensions.Configuration;

using NUnit.Framework;

using BillRelay.Configuration;

namespace BillRelay.IntegrationTests.Configuration
{
    [TestFixture]
    public sealed class BillRelaySettingsTests
    {
        [TestCase(true, 4096, 7, 42)]
        [TestCase(false, 8192, 16, 64)]
        public void GivenExistingConfigurationKeys_WhenBindingSettings_ThenRenamedPropertiesArePopulated(
            bool isEnabled,
            long maximumSizeBytes,
            int maximumAgeDays,
            int maximumFiles)
        {
            Dictionary<string, string?> values = [];
            values["imap:useSsl"] = isEnabled.ToString();
            values["imap:validateCertificates"] = isEnabled.ToString();
            values["attachments:maxSizeBytes"] = maximumSizeBytes.ToString(CultureInfo.InvariantCulture);
            values["retention:maxAgeDays"] = maximumAgeDays.ToString(CultureInfo.InvariantCulture);
            values["retention:maxFiles"] = maximumFiles.ToString(CultureInfo.InvariantCulture);
            IConfigurationRoot configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(values)
                .Build();
            BillRelaySettings settings = new();
            configuration.Bind(settings);

            Assert.That(settings.Imap.IsSslEnabled, Is.EqualTo(isEnabled));
            Assert.That(settings.Imap.IsCertificateValidationEnabled, Is.EqualTo(isEnabled));
            Assert.That(settings.Attachments.MaximumSizeBytes, Is.EqualTo(maximumSizeBytes));
            Assert.That(settings.Retention.MaximumAgeDays, Is.EqualTo(maximumAgeDays));
            Assert.That(settings.Retention.MaximumFiles, Is.EqualTo(maximumFiles));
        }

        [Test]
        public void GivenRenamedSettings_WhenSerializingThem_ThenExistingJsonKeysArePreserved()
        {
            BillRelaySettings settings = new();
            using JsonDocument document = JsonDocument.Parse(JsonSerializer.Serialize(settings));

            Assert.That(document.RootElement.GetProperty("imap").GetProperty("useSsl").GetBoolean());
            Assert.That(
                document.RootElement.GetProperty("imap").GetProperty("validateCertificates").GetBoolean());
            Assert.That(
                document.RootElement.GetProperty("attachments").GetProperty("maxSizeBytes").GetInt64(),
                Is.EqualTo(settings.Attachments.MaximumSizeBytes));
            Assert.That(
                document.RootElement.GetProperty("retention").GetProperty("maxAgeDays").GetInt32(),
                Is.EqualTo(settings.Retention.MaximumAgeDays));
            Assert.That(
                document.RootElement.GetProperty("retention").GetProperty("maxFiles").GetInt32(),
                Is.EqualTo(settings.Retention.MaximumFiles));
        }
    }
}