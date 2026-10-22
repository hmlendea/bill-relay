using System.Text.Json.Serialization;

using Microsoft.Extensions.Configuration;

namespace BillRelay.Configuration
{
    public sealed class RetentionSettings
    {
        [JsonPropertyName("maxAgeDays")]
        [ConfigurationKeyName("maxAgeDays")]
        public int MaximumAgeDays { get; set; } = 365;

        [JsonPropertyName("maxFiles")]
        [ConfigurationKeyName("maxFiles")]
        public int MaximumFiles { get; set; } = 10000;
    }
}
