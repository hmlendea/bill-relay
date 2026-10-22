using System.IO;
using System.Linq;
using System.Text;

using Microsoft.Extensions.Options;

using MimeKit;

using Moq;

using NuciLog.Core;

using NUnit.Framework;

using BillRelay.Configuration;
using BillRelay.Services.Attachments;

namespace BillRelay.UnitTests.Services.Attachments
{
    [TestFixture]
    public sealed class AttachmentValidatorTests
    {
        private Mock<ILogger> loggerMock = null!;
        private BillRelaySettings settings = null!;
        private AttachmentValidator validator = null!;

        [SetUp]
        public void SetUp()
        {
            loggerMock = new();
            settings = new()
            {
                Attachments = new()
                {
                    MaximumSizeBytes = 8,
                    PermittedMimeTypes = ["application/pdf"]
                }
            };
            validator = new(loggerMock.Object, Options.Create(settings));
        }

        [TestCase("%PDF-42", true)]
        [TestCase("%PDF-042", true)]
        [TestCase("%PDF-0042", false)]
        public void GivenAnAttachmentSize_WhenValidatingIt_ThenTheInclusiveLimitIsEnforced(
            string content,
            bool isExpectedPermitted)
        {
            using MimePart attachment = BuildAttachment("application/pdf", content);

            Assert.That(validator.IsPermitted(attachment), Is.EqualTo(isExpectedPermitted));
        }

        [TestCase("", false)]
        [TestCase("%PDF", false)]
        [TestCase("%PDF-", true)]
        [TestCase("%pdf-", false)]
        [TestCase(" %PDF-", false)]
        [TestCase("%PNG-", false)]
        public void GivenAttachmentContent_WhenValidatingIt_ThenTheExactPdfPrefixIsRequired(
            string content,
            bool isExpectedPermitted)
        {
            using MimePart attachment = BuildAttachment("application/pdf", content);

            Assert.That(validator.IsPermitted(attachment), Is.EqualTo(isExpectedPermitted));
        }

        [TestCase("image/png")]
        [TestCase("text/plain")]
        public void GivenAnUnpermittedMimeType_WhenValidatingIt_ThenItIsRejectedWithoutWarnings(
            string mimeType)
        {
            using MimePart attachment = BuildAttachment(mimeType, "%PDF-");

            Assert.That(validator.IsPermitted(attachment), Is.False);
            loggerMock.VerifyNoOtherCalls();
        }

        [Test]
        public void GivenAMultipartAttachment_WhenValidatingIt_ThenItIsRejected()
        {
            using Multipart attachment = new();

            Assert.That(validator.IsPermitted(attachment), Is.False);
            loggerMock.VerifyNoOtherCalls();
        }

        [TestCase("%PDF-0042", "ContentLength", "9")]
        [TestCase("%PNG-", "FileName", "bill.pdf")]
        public void GivenAnInvalidAttachment_WhenValidatingIt_ThenAStructuredWarningIsWritten(
            string content,
            string expectedKey,
            string expectedValue)
        {
            using MimePart attachment = BuildAttachment("application/pdf", content);

            Assert.That(validator.IsPermitted(attachment), Is.False);
            loggerMock.Verify(
                logger => logger.Warn(
                    It.Is<Operation>(operation => string.Equals(operation.Name, "ValidateAttachment")),
                    It.Is<OperationStatus>(status => string.Equals(status.Name, "Failure")),
                    It.Is<LogInfo[]>(details => details.Any(detail =>
                        string.Equals(detail.Key.Name, expectedKey) &&
                        string.Equals(detail.Value, expectedValue)))),
                Times.Once);
            loggerMock.VerifyNoOtherCalls();
        }

        [TestCase("%PDF-")]
        [TestCase("%PDF-42")]
        public void GivenAValidAttachment_WhenValidatingItTwice_ThenItsContentRemainsReadable(
            string content)
        {
            using MimePart attachment = BuildAttachment("application/pdf", content);
            using MemoryStream decodedContent = new();

            Assert.That(validator.IsPermitted(attachment));
            Assert.That(validator.IsPermitted(attachment));
            attachment.Content.DecodeTo(decodedContent);

            Assert.That(Encoding.UTF8.GetString(decodedContent.ToArray()), Is.EqualTo(content));
            loggerMock.VerifyNoOtherCalls();
        }

        private static MimePart BuildAttachment(string mimeType, string content) => new(mimeType)
        {
            FileName = "bill.pdf",
            Content = new MimeContent(new MemoryStream(Encoding.UTF8.GetBytes(content)))
        };
    }
}