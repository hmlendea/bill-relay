using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Options;

using MimeKit;

using Moq;

using NuciNotifications.Client;
using NuciNotifications.Client.Requests;

using NUnit.Framework;

using BillRelay.Configuration;
using BillRelay.Services;

namespace BillRelay.IntegrationTests.Services
{
    [TestFixture]
    public sealed class BillSenderTests
    {
        [TestCase("<EmailTemplate><Subject>Bill {ProviderName}</Subject><Body>From {ProviderName}</Body></EmailTemplate>", true)]
        [TestCase("<EmailTemplate><Body>Missing subject</Body></EmailTemplate>", false)]
        [TestCase("<!DOCTYPE EmailTemplate [<!ENTITY test 'unsafe'>]><EmailTemplate><Subject>&test;</Subject><Body>Bill</Body></EmailTemplate>", false)]
        public async Task GivenAnXmlTemplate_WhenSendingABill_ThenOnlyValidTemplatesReachTheClient(
            string template,
            bool isValidTemplate)
        {
            string templatePath = Path.GetTempFileName();

            try
            {
                await File.WriteAllTextAsync(templatePath, template);
                await VerifyDeliveryAsync(templatePath, isValidTemplate);
            }
            finally
            {
                File.Delete(templatePath);
            }
        }

        private static async Task VerifyDeliveryAsync(string templatePath, bool isValidTemplate)
        {
            Mock<INuciNotificationsClient> client = new(MockBehavior.Strict);
            client.Setup(notification => notification.SendEmail(
                "BillRelay", "recipient@example.com", "Bill Vodafone", "From Vodafone",
                It.Is<IEnumerable<EmailAttachmentRequest>>(attachments =>
                    string.Equals(attachments.Single().FileName, "bill.pdf", StringComparison.Ordinal) &&
                    string.Equals(
                        attachments.Single().Content,
                        Convert.ToBase64String(Encoding.UTF8.GetBytes("%PDF-test")),
                        StringComparison.Ordinal))))
                .Returns(Task.CompletedTask);
            BillRelaySettings settings = new()
            {
                Delivery = new() { Recipient = "recipient@example.com", TemplatePath = templatePath }
            };
            BillSender sender = new(client.Object, Options.Create(settings));
            using MimePart attachment = new("application/pdf")
            {
                FileName = "bill.pdf",
                Content = new MimeContent(new MemoryStream(Encoding.UTF8.GetBytes("%PDF-test")))
            };

            if (!isValidTemplate)
            {
                Assert.That(async () => await sender.SendAsync(attachment, "Vodafone", CancellationToken.None),
                    Throws.Exception);
                client.VerifyNoOtherCalls();

                return;
            }

            await sender.SendAsync(attachment, "Vodafone", CancellationToken.None);
            client.VerifyAll();
        }
    }
}