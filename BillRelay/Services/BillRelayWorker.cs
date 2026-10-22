using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using BillRelay.Configuration;

namespace BillRelay.Services
{
    public sealed class BillRelayWorker(
        ILogger<BillRelayWorker> logger,
        IOptions<BillRelaySettings> settings,
        IStateStore stateStore,
        IBillSender billSender,
        IBillDelivery billDelivery) : BackgroundService
    {
        private readonly BillRelaySettings settings = settings.Value;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            logger.LogInformation("BillRelay worker starting");

            billSender.ValidateConfiguration();
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(settings.Polling.IntervalSeconds);
            _ = EmailAgePolicy.GetCutoff(settings.Polling, DateTimeOffset.UtcNow);
            await stateStore.InitializeAsync(stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessInboxAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, "Error processing inbox.");
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(settings.Polling.IntervalSeconds), stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }

            logger.LogInformation("BillRelay worker stopping");
        }

        private async Task ProcessInboxAsync(CancellationToken cancellationToken)
        {
            using var client = new ImapClient();

            try
            {
                if (!settings.Imap.ValidateCertificates)
                {
                    client.ServerCertificateValidationCallback = (_, _, _, _) => true;
                }

                await client.ConnectAsync(settings.Imap.Server, settings.Imap.Port, settings.Imap.UseSsl, cancellationToken);

                await client.AuthenticateAsync(settings.Imap.Username, settings.Imap.Password, cancellationToken);

                IMailFolder mailbox = await client.GetFolderAsync(
                    settings.Imap.Mailbox,
                    cancellationToken);
                await mailbox.OpenAsync(FolderAccess.ReadOnly, cancellationToken);

                DateTimeOffset cutoff = EmailAgePolicy.GetCutoff(settings.Polling, DateTimeOffset.UtcNow);
                SearchQuery searchQuery = SearchQuery.DeliveredAfter(cutoff.UtcDateTime.Date);
                var uids = await mailbox.SearchAsync(searchQuery, cancellationToken);

                logger.LogDebug("Found {Count} candidate messages", uids.Count);

                if (uids.Count == 0)
                {
                    return;
                }

                var summaries = await mailbox.FetchAsync(
                    uids,
                    MessageSummaryItems.UniqueId | MessageSummaryItems.InternalDate,
                    cancellationToken);

                foreach (IMessageSummary summary in summaries)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (!EmailAgePolicy.IsEligible(summary.InternalDate, cutoff))
                    {
                        continue;
                    }

                    MimeMessage message = await mailbox.GetMessageAsync(summary.UniqueId, cancellationToken);
                    await ProcessMessageAsync(message, summary.UniqueId, mailbox.UidValidity, cancellationToken);
                }
            }
            finally
            {
                if (client.IsConnected)
                {
                    await client.DisconnectAsync(true, cancellationToken);
                }
            }
        }

        private async Task ProcessMessageAsync(
            MimeMessage message,
            UniqueId uid,
            uint uidValidity,
            CancellationToken cancellationToken)
        {
            string? providerName = BillProviderIdentification.IdentifyProvider(message);

            if (providerName is null ||
                !settings.Filters.Providers.Contains(providerName, StringComparer.OrdinalIgnoreCase))
            {
                return;
            }

            string messageId = $"{uidValidity}:{uid.Id}";

            foreach (var attachment in message.Attachments)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (attachment is not MimePart billAttachment ||
                    !BillProviderIdentification.IsBillAttachment(billAttachment, providerName) ||
                    !IsPermittedAttachment(attachment))
                {
                    logger.LogDebug("Skipping non-permitted attachment: {FileName}", attachment.ContentDisposition?.FileName);
                    continue;
                }

                string attachmentId = ComputeAttachmentId(messageId, attachment);
                try
                {
                    await billDelivery.DeliverAsync(
                        billAttachment,
                        attachmentId,
                        messageId,
                        providerName,
                        cancellationToken);
                    logger.LogInformation("Processed attachment: {FileName} ({AttachmentId})", attachment.ContentDisposition?.FileName, attachmentId);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    logger.LogError(
                        exception,
                        "Failed to process attachment with identifier {AttachmentIdentifier}.",
                        attachmentId);
                }
            }
        }

        private bool IsPermittedAttachment(MimeEntity attachment)
        {
            if (attachment is not MimePart part)
            {
                return false;
            }

            string mimeType = part.ContentType.MimeType;
            if (!settings.Attachments.PermittedMimeTypes.Contains(mimeType))
            {
                return false;
            }

            long contentLength = part.Content?.Stream?.Length ?? 0;
            if (contentLength > settings.Attachments.MaxSizeBytes)
            {
                logger.LogWarning("Attachment exceeds maximum size: {Size} > {MaxSize}", contentLength, settings.Attachments.MaxSizeBytes);
                return false;
            }

            if (!ValidatePdfSignature(part))
            {
                logger.LogWarning("Attachment failed PDF signature validation: {FileName}", part.FileName);
                return false;
            }

            return true;
        }

        private static bool ValidatePdfSignature(MimePart part)
        {
            using MemoryStream stream = new();
            part.Content.DecodeTo(stream);
            byte[] bytes = stream.ToArray();

            return bytes.Length >= 5
                && bytes[0] == 0x25
                && bytes[1] == 0x50
                && bytes[2] == 0x44
                && bytes[3] == 0x46
                && bytes[4] == 0x2D;
        }

        private static string ComputeAttachmentId(string messageId, MimeEntity attachment)
        {
            string fileName = attachment.ContentDisposition?.FileName
                ?? attachment.ContentType.Name
                ?? "unknown";
            string input = $"{messageId}:{fileName}";
            using SHA256 sha256 = SHA256.Create();
            byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
            return Convert.ToHexString(hash)[..16];
        }
    }
}