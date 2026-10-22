using System.Text.Json.Serialization;

namespace BillRelay.Configuration
{
    public sealed class AttachmentSettings
    {
        [JsonPropertyName("permittedMimeTypes")]
        public string[] PermittedMimeTypes { get; set; } = ["application/pdf"];

        [JsonPropertyName("maxSizeBytes")]
        public long MaxSizeBytes { get; set; } = 25_000_000;
    }
}