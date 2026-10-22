using MimeKit;

namespace BillRelay.Services.Attachments
{
    public interface IAttachmentValidator
    {
        bool IsPermitted(MimeEntity attachment);
    }
}