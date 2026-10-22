using System.Xml.Serialization;

namespace BillRelay.Services
{
    [XmlRoot("EmailTemplate")]
    public sealed class EmailTemplateDataObject
    {
        public string Subject { get; set; } = string.Empty;

        public string Body { get; set; } = string.Empty;
    }
}