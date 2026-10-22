using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using MimeKit;

using NUnit.Framework;

using BillRelay.Configuration;
using BillRelay.Services;

namespace BillRelay.IntegrationTests.Services
{
    [TestFixture]
    public sealed class ExampleEmailTests
    {
        [Test]
        public async Task GivenLocalExampleEmails_WhenSavingTheirBills_ThenEveryProviderIsIdentified()
        {
            string? exampleDirectory = Environment.GetEnvironmentVariable("BILLRELAY_EXAMPLE_DIRECTORY");

            if (string.IsNullOrWhiteSpace(exampleDirectory))
            {
                Assert.Ignore("Set BILLRELAY_EXAMPLE_DIRECTORY to validate private email samples.");

                return;
            }

            string outputDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

            try
            {
                await ValidateExamplesAsync(exampleDirectory, outputDirectory);
            }
            finally
            {
                if (Directory.Exists(outputDirectory))
                {
                    Directory.Delete(outputDirectory, true);
                }
            }
        }

        private static async Task ValidateExamplesAsync(string exampleDirectory, string outputDirectory)
        {
            string[] paths = Directory.GetFiles(exampleDirectory, "*.eml");
            Assert.That(paths, Is.Not.Empty);
            BillRelaySettings settings = new()
            {
                Storage = new() { DownloadDirectory = outputDirectory }
            };
            AttachmentProcessor processor = new(
                NullLogger<AttachmentProcessor>.Instance,
                Options.Create(settings));

            foreach (string path in paths)
            {
                using MimeMessage message = await MimeMessage.LoadAsync(path);
                string? providerName = BillProviderIdentification.IdentifyProvider(message);
                Assert.That(providerName, Is.Not.Null, "A sample provider could not be identified.");
                MimePart[] attachments = message.Attachments.OfType<MimePart>().ToArray();
                Assert.That(attachments, Is.Not.Empty);

                foreach (MimePart attachment in attachments)
                {
                    Assert.That(BillProviderIdentification.IsBillAttachment(attachment, providerName!));

                    await processor.ProcessAsync(
                        attachment,
                        Guid.NewGuid().ToString(),
                        providerName!,
                        CancellationToken.None);
                }
            }

            Assert.That(Directory.GetFiles(outputDirectory), Has.Length.EqualTo(paths.Length));
        }
    }
}