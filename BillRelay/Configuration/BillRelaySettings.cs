using System.Text.Json.Serialization;

namespace BillRelay.Configuration
{
    public sealed class BillRelaySettings
    {
        public DeliverySettings Delivery { get; set; } = new();

        [JsonPropertyName("imap")]
        public ImapSettings Imap { get; set; } = new();

        [JsonPropertyName("polling")]
        public PollingSettings Polling { get; set; } = new();

        [JsonPropertyName("filters")]
        public FilterSettings Filters { get; set; } = new();

        [JsonPropertyName("attachments")]
        public AttachmentSettings Attachments { get; set; } = new();

        [JsonPropertyName("storage")]
        public StorageSettings Storage { get; set; } = new();

        [JsonPropertyName("retention")]
        public RetentionSettings Retention { get; set; } = new();
    }
}