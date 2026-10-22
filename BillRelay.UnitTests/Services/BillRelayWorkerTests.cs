using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Options;

using Moq;

using NuciLog.Core;

using NUnit.Framework;

using BillRelay.Configuration;
using BillRelay.Services;
using BillRelay.Services.Attachments;

namespace BillRelay.UnitTests.Services
{
    [TestFixture]
    public sealed class BillRelayWorkerTests
    {
        private Mock<ILogger> loggerMock = null!;
        private Mock<IStateStore> stateStoreMock = null!;
        private Mock<IBillSender> billSenderMock = null!;
        private BillRelayWorker worker = null!;

        [SetUp]
        public void SetUp()
        {
            loggerMock = new();
            stateStoreMock = new();
            billSenderMock = new();
            worker = new(
                loggerMock.Object,
                Options.Create(new BillRelaySettings()),
                stateStoreMock.Object,
                billSenderMock.Object,
                Mock.Of<IBillDelivery>(),
                Mock.Of<IAttachmentValidator>());
            stateStoreMock.Setup(store => store.GetCheckpointAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ImapCheckpoint());
        }

        [TearDown]
        public void TearDown() => worker.Dispose();

        [Test]
        public void GivenValidDependencies_WhenConstructingTheWorker_ThenItIsCreated()
            => Assert.That(worker, Is.Not.Null);

        [Test]
        public async Task GivenCancellation_WhenStartingTheWorker_ThenItStopsWithoutAccessingTheInbox()
        {
            using CancellationTokenSource cancellation = new();
            TaskCompletionSource initialization = new(TaskCreationOptions.RunContinuationsAsynchronously);
            stateStoreMock.Setup(store => store.InitializeAsync(It.IsAny<CancellationToken>()))
                .Callback(() =>
                {
                    cancellation.Cancel();
                    initialization.SetResult();
                })
                .Returns(Task.CompletedTask);

            await worker.StartAsync(cancellation.Token);
            await initialization.Task.WaitAsync(TestContext.CurrentContext.CancellationToken);
            await worker.StopAsync(CancellationToken.None);

            billSenderMock.Verify(sender => sender.ValidateConfiguration(), Times.Once);
            stateStoreMock.Verify(
                store => store.InitializeAsync(It.IsAny<CancellationToken>()),
                Times.Once);
            stateStoreMock.Verify(
                store => store.GetCheckpointAsync(It.IsAny<CancellationToken>()),
                Times.Once);
            stateStoreMock.VerifyNoOtherCalls();
            loggerMock.Verify(
                logger => logger.Info(
                    It.Is<Operation>(operation => string.Equals(operation.Name, "RunWorker")),
                    It.Is<OperationStatus>(status => string.Equals(status.Name, "Started"))),
                Times.Once);
            loggerMock.Verify(
                logger => logger.Info(
                    It.Is<Operation>(operation => string.Equals(operation.Name, "RunWorker")),
                    It.Is<OperationStatus>(status => string.Equals(status.Name, "Success"))),
                Times.Once);
        }
    }
}