using System.Threading;
using System.Threading.Tasks;
using MimeKit;

namespace BillRelay.Services
{
    /// <summary>
    /// Processes email attachments for bill relay.
    /// </summary>
    public interface IAttachmentProcessor
    {
    /// <summary>
    /// Processes an attachment and saves it to the configured download directory.
    /// </summary>
    /// <param name="attachment">The attachment to process.</param>
    /// <param name="attachmentId">Unique identifier for the attachment.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task ProcessAsync(MimeEntity attachment, string attachmentId, CancellationToken cancellationToken);

        /// <summary>
        /// Saves an attachment with its identified provider name.
        /// </summary>
        Task ProcessAsync(
            MimeEntity attachment,
            string attachmentId,
            string providerName,
            CancellationToken cancellationToken);
    }
}