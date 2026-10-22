using System.Xml.Serialization;

namespace BillRelay.DataAccess.DataObjects
{
    [XmlRoot("EmailTemplate")]
    public sealed class EmailTemplateDataObject
    {
        public string Subject { get; set; } = string.Empty;

        public string Body { get; set; } = string.Empty;
    }
}