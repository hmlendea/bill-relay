using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Options;

using NuciDAL.Repositories;

using NuciLog.Core;

using BillRelay.Configuration;
using BillRelay.Logging;

namespace BillRelay.Services
{
    public sealed class FileStateStore(
        ILogger logger,
        IOptions<BillRelaySettings> settings) : IStateStore
    {
        private JsonRepository<ProcessedAttachmentRecord>? repository;

        public async Task InitializeAsync(CancellationToken cancellationToken)
        {
            string stateFilePath = settings.Value.Storage.StateFilePath;
            string? directory = Path.GetDirectoryName(stateFilePath);

            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (!File.Exists(stateFilePath))
            {
                await File.WriteAllTextAsync(stateFilePath, "[]", cancellationToken);
            }

            repository = new(stateFilePath);
            logger.Debug(
                BillRelayOperation.InitializeStateStore,
                OperationStatus.Success,
                new LogInfo(BillRelayInfoKey.StateFilePath, stateFilePath));
        }

        public Task<bool> IsProcessedAsync(string attachmentId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (repository is null)
            {
                throw new InvalidOperationException("The state store must be initialized before checking attachments.");
            }

            return Task.FromResult(repository.ContainsId(attachmentId));
        }

        public Task MarkProcessedAsync(
            string attachmentId,
            string messageId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (repository is null)
            {
                throw new InvalidOperationException("The state store must be initialized before recording attachments.");
            }

            ProcessedAttachmentRecord record = new()
            {
                Id = attachmentId,
                MessageId = messageId,
                ProcessedAt = DateTimeOffset.UtcNow
            };

            repository.TryAdd(record);
            repository.SaveChanges();

            return Task.CompletedTask;
        }

        public Task ApplyRetentionAsync(
            int maximumAgeDays,
            int maximumFiles,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (repository is null)
            {
                throw new InvalidOperationException("The state store must be initialized before applying retention.");
            }

            List<ProcessedAttachmentRecord> records = repository.GetAll().ToList();
            List<string> identifiersToRemove = GetExpiredIdentifiers(records, maximumAgeDays).ToList();
            HashSet<string> expiredIdentifiers = identifiersToRemove.ToHashSet(StringComparer.Ordinal);
            records = records.Where(record => !expiredIdentifiers.Contains(record.Id)).ToList();
            identifiersToRemove.AddRange(GetExcessIdentifiers(records, maximumFiles));
            RemoveRecords(identifiersToRemove);

            return Task.CompletedTask;
        }

        public void Dispose() => repository = null;

        private static IEnumerable<string> GetExpiredIdentifiers(
            IEnumerable<ProcessedAttachmentRecord> records,
            int maximumAgeDays)
        {
            if (maximumAgeDays <= 0)
            {
                return [];
            }

            DateTimeOffset cutoffDate = DateTimeOffset.UtcNow.AddDays(-maximumAgeDays);

            return
                records.Where(record => record.ProcessedAt < cutoffDate).Select(record => record.Id);
        }

        private static IEnumerable<string> GetExcessIdentifiers(
            IEnumerable<ProcessedAttachmentRecord> records,
            int maximumFiles)
        {
            if (maximumFiles <= 0)
            {
                return [];
            }

            ProcessedAttachmentRecord[] remainingRecords = records.ToArray();
            int excessCount = Math.Max(0, remainingRecords.Length - maximumFiles);

            return remainingRecords
                .OrderBy(record => record.ProcessedAt)
                .ThenBy(record => record.Id, StringComparer.Ordinal)
                .Take(excessCount)
                .Select(record => record.Id);
        }

        private void RemoveRecords(IEnumerable<string> identifiers)
        {
            string[] identifiersToRemove = identifiers.Distinct().ToArray();

            foreach (string identifier in identifiersToRemove)
            {
                repository!.TryRemove(identifier);
            }

            if (identifiersToRemove.Length > 0)
            {
                repository!.SaveChanges();
                logger.Debug(
                    BillRelayOperation.ApplyRetention,
                    OperationStatus.Success,
                    new LogInfo(BillRelayInfoKey.RecordCount, identifiersToRemove.Length));
            }
        }
    }
}