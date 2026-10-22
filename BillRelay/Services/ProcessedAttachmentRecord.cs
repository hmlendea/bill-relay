using System;
using NuciDAL.DataObjects;

namespace BillRelay.Services
{
    public sealed class ProcessedAttachmentRecord : EntityBase
    {
        public string MessageId { get; set; } = string.Empty;

        public DateTimeOffset ProcessedAt { get; set; }
    }
}