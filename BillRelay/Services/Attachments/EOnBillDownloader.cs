using System;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Options;

using MimeKit;

using NuciLog.Core;

using BillRelay.Configuration;
using BillRelay.Logging;

namespace BillRelay.Services.Attachments
{
    public sealed class EOnBillDownloader(
        IHttpClientFactory httpClientFactory,
        ILogger logger,
        IOptions<BillRelaySettings> settings) : IEOnBillDownloader
    {
        private static readonly Regex EOnDownloadLinkPattern = new(
            @"https://www\.eon\.ro/descarcare-factura\?[^""'\s<>]+",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(1000));

        private static readonly Regex EOnClickTrackingPattern = new(
            @"http://noutati\.myline-eon\.ro/click/[^""'\s<>]+",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(1000));

        public async Task<MimePart?> DownloadBillAsync(
            MimeMessage message,
            string messageId,
            CancellationToken cancellationToken)
        {
            string htmlBody = message.HtmlBody ?? string.Empty;

            Match downloadMatch = EOnDownloadLinkPattern.Match(htmlBody);
            if (!downloadMatch.Success)
            {
                logger.Debug(
                    BillRelayOperation.ProcessAttachment,
                    OperationStatus.Failure,
                    new LogInfo(BillRelayInfoKey.MessageId, messageId),
                    new LogInfo(BillRelayInfoKey.Reason, "E.ON download link not found in email body."));
                return null;
            }

            string downloadUrl = downloadMatch.Value;
            logger.Debug(
                BillRelayOperation.ProcessAttachment,
                OperationStatus.InProgress,
                new LogInfo(BillRelayInfoKey.MessageId, messageId),
                new LogInfo(BillRelayInfoKey.Reason, "Found E.ON download link."),
                new LogInfo(BillRelayInfoKey.Url, downloadUrl));

            using HttpClient httpClient = httpClientFactory.CreateClient();
            httpClient.Timeout = TimeSpan.FromSeconds(settings.Value.Attachments.DownloadTimeoutSeconds);

            try
            {
                using HttpResponseMessage response = await httpClient.GetAsync(downloadUrl, cancellationToken);
                response.EnsureSuccessStatusCode();

                string contentType = response.Content.Headers.ContentType?.MediaType ?? "application/pdf";
                if (!string.Equals(contentType, "application/pdf", StringComparison.OrdinalIgnoreCase))
                {
                    logger.Error(
                        BillRelayOperation.ProcessAttachment,
                        OperationStatus.Failure,
                        new LogInfo(BillRelayInfoKey.MessageId, messageId),
                        new LogInfo(BillRelayInfoKey.Reason, $"Unexpected content type: {contentType}"));
                    return null;
                }

                byte[] pdfContent = await response.Content.ReadAsByteArrayAsync(cancellationToken);

                if (pdfContent.Length == 0)
                {
                    logger.Error(
                        BillRelayOperation.ProcessAttachment,
                        OperationStatus.Failure,
                        new LogInfo(BillRelayInfoKey.MessageId, messageId),
                        new LogInfo(BillRelayInfoKey.Reason, "Downloaded PDF is empty."));
                    return null;
                }

                MimePart pdfPart = new("application", "pdf")
                {
                    FileName = $"E.ON_Factura_{messageId.Replace(':', '_')}.pdf",
                    Content = new MimeContent(new MemoryStream(pdfContent), ContentEncoding.Default),
                    ContentDisposition = new ContentDisposition(ContentDisposition.Attachment),
                    ContentTransferEncoding = ContentEncoding.Base64
                };

                logger.Info(
                    BillRelayOperation.ProcessAttachment,
                    OperationStatus.Success,
                    new LogInfo(BillRelayInfoKey.MessageId, messageId),
                    new LogInfo(BillRelayInfoKey.FileName, pdfPart.FileName),
                    new LogInfo(BillRelayInfoKey.ContentLength, pdfContent.Length));

                return pdfPart;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.Error(
                    BillRelayOperation.ProcessAttachment,
                    OperationStatus.Failure,
                    exception,
                    new LogInfo(BillRelayInfoKey.MessageId, messageId),
                    new LogInfo(BillRelayInfoKey.Reason, "Failed to download E.ON bill PDF."));
                return null;
            }
        }
    }
}