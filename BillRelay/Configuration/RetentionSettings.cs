using System.Text.Json.Serialization;

namespace BillRelay.Configuration
{
    public sealed class RetentionSettings
    {
        [JsonPropertyName("maxAgeDays")]
        public int MaxAgeDays { get; set; } = 365;

        [JsonPropertyName("maxFiles")]
        public int MaxFiles { get; set; } = 10000;
    }
}