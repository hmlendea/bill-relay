using System.Text.Json.Serialization;

using Microsoft.Extensions.Configuration;

namespace BillRelay.Configuration
{
    public sealed class AttachmentSettings
    {
        [JsonPropertyName("permittedMimeTypes")]
        public string[] PermittedMimeTypes { get; set; } = ["application/pdf"];

        [JsonPropertyName("maxSizeBytes")]
        [ConfigurationKeyName("maxSizeBytes")]
        public long MaximumSizeBytes { get; set; } = 25_000_000;
    }
}
