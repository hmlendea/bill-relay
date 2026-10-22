using MimeKit;

using NUnit.Framework;

using BillRelay.Services;

namespace BillRelay.UnitTests.Services
{
    [TestFixture]
    public sealed class BillProviderIdentificationTests
    {
        [TestCase("Vodafone", "VDF123456789.pdf", "application/pdf", true)]
        [TestCase("Electrica Furnizare", "DII0P123456789.pdf", "application/pdf", true)]
        [TestCase("E-bloc", "Nota de plat\u0103.pdf", "application/pdf", true)]
        [TestCase("Vodafone", "terms.pdf", "application/pdf", false)]
        [TestCase("Vodafone", "VDF123.pdf.exe", "application/pdf", false)]
        [TestCase("Vodafone", "VDF123.pdf", "image/png", false)]
        [TestCase("Electrica Furnizare", "VDF123.pdf", "application/pdf", false)]
        [TestCase("Unknown", "invoice.pdf", "application/pdf", false)]
        [TestCase("E-bloc", "", "application/pdf", false)]
        public void GivenAnAttachment_WhenClassifyingItsName_ThenOnlyProviderBillNamesAreAccepted(
            string providerName,
            string fileName,
            string mimeType,
            bool isExpectedBill)
        {
            using MimePart attachment = new(mimeType) { FileName = fileName };

            Assert.That(
                BillProviderIdentification.IsBillAttachment(attachment, providerName),
                Is.EqualTo(isExpectedBill));
        }

        [TestCase("Factura ta Vodafone a fost emisa", "https://www.vodafone.ro/bills", "Vodafone")]
        [TestCase("Factura ta a fost emisa", "https://info.electricafurnizare.ro/", "Electrica Furnizare")]
        [TestCase("\u00centre\u0163inere August 2026", "https://www.e-bloc.ro/", "E-bloc")]
        [TestCase("INTRETINERE August 2026", "https://WWW.E-BLOC.RO/", "E-bloc")]
        public void GivenAForwardedBill_WhenIdentifyingItsProvider_ThenTheLinkedProviderIsReturned(
            string subject,
            string address,
            string expectedProvider)
        {
            using MimeMessage message = BuildMessage(subject, address);

            Assert.That(
                BillProviderIdentification.IdentifyProvider(message),
                Is.EqualTo(expectedProvider));
        }

        [TestCase("Newsletter", "https://www.vodafone.ro/")]
        [TestCase("Factura", "https://vodafone.ro.example.com/")]
        [TestCase("Factura", "https://example.com/vodafone.ro")]
        [TestCase("Factura", "https://[invalid")]
        [TestCase("Factura", "https://vodafone.ro/ https://myelectrica.ro/")]
        public void GivenUnreliableProviderEvidence_WhenIdentifyingItsProvider_ThenNoProviderIsReturned(
            string subject,
            string body)
        {
            using MimeMessage message = BuildMessage(subject, body);

            Assert.That(BillProviderIdentification.IdentifyProvider(message), Is.Null);
        }

        private static MimeMessage BuildMessage(string subject, string body)
        {
            MimeMessage message = new()
            {
                Subject = subject,
                Body = new TextPart("html") { Text = body }
            };
            message.From.Add(new MailboxAddress("DummyUser", "alias@example.com"));

            return message;
        }
    }
}