using System.Text.Json.Serialization;

namespace BillRelay.Configuration
{
    public sealed class FilterSettings
    {
        [JsonPropertyName("providers")]
        public string[] Providers { get; set; } = [];

        [JsonPropertyName("senderPatterns")]
        public string[] SenderPatterns { get; set; } = [];

        [JsonPropertyName("subjectPatterns")]
        public string[] SubjectPatterns { get; set; } = [];
    }
}
