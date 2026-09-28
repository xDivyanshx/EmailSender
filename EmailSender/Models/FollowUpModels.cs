namespace EmailSender.Models;

public sealed record FollowUpPreviewRequest(
    IReadOnlyCollection<string>? Emails,
    string Body,
    string? Subject,
    IReadOnlyCollection<string>? Attachments,
    IReadOnlyCollection<RecipientMessageOverride>? RecipientOverrides,
    bool AllowSubjectChange = false);

public sealed record SendFollowUpsRequest(
    IReadOnlyCollection<string>? Emails,
    string Body,
    string? Subject,
    IReadOnlyCollection<string>? Attachments,
    IReadOnlyCollection<RecipientMessageOverride>? RecipientOverrides,
    bool AllowSubjectChange,
    bool Confirm,
    string? ConfirmationText);

public sealed record FollowUpPreviewItem(
    string Name,
    string Email,
    string Organization,
    string ReplyStatus,
    DateTimeOffset LastOutboundAt,
    double ElapsedHours,
    string OriginalSubject,
    string Subject,
    string RenderedBody,
    bool SubjectChanged,
    bool SubjectChangeApproved,
    IReadOnlyCollection<AttachmentPreview> Attachments,
    bool Eligible,
    IReadOnlyCollection<string> Errors);

public sealed record FollowUpPreview(
    Guid CampaignId,
    int TotalRecipients,
    int SelectedRecipients,
    int EligibleRecipients,
    IReadOnlyCollection<FollowUpPreviewItem> Recipients);

public sealed record FollowUpSendItem(
    string Email,
    bool Success,
    string Status,
    string? ProviderMessageId = null,
    string? ProviderThreadId = null,
    string? Error = null);

public sealed record FollowUpSendResult(
    Guid CampaignId,
    int Requested,
    int Attempted,
    int Sent,
    int Failed,
    int Skipped,
    IReadOnlyCollection<FollowUpSendItem> Results);
