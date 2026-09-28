using EmailSender.Models;
using System.Text.Json;

namespace EmailSender.Services;

public sealed class OrganizationTrackerService
{
    private sealed record OrganizationRecipient(CampaignState Campaign, CampaignRecipientState Recipient);

    private static readonly HashSet<string> AllowedOutcomes = new(StringComparer.OrdinalIgnoreCase)
    {
        "unreviewed", "no-response", "interested", "not-interested", "follow-up", "other"
    };

    private readonly string _path;
    private readonly CampaignStoreService _campaigns;
    private readonly object _sync = new();
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public OrganizationTrackerService(IConfiguration config, CampaignStoreService campaigns)
    {
        _path = config["FilePaths:OrganizationTracking"]
            ?? "../../../../resources/app-data/organization-tracking.json";
        _campaigns = campaigns;
    }

    public IReadOnlyCollection<OrganizationSummary> GetAll()
    {
        lock (_sync)
        {
            Dictionary<string, OrganizationTrackingState> tracking = Load().Organizations
                .ToDictionary(item => item.OrganizationKey, StringComparer.OrdinalIgnoreCase);

            return _campaigns.GetAll()
                .SelectMany(campaign => campaign.Recipients
                    .Where(recipient => !string.IsNullOrWhiteSpace(recipient.Organization))
                    .Select(recipient => new OrganizationRecipient(campaign, recipient)))
                .GroupBy(item => NormalizeKey(item.Recipient.Organization), StringComparer.OrdinalIgnoreCase)
                .Select(group => BuildSummary(group, tracking.GetValueOrDefault(group.Key)))
                .OrderBy(summary => summary.Organization, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    public OrganizationSummary Update(UpdateOrganizationTrackingRequest request)
    {
        string key = NormalizeKey(request.Organization);
        string outcome = request.Outcome.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Organization is required.");
        if (!AllowedOutcomes.Contains(outcome)) throw new ArgumentException("Organization outcome is invalid.");

        lock (_sync)
        {
            OrganizationTrackingDocument document = Load();
            OrganizationTrackingState? state = document.Organizations
                .SingleOrDefault(item => string.Equals(item.OrganizationKey, key, StringComparison.OrdinalIgnoreCase));
            if (state is null)
            {
                state = new OrganizationTrackingState { OrganizationKey = key };
                document.Organizations.Add(state);
            }

            state.Outcome = outcome;
            state.Notes = request.Notes?.Trim() ?? string.Empty;
            state.UpdatedAt = DateTimeOffset.Now;
            Save(document);
        }

        return GetAll().Single(summary => NormalizeKey(summary.Organization) == key);
    }

    private static OrganizationSummary BuildSummary(
        IGrouping<string, OrganizationRecipient> group,
        OrganizationTrackingState? tracking)
    {
        var rows = group.ToArray();
        string organization = rows
            .Select(row => NormalizeDisplayName(row.Recipient.Organization))
            .GroupBy(name => name, StringComparer.Ordinal)
            .OrderByDescending(names => names.Count())
            .ThenByDescending(names => names.Key.Count(char.IsUpper))
            .ThenBy(names => names.Key, StringComparer.OrdinalIgnoreCase)
            .First().Key;

        OrganizationContactSummary[] contacts = rows.Select(row =>
        {
            CampaignState campaign = row.Campaign;
            CampaignRecipientState recipient = row.Recipient;
            DateTimeOffset? lastOutbound = recipient.FollowUps.LastOrDefault()?.SentAt ?? recipient.OriginalMessage?.SentAt;
            return new OrganizationContactSummary(
                campaign.Id,
                campaign.Name,
                recipient.Id,
                recipient.Name,
                recipient.Email,
                recipient.OriginalMessage is not null,
                lastOutbound,
                recipient.ReplyCheck.Status,
                recipient.ReplyCheck.CheckedAt,
                recipient.ReplyCheck.MatchedMessageId is not null);
        }).OrderBy(contact => contact.Name, StringComparer.OrdinalIgnoreCase).ToArray();

        return new OrganizationSummary(
            organization,
            tracking?.Outcome ?? "unreviewed",
            tracking?.Notes ?? string.Empty,
            tracking?.UpdatedAt,
            contacts.Length,
            contacts.Select(contact => contact.CampaignId).Distinct().Count(),
            contacts.Count(contact => contact.OriginalSent),
            contacts.Count(contact => contact.ReplyStatus == "replied"),
            contacts.Count(contact => contact.ReplyStatus == "no-reply"),
            contacts.Count(contact => contact.ReplyStatus == "automated"),
            contacts.Count(contact => contact.ReplyStatus == "bounced"),
            rows.Sum(row => row.Recipient.FollowUps.Count),
            contacts.Max(contact => contact.LastOutboundAt),
            contacts.Where(contact => contact.ReplyStatus == "replied").Max(contact => contact.ReplyCheckedAt),
            contacts.Max(contact => contact.ReplyCheckedAt),
            contacts);
    }

    private OrganizationTrackingDocument Load()
    {
        if (!File.Exists(_path)) return new();
        OrganizationTrackingDocument document = JsonSerializer.Deserialize<OrganizationTrackingDocument>(File.ReadAllText(_path), _jsonOptions) ?? new();
        if (document.SchemaVersion != 1) throw new InvalidDataException($"Unsupported organization tracking schema version {document.SchemaVersion}.");
        return document;
    }

    private void Save(OrganizationTrackingDocument document)
    {
        string? directory = Path.GetDirectoryName(Path.GetFullPath(_path));
        if (directory is not null) Directory.CreateDirectory(directory);
        string temporaryPath = _path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(document, _jsonOptions));
        File.Move(temporaryPath, _path, overwrite: true);
    }

    private static string NormalizeKey(string value) =>
        string.Join(' ', value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();

    private static string NormalizeDisplayName(string value) =>
        string.Join(' ', value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries));
}
