using System;
using System.Threading;
using System.Threading.Tasks;

using MimeKit;

using Moq;

using NUnit.Framework;

using BillRelay.Services;

namespace BillRelay.UnitTests.Services
{
    [TestFixture]
    public sealed class BillDeliveryTests
    {
        [TestCase(true)]
        [TestCase(false)]
        public async Task GivenASendOutcome_WhenDelivering_ThenOnlySuccessfulSubmissionsAreRecorded(
            bool isSuccessful)
        {
            Mock<IAttachmentProcessor> processor = new();
            Mock<IBillSender> sender = new();
            Mock<IStateStore> store = new();
            using MimePart attachment = new("application/pdf");
            bool wasSent = false;
            sender.Setup(instance => instance.SendAsync(attachment, "Vodafone", CancellationToken.None))
                .Returns(() =>
                {
                    if (!isSuccessful)
                    {
                        throw new InvalidOperationException("Submission failed.");
                    }

                    wasSent = true;

                    return Task.CompletedTask;
                });
            store.Setup(instance => instance.MarkProcessedAsync("delivery:attachment", "message", CancellationToken.None))
                .Callback(() => Assert.That(wasSent))
                .Returns(Task.CompletedTask);
            BillDelivery delivery = new(processor.Object, sender.Object, store.Object);

            if (!isSuccessful)
            {
                Assert.That(async () => await delivery.DeliverAsync(
                    attachment, "attachment", "message", "Vodafone", CancellationToken.None),
                    Throws.TypeOf<InvalidOperationException>());
                store.Verify(instance => instance.MarkProcessedAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

                return;
            }

            await delivery.DeliverAsync(attachment, "attachment", "message", "Vodafone", CancellationToken.None);
            store.Verify(instance => instance.MarkProcessedAsync(
                "delivery:attachment", "message", CancellationToken.None), Times.Once);
        }

        [Test]
        public async Task GivenADeliveredBill_WhenDeliveringAgain_ThenNoDownloadOrSendOccurs()
        {
            Mock<IAttachmentProcessor> processor = new(MockBehavior.Strict);
            Mock<IBillSender> sender = new(MockBehavior.Strict);
            Mock<IStateStore> store = new();
            store.Setup(instance => instance.IsProcessedAsync("delivery:attachment", CancellationToken.None))
                .ReturnsAsync(true);
            BillDelivery delivery = new(processor.Object, sender.Object, store.Object);
            using MimePart attachment = new("application/pdf");

            await delivery.DeliverAsync(attachment, "attachment", "message", "Vodafone", CancellationToken.None);

            processor.VerifyNoOtherCalls();
            sender.VerifyNoOtherCalls();
        }
    }
}