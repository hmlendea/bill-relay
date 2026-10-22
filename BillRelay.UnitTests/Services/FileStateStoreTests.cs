using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using BillRelay.Configuration;
using BillRelay.Services;

namespace BillRelay.UnitTests.Services;

[TestFixture]
public class FileStateStoreTests
{
    private static readonly JsonSerializerOptions jsonSerializerOptions = new() { WriteIndented = true };
    private Mock<ILogger<FileStateStore>> loggerMock = null!;
    private IOptions<BillRelaySettings> settings = null!;
    private FileStateStore stateStore = null!;
    private string tempStatePath = null!;

    [SetUp]
    public void SetUp()
    {
        loggerMock = new Mock<ILogger<FileStateStore>>();
        tempStatePath = Path.Combine(Path.GetTempPath(), $"billrelay_test_{Guid.NewGuid()}.json");

        var billRelaySettings = new BillRelaySettings
        {
            Storage = new StorageSettings
            {
                StateFilePath = tempStatePath
            }
        };

        settings = Options.Create(billRelaySettings);
        stateStore = new FileStateStore(loggerMock.Object, settings);
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
    public async Task GivenNewStore_WhenInitialized_ThenCreatesFile()
    {
        await stateStore.InitializeAsync(CancellationToken.None);

        var isProcessed = await stateStore.IsProcessedAsync("nonexistent", CancellationToken.None);
        Assert.That(isProcessed, Is.False);
        Assert.That(File.Exists(tempStatePath), Is.True);
    }

    [Test]
    public async Task GivenUnprocessedAttachment_WhenChecking_ThenReturnsFalse()
    {
        await stateStore.InitializeAsync(CancellationToken.None);

        var result = await stateStore.IsProcessedAsync("attachment123", CancellationToken.None);
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task GivenProcessedAttachment_WhenChecking_ThenReturnsTrue()
    {
        await stateStore.InitializeAsync(CancellationToken.None);
        await stateStore.MarkProcessedAsync("attachment123", "message456", CancellationToken.None);

        var result = await stateStore.IsProcessedAsync("attachment123", CancellationToken.None);
        Assert.That(result, Is.True);
    }

    [Test]
    public async Task GivenMultipleAttachments_WhenMarkingProcessed_ThenAllAreTracked()
    {
        await stateStore.InitializeAsync(CancellationToken.None);

        await stateStore.MarkProcessedAsync("attachment1", "message1", CancellationToken.None);
        await stateStore.MarkProcessedAsync("attachment2", "message2", CancellationToken.None);
        await stateStore.MarkProcessedAsync("attachment3", "message1", CancellationToken.None);

        Assert.That(await stateStore.IsProcessedAsync("attachment1", CancellationToken.None), Is.True);
        Assert.That(await stateStore.IsProcessedAsync("attachment2", CancellationToken.None), Is.True);
        Assert.That(await stateStore.IsProcessedAsync("attachment3", CancellationToken.None), Is.True);
    }

    [Test]
    public async Task GivenOldRecords_WhenApplyingRetention_ThenDeletesOldRecords()
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
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
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

    [Test]
    public async Task GivenExcessRecords_WhenApplyingRetention_ThenDeletesOldestRecords()
    {
        await stateStore.InitializeAsync(CancellationToken.None);

        await stateStore.MarkProcessedAsync("attach1", "msg1", CancellationToken.None);
        await Task.Delay(10);
        await stateStore.MarkProcessedAsync("attach2", "msg2", CancellationToken.None);
        await Task.Delay(10);
        await stateStore.MarkProcessedAsync("attach3", "msg3", CancellationToken.None);
        await Task.Delay(10);
        await stateStore.MarkProcessedAsync("attach4", "msg4", CancellationToken.None);

        await stateStore.ApplyRetentionAsync(365, 2, CancellationToken.None);

        Assert.That(await stateStore.IsProcessedAsync("attach1", CancellationToken.None), Is.False);
        Assert.That(await stateStore.IsProcessedAsync("attach2", CancellationToken.None), Is.False);
        Assert.That(await stateStore.IsProcessedAsync("attach3", CancellationToken.None), Is.True);
        Assert.That(await stateStore.IsProcessedAsync("attach4", CancellationToken.None), Is.True);
    }
}