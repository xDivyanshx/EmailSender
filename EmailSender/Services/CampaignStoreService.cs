using EmailSender.Models;
using System.Text.Json;

namespace EmailSender.Services;

public sealed class CampaignStoreService
{
    private readonly string _filePath;
    private readonly object _sync = new();
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public CampaignStoreService(IConfiguration config)
    {
        _filePath = config["FilePaths:CampaignStore"]
            ?? "../../../../resources/app-data/campaigns.json";
    }

    public IReadOnlyCollection<CampaignState> GetAll()
    {
        lock (_sync) return Load().Campaigns.OrderByDescending(c => c.UpdatedAt).ToArray();
    }

    public CampaignState? Get(Guid id)
    {
        lock (_sync) return Load().Campaigns.SingleOrDefault(c => c.Id == id);
    }

    public CampaignState Create(CreateCampaignRequest request, string fallbackSubject, string fallbackTemplate)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new ArgumentException("Campaign name is required.");

        lock (_sync)
        {
            CampaignStoreDocument document = Load();
            CampaignState campaign = new()
            {
                Name = request.Name.Trim(),
                DefaultSubject = string.IsNullOrWhiteSpace(request.DefaultSubject) ? fallbackSubject : request.DefaultSubject.Trim(),
                DefaultTemplate = string.IsNullOrWhiteSpace(request.DefaultTemplate) ? fallbackTemplate : request.DefaultTemplate.Trim(),
                DefaultAttachments = NormalizePaths(request.DefaultAttachments)
            };
            document.Campaigns.Add(campaign);
            Save(document);
            return campaign;
        }
    }

    public CampaignRecipientState AddRecipient(Guid campaignId, SaveCampaignRecipientRequest request)
    {
        ValidateRecipient(request);
        lock (_sync)
        {
            CampaignStoreDocument document = Load();
            CampaignState campaign = FindCampaign(document, campaignId);
            if (campaign.Recipients.Any(item => string.Equals(item.Email, request.Email, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"Recipient '{request.Email}' already exists in this campaign.");
            CampaignRecipientState recipient = NewRecipient(request);
            campaign.Recipients.Add(recipient);
            campaign.UpdatedAt = DateTimeOffset.Now;
            Save(document);
            return recipient;
        }
    }

    public CampaignState Rename(Guid campaignId, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Campaign name is required.");
        lock (_sync)
        {
            CampaignStoreDocument document = Load();
            CampaignState campaign = FindCampaign(document, campaignId);
            campaign.Name = name.Trim();
            campaign.UpdatedAt = DateTimeOffset.Now;
            Save(document);
            return campaign;
        }
    }

    public CampaignState UpdateDefaults(Guid campaignId, UpdateCampaignDefaultsRequest request)
    {
        lock (_sync)
        {
            CampaignStoreDocument document = Load();
            CampaignState campaign = FindCampaign(document, campaignId);
            campaign.DefaultSubject = request.DefaultSubject?.Trim() ?? string.Empty;
            campaign.DefaultTemplate = request.DefaultTemplate?.Trim() ?? string.Empty;
            campaign.DefaultAttachments = NormalizePaths(request.DefaultAttachments);
            campaign.UpdatedAt = DateTimeOffset.Now;
            Save(document);
            return campaign;
        }
    }

    public CampaignState Duplicate(Guid campaignId)
    {
        lock (_sync)
        {
            CampaignStoreDocument document = Load();
            CampaignState source = FindCampaign(document, campaignId);
            CampaignState copy = new()
            {
                Name = $"{source.Name} Copy",
                DefaultSubject = source.DefaultSubject,
                DefaultTemplate = source.DefaultTemplate,
                DefaultAttachments = source.DefaultAttachments.ToList(),
                Recipients = source.Recipients.Select(recipient => new CampaignRecipientState
                {
                    Name = recipient.Name,
                    Email = recipient.Email,
                    Organization = recipient.Organization,
                    Subject = recipient.Subject,
                    Template = recipient.Template,
                    Attachments = recipient.Attachments.ToList()
                }).ToList()
            };
            document.Campaigns.Add(copy);
            Save(document);
            return copy;
        }
    }

    public void Delete(Guid campaignId)
    {
        lock (_sync)
        {
            CampaignStoreDocument document = Load();
            CampaignState campaign = FindCampaign(document, campaignId);
            document.Campaigns.Remove(campaign);
            Save(document);
        }
    }

    public CampaignRecipientState UpdateRecipient(Guid campaignId, Guid recipientId, SaveCampaignRecipientRequest request)
    {
        ValidateRecipient(request);
        lock (_sync)
        {
            CampaignStoreDocument document = Load();
            CampaignState campaign = FindCampaign(document, campaignId);
            CampaignRecipientState recipient = campaign.Recipients.SingleOrDefault(item => item.Id == recipientId)
                ?? throw new KeyNotFoundException($"Recipient '{recipientId}' was not found.");
            if (campaign.Recipients.Any(item => item.Id != recipientId && string.Equals(item.Email, request.Email, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"Recipient '{request.Email}' already exists in this campaign.");
            ApplyRecipient(recipient, request);
            campaign.UpdatedAt = DateTimeOffset.Now;
            Save(document);
            return recipient;
        }
    }

    public void DeleteRecipient(Guid campaignId, Guid recipientId)
    {
        lock (_sync)
        {
            CampaignStoreDocument document = Load();
            CampaignState campaign = FindCampaign(document, campaignId);
            CampaignRecipientState recipient = campaign.Recipients.SingleOrDefault(item => item.Id == recipientId)
                ?? throw new KeyNotFoundException($"Recipient '{recipientId}' was not found.");
            campaign.Recipients.Remove(recipient);
            campaign.UpdatedAt = DateTimeOffset.Now;
            Save(document);
        }
    }

    public int ImportRecipients(Guid campaignId, IReadOnlyCollection<Recipient> imported)
    {
        lock (_sync)
        {
            CampaignStoreDocument document = Load();
            CampaignState campaign = FindCampaign(document, campaignId);
            foreach (Recipient source in imported)
            {
                SaveCampaignRecipientRequest request = new(
                    source.Name,
                    source.Email,
                    source.Organization,
                    source.Subject,
                    source.Template,
                    source.Attachments,
                    source.PresetName,
                    source.HtmlBody,
                    source.AttachmentDisplayName);
                ValidateRecipient(request);
                CampaignRecipientState? existing = campaign.Recipients.SingleOrDefault(item =>
                    string.Equals(item.Email, source.Email, StringComparison.OrdinalIgnoreCase));
                if (existing is null) campaign.Recipients.Add(NewRecipient(request));
                else ApplyRecipient(existing, request);
            }
            campaign.UpdatedAt = DateTimeOffset.Now;
            Save(document);
            return imported.Count;
        }
    }

    public bool IsAttachmentReferenced(string fileName)
    {
        lock (_sync)
        {
            CampaignStoreDocument document = Load();
            return document.Campaigns.Any(campaign =>
                campaign.DefaultAttachments.Contains(fileName, StringComparer.OrdinalIgnoreCase)
                || campaign.Recipients.Any(recipient =>
                    recipient.Attachments.Contains(fileName, StringComparer.OrdinalIgnoreCase)
                    || recipient.FollowUps.Any(followUp => followUp.Attachments.Contains(fileName, StringComparer.OrdinalIgnoreCase))));
        }
    }

    public IReadOnlyCollection<string> GetAttachmentReferences(Guid campaignId, string fileName)
    {
        lock (_sync)
        {
            CampaignStoreDocument document = Load();
            List<string> references = [];
            foreach (CampaignState campaign in document.Campaigns.Where(item => item.Id == campaignId))
            {
                if (campaign.DefaultAttachments.Contains(fileName, StringComparer.OrdinalIgnoreCase))
                    references.Add($"campaign '{campaign.Name}' common attachments");
                foreach (CampaignRecipientState recipient in campaign.Recipients)
                {
                    if (recipient.Attachments.Contains(fileName, StringComparer.OrdinalIgnoreCase))
                        references.Add($"{recipient.Email} in campaign '{campaign.Name}'");
                    if (recipient.FollowUps.Any(followUp => followUp.Attachments.Contains(fileName, StringComparer.OrdinalIgnoreCase)))
                        references.Add($"follow-up history for {recipient.Email} in campaign '{campaign.Name}'");
                }
            }
            return references.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }
    }

    public void RecordOriginalDelivery(
        Guid campaignId,
        Recipient recipient,
        string subject,
        IReadOnlyCollection<string> attachments,
        EmailDispatchResult delivery)
    {
        if (!delivery.Success) throw new ArgumentException("Only successful deliveries can be recorded.");

        lock (_sync)
        {
            CampaignStoreDocument document = Load();
            CampaignState campaign = document.Campaigns.SingleOrDefault(item => item.Id == campaignId)
                ?? throw new KeyNotFoundException($"Campaign '{campaignId}' was not found.");
            CampaignRecipientState state = campaign.Recipients.SingleOrDefault(item =>
                string.Equals(item.Email, recipient.Email, StringComparison.OrdinalIgnoreCase)) ?? new()
                {
                    Email = recipient.Email
                };

            if (!campaign.Recipients.Contains(state)) campaign.Recipients.Add(state);
            state.Name = recipient.Name;
            state.Organization = recipient.Organization;
            state.Subject = subject;
            state.Attachments = attachments.ToList();
            state.OriginalMessage = new()
            {
                SentAt = DateTimeOffset.Now,
                Subject = subject,
                Attachments = attachments.ToList(),
                ProviderMessageId = delivery.ProviderMessageId,
                ProviderThreadId = delivery.ProviderThreadId,
                InternetMessageId = delivery.InternetMessageId
            };
            state.ReplyCheck = new();
            campaign.UpdatedAt = DateTimeOffset.Now;
            Save(document);
        }
    }

    public void RecordFollowUp(
        Guid campaignId,
        string email,
        string subject,
        IReadOnlyCollection<string> attachments,
        EmailDispatchResult delivery)
    {
        if (!delivery.Success) throw new ArgumentException("Only successful deliveries can be recorded.");
        lock (_sync)
        {
            CampaignStoreDocument document = Load();
            CampaignState campaign = document.Campaigns.SingleOrDefault(item => item.Id == campaignId)
                ?? throw new KeyNotFoundException($"Campaign '{campaignId}' was not found.");
            CampaignRecipientState recipient = campaign.Recipients.SingleOrDefault(item =>
                string.Equals(item.Email, email, StringComparison.OrdinalIgnoreCase))
                ?? throw new KeyNotFoundException($"Recipient '{email}' was not found in campaign.");
            recipient.FollowUps.Add(new()
            {
                SentAt = DateTimeOffset.Now,
                Subject = subject,
                Attachments = attachments.ToList(),
                ProviderMessageId = delivery.ProviderMessageId,
                ProviderThreadId = delivery.ProviderThreadId,
                InternetMessageId = delivery.InternetMessageId
            });
            recipient.ReplyCheck = new();
            campaign.UpdatedAt = DateTimeOffset.Now;
            Save(document);
        }
    }

    public void RecordReplyChecks(
        Guid campaignId,
        DateTimeOffset checkedAt,
        IReadOnlyCollection<RecipientReplyCheckResult> results)
    {
        lock (_sync)
        {
            CampaignStoreDocument document = Load();
            CampaignState campaign = document.Campaigns.SingleOrDefault(item => item.Id == campaignId)
                ?? throw new KeyNotFoundException($"Campaign '{campaignId}' was not found.");
            foreach (RecipientReplyCheckResult result in results)
            {
                CampaignRecipientState? recipient = campaign.Recipients.SingleOrDefault(item =>
                    string.Equals(item.Email, result.Email, StringComparison.OrdinalIgnoreCase));
                if (recipient is null) continue;
                recipient.ReplyCheck = new()
                {
                    CheckedAt = checkedAt,
                    Status = result.Status,
                    MatchedMessageId = result.MatchedMessageId,
                    Notes = result.Notes
                };
            }
            campaign.UpdatedAt = checkedAt;
            Save(document);
        }
    }

    private CampaignStoreDocument Load()
    {
        if (!File.Exists(_filePath)) return new();
        string json = File.ReadAllText(_filePath);
        CampaignStoreDocument document = JsonSerializer.Deserialize<CampaignStoreDocument>(json, _jsonOptions) ?? new();
        if (document.SchemaVersion != 1)
            throw new InvalidDataException($"Unsupported campaign store schema version {document.SchemaVersion}.");
        return document;
    }

    private void Save(CampaignStoreDocument document)
    {
        string fullPath = Path.GetFullPath(_filePath);
        string directory = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(directory);
        string temporaryPath = fullPath + ".tmp";
        string backupPath = fullPath + ".bak";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(document, _jsonOptions));
        if (File.Exists(fullPath)) File.Copy(fullPath, backupPath, overwrite: true);
        File.Move(temporaryPath, fullPath, overwrite: true);
    }

    private static List<string> NormalizePaths(IReadOnlyCollection<string>? paths) =>
        paths?.Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];

    private static CampaignState FindCampaign(CampaignStoreDocument document, Guid campaignId) =>
        document.Campaigns.SingleOrDefault(item => item.Id == campaignId)
        ?? throw new KeyNotFoundException($"Campaign '{campaignId}' was not found.");

    private static CampaignRecipientState NewRecipient(SaveCampaignRecipientRequest request)
    {
        CampaignRecipientState recipient = new();
        ApplyRecipient(recipient, request);
        return recipient;
    }

    private static void ApplyRecipient(CampaignRecipientState recipient, SaveCampaignRecipientRequest request)
    {
        recipient.Name = request.Name.Trim();
        recipient.Email = request.Email.Trim();
        recipient.Organization = request.Organization.Trim();
        recipient.Subject = request.Subject?.Trim() ?? string.Empty;
        recipient.Template = request.Template?.Trim() ?? string.Empty;
        recipient.PresetName = request.PresetName?.Trim() ?? string.Empty;
        recipient.HtmlBody = request.HtmlBody?.Trim() ?? string.Empty;
        recipient.AttachmentDisplayName = request.AttachmentDisplayName?.Trim() ?? string.Empty;
        recipient.Attachments = NormalizePaths(request.Attachments);
    }

    private static void ValidateRecipient(SaveCampaignRecipientRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) throw new ArgumentException("Recipient name is required.");
        if (string.IsNullOrWhiteSpace(request.Email)) throw new ArgumentException("Recipient email is required.");
        if (!System.Net.Mail.MailAddress.TryCreate(request.Email, out _)) throw new ArgumentException("Recipient email is invalid.");
        if (string.IsNullOrWhiteSpace(request.Organization)) throw new ArgumentException("Recipient organization is required.");
    }
}
