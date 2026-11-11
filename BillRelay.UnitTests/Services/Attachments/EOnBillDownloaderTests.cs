using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Options;

using MimeKit;

using Moq;

using NuciLog.Core;

using NUnit.Framework;

using BillRelay.Configuration;
using BillRelay.Services.Attachments;

namespace BillRelay.UnitTests.Services.Attachments
{
    [TestFixture]
    public sealed class EOnBillDownloaderTests
    {
        private const string SubscriptionKey = "0123456789abcdef0123456789abcdef";

        private const string PdfContent = "%PDF-1.4\n%Factura\n%%EOF";

        private Mock<ILogger> loggerMock = null!;
        private StubHttpMessageHandler handler = null!;
        private EOnBillDownloader downloader = null!;

        [SetUp]
        public void SetUp()
        {
            loggerMock = new();
            handler = new(_ => BuildJsonResponse(PdfContent));
            Mock<IHttpClientFactory> httpClientFactoryMock = new();
            httpClientFactoryMock
                .Setup(factory => factory.CreateClient(It.IsAny<string>()))
                .Returns(() => new HttpClient(handler));

            BillRelaySettings billRelaySettings = new()
            {
                Attachments = new()
                {
                    DownloadTimeoutSeconds = 5
                }
            };

            downloader = new(
                httpClientFactoryMock.Object,
                loggerMock.Object,
                Options.Create(billRelaySettings));
        }

        [TestCase("Factura ta E.ON", "Fără link.")]
        [TestCase("Factura ta E.ON", "<a href=\"https://www.eon.ro/descarca-factura\">Descarcă</a>")]
        [TestCase("Factura ta E.ON", "<a href=\"https://www.eon.ro/descarca-factura?ac=002103941994\">Descarcă</a>")]
        public async Task GivenAMessageWithoutADownloadToken_WhenDownloadingTheBill_ThenNoBillIsReturned(
            string subject,
            string htmlBody)
        {
            using MimeMessage message = BuildMessage(subject, htmlBody);

            Assert.That(await downloader.DownloadBillAsync(message, "1:2", CancellationToken.None), Is.Null);
        }

        [TestCase("https://www.eon.ro/descarca-factura?ac=002103941994&token=tokenValue")]
        [TestCase("https://www.eon.ro/descarcare-factura?ac=002103941994&amp;token=tokenValue")]
        public async Task GivenADownloadLink_WhenDownloadingTheBill_ThenTheDecodedPdfIsReturned(
            string link)
        {
            using MimeMessage message = BuildMessage(
                "Factura ta E.ON",
                $"<a href=\"{link}\">Descarcă factura</a>");

            using MimePart bill = (await downloader.DownloadBillAsync(
                message,
                "1:2",
                CancellationToken.None))!;

            Assert.That(bill, Is.Not.Null);

            using MemoryStream decodedContent = new();
            bill.Content!.DecodeTo(decodedContent);

            Assert.That(
                Encoding.UTF8.GetString(decodedContent.ToArray()),
                Is.EqualTo(PdfContent));
        }

        [TestCase("https://www.eon.ro/descarca-factura?ac=002103941994&token=tokenValue")]
        [TestCase("https://www.eon.ro/descarcare-factura?ac=002103941994&amp;token=tokenValue")]
        public async Task GivenADownloadLink_WhenDownloadingTheBill_ThenTheInvoiceApiIsCalledWithTheToken(
            string link)
        {
            using MimeMessage message = BuildMessage(
                "Factura ta E.ON",
                $"<a href=\"{link}\">Descarcă factura</a>");

            await downloader.DownloadBillAsync(message, "1:2", CancellationToken.None);

            Assert.That(handler.LastRequest, Is.Not.Null);
            Assert.That(handler.LastRequest!.Method, Is.EqualTo(HttpMethod.Post));
            Assert.That(
                handler.LastRequest!.RequestUri,
                Is.EqualTo(new Uri("https://api2.eon.ro/invoices/v1/guest/pdf")));
            Assert.That(handler.LastRequestBody, Does.Contain("\"token\":\"tokenValue\""));
        }

        [Test]
        public async Task GivenAConfiguredSubscriptionKeyInTheMessage_WhenDownloadingTheBill_ThenItIsForwarded()
        {
            using MimeMessage message = BuildMessage(
                "Factura ta E.ON",
                $"<img src=\"https://api2.eon.ro/function-app/GenerateBarcode?barcode=1&amp;subscription-key={SubscriptionKey}\" />" +
                "<a href=\"https://www.eon.ro/descarca-factura?ac=002103941994&amp;token=tokenValue\">Descarcă</a>");

            await downloader.DownloadBillAsync(message, "1:2", CancellationToken.None);

            Assert.That(
                handler.LastRequest!.Headers.GetValues("Ocp-Apim-Subscription-Key"),
                Does.Contain(SubscriptionKey));
        }

        [TestCase(HttpStatusCode.InternalServerError)]
        [TestCase(HttpStatusCode.NotFound)]
        public async Task GivenAnUnsuccessfulApiResponse_WhenDownloadingTheBill_ThenNoBillIsReturned(
            HttpStatusCode statusCode)
        {
            handler.ResponseFactory = _ => new HttpResponseMessage(statusCode);
            using MimeMessage message = BuildMessage(
                "Factura ta E.ON",
                "<a href=\"https://www.eon.ro/descarca-factura?ac=002103941994&amp;token=tokenValue\">Descarcă</a>");

            Assert.That(await downloader.DownloadBillAsync(message, "1:2", CancellationToken.None), Is.Null);
        }

        [TestCase("{\"file\":\"\"}")]
        [TestCase("{\"file\":null}")]
        [TestCase("{\"description\":\"Invalid generated pdf password token\"}")]
        public async Task GivenAnApiResponseWithoutAFile_WhenDownloadingTheBill_ThenNoBillIsReturned(
            string responseContent)
        {
            handler.ResponseFactory = _ => BuildRawJsonResponse(responseContent);
            using MimeMessage message = BuildMessage(
                "Factura ta E.ON",
                "<a href=\"https://www.eon.ro/descarca-factura?ac=002103941994&amp;token=tokenValue\">Descarcă</a>");

            Assert.That(await downloader.DownloadBillAsync(message, "1:2", CancellationToken.None), Is.Null);
        }

        private static MimeMessage BuildMessage(string subject, string htmlBody) => new()
        {
            Subject = subject,
            Body = new TextPart("html") { Text = htmlBody }
        };

        private static HttpResponseMessage BuildJsonResponse(string pdfContent)
        {
            string encodedFile = Convert.ToBase64String(Encoding.UTF8.GetBytes(pdfContent));

            return BuildRawJsonResponse(JsonSerializer.Serialize(new { file = encodedFile }));
        }

        private static HttpResponseMessage BuildRawJsonResponse(string responseContent) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(responseContent, Encoding.UTF8, "application/json")
        };

        private sealed class StubHttpMessageHandler(
            Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
        {
            public Func<HttpRequestMessage, HttpResponseMessage> ResponseFactory { get; set; } =
                responseFactory;

            public HttpRequestMessage? LastRequest { get; private set; }

            public string? LastRequestBody { get; private set; }

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                LastRequest = request;

                if (request.Content is not null)
                {
                    LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
                }

                return ResponseFactory(request);
            }
        }
    }
}