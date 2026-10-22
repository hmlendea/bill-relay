using System.Threading;
using System.Threading.Tasks;

namespace BillRelay.Services
{
    /// <summary>
    /// Stores and retrieves processing state for deduplication.
    /// </summary>
    public interface IStateStore
    {
    /// <summary>
    /// Initializes the state store.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task InitializeAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Checks if an attachment has already been processed.
    /// </summary>
    /// <param name="attachmentId">The attachment identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if processed, false otherwise.</returns>
    Task<bool> IsProcessedAsync(string attachmentId, CancellationToken cancellationToken);

    /// <summary>
    /// Marks an attachment as processed.
    /// </summary>
    /// <param name="attachmentId">The attachment identifier.</param>
    /// <param name="messageId">The message identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task MarkProcessedAsync(string attachmentId, string messageId, CancellationToken cancellationToken);

    /// <summary>
    /// Applies retention policy to remove old records.
    /// </summary>
    /// <param name="maxAgeDays">Maximum age in days.</param>
    /// <param name="maxFiles">Maximum number of files to retain.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task ApplyRetentionAsync(int maxAgeDays, int maxFiles, CancellationToken cancellationToken);
    }
}