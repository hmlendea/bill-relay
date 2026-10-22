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

using BillRelay.Configuration;

namespace BillRelay.Services
{
    public sealed class BillSender(
        INuciNotificationsClient client,
        IOptions<BillRelaySettings> settings) : IBillSender
    {
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
                .Replace("{ProviderName}", providerName, StringComparison.Ordinal);
            string body = template.Body
                .Replace("{ProviderName}", WebUtility.HtmlEncode(providerName), StringComparison.Ordinal);
            using MemoryStream content = new();
            await attachment.Content.DecodeToAsync(content, cancellationToken);
            EmailAttachmentRequest bill = new()
            {
                FileName = Path.GetFileName(attachment.FileName.Replace('\\', '/')),
                Content = Convert.ToBase64String(content.ToArray())
            };
            cancellationToken.ThrowIfCancellationRequested();
            await client.SendEmail(delivery.SenderName, delivery.Recipient, subject, body, [bill]);
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