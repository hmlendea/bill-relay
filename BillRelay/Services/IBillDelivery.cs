using System.Threading;
using System.Threading.Tasks;

using MimeKit;

namespace BillRelay.Services
{
    public interface IBillDelivery
    {
        Task DeliverAsync(
            MimePart attachment,
            string attachmentId,
            string messageId,
            string providerName,
            CancellationToken cancellationToken);
    }
}