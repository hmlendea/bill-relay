using System;
using System.IO;
using System.Linq;

using Microsoft.Extensions.Options;

using MimeKit;

using NuciLog.Core;

using BillRelay.Configuration;
using BillRelay.Logging;

namespace BillRelay.Services.Attachments
{
    public sealed class AttachmentValidator(
        ILogger logger,
        IOptions<BillRelaySettings> settings) : IAttachmentValidator
    {
        public bool IsPermitted(MimeEntity attachment)
        {
            if (attachment is not MimePart part)
            {
                return false;
            }

            if (!settings.Value.Attachments.PermittedMimeTypes.Contains(part.ContentType.MimeType))
            {
                return false;
            }

            return IsWithinSizeLimit(part) && HasPdfSignature(part);
        }

        private bool IsWithinSizeLimit(MimePart attachment)
        {
            long contentLength = 0;

            if (attachment.Content?.Stream is not null)
            {
                contentLength = attachment.Content.Stream.Length;
            }

            if (contentLength > settings.Value.Attachments.MaximumSizeBytes)
            {
                logger.Warn(
                    BillRelayOperation.ValidateAttachment,
                    OperationStatus.Failure,
                    new LogInfo(BillRelayInfoKey.ContentLength, contentLength),
                    new LogInfo(
                        BillRelayInfoKey.MaximumSizeBytes,
                        settings.Value.Attachments.MaximumSizeBytes),
                    new LogInfo(BillRelayInfoKey.Reason, "The attachment exceeds the maximum size."));

                return false;
            }

            return true;
        }

        private bool HasPdfSignature(MimePart attachment)
        {
            if (HasValidPdfSignature(attachment))
            {
                return true;
            }

            logger.Warn(
                BillRelayOperation.ValidateAttachment,
                OperationStatus.Failure,
                new LogInfo(BillRelayInfoKey.FileName, attachment.FileName),
                new LogInfo(BillRelayInfoKey.Reason, "The attachment failed PDF signature validation."));

            return false;
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