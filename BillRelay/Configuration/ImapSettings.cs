using System.Text.Json.Serialization;

namespace BillRelay.Configuration
{
    public sealed class ImapSettings
    {
        [JsonPropertyName("server")]
        public string Server { get; set; } = string.Empty;

        [JsonPropertyName("port")]
        public int Port { get; set; } = 993;

        [JsonPropertyName("mailbox")]
        public string Mailbox { get; set; } = "INBOX";

        [JsonPropertyName("username")]
        public string Username { get; set; } = string.Empty;

        [JsonPropertyName("password")]
        public string Password { get; set; } = string.Empty;

        [JsonPropertyName("useSsl")]
        public bool UseSsl { get; set; } = true;

        [JsonPropertyName("validateCertificates")]
        public bool ValidateCertificates { get; set; } = true;
    }
}