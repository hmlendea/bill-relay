using System.IO;
using System.Linq;
using System.Text;

using Microsoft.Extensions.Options;

using MimeKit;

using NUnit.Framework;

using BillRelay.Configuration;
using BillRelay.Services.Attachments;

namespace BillRelay.UnitTests.Services.Attachments
{
    [TestFixture]
    public sealed class AttachmentValidatorTests
    {
        private BillRelaySettings settings = null!;
        private AttachmentValidator validator = null!;

        [SetUp]
        public void SetUp()
        {
            settings = new()
            {
                Attachments = new()
                {
                    MaximumSizeBytes = 8,
                    PermittedMimeTypes = ["application/pdf"]
                }
            };
            validator = new(Options.Create(settings));
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
        }

        [Test]
        public void GivenAMultipartAttachment_WhenValidatingIt_ThenItIsRejected()
        {
            using Multipart attachment = new();

            Assert.That(validator.IsPermitted(attachment), Is.False);
        }

        [TestCase("%PDF-0042", "The attachment size (9 bytes) exceeds the maximum of 8 bytes.")]
        [TestCase("%PNG-", "The attachment does not start with the required PDF signature '%PDF-'.")]
        public void GivenAnInvalidAttachment_WhenGettingItsFailureReason_ThenTheExactReasonIsReturned(
            string content,
            string expectedReason)
        {
            using MimePart attachment = BuildAttachment("application/pdf", content);

            Assert.That(validator.GetFailureReason(attachment), Is.EqualTo(expectedReason));
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
        }

        private static MimePart BuildAttachment(string mimeType, string content) => new(mimeType)
        {
            FileName = "bill.pdf",
            Content = new MimeContent(new MemoryStream(Encoding.UTF8.GetBytes(content)))
        };
    }
}