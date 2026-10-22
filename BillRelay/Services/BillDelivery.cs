using System.Threading;
using System.Threading.Tasks;

using MimeKit;

namespace BillRelay.Services
{
    public sealed class BillDelivery(
        IAttachmentProcessor attachmentProcessor,
        IBillSender billSender,
        IStateStore stateStore) : IBillDelivery
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
                return;
            }

            await attachmentProcessor.ProcessAsync(
                attachment,
                attachmentId,
                providerName,
                cancellationToken);
            await billSender.SendAsync(attachment, providerName, cancellationToken);
            await stateStore.MarkProcessedAsync(deliveryId, messageId, CancellationToken.None);
        }
    }
}