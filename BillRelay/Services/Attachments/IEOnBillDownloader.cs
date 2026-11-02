using System.Threading;
using System.Threading.Tasks;

using MimeKit;

namespace BillRelay.Services.Attachments
{
    public interface IEOnBillDownloader
    {
        Task<MimePart?> DownloadBillAsync(
            MimeMessage message,
            string messageId,
            CancellationToken cancellationToken);
    }
}