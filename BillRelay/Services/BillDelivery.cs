using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Options;

using MimeKit;

using NuciLog.Core;

using BillRelay.Configuration;
using BillRelay.Logging;

namespace BillRelay.Services
{
    public sealed class BillDelivery(
        IAttachmentProcessor attachmentProcessor,
        IBillSender billSender,
        IStateStore stateStore,
        ILogger logger,
        IOptions<BillRelaySettings> settings) : IBillDelivery
    {
        public async Task DeliverAsync(
            MimePart attachment,
            string attachmentId,
            string messageId,
            string providerName,
            CancellationToken cancellationToken)
        {
            string deliveryId = "delivery:" + attachmentId;

            if (await stateStore.IsProcessedAsync(deliveryId, cancellationToken))
            {
                logger.Info(
                    BillRelayOperation.ProcessAttachment,
                    OperationStatus.Success,
                    new LogInfo(BillRelayInfoKey.AttachmentId, attachmentId),
                    new LogInfo(BillRelayInfoKey.Reason, "The delivery was already completed."));
                return;
            }

            try
            {
                await attachmentProcessor.ProcessAsync(
                    attachment,
                    attachmentId,
                    providerName,
                    cancellationToken);
                logger.Info(
                    BillRelayOperation.WriteAttachment,
                    OperationStatus.Success,
                    new LogInfo(BillRelayInfoKey.AttachmentId, attachmentId));

                await billSender.SendAsync(attachment, providerName, cancellationToken);
                logger.Info(
                    BillRelayOperation.ProcessAttachment,
                    OperationStatus.Success,
                    "The notification email was sent.",
                    new LogInfo(BillRelayInfoKey.AttachmentId, attachmentId),
                    new LogInfo(BillRelayInfoKey.ProviderName, providerName),
                    new LogInfo(BillRelayInfoKey.Recipient, settings.Value.Delivery.Recipient));
                await stateStore.MarkProcessedAsync(deliveryId, messageId, CancellationToken.None);
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
                    new LogInfo(BillRelayInfoKey.AttachmentId, attachmentId),
                    new LogInfo(BillRelayInfoKey.ProviderName, providerName),
                    new LogInfo(BillRelayInfoKey.Recipient, settings.Value.Delivery.Recipient),
                    new LogInfo(BillRelayInfoKey.Reason, "The notification email could not be sent."));
                throw;
            }
        }
    }
}