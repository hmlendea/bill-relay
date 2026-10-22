using System;
using System.IO;
using System.Linq;

using Microsoft.Extensions.Options;

using MimeKit;

using BillRelay.Configuration;

namespace BillRelay.Services.Attachments
{
    public sealed class AttachmentValidator(
        IOptions<BillRelaySettings> settings) : IAttachmentValidator
    {
        public bool IsPermitted(MimeEntity attachment)
            => GetFailureReason(attachment) is null;

        public string? GetFailureReason(MimeEntity attachment)
        {
            if (attachment is not MimePart part)
            {
                return "The attachment is not a MIME part.";
            }

            if (!settings.Value.Attachments.PermittedMimeTypes.Contains(part.ContentType.MimeType))
            {
                return $"The MIME type '{part.ContentType.MimeType}' is not permitted.";
            }

            if (GetSizeFailureReason(part) is string sizeFailureReason)
            {
                return sizeFailureReason;
            }

            if (!HasValidPdfSignature(part))
            {
                return "The attachment does not start with the required PDF signature '%PDF-'.";
            }

            return null;
        }

        private string? GetSizeFailureReason(MimePart attachment)
        {
            long contentLength = 0;

            if (attachment.Content?.Stream is not null)
            {
                contentLength = attachment.Content.Stream.Length;
            }

            if (contentLength > settings.Value.Attachments.MaximumSizeBytes)
            {
                return $"The attachment size ({contentLength} bytes) exceeds the maximum " +
                    $"of {settings.Value.Attachments.MaximumSizeBytes} bytes.";
            }

            return null;
        }

        private static bool HasValidPdfSignature(MimePart attachment)
        {
            using MemoryStream content = new();
            attachment.Content.DecodeTo(content);
            byte[] bytes = content.ToArray();

            return bytes.AsSpan().StartsWith("%PDF-"u8);
        }
    }
}