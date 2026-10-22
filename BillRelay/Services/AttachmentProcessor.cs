using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using BillRelay.Configuration;

namespace BillRelay.Services
{
    public sealed class AttachmentProcessor(
        ILogger<AttachmentProcessor> logger,
        IOptions<BillRelaySettings> settings) : IAttachmentProcessor
    {
        private readonly BillRelaySettings settings = settings.Value;

        public Task ProcessAsync(
            MimeEntity attachment,
            string attachmentId,
            CancellationToken cancellationToken) =>
            ProcessAsync(attachment, attachmentId, string.Empty, cancellationToken);

        public async Task ProcessAsync(
            MimeEntity attachment,
            string attachmentId,
            string providerName,
            CancellationToken cancellationToken)
        {
            if (attachment is not MimePart part)
            {
                throw new InvalidOperationException(
                    $"Attachment with identifier '{attachmentId}' is not a MimePart.");
            }

            string downloadDirectory = settings.Storage.DownloadDirectory;
            Directory.CreateDirectory(downloadDirectory);

            string fileName = SanitizeFileName(part.FileName ?? $"attachment_{attachmentId}.pdf");

            if (!string.IsNullOrWhiteSpace(providerName))
            {
                fileName = SanitizeFileName(providerName + "_" + fileName);
            }

            string filePath = Path.Combine(downloadDirectory, fileName);

            string temporaryFilePath = Path.Combine(downloadDirectory, $"{Guid.NewGuid():N}.tmp");

            try
            {
                using FileStream fileStream = new(
                    temporaryFilePath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None);
                await part.Content.DecodeToAsync(fileStream, cancellationToken);
                await fileStream.FlushAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                if (File.Exists(temporaryFilePath))
                {
                    File.Delete(temporaryFilePath);
                }

                logger.LogError(ex, "Failed to write attachment {AttachmentIdentifier}.", attachmentId);
                throw;
            }

            try
            {
                if (File.Exists(filePath))
                {
                    string existingHash = await ComputeFileHashAsync(filePath, cancellationToken);
                    string newHash = await ComputeFileHashAsync(temporaryFilePath, cancellationToken);

                    if (string.Equals(existingHash, newHash, StringComparison.Ordinal))
                    {
                        logger.LogDebug("Identical file already exists: {FilePath}", filePath);
                        File.Delete(temporaryFilePath);
                        return;
                    }

                    filePath = GetUniqueFilePath(filePath);
                }

                File.Move(temporaryFilePath, filePath);
                logger.LogInformation("Saved attachment to: {FilePath}", filePath);
            }
            catch (Exception ex)
            {
                if (File.Exists(temporaryFilePath))
                {
                    File.Delete(temporaryFilePath);
                }

                logger.LogError(ex, "Failed to finalize attachment {AttachmentIdentifier}.", attachmentId);
                throw;
            }
        }

        private static string SanitizeFileName(string fileName)
        {
            HashSet<char> invalidCharacters = new(Path.GetInvalidFileNameChars())
            {
                '<', '>', ':', '"', '|', '?', '*', '/', '\\'
            };
            string sanitizedFileName = new(fileName.Select(character =>
                invalidCharacters.Contains(character) ? '_' : character).ToArray());

            if (sanitizedFileName.Length > 255)
            {
                return sanitizedFileName[..255];
            }

            return sanitizedFileName;
        }

        private static string GetUniqueFilePath(string filePath)
        {
            string directory = Path.GetDirectoryName(filePath) ?? ".";
            string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(filePath);
            string extension = Path.GetExtension(filePath);
            int counter = 1;

            string uniqueFilePath;
            do
            {
                uniqueFilePath = Path.Combine(
                    directory,
                    $"{fileNameWithoutExtension}_{counter}{extension}");
                counter += 1;
            }
            while (File.Exists(uniqueFilePath));

            return uniqueFilePath;
        }

        private static async Task<string> ComputeFileHashAsync(string filePath, CancellationToken cancellationToken)
        {
            using SHA256 sha256 = SHA256.Create();
            using FileStream stream = new(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            byte[] hash = await sha256.ComputeHashAsync(stream, cancellationToken);

            return Convert.ToHexString(hash);
        }
    }
}