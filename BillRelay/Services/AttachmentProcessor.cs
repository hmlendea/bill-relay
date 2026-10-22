using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Options;

using MimeKit;

using NuciLog.Core;

using BillRelay.Configuration;
using BillRelay.Logging;

namespace BillRelay.Services
{
    public sealed class AttachmentProcessor(
        ILogger logger,
        IOptions<BillRelaySettings> settings) : IAttachmentProcessor
    {
        private static readonly HashSet<char> invalidFileNameCharacters =
            [.. Path.GetInvalidFileNameChars(), '<', '>', ':', '"', '|', '?', '*', '/', '\\'];

        private static int MaximumFileNameLength => 255;

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

            string downloadDirectory = settings.Value.Storage.DownloadDirectory;
            Directory.CreateDirectory(downloadDirectory);
            string fileName = GetFileName(part, attachmentId, providerName);
            string filePath = Path.Combine(downloadDirectory, fileName);
            string temporaryFilePath = Path.Combine(downloadDirectory, $"{Guid.NewGuid():N}.tmp");
            await WriteTemporaryFileAsync(
                part,
                temporaryFilePath,
                attachmentId,
                cancellationToken);
            await FinalizeFileAsync(
                filePath,
                temporaryFilePath,
                attachmentId,
                cancellationToken);
        }

        private static string GetFileName(
            MimePart part,
            string attachmentId,
            string providerName)
        {
            string originalFileName = $"attachment_{attachmentId}.pdf";

            if (part.FileName is not null)
            {
                originalFileName = part.FileName;
            }

            string fileName = SanitizeFileName(originalFileName);

            if (!string.IsNullOrWhiteSpace(providerName))
            {
                fileName = SanitizeFileName(providerName + "_" + fileName);
            }

            return fileName;
        }

        private async Task WriteTemporaryFileAsync(
            MimePart part,
            string temporaryFilePath,
            string attachmentId,
            CancellationToken cancellationToken)
        {
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
            catch (Exception exception)
            {
                if (File.Exists(temporaryFilePath))
                {
                    File.Delete(temporaryFilePath);
                }

                logger.Error(
                    BillRelayOperation.WriteAttachment,
                    OperationStatus.Failure,
                    exception,
                    new LogInfo(BillRelayInfoKey.AttachmentId, attachmentId));

                throw;
            }
        }

        private async Task FinalizeFileAsync(
            string filePath,
            string temporaryFilePath,
            string attachmentId,
            CancellationToken cancellationToken)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    string existingHash = await ComputeFileHashAsync(filePath, cancellationToken);
                    string newHash = await ComputeFileHashAsync(temporaryFilePath, cancellationToken);

                    if (string.Equals(existingHash, newHash, StringComparison.Ordinal))
                    {
                        logger.Debug(
                            BillRelayOperation.SaveAttachment,
                            OperationStatus.Success,
                            new LogInfo(BillRelayInfoKey.FilePath, filePath),
                            new LogInfo(BillRelayInfoKey.Reason, "An identical file already exists."));
                        File.Delete(temporaryFilePath);

                        return;
                    }

                    filePath = GetUniqueFilePath(filePath);
                }

                File.Move(temporaryFilePath, filePath);
                logger.Info(
                    BillRelayOperation.SaveAttachment,
                    OperationStatus.Success,
                    new LogInfo(BillRelayInfoKey.FilePath, filePath));
            }
            catch (Exception exception)
            {
                if (File.Exists(temporaryFilePath))
                {
                    File.Delete(temporaryFilePath);
                }

                logger.Error(
                    BillRelayOperation.SaveAttachment,
                    OperationStatus.Failure,
                    exception,
                    new LogInfo(BillRelayInfoKey.AttachmentId, attachmentId));

                throw;
            }
        }

        private static string SanitizeFileName(string fileName)
        {
            string sanitizedFileName = new(fileName.Select(SanitizeCharacter).ToArray());

            if (sanitizedFileName.Length > MaximumFileNameLength)
            {
                return sanitizedFileName[..MaximumFileNameLength];
            }

            return sanitizedFileName;
        }

        private static char SanitizeCharacter(char character)
        {
            if (invalidFileNameCharacters.Contains(character))
            {
                return '_';
            }

            return character;
        }

        private static string GetUniqueFilePath(string filePath)
        {
            string directory = ".";
            string? parentDirectory = Path.GetDirectoryName(filePath);

            if (parentDirectory is not null)
            {
                directory = parentDirectory;
            }

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

        private static async Task<string> ComputeFileHashAsync(
            string filePath,
            CancellationToken cancellationToken)
        {
            using SHA256 sha256 = SHA256.Create();
            using FileStream stream = new(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            byte[] hash = await sha256.ComputeHashAsync(stream, cancellationToken);

            return Convert.ToHexString(hash);
        }
    }
}