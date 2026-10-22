using System;
using System.IO;
using System.Net;
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Options;

using MimeKit;

using NuciDAL.IO;

using NuciNotifications.Client;
using NuciNotifications.Client.Requests;

using NuciLog.Core;

using BillRelay.Configuration;
using BillRelay.DataAccess.DataObjects;
using BillRelay.Logging;

namespace BillRelay.Services
{
    public sealed class BillSender(
        INuciNotificationsClient client,
        IOptions<BillRelaySettings> settings,
        ILogger logger) : IBillSender
    {
        private static string ProviderNamePlaceholder => "{ProviderName}";

        public void ValidateConfiguration()
        {
            _ = new MailAddress(settings.Value.Delivery.Recipient);
            _ = LoadTemplate(settings.Value.Delivery.TemplatePath);
        }

        public async Task SendAsync(
            MimePart attachment,
            string providerName,
            CancellationToken cancellationToken)
        {
            DeliverySettings delivery = settings.Value.Delivery;
            _ = new MailAddress(delivery.Recipient);
            EmailTemplateDataObject template = LoadTemplate(delivery.TemplatePath);
            string subject = template.Subject
                .Replace(ProviderNamePlaceholder, providerName, StringComparison.Ordinal);
            string body = template.Body
                .Replace(
                    ProviderNamePlaceholder,
                    WebUtility.HtmlEncode(providerName),
                    StringComparison.Ordinal);
            EmailAttachmentRequest bill = await BuildAttachmentAsync(attachment, cancellationToken);
            logger.Info(
                BillRelayOperation.ProcessAttachment,
                OperationStatus.InProgress,
                new LogInfo(BillRelayInfoKey.FileName, bill.FileName),
                new LogInfo(BillRelayInfoKey.ContentLength, bill.Content.Length),
                new LogInfo(BillRelayInfoKey.EncodedContentLength, bill.Content.Length));
            cancellationToken.ThrowIfCancellationRequested();
            await client.SendEmail(
                delivery.SenderName,
                delivery.Recipient,
                subject,
                body,
                [bill]);
        }

        private static async Task<EmailAttachmentRequest> BuildAttachmentAsync(
            MimePart attachment,
            CancellationToken cancellationToken)
        {
            using MemoryStream content = new();
            await attachment.Content.DecodeToAsync(content, cancellationToken);

            string fileName = attachment.FileName;

            if (string.IsNullOrWhiteSpace(fileName))
            {
                fileName = "bill.pdf";
            }

            return new()
            {
                FileName = Path.GetFileName(fileName.Replace('\\', '/')),
                Content = Convert.ToBase64String(content.ToArray())
            };
        }

        private static EmailTemplateDataObject LoadTemplate(string path)
        {
            string templatePath = Path.GetFullPath(path, AppContext.BaseDirectory);

            if (File.ReadAllText(templatePath).Contains("<!DOCTYPE", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Email templates must not contain a document type declaration.");
            }

            XmlFileObject<EmailTemplateDataObject> templateFile = new();
            EmailTemplateDataObject template = templateFile.Read(templatePath);
            ValidateElement(template.Subject, nameof(template.Subject));
            ValidateElement(template.Body, nameof(template.Body));

            return template;
        }

        private static void ValidateElement(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidDataException($"The email template requires a non-empty '{name}' element.");
            }
        }
    }
}