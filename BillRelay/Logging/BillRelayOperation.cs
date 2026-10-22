using NuciLog.Core;

namespace BillRelay.Logging
{
    public sealed class BillRelayOperation : Operation
    {
        public static Operation RunWorker => new BillRelayOperation(nameof(RunWorker));

        public static Operation ProcessInbox => new BillRelayOperation(nameof(ProcessInbox));

        public static Operation ProcessAttachment => new BillRelayOperation(nameof(ProcessAttachment));

        public static Operation ValidateAttachment => new BillRelayOperation(nameof(ValidateAttachment));

        public static Operation WriteAttachment => new BillRelayOperation(nameof(WriteAttachment));

        public static Operation SaveAttachment => new BillRelayOperation(nameof(SaveAttachment));

        public static Operation InitializeStateStore => new BillRelayOperation(nameof(InitializeStateStore));

        public static Operation ApplyRetention => new BillRelayOperation(nameof(ApplyRetention));

        private BillRelayOperation(string name) : base(name)
        {
        }
    }
}