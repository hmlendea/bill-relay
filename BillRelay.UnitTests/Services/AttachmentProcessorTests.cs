using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using Moq;
using NUnit.Framework;
using BillRelay.Configuration;
using BillRelay.Services;

namespace BillRelay.UnitTests.Services;

[TestFixture]
public class AttachmentProcessorTests
{
    private Mock<ILogger<AttachmentProcessor>> loggerMock = null!;
    private IOptions<BillRelaySettings> settings = null!;
    private AttachmentProcessor processor = null!;
    private string tempDirectory = null!;

    [SetUp]
    public void SetUp()
    {
        loggerMock = new Mock<ILogger<AttachmentProcessor>>();
        tempDirectory = Path.Combine(Path.GetTempPath(), $"billrelay_tests_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDirectory);

        var billRelaySettings = new BillRelaySettings
        {
            Storage = new StorageSettings
            {
                DownloadDirectory = tempDirectory
            },
            Attachments = new AttachmentSettings
            {
                PermittedMimeTypes = ["application/pdf"],
                MaxSizeBytes = 25_000_000
            }
        };

        settings = Options.Create(billRelaySettings);
        processor = new AttachmentProcessor(loggerMock.Object, settings);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(tempDirectory))
        {
            Directory.Delete(tempDirectory, true);
        }
    }

    [Test]
    public async Task GivenValidPdfAttachment_WhenProcessing_ThenSavesFile()
    {
        string pdfContent = "%PDF-1.4\n%Test PDF content\n%%EOF";
        MimePart attachment = CreateMimePart("test.pdf", "application/pdf", pdfContent);
        string attachmentIdentifier = "test123";

        await processor.ProcessAsync(attachment, attachmentIdentifier, CancellationToken.None);

        string[] files = Directory.GetFiles(tempDirectory);
        Assert.That(files.Length, Is.EqualTo(1));
        Assert.That(Path.GetFileName(files[0]), Is.EqualTo("test.pdf"));

        string savedContent = await File.ReadAllTextAsync(files[0]);
        Assert.That(savedContent, Is.EqualTo(pdfContent));
    }

    [Test]
    public async Task GivenDuplicateAttachment_WhenProcessing_ThenDoesNotCreateDuplicate()
    {
        string pdfContent = "%PDF-1.4\n%Test PDF content\n%%EOF";
        MimePart attachment = CreateMimePart("test.pdf", "application/pdf", pdfContent);
        string attachmentIdentifier = "test123";

        await processor.ProcessAsync(attachment, attachmentIdentifier, CancellationToken.None);
        await processor.ProcessAsync(attachment, attachmentIdentifier, CancellationToken.None);

        string[] files = Directory.GetFiles(tempDirectory);
        Assert.That(files.Length, Is.EqualTo(1));
    }

    [Test]
    public async Task GivenSameNameDifferentContent_WhenProcessing_ThenCreatesUniqueFileName()
    {
        string firstPdfContent = "%PDF-1.4\n%Test PDF content 1\n%%EOF";
        string secondPdfContent = "%PDF-1.4\n%Test PDF content 2\n%%EOF";
        MimePart firstAttachment = CreateMimePart("test.pdf", "application/pdf", firstPdfContent);
        MimePart secondAttachment = CreateMimePart("test.pdf", "application/pdf", secondPdfContent);

        await processor.ProcessAsync(firstAttachment, "id1", CancellationToken.None);
        await processor.ProcessAsync(secondAttachment, "id2", CancellationToken.None);

        string[] files = Directory.GetFiles(tempDirectory);
        Assert.That(files.Length, Is.EqualTo(2));
        Assert.That(files.Any(f => Path.GetFileName(f) == "test.pdf"), Is.True);
        Assert.That(files.Any(f => Path.GetFileName(f) == "test_1.pdf"), Is.True);
    }

    [Test]
    public async Task GivenFilenameWithInvalidChars_WhenProcessing_ThenSanitizesFileName()
    {
        string pdfContent = "%PDF-1.4\n%Test\n%%EOF";
        MimePart attachment = CreateMimePart("test<>:\"|?.pdf", "application/pdf", pdfContent);
        string attachmentIdentifier = "test123";

        await processor.ProcessAsync(attachment, attachmentIdentifier, CancellationToken.None);

        string[] files = Directory.GetFiles(tempDirectory);
        Assert.That(files.Length, Is.EqualTo(1));
        string? fileName = Path.GetFileName(files[0]);
        Assert.That(fileName, Does.Not.Contain("<"));
        Assert.That(fileName, Does.Not.Contain(">"));
        Assert.That(fileName, Does.Not.Contain(":"));
        Assert.That(fileName, Does.Not.Contain("\""));
        Assert.That(fileName, Does.Not.Contain("|"));
        Assert.That(fileName, Does.Not.Contain("?"));
    }

    private static MimePart CreateMimePart(string fileName, string mimeType, string content)
    {
        MimePart part = new(mimeType)
        {
            FileName = fileName,
            Content = new MimeContent(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content)))
        };
        return part;
    }
}