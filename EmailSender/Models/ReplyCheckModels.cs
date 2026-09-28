namespace EmailSender.Models;

public sealed record GmailThreadMessage(
    string Id,
    DateTimeOffset? Date,
    string From,
    string Subject,
    string? InternetMessageId,
    string? AutoSubmitted,
    string? Precedence,
    string? AutoReplyHeader);

public sealed record GmailThreadSnapshot(string ThreadId, IReadOnlyCollection<GmailThreadMessage> Messages);

public sealed record RecipientReplyCheckResult(
    string Email,
    string Status,
    string? MatchedMessageId,
    string? Notes);

public sealed record CampaignReplyCheckResult(
    Guid CampaignId,
    DateTimeOffset CheckedAt,
    int TotalRecipients,
    int CheckedThreads,
    int Replied,
    int NoReply,
    int Automated,
    int Bounced,
    int Uncertain,
    int MissingThread,
    IReadOnlyCollection<RecipientReplyCheckResult> Recipients);

public sealed record WorkspaceReplyCheckResult(
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    int Campaigns,
    int TotalRecipients,
    int CheckedThreads,
    int Replied,
    int NoReply,
    int Automated,
    int Bounced,
    int Uncertain,
    int MissingThread,
    IReadOnlyCollection<CampaignReplyCheckResult> CampaignResults);

public sealed record GmailAttachmentMetadata(string FileName, string MimeType, long Size);

public sealed record GmailReplyView(
    string MessageId,
    string? ThreadId,
    string From,
    string To,
    string Subject,
    DateTimeOffset? ReceivedAt,
    string PlainTextBody,
    string SanitizedHtmlBody,
    IReadOnlyCollection<GmailAttachmentMetadata> Attachments);
