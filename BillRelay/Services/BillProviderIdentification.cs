using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

using MimeKit;

namespace BillRelay.Services
{
    public static class BillProviderIdentification
    {
        private static readonly Regex linkPattern = new(
            "https?://[^\\s<>\"']+",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));

        public static bool IsBillAttachment(
            MimePart attachment,
            string providerName)
        {
            string pattern = providerName switch
            {
                "Vodafone" => @"\AVDF[0-9]+\.pdf\z",
                "Electrica Furnizare" => @"\ADII[0-9]P[0-9]+\.pdf\z",
                "E-bloc" => "\\ANota de plat[\u0103a]\\.pdf\\z",
                _ => @"\b\B"
            };

            return string.Equals(
                attachment.ContentType.MimeType,
                "application/pdf",
                StringComparison.OrdinalIgnoreCase) &&
                Regex.IsMatch(
                    attachment.FileName ?? string.Empty,
                    pattern,
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                    TimeSpan.FromSeconds(1));
        }

        public static string? IdentifyProvider(MimeMessage message)
        {
            if (!HasBillingSubject(message.Subject ?? string.Empty))
            {
                return null;
            }

            string body = message.HtmlBody ?? message.TextBody ?? string.Empty;
            string[] domains = linkPattern.Matches(body)
                .Select(match => GetDomain(match.Value))
                .Concat(message.From.Mailboxes.Select(sender => sender.Address.Split('@').Last()))
                .ToArray();
            string[] providers = domains.Select(IdentifyDomain)
                .Where(provider => !string.IsNullOrEmpty(provider))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            if (providers.Length == 1)
            {
                return providers[0];
            }

            return null;
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

            if (IsDomain(domain, "electricafurnizare.ro") || IsDomain(domain, "myelectrica.ro"))
            {
                return "Electrica Furnizare";
            }

            if (IsDomain(domain, "e-bloc.ro"))
            {
                return "E-bloc";
            }

            return string.Empty;
        }

        private static bool IsDomain(string domain, string expectedDomain) =>
            string.Equals(domain, expectedDomain, StringComparison.OrdinalIgnoreCase) ||
            domain.EndsWith("." + expectedDomain, StringComparison.OrdinalIgnoreCase);

        private static bool HasBillingSubject(string subject)
        {
            string normalizedSubject = new(subject.Normalize(NormalizationForm.FormD)
                .Where(character => CharUnicodeInfo.GetUnicodeCategory(character) !=
                    UnicodeCategory.NonSpacingMark)
                .ToArray());

            return normalizedSubject.Contains("factur", StringComparison.OrdinalIgnoreCase) ||
                normalizedSubject.Contains("intretinere", StringComparison.OrdinalIgnoreCase) ||
                normalizedSubject.Contains("invoice", StringComparison.OrdinalIgnoreCase) ||
                normalizedSubject.Contains("bill", StringComparison.OrdinalIgnoreCase);
        }
    }
}