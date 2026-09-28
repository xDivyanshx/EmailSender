namespace EmailSender.Models;

public sealed record RecipientMessageOverride(
    string Email,
    string? Subject,
    IReadOnlyCollection<string>? Attachments,
    string? Body = null);

public sealed record AttachmentSpec(string Path, string? DisplayName = null);

public sealed record OriginalEmailSelection(
    IReadOnlyCollection<string>? Emails,
    string? Subject,
    IReadOnlyCollection<string>? Attachments,
    IReadOnlyCollection<RecipientMessageOverride>? RecipientOverrides,
    bool AllowResend = false,
    Guid? CampaignId = null,
    string? Body = null);

public sealed record SendOriginalEmailsRequest(
    Guid? CampaignId,
    IReadOnlyCollection<string>? Emails,
    string? Subject,
    IReadOnlyCollection<string>? Attachments,
    IReadOnlyCollection<RecipientMessageOverride>? RecipientOverrides,
    bool AllowResend,
    bool Confirm,
    string? ConfirmationText,
    string? Body = null);
