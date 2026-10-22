using System;

using BillRelay.Configuration;

namespace BillRelay.Services
{
    public static class EmailAgePolicy
    {
        public static DateTimeOffset GetCutoff(PollingSettings settings, DateTimeOffset currentTime)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(settings.MaximumEmailAgeDays);
            DateTimeOffset cutoff = currentTime.AddDays(-settings.MaximumEmailAgeDays);

            if (settings.InitialProcessingDate.HasValue &&
                settings.InitialProcessingDate.Value > cutoff)
            {
                cutoff = settings.InitialProcessingDate.Value;
            }

            return cutoff;
        }

        public static bool IsEligible(DateTimeOffset? arrivalTime, DateTimeOffset cutoff) =>
            arrivalTime.HasValue && arrivalTime.Value >= cutoff;
    }
}