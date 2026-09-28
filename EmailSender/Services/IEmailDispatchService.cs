namespace EmailSender.Services;

using EmailSender.Models;

public interface IEmailDispatchService
{
    EmailDispatchResult TrySendMail(
        string subject,
        string body,
        string toEmailAddress,
        string name,
        IEnumerable<AttachmentSpec>? attachments = null);

    EmailDispatchResult TrySendReply(
        string subject,
        string body,
        string toEmailAddress,
        string name,
        string providerThreadId,
        string inReplyTo,
        IEnumerable<string> references,
        IEnumerable<string>? attachmentPaths = null);
}

public sealed record EmailDispatchResult(
    bool Success,
    string? ProviderMessageId = null,
    string? ProviderThreadId = null,
    string? InternetMessageId = null,
    string? Error = null);
