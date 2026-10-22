using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using NUnit.Framework;

using BillRelay.Configuration;
using BillRelay.Services;

namespace BillRelay.IntegrationTests.Services
{
    [TestFixture]
    [Category("Integration")]
    public sealed class FileStateStoreIntegrationTests
    {
        private FileStateStore stateStore = null!;
        private string temporaryStatePath = null!;

        [SetUp]
        public void SetUp()
        {
            temporaryStatePath = Path.Combine(Path.GetTempPath(), $"billrelay_integration_{Guid.NewGuid()}.json");
            stateStore = BuildStateStore(temporaryStatePath);
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
        public async Task GivenARealFile_WhenCompletingTheFullLifecycle_ThenStateIsPersisted()
        {
            await stateStore.InitializeAsync(CancellationToken.None);

            await stateStore.MarkProcessedAsync("attach1", "msg1", CancellationToken.None);
            await stateStore.MarkProcessedAsync("attach2", "msg2", CancellationToken.None);

            Assert.That(await stateStore.IsProcessedAsync("attach1", CancellationToken.None));
            Assert.That(await stateStore.IsProcessedAsync("attach2", CancellationToken.None));
            Assert.That(await stateStore.IsProcessedAsync("attach3", CancellationToken.None), Is.False);

            stateStore.Dispose();

            FileStateStore newStore = BuildStateStore(temporaryStatePath);
            await newStore.InitializeAsync(CancellationToken.None);

            Assert.That(await newStore.IsProcessedAsync("attach1", CancellationToken.None));
            Assert.That(await newStore.IsProcessedAsync("attach2", CancellationToken.None));

            newStore.Dispose();
        }

        [Test]
        public async Task GivenExpiredAndExcessRecords_WhenApplyingRetention_ThenRecentRecordsArePreserved()
        {
            DateTimeOffset currentTime = DateTimeOffset.UtcNow;
            string oldTimestamp = currentTime.AddDays(-10).ToString("O", CultureInfo.InvariantCulture);
            string recentTimestamp = currentTime.ToString("O", CultureInfo.InvariantCulture);
            string json = $$"""
            [
              {"id":"expired","messageId":"message","processedAt":"{{oldTimestamp}}"},
              {"id":"recent1","messageId":"message","processedAt":"{{recentTimestamp}}"},
              {"id":"recent2","messageId":"message","processedAt":"{{recentTimestamp}}"}
            ]
            """;
            await File.WriteAllTextAsync(temporaryStatePath, json);
            await stateStore.InitializeAsync(CancellationToken.None);

            await stateStore.ApplyRetentionAsync(7, 2, CancellationToken.None);

            Assert.That(await stateStore.IsProcessedAsync("expired", CancellationToken.None), Is.False);
            Assert.That(await stateStore.IsProcessedAsync("recent1", CancellationToken.None));
            Assert.That(await stateStore.IsProcessedAsync("recent2", CancellationToken.None));
        }

        [Test]
        public async Task GivenCancellation_WhenRecordingAnAttachment_ThenTheLedgerIsNotModified()
        {
            await stateStore.InitializeAsync(CancellationToken.None);
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();

            Assert.That(
                () => stateStore.MarkProcessedAsync("canceled", "message", cancellation.Token),
                Throws.TypeOf<OperationCanceledException>());
            Assert.That(await stateStore.IsProcessedAsync("canceled", CancellationToken.None), Is.False);
        }

        [Test]
        public async Task GivenARetentionPolicy_WhenApplyingIt_ThenOldRecordsAreRemoved()
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

        private static FileStateStore BuildStateStore(string statePath) => new(
            NullLogger<FileStateStore>.Instance,
            Options.Create(new BillRelaySettings
            {
                Storage = new() { StateFilePath = statePath }
            }));
    }
}