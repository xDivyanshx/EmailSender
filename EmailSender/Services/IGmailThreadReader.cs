using EmailSender.Models;

namespace EmailSender.Services;

public interface IGmailThreadReader
{
    Task<GmailThreadSnapshot> GetThreadAsync(string threadId, CancellationToken cancellationToken);
    Task<GmailReplyView> GetMessageAsync(string messageId, CancellationToken cancellationToken);
}
