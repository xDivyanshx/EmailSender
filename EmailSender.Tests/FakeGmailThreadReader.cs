using EmailSender.Models;
using EmailSender.Services;

namespace EmailSender.Tests;

internal sealed class FakeGmailThreadReader : IGmailThreadReader
{
    public Dictionary<string, GmailThreadSnapshot> Threads { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, GmailReplyView> Messages { get; } = new(StringComparer.Ordinal);

    public Task<GmailThreadSnapshot> GetThreadAsync(string threadId, CancellationToken cancellationToken) =>
        Task.FromResult(Threads.TryGetValue(threadId, out GmailThreadSnapshot? thread)
            ? thread
            : throw new KeyNotFoundException($"Thread '{threadId}' was not configured."));

    public Task<GmailReplyView> GetMessageAsync(string messageId, CancellationToken cancellationToken) =>
        Task.FromResult(Messages.TryGetValue(messageId, out GmailReplyView? message)
            ? message
            : throw new KeyNotFoundException($"Message '{messageId}' was not configured."));
}
