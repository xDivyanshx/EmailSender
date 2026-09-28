using EmailSender.Services;
using EmailSender.Models;

namespace EmailSender.Tests;

internal sealed class RecordingEmailDispatcher : IEmailDispatchService
{
    public List<RecordedEmail> Messages { get; } = [];
    public List<RecordedReply> Replies { get; } = [];
    public bool Result { get; set; } = true;

    public EmailDispatchResult TrySendMail(string subject, string body, string toEmailAddress, string name, IEnumerable<AttachmentSpec>? attachments = null)
    {
        Messages.Add(new(subject, body, toEmailAddress, name, attachments?.Select(item => item.Path).ToArray() ?? []));
        return new(Result, Result ? "message-test" : null, Result ? "thread-test" : null, Result ? "<internet-test@example.com>" : null, Result ? null : "fake failure");
    }

    public EmailDispatchResult TrySendReply(
        string subject,
        string body,
        string toEmailAddress,
        string name,
        string providerThreadId,
        string inReplyTo,
        IEnumerable<string> references,
        IEnumerable<string>? attachmentPaths = null)
    {
        Messages.Add(new(subject, body, toEmailAddress, name, attachmentPaths?.ToArray() ?? []));
        Replies.Add(new(providerThreadId, inReplyTo, references.ToArray()));
        return new(Result, Result ? "followup-test" : null, providerThreadId, Result ? "<followup-test@example.com>" : null, Result ? null : "fake failure");
    }
}

internal sealed record RecordedEmail(string Subject, string Body, string Email, string Name, IReadOnlyCollection<string> Attachments);
internal sealed record RecordedReply(string ThreadId, string InReplyTo, IReadOnlyCollection<string> References);
