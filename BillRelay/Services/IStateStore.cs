using System;
using System.Threading;
using System.Threading.Tasks;

namespace BillRelay.Services
{
    /// <summary>
    /// Stores and retrieves processing state for deduplication.
    /// </summary>
    public interface IStateStore : IDisposable
    {
        /// <summary>
        /// Initializes the state store.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        Task InitializeAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Loads the IMAP checkpoint.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The persisted checkpoint, or a new checkpoint.</returns>
        Task<ImapCheckpoint> GetCheckpointAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Saves the IMAP checkpoint.
        /// </summary>
        /// <param name="checkpoint">The checkpoint to save.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        Task SaveCheckpointAsync(ImapCheckpoint checkpoint, CancellationToken cancellationToken);

        /// <summary>
        /// Checks if an attachment has already been processed.
        /// </summary>
        /// <param name="attachmentId">The attachment identifier.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>True if processed, false otherwise.</returns>
        Task<bool> IsProcessedAsync(string attachmentId, CancellationToken cancellationToken);

        /// <summary>
        /// Marks an attachment as processed.
        /// </summary>
        /// <param name="attachmentId">The attachment identifier.</param>
        /// <param name="messageId">The message identifier.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        Task MarkProcessedAsync(
            string attachmentId,
            string messageId,
            CancellationToken cancellationToken);

        /// <summary>
        /// Applies the retention policy to remove old records.
        /// </summary>
        /// <param name="maximumAgeDays">The maximum age in days.</param>
        /// <param name="maximumFiles">The maximum number of files to retain.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        Task ApplyRetentionAsync(
            int maximumAgeDays,
            int maximumFiles,
            CancellationToken cancellationToken);
    }
}