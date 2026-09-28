namespace EmailSender.Models;

public sealed class CampaignStoreDocument
{
    public int SchemaVersion { get; set; } = 1;
    public List<CampaignState> Campaigns { get; set; } = [];
}

public sealed class CampaignState
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
    public string DefaultSubject { get; set; } = string.Empty;
    public string DefaultTemplate { get; set; } = string.Empty;
    public List<string> DefaultAttachments { get; set; } = [];
    public List<CampaignRecipientState> Recipients { get; set; } = [];
}

public sealed class CampaignRecipientState
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Organization { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Template { get; set; } = string.Empty;
    public string PresetName { get; set; } = string.Empty;
    public string HtmlBody { get; set; } = string.Empty;
    public string AttachmentDisplayName { get; set; } = string.Empty;
    public List<string> Attachments { get; set; } = [];
    public MessageDeliveryState? OriginalMessage { get; set; }
    public ReplyCheckState ReplyCheck { get; set; } = new();
    public List<MessageDeliveryState> FollowUps { get; set; } = [];
}

public sealed class MessageDeliveryState
{
    public DateTimeOffset SentAt { get; set; }
    public string Subject { get; set; } = string.Empty;
    public List<string> Attachments { get; set; } = [];
    public string? ProviderMessageId { get; set; }
    public string? ProviderThreadId { get; set; }
    public string? InternetMessageId { get; set; }
}

public sealed class ReplyCheckState
{
    public DateTimeOffset? CheckedAt { get; set; }
    public string Status { get; set; } = "not-checked";
    public string? MatchedMessageId { get; set; }
    public string? Notes { get; set; }
}

public sealed record CreateCampaignRequest(
    string Name,
    string? DefaultSubject,
    string? DefaultTemplate,
    IReadOnlyCollection<string>? DefaultAttachments);

public sealed record RenameCampaignRequest(string Name);

public sealed record UpdateCampaignDefaultsRequest(
    string? DefaultSubject,
    string? DefaultTemplate,
    IReadOnlyCollection<string>? DefaultAttachments);

public sealed record SaveCampaignRecipientRequest(
    string Name,
    string Email,
    string Organization,
    string? Subject,
    string? Template,
    IReadOnlyCollection<string>? Attachments,
    string? PresetName = null,
    string? HtmlBody = null,
    string? AttachmentDisplayName = null);
