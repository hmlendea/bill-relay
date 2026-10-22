using System;

using NuciDAL.DataObjects;

namespace BillRelay.DataAccess.DataObjects
{
    public sealed class ProcessedAttachmentDataObject : EntityBase
    {
        public string MessageId { get; set; } = string.Empty;

        public DateTimeOffset ProcessedAt { get; set; }
    }
}