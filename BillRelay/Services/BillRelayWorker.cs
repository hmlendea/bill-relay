using System;
using System.Collections.Generic;
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
using BillRelay.Services.Attachments;

namespace BillRelay.Services
{
    public sealed class BillRelayWorker(
        ILogger logger,
        IOptions<BillRelaySettings> settings,
        IStateStore stateStore,
        IBillSender billSender,
        IBillDelivery billDelivery,
        IAttachmentValidator attachmentValidator) : BackgroundService
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
            ImapCheckpoint checkpoint = await stateStore.GetCheckpointAsync(stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    checkpoint = await ProcessInboxSafelyAsync(checkpoint, stoppingToken);
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

        private async Task<ImapCheckpoint> ProcessInboxSafelyAsync(
            ImapCheckpoint checkpoint,
            CancellationToken cancellationToken)
        {
            try
            {
                return await ProcessInboxAsync(checkpoint, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.Error(BillRelayOperation.ProcessInbox, OperationStatus.Failure, exception);
                return checkpoint;
            }
        }

        private async Task<ImapCheckpoint> ProcessInboxAsync(
            ImapCheckpoint checkpoint,
            CancellationToken cancellationToken)
        {
            using ImapClient client = new();

            try
            {
                await ConnectAsync(client, cancellationToken);
                IMailFolder mailbox = await client.GetFolderAsync(Settings.Imap.Mailbox, cancellationToken);
                await mailbox.OpenAsync(FolderAccess.ReadOnly, cancellationToken);
                return await ProcessMailboxAsync(mailbox, checkpoint, cancellationToken);
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

        private async Task<ImapCheckpoint> ProcessMailboxAsync(
            IMailFolder mailbox,
            ImapCheckpoint checkpoint,
            CancellationToken cancellationToken)
        {
            if (!Equals(checkpoint.UidValidity, mailbox.UidValidity))
            {
                checkpoint = new ImapCheckpoint { UidValidity = mailbox.UidValidity };
                await stateStore.SaveCheckpointAsync(checkpoint, cancellationToken);
            }

            DateTimeOffset cutoff = EmailAgePolicy.GetCutoff(
                Settings.Polling,
                DateTimeOffset.UtcNow,
                checkpoint.LastProcessedDate);
            SearchQuery searchQuery = SearchQuery.DeliveredAfter(cutoff.UtcDateTime.Date);
            IList<UniqueId> uniqueIds = await mailbox.SearchAsync(searchQuery, cancellationToken);

            if (Equals(uniqueIds.Count, 0))
            {
                LogMailboxCounts(0);
                return checkpoint;
            }

            IList<IMessageSummary> summaries = await mailbox.FetchAsync(
                uniqueIds,
                MessageSummaryItems.UniqueId | MessageSummaryItems.InternalDate,
                cancellationToken);
            IMessageSummary[] eligibleSummaries = summaries
                .Where(summary =>
                    summary.UniqueId.Id > checkpoint.LastProcessedUid &&
                    EmailAgePolicy.IsEligible(summary.InternalDate, cutoff))
                .ToArray();
            LogMailboxCounts(eligibleSummaries.Length);

            foreach (IMessageSummary summary in eligibleSummaries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                using MimeMessage message = await mailbox.GetMessageAsync(summary.UniqueId, cancellationToken);
                logger.Debug(
                    BillRelayOperation.ProcessInbox,
                    OperationStatus.InProgress,
                    new LogInfo(BillRelayInfoKey.MessageId, summary.UniqueId.Id),
                    new LogInfo(BillRelayInfoKey.Subject, message.Subject),
                    new LogInfo(
                        BillRelayInfoKey.Sender,
                        string.Join(",", message.From.Mailboxes.Select(mailbox => mailbox.Address))));
                await ProcessMessageAsync(
                    message,
                    summary.UniqueId,
                    mailbox.UidValidity,
                    cancellationToken);
                checkpoint.LastProcessedUid = summary.UniqueId.Id;
                checkpoint.LastProcessedDate = summary.InternalDate;
                await stateStore.SaveCheckpointAsync(checkpoint, cancellationToken);
            }

            return checkpoint;
        }

        private void LogMailboxCounts(int eligibleMessageCount)
        {
            logger.Info(
                BillRelayOperation.ProcessInbox,
                OperationStatus.InProgress,
            new LogInfo(BillRelayInfoKey.Reason, "Messages within the checked range."),
            new LogInfo(BillRelayInfoKey.MessageCount, eligibleMessageCount));
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
                logger.Debug(
                    BillRelayOperation.ProcessInbox,
                    OperationStatus.Failure,
                    new LogInfo(BillRelayInfoKey.MessageId, uniqueId.Id),
                    new LogInfo(BillRelayInfoKey.Subject, message.Subject),
                    new LogInfo(BillRelayInfoKey.ProviderName, providerName),
                    new LogInfo(BillRelayInfoKey.Reason, "No configured provider was identified."));
                return;
            }

            string messageId = $"{uniqueIdValidity}:{uniqueId.Id}";
            int attachmentCount = message.Attachments.Count();
            logger.Info(
                BillRelayOperation.ProcessInbox,
                OperationStatus.Success,
                new LogInfo(BillRelayInfoKey.MessageId, uniqueId.Id),
                new LogInfo(BillRelayInfoKey.ProviderName, providerName),
                new LogInfo(BillRelayInfoKey.AttachmentCount, attachmentCount));

            foreach (MimeEntity attachment in message.Attachments)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (attachment is not MimePart billAttachment)
                {
                    logger.Error(
                        BillRelayOperation.ValidateAttachment,
                        OperationStatus.Failure,
                        new LogInfo(BillRelayInfoKey.Reason, "The attachment is not a MIME part."));

                    continue;
                }

                string? rejectionReason = GetAttachmentRejectionReason(billAttachment, providerName);

                if (rejectionReason is not null)
                {
                    logger.Error(
                        BillRelayOperation.ValidateAttachment,
                        OperationStatus.Failure,
                        new LogInfo(BillRelayInfoKey.FileName, billAttachment.FileName),
                        new LogInfo(BillRelayInfoKey.ProviderName, providerName),
                        new LogInfo(BillRelayInfoKey.Reason, rejectionReason));

                    continue;
                }

                await DeliverAttachmentAsync(
                    billAttachment,
                    messageId,
                    providerName,
                    cancellationToken);
            }
        }

        private string? GetAttachmentRejectionReason(MimePart attachment, string providerName)
        {
            if (!BillProviderIdentification.IsBillAttachment(attachment, providerName))
            {
                return $"The filename '{attachment.FileName}' does not match the {providerName} " +
                    "invoice attachment pattern, or the MIME type is not application/pdf.";
            }

            return attachmentValidator.GetFailureReason(attachment);
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