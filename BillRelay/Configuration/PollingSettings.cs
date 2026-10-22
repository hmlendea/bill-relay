using System;
using System.Text.Json.Serialization;

namespace BillRelay.Configuration
{
    public sealed class PollingSettings
    {
        [JsonPropertyName("intervalSeconds")]
        public int IntervalSeconds { get; set; } = 300;

        [JsonPropertyName("maximumEmailAgeDays")]
        public int MaximumEmailAgeDays { get; set; } = 30;

        [JsonPropertyName("initialProcessingDate")]
        public DateTimeOffset? InitialProcessingDate { get; set; }
    }
}
