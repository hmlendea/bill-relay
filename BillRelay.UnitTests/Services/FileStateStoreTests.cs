using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Options;

using Moq;

using NuciLog.Core;

using NUnit.Framework;

using BillRelay.Configuration;
using BillRelay.Services;

namespace BillRelay.UnitTests.Services
{
    [TestFixture]
    public sealed class FileStateStoreTests
    {
        private Mock<ILogger> loggerMock = null!;
        private IOptions<BillRelaySettings> settings = null!;
        private FileStateStore stateStore = null!;
        private string temporaryStatePath = null!;

        [SetUp]
        public void SetUp()
        {
            loggerMock = new();
            temporaryStatePath = Path.Combine(Path.GetTempPath(), $"billrelay_test_{Guid.NewGuid()}.json");

            BillRelaySettings billRelaySettings = new()
            {
                Storage = new()
                {
                    StateFilePath = temporaryStatePath
                }
            };

            settings = Options.Create(billRelaySettings);
            stateStore = new(loggerMock.Object, settings);
        }

        [TearDown]
        public void TearDown()
        {
            stateStore?.Dispose();

            if (File.Exists(temporaryStatePath))
            {
                File.Delete(temporaryStatePath);
            }
        }

        [Test]
        public async Task GivenANewStore_WhenInitializingIt_ThenAFileIsCreated()
        {
            await stateStore.InitializeAsync(CancellationToken.None);

            bool isProcessed = await stateStore.IsProcessedAsync("nonexistent", CancellationToken.None);
            Assert.That(isProcessed, Is.False);
            Assert.That(File.Exists(temporaryStatePath));
        }

        [Test]
        public async Task GivenAnUnprocessedAttachment_WhenCheckingItsState_ThenFalseIsReturned()
        {
            await stateStore.InitializeAsync(CancellationToken.None);

            bool isProcessed = await stateStore.IsProcessedAsync("attachment123", CancellationToken.None);
            Assert.That(isProcessed, Is.False);
        }

        [Test]
        public async Task GivenAProcessedAttachment_WhenCheckingItsState_ThenTrueIsReturned()
        {
            await stateStore.InitializeAsync(CancellationToken.None);
            await stateStore.MarkProcessedAsync("attachment123", "message456", CancellationToken.None);

            bool isProcessed = await stateStore.IsProcessedAsync("attachment123", CancellationToken.None);
            Assert.That(isProcessed);
        }

        [Test]
        public async Task GivenMultipleAttachments_WhenMarkingThemAsProcessed_ThenAllAreTracked()
        {
            await stateStore.InitializeAsync(CancellationToken.None);

            await stateStore.MarkProcessedAsync("attachment1", "message1", CancellationToken.None);
            await stateStore.MarkProcessedAsync("attachment2", "message2", CancellationToken.None);
            await stateStore.MarkProcessedAsync("attachment3", "message1", CancellationToken.None);

            Assert.That(await stateStore.IsProcessedAsync("attachment1", CancellationToken.None));
            Assert.That(await stateStore.IsProcessedAsync("attachment2", CancellationToken.None));
            Assert.That(await stateStore.IsProcessedAsync("attachment3", CancellationToken.None));
        }

        [Test]
        public async Task GivenOldRecords_WhenApplyingRetention_ThenTheOldRecordsAreDeleted()
        {
            DateTimeOffset currentTime = DateTimeOffset.UtcNow;
            string oldTimestamp = currentTime.AddDays(-2).ToString("O", CultureInfo.InvariantCulture);
            string recentTimestamp = currentTime.ToString("O", CultureInfo.InvariantCulture);
            string json = $$"""
            [
              {"id":"old1","messageId":"message1","processedAt":"{{oldTimestamp}}"},
              {"id":"old2","messageId":"message2","processedAt":"{{oldTimestamp}}"},
              {"id":"new1","messageId":"message3","processedAt":"{{recentTimestamp}}"}
            ]
            """;
            await File.WriteAllTextAsync(temporaryStatePath, json);
            await stateStore.InitializeAsync(CancellationToken.None);

            await stateStore.ApplyRetentionAsync(1, 10000, CancellationToken.None);

            Assert.That(await stateStore.IsProcessedAsync("old1", CancellationToken.None), Is.False);
            Assert.That(await stateStore.IsProcessedAsync("old2", CancellationToken.None), Is.False);
            Assert.That(await stateStore.IsProcessedAsync("new1", CancellationToken.None));
        }

        [Test]
        public async Task GivenExcessRecords_WhenApplyingRetention_ThenTheOldestRecordsAreDeleted()
        {
            string timestamp = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            string json = $$"""
                        [
                            {"id":"attach1","messageId":"message1","processedAt":"{{timestamp}}"},
                            {"id":"attach2","messageId":"message2","processedAt":"{{timestamp}}"},
                            {"id":"attach3","messageId":"message3","processedAt":"{{timestamp}}"},
                            {"id":"attach4","messageId":"message4","processedAt":"{{timestamp}}"}
                        ]
                        """;
            await File.WriteAllTextAsync(temporaryStatePath, json);
            await stateStore.InitializeAsync(CancellationToken.None);

            await stateStore.ApplyRetentionAsync(365, 2, CancellationToken.None);

            Assert.That(await stateStore.IsProcessedAsync("attach1", CancellationToken.None), Is.False);
            Assert.That(await stateStore.IsProcessedAsync("attach2", CancellationToken.None), Is.False);
            Assert.That(await stateStore.IsProcessedAsync("attach3", CancellationToken.None));
            Assert.That(await stateStore.IsProcessedAsync("attach4", CancellationToken.None));
        }
    }
}