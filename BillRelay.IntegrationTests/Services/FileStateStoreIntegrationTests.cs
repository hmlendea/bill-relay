using System;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using BillRelay.Configuration;
using BillRelay.Services;

namespace BillRelay.IntegrationTests.Services;

[TestFixture]
[Category("Integration")]
public class FileStateStoreIntegrationTests
{
    private static readonly JsonSerializerOptions jsonSerializerOptions = new() { WriteIndented = true };
    private FileStateStore stateStore = null!;
    private string tempStatePath = null!;

    [SetUp]
    public void SetUp()
    {
        tempStatePath = Path.Combine(Path.GetTempPath(), $"billrelay_integration_{Guid.NewGuid()}.json");

        var settings = new BillRelaySettings
        {
            Storage = new StorageSettings
            {
                StateFilePath = tempStatePath
            }
        };

        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<FileStateStore>();
        var options = Options.Create(settings);

        stateStore = new FileStateStore(logger, options);
    }

    [TearDown]
    public void TearDown()
    {
        stateStore?.Dispose();
        if (File.Exists(tempStatePath))
        {
            File.Delete(tempStatePath);
        }
    }

    [Test]
    public async Task GivenRealFile_WhenFullLifecycle_ThenPersistsCorrectly()
    {
        await stateStore.InitializeAsync(CancellationToken.None);

        await stateStore.MarkProcessedAsync("attach1", "msg1", CancellationToken.None);
        await stateStore.MarkProcessedAsync("attach2", "msg2", CancellationToken.None);

        Assert.That(await stateStore.IsProcessedAsync("attach1", CancellationToken.None), Is.True);
        Assert.That(await stateStore.IsProcessedAsync("attach2", CancellationToken.None), Is.True);
        Assert.That(await stateStore.IsProcessedAsync("attach3", CancellationToken.None), Is.False);

        stateStore.Dispose();

        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<FileStateStore>();
        var options = Options.Create(new BillRelaySettings
        {
            Storage = new StorageSettings { StateFilePath = tempStatePath }
        });

        var newStore = new FileStateStore(logger, options);
        await newStore.InitializeAsync(CancellationToken.None);

        Assert.That(await newStore.IsProcessedAsync("attach1", CancellationToken.None), Is.True);
        Assert.That(await newStore.IsProcessedAsync("attach2", CancellationToken.None), Is.True);

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
        await File.WriteAllTextAsync(tempStatePath, json);
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
    public async Task GivenRetentionPolicy_WhenApplied_ThenRemovesOldRecords()
    {
        await stateStore.InitializeAsync(CancellationToken.None);

        await stateStore.MarkProcessedAsync("old1", "msg1", CancellationToken.None);
        await stateStore.MarkProcessedAsync("old2", "msg2", CancellationToken.None);
        await stateStore.MarkProcessedAsync("new1", "msg3", CancellationToken.None);

        stateStore.Dispose();

        // Manually update old records to have old timestamps by modifying the JSON file
        var json = await File.ReadAllTextAsync(tempStatePath);
        var oldDate = DateTimeOffset.UtcNow.AddDays(-2).ToString("O");

        // Parse and modify JSON using System.Text.Json
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var array = new System.Text.Json.Nodes.JsonArray();
        foreach (var element in doc.RootElement.EnumerateArray())
        {
            var id = element.GetProperty("id").GetString();
            if (id == "old1" || id == "old2")
            {
                var newObj = new System.Text.Json.Nodes.JsonObject();
                foreach (var prop in element.EnumerateObject())
                {
                    if (prop.Name == "processedAt")
                    {
                        newObj[prop.Name] = oldDate;
                    }
                    else
                    {
                        newObj[prop.Name] = prop.Value.GetString() ?? string.Empty;
                    }
                }
                array.Add(newObj);
            }
            else
            {
                var newObj = new System.Text.Json.Nodes.JsonObject();
                foreach (var prop in element.EnumerateObject())
                {
                    newObj[prop.Name] = prop.Value.GetString() ?? string.Empty;
                }
                array.Add(newObj);
            }
        }

        json = JsonSerializer.Serialize(array, jsonSerializerOptions);
        await File.WriteAllTextAsync(tempStatePath, json);

        // Recreate state store to reload from file
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<FileStateStore>();
        var options = Options.Create(new BillRelaySettings
        {
            Storage = new StorageSettings { StateFilePath = tempStatePath }
        });
        stateStore = new FileStateStore(logger, options);
        await stateStore.InitializeAsync(CancellationToken.None);

        await stateStore.ApplyRetentionAsync(1, 10000, CancellationToken.None);

        Assert.That(await stateStore.IsProcessedAsync("old1", CancellationToken.None), Is.False);
        Assert.That(await stateStore.IsProcessedAsync("old2", CancellationToken.None), Is.False);
        Assert.That(await stateStore.IsProcessedAsync("new1", CancellationToken.None), Is.True);
    }
}