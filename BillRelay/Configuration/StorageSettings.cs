using System.Text.Json.Serialization;

namespace BillRelay.Configuration
{
    public sealed class StorageSettings
    {
        [JsonPropertyName("downloadDirectory")]
        public string DownloadDirectory { get; set; } = "./bills";

        [JsonPropertyName("stateFilePath")]
        public string StateFilePath { get; set; } = "./state/billrelay.json";
    }
}
