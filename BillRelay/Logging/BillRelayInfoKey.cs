using NuciLog.Core;

namespace BillRelay.Logging
{
    public sealed class BillRelayInfoKey : LogInfoKey
    {
        public static LogInfoKey AttachmentId => new BillRelayInfoKey(nameof(AttachmentId));

        public static LogInfoKey FilePath => new BillRelayInfoKey(nameof(FilePath));

        public static LogInfoKey FileName => new BillRelayInfoKey(nameof(FileName));

        public static LogInfoKey StateFilePath => new BillRelayInfoKey(nameof(StateFilePath));

        public static LogInfoKey MessageCount => new BillRelayInfoKey(nameof(MessageCount));

        public static LogInfoKey MessageId => new BillRelayInfoKey(nameof(MessageId));

        public static LogInfoKey Subject => new BillRelayInfoKey(nameof(Subject));

        public static LogInfoKey Sender => new BillRelayInfoKey(nameof(Sender));

        public static LogInfoKey ProviderName => new BillRelayInfoKey(nameof(ProviderName));

        public static LogInfoKey Recipient => new BillRelayInfoKey(nameof(Recipient));

        public static LogInfoKey AttachmentCount => new BillRelayInfoKey(nameof(AttachmentCount));

        public static LogInfoKey CandidateCount => new BillRelayInfoKey(nameof(CandidateCount));

        public static LogInfoKey EligibleMessageCount => new BillRelayInfoKey(nameof(EligibleMessageCount));

        public static LogInfoKey RecordCount => new BillRelayInfoKey(nameof(RecordCount));

        public static LogInfoKey ContentLength => new BillRelayInfoKey(nameof(ContentLength));

        public static LogInfoKey EncodedContentLength => new BillRelayInfoKey(nameof(EncodedContentLength));

        public static LogInfoKey MaximumSizeBytes => new BillRelayInfoKey(nameof(MaximumSizeBytes));

        public static LogInfoKey Reason => new BillRelayInfoKey(nameof(Reason));

        private BillRelayInfoKey(string name) : base(name)
        {
        }
    }
}