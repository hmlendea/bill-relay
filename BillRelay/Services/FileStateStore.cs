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
using BillRelay.DataAccess.DataObjects;
using BillRelay.Logging;

namespace BillRelay.Services
{
    public sealed class FileStateStore(
        ILogger logger,
        IOptions<BillRelaySettings> settings) : IStateStore
    {
        private JsonRepository<ProcessedAttachmentDataObject>? repository;

        private JsonRepository<ProcessedAttachmentDataObject> Repository
        {
            get
            {
                if (repository is null)
                {
                    throw new InvalidOperationException(
                        "The state store must be initialized before accessing attachment records.");
                }

                return repository;
            }
        }

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

            return Task.FromResult(Repository.ContainsId(attachmentId));
        }

        public Task MarkProcessedAsync(
            string attachmentId,
            string messageId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            JsonRepository<ProcessedAttachmentDataObject> stateRepository = Repository;
            ProcessedAttachmentDataObject record = new()
            {
                Id = attachmentId,
                MessageId = messageId,
                ProcessedAt = DateTimeOffset.UtcNow
            };

            stateRepository.TryAdd(record);
            stateRepository.SaveChanges();

            return Task.CompletedTask;
        }

        public Task ApplyRetentionAsync(
            int maximumAgeDays,
            int maximumFiles,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            List<ProcessedAttachmentDataObject> records = Repository.GetAll().ToList();
            List<string> identifiersToRemove = GetExpiredIdentifiers(records, maximumAgeDays).ToList();
            HashSet<string> expiredIdentifiers = identifiersToRemove.ToHashSet(StringComparer.Ordinal);
            records = records.Where(record => !expiredIdentifiers.Contains(record.Id)).ToList();
            identifiersToRemove.AddRange(GetExcessIdentifiers(records, maximumFiles));
            RemoveRecords(identifiersToRemove);

            return Task.CompletedTask;
        }

        public void Dispose() => repository = null;

        private static IEnumerable<string> GetExpiredIdentifiers(
            IEnumerable<ProcessedAttachmentDataObject> records,
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
            IEnumerable<ProcessedAttachmentDataObject> records,
            int maximumFiles)
        {
            if (maximumFiles <= 0)
            {
                return [];
            }

            ProcessedAttachmentDataObject[] remainingRecords = records.ToArray();
            int excessCount = Math.Max(0, remainingRecords.Length - maximumFiles);

            return
                remainingRecords
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
                Repository.TryRemove(identifier);
            }

            if (identifiersToRemove.Length > 0)
            {
                Repository.SaveChanges();
                logger.Debug(
                    BillRelayOperation.ApplyRetention,
                    OperationStatus.Success,
                    new LogInfo(BillRelayInfoKey.RecordCount, identifiersToRemove.Length));
            }
        }
    }
}