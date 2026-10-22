using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

using MimeKit;

namespace BillRelay.Services
{
    public static partial class BillProviderIdentification
    {
        public static bool IsBillAttachment(
            MimePart attachment,
            string providerName)
        {
            Regex pattern = providerName switch
            {
                "Vodafone" => VodafoneAttachmentPattern(),
                "Electrica Furnizare" => ElectricaAttachmentPattern(),
                "E-bloc" => EBlocAttachmentPattern(),
                "Hidroelectrica" => HidroelectricaAttachmentPattern(),
                _ => UnmatchedAttachmentPattern()
            };
            string fileName = string.Empty;

            if (attachment.FileName is not null)
            {
                fileName = attachment.FileName;
            }

            return
                string.Equals(
                    attachment.ContentType.MimeType,
                    "application/pdf",
                    StringComparison.OrdinalIgnoreCase) &&
                pattern.IsMatch(fileName);
        }

        public static string? IdentifyProvider(MimeMessage message)
        {
            string subject = string.Empty;

            if (message.Subject is not null)
            {
                subject = message.Subject;
            }

            if (!HasBillingSubject(subject))
            {
                return null;
            }

            if (subject.Contains("hidroelectrica", StringComparison.OrdinalIgnoreCase))
            {
                return "Hidroelectrica";
            }

            string[] domains = LinkPattern().Matches(GetBody(message))
                .Select(match => GetDomain(match.Value))
                .Concat(message.From.Mailboxes.Select(sender => sender.Address.Split('@').Last()))
                .ToArray();
            string[] providers = domains.Select(IdentifyDomain)
                .Where(provider => !string.IsNullOrEmpty(provider))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            if (Equals(providers.Length, 1))
            {
                return providers[0];
            }

            return null;
        }

        private static string GetBody(MimeMessage message)
        {
            if (message.HtmlBody is not null)
            {
                return message.HtmlBody;
            }

            if (message.TextBody is not null)
            {
                return message.TextBody;
            }

            return string.Empty;
        }

        private static string GetDomain(string address)
        {
            if (Uri.TryCreate(address, UriKind.Absolute, out Uri? parsedAddress))
            {
                return parsedAddress.Host;
            }

            return string.Empty;
        }

        private static string IdentifyDomain(string domain)
        {
            if (IsDomain(domain, "vodafone.ro"))
            {
                return "Vodafone";
            }

            if (IsDomain(domain, "electricafurnizare.ro") ||
                IsDomain(domain, "myelectrica.ro"))
            {
                return "Electrica Furnizare";
            }

            if (IsDomain(domain, "e-bloc.ro"))
            {
                return "E-bloc";
            }

            if (IsDomain(domain, "hidroelectrica.ro"))
            {
                return "Hidroelectrica";
            }

            return string.Empty;
        }

        private static bool IsDomain(string domain, string expectedDomain) =>
            string.Equals(domain, expectedDomain, StringComparison.OrdinalIgnoreCase) ||
            domain.EndsWith("." + expectedDomain, StringComparison.OrdinalIgnoreCase);

        private static bool HasBillingSubject(string subject)
        {
            string normalizedSubject = new(subject.Normalize(NormalizationForm.FormD)
                .Where(character => !Equals(
                    CharUnicodeInfo.GetUnicodeCategory(character),
                    UnicodeCategory.NonSpacingMark))
                .ToArray());

            return
                normalizedSubject.Contains("factur", StringComparison.OrdinalIgnoreCase) ||
                normalizedSubject.Contains("intretinere", StringComparison.OrdinalIgnoreCase) ||
                normalizedSubject.Contains("invoice", StringComparison.OrdinalIgnoreCase) ||
                normalizedSubject.Contains("bill", StringComparison.OrdinalIgnoreCase);
        }

        [GeneratedRegex("https?://[^\\s<>\"']+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 1000)]
        private static partial Regex LinkPattern();

        [GeneratedRegex(@"\AVDF[0-9]+\.pdf\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 1000)]
        private static partial Regex VodafoneAttachmentPattern();

        [GeneratedRegex(@"\ADII[0-9]P[0-9]+\.pdf\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 1000)]
        private static partial Regex ElectricaAttachmentPattern();

        [GeneratedRegex("\\ANota de plat[\u0103a]\\.pdf\\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 1000)]
        private static partial Regex EBlocAttachmentPattern();

        [GeneratedRegex(@"\AFX-[0-9]+\.pdf\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 1000)]
        private static partial Regex HidroelectricaAttachmentPattern();

        [GeneratedRegex(@"\b\B", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 1000)]
        private static partial Regex UnmatchedAttachmentPattern();
    }
}