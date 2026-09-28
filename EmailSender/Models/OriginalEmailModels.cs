namespace EmailSender.Models;

public sealed record AttachmentPreview(string FileName, bool Exists);

public sealed record OriginalEmailPreviewItem(
    string Name,
    string Email,
    string Organization,
    string Subject,
    string Template,
    string BodySource,
    string RenderedBody,
    IReadOnlyCollection<AttachmentPreview> Attachments,
    bool AlreadySent,
    DateTimeOffset? PreviouslySentAt,
    bool ResendApproved,
    bool Eligible,
    IReadOnlyCollection<string> Errors);

public sealed record OriginalEmailPreview(
    int TotalRecipients,
    int SelectedRecipients,
    int EligibleRecipients,
    int AlreadySentRecipients,
    int InvalidRecipients,
    int SentToday,
    int DailyLimit,
    int RemainingToday,
    IReadOnlyCollection<OriginalEmailPreviewItem> Recipients);

public sealed record OriginalEmailSendItem(
    string Email,
    bool Success,
    string Status,
    string? ProviderMessageId = null,
    string? ProviderThreadId = null,
    string? Error = null);

public sealed record OriginalEmailSendResult(
    int Requested,
    int Attempted,
    int Sent,
    int Failed,
    int Skipped,
    IReadOnlyCollection<OriginalEmailSendItem> Results);
