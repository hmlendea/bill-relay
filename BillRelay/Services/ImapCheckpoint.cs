namespace BillRelay.Services
{
    using System;

    public sealed class ImapCheckpoint
    {
        public uint UidValidity { get; set; }

        public uint LastProcessedUid { get; set; }

        public DateTimeOffset? LastProcessedDate { get; set; }
    }
}