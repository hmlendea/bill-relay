using System.Text.Json.Serialization;

using Microsoft.Extensions.Configuration;

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
        [ConfigurationKeyName("useSsl")]
        public bool IsSslEnabled { get; set; } = true;

        [JsonPropertyName("validateCertificates")]
        [ConfigurationKeyName("validateCertificates")]
        public bool IsCertificateValidationEnabled { get; set; } = true;
    }
}
