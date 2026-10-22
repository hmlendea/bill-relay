using System.Threading;
using System.Threading.Tasks;

using MimeKit;

namespace BillRelay.Services
{
    public interface IBillSender
    {
        void ValidateConfiguration();

        Task SendAsync(
            MimePart attachment,
            string providerName,
            CancellationToken cancellationToken);
    }
}