using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Options;

using MimeKit;

using Moq;

using NuciLog.Core;

using NUnit.Framework;

using BillRelay.Configuration;
using BillRelay.Services;

namespace BillRelay.UnitTests.Services
{
    [TestFixture]
    public sealed class AttachmentProcessorTests
    {
        private Mock<ILogger> loggerMock = null!;
        private IOptions<BillRelaySettings> settings = null!;
        private AttachmentProcessor processor = null!;
        private string temporaryDirectory = null!;

        [SetUp]
        public void SetUp()
        {
            loggerMock = new();
            temporaryDirectory = Path.Combine(Path.GetTempPath(), $"billrelay_tests_{Guid.NewGuid()}");
            Directory.CreateDirectory(temporaryDirectory);

            BillRelaySettings billRelaySettings = new()
            {
                Storage = new()
                {
                    DownloadDirectory = temporaryDirectory
                },
                Attachments = new()
                {
                    PermittedMimeTypes = ["application/pdf"],
                    MaximumSizeBytes = 25_000_000
                }
            };

            settings = Options.Create(billRelaySettings);
            processor = new(loggerMock.Object, settings);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(temporaryDirectory))
            {
                Directory.Delete(temporaryDirectory, true);
            }
        }

        [Test]
        public async Task GivenAValidPdfAttachment_WhenProcessingIt_ThenTheFileIsSaved()
        {
            string pdfContent = "%PDF-1.4\n%Test PDF content\n%%EOF";
            MimePart attachment = CreateMimePart("test.pdf", "application/pdf", pdfContent);
            string attachmentIdentifier = "test123";

            await processor.ProcessAsync(attachment, attachmentIdentifier, CancellationToken.None);

            string[] files = Directory.GetFiles(temporaryDirectory);
            Assert.That(files, Has.Length.EqualTo(1));
            Assert.That(Path.GetFileName(files[0]), Is.EqualTo("test.pdf"));

            string savedContent = await File.ReadAllTextAsync(files[0]);
            Assert.That(savedContent, Is.EqualTo(pdfContent));
        }

        [Test]
        public async Task GivenADuplicateAttachment_WhenProcessingIt_ThenNoDuplicateFileIsCreated()
        {
            string pdfContent = "%PDF-1.4\n%Test PDF content\n%%EOF";
            MimePart attachment = CreateMimePart("test.pdf", "application/pdf", pdfContent);
            string attachmentIdentifier = "test123";

            await processor.ProcessAsync(attachment, attachmentIdentifier, CancellationToken.None);
            await processor.ProcessAsync(attachment, attachmentIdentifier, CancellationToken.None);

            string[] files = Directory.GetFiles(temporaryDirectory);
            Assert.That(files, Has.Length.EqualTo(1));
        }

        [Test]
        public async Task GivenTheSameNameWithDifferentContent_WhenProcessingIt_ThenAUniqueFileIsCreated()
        {
            string firstPdfContent = "%PDF-1.4\n%Test PDF content 1\n%%EOF";
            string secondPdfContent = "%PDF-1.4\n%Test PDF content 2\n%%EOF";
            MimePart firstAttachment = CreateMimePart("test.pdf", "application/pdf", firstPdfContent);
            MimePart secondAttachment = CreateMimePart("test.pdf", "application/pdf", secondPdfContent);

            await processor.ProcessAsync(firstAttachment, "id1", CancellationToken.None);
            await processor.ProcessAsync(secondAttachment, "id2", CancellationToken.None);

            string[] files = Directory.GetFiles(temporaryDirectory);
            Assert.That(files, Has.Length.EqualTo(2));
            Assert.That(files.Select(Path.GetFileName), Does.Contain("test.pdf"));
            Assert.That(files.Select(Path.GetFileName), Does.Contain("test_1.pdf"));
        }

        [Test]
        public async Task GivenAFileNameWithInvalidCharacters_WhenProcessingIt_ThenTheNameIsSanitized()
        {
            string pdfContent = "%PDF-1.4\n%Test\n%%EOF";
            MimePart attachment = CreateMimePart("test<>:\"|?.pdf", "application/pdf", pdfContent);
            string attachmentIdentifier = "test123";

            await processor.ProcessAsync(attachment, attachmentIdentifier, CancellationToken.None);

            string[] files = Directory.GetFiles(temporaryDirectory);
            Assert.That(files, Has.Length.EqualTo(1));
            string? fileName = Path.GetFileName(files[0]);
            Assert.That(fileName, Does.Not.Contain("<"));
            Assert.That(fileName, Does.Not.Contain(">"));
            Assert.That(fileName, Does.Not.Contain(":"));
            Assert.That(fileName, Does.Not.Contain("\""));
            Assert.That(fileName, Does.Not.Contain("|"));
            Assert.That(fileName, Does.Not.Contain("?"));
        }

        [Test]
        public async Task GivenAStaleTemporaryFile_WhenSavingABill_ThenItIsPreservedAndTheBillIsSaved()
        {
            string stalePath = Path.Combine(temporaryDirectory, "test.pdf.tmp");
            await File.WriteAllTextAsync(stalePath, "Existing download.");
            using MimePart attachment = CreateMimePart("test.pdf", "application/pdf", "%PDF-bill");

            await processor.ProcessAsync(attachment, "bill", CancellationToken.None);

            Assert.That(await File.ReadAllTextAsync(stalePath), Is.EqualTo("Existing download."));
            Assert.That(await File.ReadAllTextAsync(Path.Combine(temporaryDirectory, "test.pdf")),
                Is.EqualTo("%PDF-bill"));
        }

        [TestCase("Vodafone", "Vodafone_test.pdf")]
        [TestCase("E-bloc", "E-bloc_test.pdf")]
        public async Task GivenAProvider_WhenSavingAnAttachment_ThenItsNamePrefixesTheFileName(
            string providerName,
            string expectedFileName)
        {
            using MimePart attachment = CreateMimePart("test.pdf", "application/pdf", "%PDF-test");

            await processor.ProcessAsync(attachment, "attachment", providerName, CancellationToken.None);

            Assert.That(
                Directory.GetFiles(temporaryDirectory).Select(Path.GetFileName),
                Is.EquivalentTo((string[])[expectedFileName]));
            Assert.That(
                await File.ReadAllTextAsync(Path.Combine(temporaryDirectory, expectedFileName)),
                Is.EqualTo("%PDF-test"));
            loggerMock.Verify(
                logger => logger.Info(
                    It.Is<Operation>(operation => string.Equals(operation.Name, "SaveAttachment")),
                    It.Is<OperationStatus>(status => string.Equals(status.Name, "Success")),
                    It.Is<LogInfo[]>(details => details.Any(detail =>
                        string.Equals(detail.Key.Name, "FilePath") &&
                        string.Equals(detail.Value, Path.Combine(temporaryDirectory, expectedFileName))))),
                Times.Once);
        }

        [Test]
        public void GivenANonPartAttachment_WhenProcessingIt_ThenItIsRejectedWithoutCreatingFiles()
        {
            using Multipart attachment = new();

            Assert.That(
                () => processor.ProcessAsync(attachment, "attachment", CancellationToken.None),
                Throws.TypeOf<InvalidOperationException>());
            Assert.That(Directory.GetFiles(temporaryDirectory), Is.Empty);
        }

        [Test]
        public void GivenCancellation_WhenWritingAnAttachment_ThenTheTemporaryFileIsRemoved()
        {
            using MimePart attachment = CreateMimePart("test.pdf", "application/pdf", "%PDF-test");
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();

            Assert.That(
                () => processor.ProcessAsync(attachment, "attachment", cancellation.Token),
                Throws.InstanceOf<OperationCanceledException>());
            Assert.That(Directory.GetFiles(temporaryDirectory), Is.Empty);
            loggerMock.Verify(
                logger => logger.Error(
                    It.Is<Operation>(operation => string.Equals(operation.Name, "WriteAttachment")),
                    It.Is<OperationStatus>(status => string.Equals(status.Name, "Failure")),
                    It.Is<Exception>(exception => exception is OperationCanceledException),
                    It.Is<LogInfo[]>(details => details.Any(detail =>
                        string.Equals(detail.Key.Name, "AttachmentId") &&
                        string.Equals(detail.Value, "attachment")))),
                Times.Once);
        }

        private static MimePart CreateMimePart(string fileName, string mimeType, string content)
            => new(mimeType)
            {
                FileName = fileName,
                Content = new MimeContent(new MemoryStream(Encoding.UTF8.GetBytes(content)))
            };
    }
}