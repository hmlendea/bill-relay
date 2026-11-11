using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
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
        private const string InvoiceApiUrl = "https://api2.eon.ro/invoices/v1/guest/pdf";

        private const string DefaultSubscriptionKey = "674e9032df9d456fa371e17a4097a5b8";

        private static readonly Regex EOnDownloadLinkPattern = new(
            @"https://www\.eon\.ro/descarc(?:are|a)-factura\?[^""'\s<>]+",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(1000));

        private static readonly Regex EOnTokenPattern = new(
            @"[?&]token=([^&\s""'<>]+)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(1000));

        private static readonly Regex EOnSubscriptionKeyPattern = new(
            @"subscription-key=([A-Za-z0-9]+)",
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

            string downloadUrl = WebUtility.HtmlDecode(downloadMatch.Value);
            Match tokenMatch = EOnTokenPattern.Match(downloadUrl);

            if (!tokenMatch.Success)
            {
                logger.Debug(
                    BillRelayOperation.ProcessAttachment,
                    OperationStatus.Failure,
                    new LogInfo(BillRelayInfoKey.MessageId, messageId),
                    new LogInfo(BillRelayInfoKey.Reason, "E.ON download link does not contain a token."));
                return null;
            }

            string token = Uri.UnescapeDataString(tokenMatch.Groups[1].Value);
            logger.Debug(
                BillRelayOperation.ProcessAttachment,
                OperationStatus.InProgress,
                new LogInfo(BillRelayInfoKey.MessageId, messageId),
                new LogInfo(BillRelayInfoKey.Reason, "Found E.ON download token."));

            byte[]? pdfContent = await RequestBillAsync(
                token,
                GetSubscriptionKey(htmlBody),
                messageId,
                cancellationToken);

            if (pdfContent is null)
            {
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

        private static string GetSubscriptionKey(string htmlBody)
        {
            Match keyMatch = EOnSubscriptionKeyPattern.Match(htmlBody);

            if (keyMatch.Success)
            {
                return keyMatch.Groups[1].Value;
            }

            return DefaultSubscriptionKey;
        }

        private async Task<byte[]?> RequestBillAsync(
            string token,
            string subscriptionKey,
            string messageId,
            CancellationToken cancellationToken)
        {
            using HttpClient httpClient = httpClientFactory.CreateClient();
            httpClient.Timeout = TimeSpan.FromSeconds(settings.Value.Attachments.DownloadTimeoutSeconds);

            string requestBody = JsonSerializer.Serialize(new { token });

            try
            {
                using HttpRequestMessage request = new(HttpMethod.Post, InvoiceApiUrl)
                {
                    Content = new StringContent(requestBody, Encoding.UTF8, "application/json")
                };
                request.Headers.Add("Ocp-Apim-Subscription-Key", subscriptionKey);
                request.Headers.Add("X-Client-Source", "mylineWeb");

                using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
                response.EnsureSuccessStatusCode();

                string contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
                if (!string.Equals(contentType, "application/json", StringComparison.OrdinalIgnoreCase))
                {
                    logger.Error(
                        BillRelayOperation.ProcessAttachment,
                        OperationStatus.Failure,
                        new LogInfo(BillRelayInfoKey.MessageId, messageId),
                        new LogInfo(BillRelayInfoKey.Reason, $"Unexpected content type: {contentType}"));
                    return null;
                }

                string responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
                byte[] pdfContent = DecodeInvoice(responseContent);

                if (pdfContent.Length == 0)
                {
                    logger.Error(
                        BillRelayOperation.ProcessAttachment,
                        OperationStatus.Failure,
                        new LogInfo(BillRelayInfoKey.MessageId, messageId),
                        new LogInfo(BillRelayInfoKey.Reason, "Downloaded PDF is empty."));
                    return null;
                }

                return pdfContent;
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

        private static byte[] DecodeInvoice(string responseContent)
        {
            using JsonDocument document = JsonDocument.Parse(responseContent);

            if (!document.RootElement.TryGetProperty("file", out JsonElement fileElement))
            {
                return [];
            }

            string? encodedFile = fileElement.GetString();

            if (string.IsNullOrWhiteSpace(encodedFile))
            {
                return [];
            }

            return Convert.FromBase64String(encodedFile);
        }
    }
}