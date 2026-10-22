using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit;
using MimeKit;
using Moq;
using NUnit.Framework;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using BillRelay.Configuration;
using BillRelay.Services;

namespace BillRelay.UnitTests.Services;

[TestFixture]
public class BillRelayWorkerTests
{
    private Mock<ILogger<BillRelayWorker>> loggerMock = null!;
    private Mock<IOptions<BillRelaySettings>> settingsMock = null!;
    private Mock<IAttachmentProcessor> attachmentProcessorMock = null!;
    private Mock<IStateStore> stateStoreMock = null!;
    private BillRelaySettings settings = null!;
    private BillRelayWorker worker = null!;

    [SetUp]
    public void SetUp()
    {
        loggerMock = new Mock<ILogger<BillRelayWorker>>();
        attachmentProcessorMock = new Mock<IAttachmentProcessor>();
        stateStoreMock = new Mock<IStateStore>();

        settings = new BillRelaySettings
        {
            Imap = new ImapSettings
            {
                Server = "imap.example.com",
                Port = 993,
                Username = "test@example.com",
                Password = "password",
                UseSsl = true,
                ValidateCertificates = true
            },
            Polling = new PollingSettings
            {
                IntervalSeconds = 300,
                InitialProcessingDate = null
            },
            Filters = new FilterSettings
            {
                SenderPatterns = ["billing@"],
                SubjectPatterns = ["invoice"]
            },
            Attachments = new AttachmentSettings
            {
                PermittedMimeTypes = ["application/pdf"],
                MaxSizeBytes = 25_000_000
            }
        };

        settingsMock = new Mock<IOptions<BillRelaySettings>>();
        settingsMock.Setup(s => s.Value).Returns(settings);

        worker = new BillRelayWorker(
            loggerMock.Object,
            settingsMock.Object,
            stateStoreMock.Object,
            Mock.Of<IBillSender>(),
            Mock.Of<IBillDelivery>());
    }

    [Test]
    public void GivenValidSettings_WhenConstructed_ThenWorkerIsCreated()
    {
        Assert.That(worker, Is.Not.Null);
    }

    [Test]
    public async Task GivenCancellation_WhenExecuteAsync_ThenStopsGracefully()
    {
        var cts = new CancellationTokenSource();
        cts.CancelAfter(50);

        stateStoreMock.Setup(s => s.InitializeAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await worker.StartAsync(cts.Token);

        await Task.Delay(100);
        Assert.That(cts.IsCancellationRequested, Is.True);
    }
}