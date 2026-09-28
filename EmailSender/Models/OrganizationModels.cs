namespace EmailSender.Models;

public sealed class OrganizationTrackingDocument
{
    public int SchemaVersion { get; set; } = 1;
    public List<OrganizationTrackingState> Organizations { get; set; } = [];
}

public sealed class OrganizationTrackingState
{
    public string OrganizationKey { get; set; } = string.Empty;
    public string Outcome { get; set; } = "unreviewed";
    public string Notes { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
}

public sealed record UpdateOrganizationTrackingRequest(string Organization, string Outcome, string? Notes);

public sealed record OrganizationContactSummary(
    Guid CampaignId,
    string CampaignName,
    Guid RecipientId,
    string Name,
    string Email,
    bool OriginalSent,
    DateTimeOffset? LastOutboundAt,
    string ReplyStatus,
    DateTimeOffset? ReplyCheckedAt,
    bool ReplyAvailable);

public sealed record OrganizationSummary(
    string Organization,
    string Outcome,
    string Notes,
    DateTimeOffset? TrackingUpdatedAt,
    int Contacts,
    int Campaigns,
    int Sent,
    int Replied,
    int AwaitingReply,
    int Automated,
    int Bounced,
    int FollowUps,
    DateTimeOffset? LastOutboundAt,
    DateTimeOffset? LastReplyAt,
    DateTimeOffset? LastCheckedAt,
    IReadOnlyCollection<OrganizationContactSummary> ContactDetails);
