using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;

using MimeKit;

using NuciLog.Core;

using BillRelay.Configuration;
using BillRelay.Logging;

namespace BillRelay.Services
{
    public sealed class BillRelayWorker(
        ILogger logger,
        IOptions<BillRelaySettings> settings,
        IStateStore stateStore,
        IBillSender billSender,
        IBillDelivery billDelivery) : BackgroundService
    {
        private static int AttachmentIdentifierLength => 16;

        private BillRelaySettings Settings => settings.Value;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            logger.Info(BillRelayOperation.RunWorker, OperationStatus.Started);

            billSender.ValidateConfiguration();
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(Settings.Polling.IntervalSeconds);
            _ = EmailAgePolicy.GetCutoff(Settings.Polling, DateTimeOffset.UtcNow);
            await stateStore.InitializeAsync(stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessInboxSafelyAsync(stoppingToken);
                    await Task.Delay(
                        TimeSpan.FromSeconds(Settings.Polling.IntervalSeconds),
                        stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }

            logger.Info(BillRelayOperation.RunWorker, OperationStatus.Success);
        }

        private async Task ProcessInboxSafelyAsync(CancellationToken cancellationToken)
        {
            try
            {
                await ProcessInboxAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.Error(BillRelayOperation.ProcessInbox, OperationStatus.Failure, exception);
            }
        }

        private async Task ProcessInboxAsync(CancellationToken cancellationToken)
        {
            using ImapClient client = new();

            try
            {
                await ConnectAsync(client, cancellationToken);
                IMailFolder mailbox = await client.GetFolderAsync(Settings.Imap.Mailbox, cancellationToken);
                await mailbox.OpenAsync(FolderAccess.ReadOnly, cancellationToken);
                await ProcessMailboxAsync(mailbox, cancellationToken);
            }
            finally
            {
                if (client.IsConnected)
                {
                    await client.DisconnectAsync(true, cancellationToken);
                }
            }
        }

        private async Task ConnectAsync(ImapClient client, CancellationToken cancellationToken)
        {
            if (!Settings.Imap.IsCertificateValidationEnabled)
            {
                client.ServerCertificateValidationCallback = (_, _, _, _) => true;
            }

            await client.ConnectAsync(
                Settings.Imap.Server,
                Settings.Imap.Port,
                Settings.Imap.IsSslEnabled,
                cancellationToken);
            await client.AuthenticateAsync(
                Settings.Imap.Username,
                Settings.Imap.Password,
                cancellationToken);
        }

        private async Task ProcessMailboxAsync(
            IMailFolder mailbox,
            CancellationToken cancellationToken)
        {
            DateTimeOffset cutoff = EmailAgePolicy.GetCutoff(Settings.Polling, DateTimeOffset.UtcNow);
            SearchQuery searchQuery = SearchQuery.DeliveredAfter(cutoff.UtcDateTime.Date);
            IList<UniqueId> uniqueIds = await mailbox.SearchAsync(searchQuery, cancellationToken);
            logger.Debug(
                BillRelayOperation.ProcessInbox,
                OperationStatus.InProgress,
                new LogInfo(BillRelayInfoKey.MessageCount, uniqueIds.Count));

            if (Equals(uniqueIds.Count, 0))
            {
                return;
            }

            IList<IMessageSummary> summaries = await mailbox.FetchAsync(
                uniqueIds,
                MessageSummaryItems.UniqueId | MessageSummaryItems.InternalDate,
                cancellationToken);

            foreach (IMessageSummary summary in summaries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!EmailAgePolicy.IsEligible(summary.InternalDate, cutoff))
                {
                    continue;
                }

                using MimeMessage message = await mailbox.GetMessageAsync(summary.UniqueId, cancellationToken);
                await ProcessMessageAsync(
                    message,
                    summary.UniqueId,
                    mailbox.UidValidity,
                    cancellationToken);
            }
        }

        private async Task ProcessMessageAsync(
            MimeMessage message,
            UniqueId uniqueId,
            uint uniqueIdValidity,
            CancellationToken cancellationToken)
        {
            string? providerName = BillProviderIdentification.IdentifyProvider(message);

            if (providerName is null ||
                !Settings.Filters.Providers.Contains(providerName, StringComparer.OrdinalIgnoreCase))
            {
                return;
            }

            string messageId = $"{uniqueIdValidity}:{uniqueId.Id}";

            foreach (MimeEntity attachment in message.Attachments)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (attachment is not MimePart billAttachment ||
                    !BillProviderIdentification.IsBillAttachment(billAttachment, providerName) ||
                    !IsPermittedAttachment(attachment))
                {
                    logger.Debug(
                        BillRelayOperation.ValidateAttachment,
                        OperationStatus.Failure,
                        new LogInfo(BillRelayInfoKey.FileName, attachment.ContentDisposition?.FileName),
                        new LogInfo(BillRelayInfoKey.Reason, "The attachment is not permitted."));

                    continue;
                }

                await DeliverAttachmentAsync(
                    billAttachment,
                    messageId,
                    providerName,
                    cancellationToken);
            }
        }

        private async Task DeliverAttachmentAsync(
            MimePart attachment,
            string messageId,
            string providerName,
            CancellationToken cancellationToken)
        {
            string attachmentId = ComputeAttachmentId(messageId, attachment);

            try
            {
                await billDelivery.DeliverAsync(
                    attachment,
                    attachmentId,
                    messageId,
                    providerName,
                    cancellationToken);
                logger.Info(
                    BillRelayOperation.ProcessAttachment,
                    OperationStatus.Success,
                    new LogInfo(BillRelayInfoKey.FileName, attachment.ContentDisposition?.FileName),
                    new LogInfo(BillRelayInfoKey.AttachmentId, attachmentId));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.Error(
                    BillRelayOperation.ProcessAttachment,
                    OperationStatus.Failure,
                    exception,
                    new LogInfo(BillRelayInfoKey.AttachmentId, attachmentId));
            }
        }

        private bool IsPermittedAttachment(MimeEntity attachment)
        {
            if (attachment is not MimePart part)
            {
                return false;
            }

            string mimeType = part.ContentType.MimeType;

            if (!Settings.Attachments.PermittedMimeTypes.Contains(mimeType))
            {
                return false;
            }

            long contentLength = 0;

            if (part.Content?.Stream is not null)
            {
                contentLength = part.Content.Stream.Length;
            }

            if (contentLength > Settings.Attachments.MaximumSizeBytes)
            {
                logger.Warn(
                    BillRelayOperation.ValidateAttachment,
                    OperationStatus.Failure,
                    new LogInfo(BillRelayInfoKey.ContentLength, contentLength),
                    new LogInfo(BillRelayInfoKey.MaximumSizeBytes, Settings.Attachments.MaximumSizeBytes),
                    new LogInfo(BillRelayInfoKey.Reason, "The attachment exceeds the maximum size."));

                return false;
            }

            if (!ValidatePdfSignature(part))
            {
                logger.Warn(
                    BillRelayOperation.ValidateAttachment,
                    OperationStatus.Failure,
                    new LogInfo(BillRelayInfoKey.FileName, part.FileName),
                    new LogInfo(BillRelayInfoKey.Reason, "The attachment failed PDF signature validation."));

                return false;
            }

            return true;
        }

        private static bool ValidatePdfSignature(MimePart part)
        {
            using MemoryStream stream = new();
            part.Content.DecodeTo(stream);
            byte[] bytes = stream.ToArray();

            return bytes.AsSpan().StartsWith("%PDF-"u8);
        }

        private static string ComputeAttachmentId(string messageId, MimeEntity attachment)
        {
            string fileName = "unknown";

            if (attachment.ContentType.Name is not null)
            {
                fileName = attachment.ContentType.Name;
            }

            if (attachment.ContentDisposition?.FileName is not null)
            {
                fileName = attachment.ContentDisposition.FileName;
            }

            string input = $"{messageId}:{fileName}";
            using SHA256 sha256 = SHA256.Create();
            byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));

            return Convert.ToHexString(hash)[..AttachmentIdentifierLength];
        }
    }
}