using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NuciDAL.Repositories;
using BillRelay.Configuration;

namespace BillRelay.Services
{
    public sealed class FileStateStore(
        ILogger<FileStateStore> logger,
        IOptions<BillRelaySettings> settings) : IStateStore
    {
        private readonly BillRelaySettings settings = settings.Value;
        private JsonRepository<ProcessedAttachmentRecord>? repository;

        public async Task InitializeAsync(CancellationToken cancellationToken)
        {
            string stateFilePath = settings.Storage.StateFilePath;
            string? directory = Path.GetDirectoryName(stateFilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (!File.Exists(stateFilePath))
            {
                await File.WriteAllTextAsync(stateFilePath, "[]", cancellationToken);
            }

            repository = new JsonRepository<ProcessedAttachmentRecord>(stateFilePath);
            logger.LogDebug("State store initialized at: {StateFilePath}", stateFilePath);
        }

        public Task<bool> IsProcessedAsync(string attachmentId, CancellationToken cancellationToken)
        {
            if (repository is null)
            {
                throw new InvalidOperationException("State store not initialized");
            }

            return Task.FromResult(repository.ContainsId(attachmentId));
        }

        public Task MarkProcessedAsync(string attachmentId, string messageId, CancellationToken cancellationToken)
        {
            if (repository is null)
            {
                throw new InvalidOperationException("State store not initialized");
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

        public Task ApplyRetentionAsync(int maxAgeDays, int maxFiles, CancellationToken cancellationToken)
        {
            if (repository is null)
            {
                throw new InvalidOperationException("State store not initialized");
            }

            List<ProcessedAttachmentRecord> records = repository.GetAll().ToList();
            List<string> identifiersToRemove = [];

            if (maxAgeDays > 0)
            {
                DateTimeOffset cutoffDate = DateTimeOffset.UtcNow.AddDays(-maxAgeDays);
                identifiersToRemove.AddRange(records
                    .Where(r => r.ProcessedAt < cutoffDate)
                    .Select(r => r.Id));
            }

            if (maxFiles > 0 && records.Count > maxFiles)
            {
                int excessCount = records.Count - maxFiles;
                IEnumerable<string> oldestRecordIdentifiers = records
                    .OrderBy(r => r.ProcessedAt)
                    .Take(excessCount)
                    .Select(r => r.Id);
                identifiersToRemove.AddRange(oldestRecordIdentifiers);
            }

            foreach (string identifier in identifiersToRemove.Distinct())
            {
                repository.TryRemove(identifier);
            }

            if (identifiersToRemove.Count > 0)
            {
                repository.SaveChanges();
                logger.LogDebug(
                    "Retention removed {RecordCount} records.",
                    identifiersToRemove.Count);
            }

            return Task.CompletedTask;
        }

        public void Dispose()
        {
            repository = null;
        }
    }

}