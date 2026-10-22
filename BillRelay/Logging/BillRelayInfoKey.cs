using NuciLog.Core;

namespace BillRelay.Logging
{
    public sealed class BillRelayInfoKey : LogInfoKey
    {
        public static LogInfoKey AttachmentId => new BillRelayInfoKey(nameof(AttachmentId));

        public static LogInfoKey FilePath => new BillRelayInfoKey(nameof(FilePath), true);

        public static LogInfoKey FileName => new BillRelayInfoKey(nameof(FileName), true);

        public static LogInfoKey StateFilePath => new BillRelayInfoKey(nameof(StateFilePath), true);

        public static LogInfoKey MessageCount => new BillRelayInfoKey(nameof(MessageCount));

        public static LogInfoKey RecordCount => new BillRelayInfoKey(nameof(RecordCount));

        public static LogInfoKey ContentLength => new BillRelayInfoKey(nameof(ContentLength));

        public static LogInfoKey MaximumSizeBytes => new BillRelayInfoKey(nameof(MaximumSizeBytes));

        public static LogInfoKey Reason => new BillRelayInfoKey(nameof(Reason));

        private BillRelayInfoKey(string name) : base(name)
        {
        }

        private BillRelayInfoKey(string name, bool isSensitive) : base(name, isSensitive)
        {
        }
    }
}