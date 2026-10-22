namespace BillRelay.Configuration
{
    public sealed class DeliverySettings
    {
        public string Recipient { get; set; } = string.Empty;

        public string SenderName { get; set; } = "BillRelay";

        public string TemplatePath { get; set; } = "email-template.xml";
    }
}