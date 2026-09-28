using EmailSender.Models;
using System.Net.Mail;

namespace EmailSender.Services;

public sealed class OriginalEmailService
{
    public const string RequiredConfirmationText = "SEND ORIGINAL EMAILS";

    private readonly IConfiguration _config;
    private readonly CsvReaderService _csvReader;
    private readonly TemplateService _templates;
    private readonly SentMailTrackerService _tracker;
    private readonly IEmailDispatchService _dispatch;
    private readonly CampaignStoreService _campaigns;
    private readonly AttachmentService _attachments;
    private readonly BatchOperationService _batch;
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public OriginalEmailService(
        IConfiguration config,
        CsvReaderService csvReader,
        TemplateService templates,
        SentMailTrackerService tracker,
        IEmailDispatchService dispatch,
        CampaignStoreService campaigns,
        AttachmentService attachments,
        BatchOperationService batch)
    {
        _config = config;
        _csvReader = csvReader;
        _templates = templates;
        _tracker = tracker;
        _dispatch = dispatch;
        _campaigns = campaigns;
        _attachments = attachments;
        _batch = batch;
    }

    public OriginalEmailPreview Preview(OriginalEmailSelection request)
    {
        string defaultTemplate = GetRequiredSetting("FilePaths:MailTemplate");
        string defaultSubject = GetRequiredSetting("Mail:Subject");
        int dailyLimit = GetDailyLimit();

        if (!_templates.TemplateExists(defaultTemplate))
        {
            throw new InvalidOperationException($"Default template '{defaultTemplate}' was not found.");
        }

        HashSet<string>? selection = request.Emails is { Count: > 0 }
            ? new HashSet<string>(request.Emails, StringComparer.OrdinalIgnoreCase)
            : null;
        Dictionary<string, RecipientMessageOverride> overrides = request.RecipientOverrides?
            .Where(item => !string.IsNullOrWhiteSpace(item.Email))
            .GroupBy(item => item.Email, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase)
            ?? new(StringComparer.OrdinalIgnoreCase);

        List<Recipient> allRecipients = request.CampaignId is Guid campaignId
            ? GetCampaignRecipients(campaignId)
            : _csvReader.ReadRecipients();
        List<Recipient> recipients = selection is null
            ? allRecipients
            : allRecipients.Where(recipient => selection.Contains(recipient.Email)).ToList();

        List<OriginalEmailPreviewItem> items = recipients.Select(recipient =>
        {
            List<string> errors = [];
            if (string.IsNullOrWhiteSpace(recipient.Name)) errors.Add("Name is required.");
            if (!MailAddress.TryCreate(recipient.Email, out _)) errors.Add("Email address is invalid.");

            string template = !string.IsNullOrWhiteSpace(recipient.Template) && _templates.TemplateExists(recipient.Template)
                ? recipient.Template
                : _templates.TemplateExists(recipient.OrganizationKey)
                    ? recipient.OrganizationKey
                    : defaultTemplate;
            bool hasRecipientTemplate = !string.IsNullOrWhiteSpace(recipient.Template) && _templates.TemplateExists(recipient.Template);
            string bodySource = !string.IsNullOrWhiteSpace(recipient.HtmlBody)
                ? "recipient-html"
                : hasRecipientTemplate
                ? $"template:{template}"
                : !string.IsNullOrWhiteSpace(request.Body)
                    ? "manual-html"
                    : $"template:{template}";
            string renderedBody = bodySource == "recipient-html"
                ? TemplateService.FormatBody(recipient.HtmlBody, recipient.FirstName, recipient.Organization)
                : bodySource == "manual-html"
                ? TemplateService.FormatBody(request.Body!, recipient.FirstName, recipient.Organization)
                : _templates.GetFormattedTemplate(template, template, recipient.FirstName, recipient.Organization);
            if (string.IsNullOrWhiteSpace(renderedBody)) errors.Add("Email body is required.");
            overrides.TryGetValue(recipient.Email, out RecipientMessageOverride? recipientOverride);
            string? overrideBody = recipientOverride?.Body;
            if (!string.IsNullOrWhiteSpace(overrideBody))
            {
                bodySource = "recipient-preset";
                renderedBody = TemplateService.FormatBody(overrideBody, recipient.FirstName, recipient.Organization);
            }
            string subject = FirstNonEmpty(recipientOverride?.Subject, recipient.Subject, request.Subject, defaultSubject);
            string[] attachmentNames = CombineAttachments(request.Attachments, recipient.Attachments, recipientOverride?.Attachments)
                .Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            List<AttachmentPreview> attachmentPreviews = attachmentNames
                .Select(name => new AttachmentPreview(name, File.Exists(_attachments.Resolve(request.CampaignId, name))))
                .ToList();
            string[] missingAttachments = attachmentPreviews.Where(attachment => !attachment.Exists)
                .Select(attachment => attachment.FileName).ToArray();
            if (missingAttachments.Length > 0)
                errors.Add($"Missing attachments: {string.Join(", ", missingAttachments)}.");

            bool alreadySent = _tracker.HasBeenSent(recipient.Email);
            DateTimeOffset? previouslySentAt = _tracker.GetSentAt(recipient.Email);
            bool resendApproved = alreadySent && request.AllowResend;
            return new OriginalEmailPreviewItem(
                recipient.Name,
                recipient.Email,
                recipient.Organization,
                subject,
                template,
                bodySource,
                renderedBody,
                attachmentPreviews,
                alreadySent,
                previouslySentAt,
                resendApproved,
                (!alreadySent || resendApproved) && errors.Count == 0,
                errors);
        }).ToList();

        int sentToday = _tracker.GetSentTodayCount();
        return new OriginalEmailPreview(
            allRecipients.Count,
            items.Count,
            items.Count(item => item.Eligible),
            items.Count(item => item.AlreadySent),
            items.Count(item => item.Errors.Count > 0),
            sentToday,
            dailyLimit,
            Math.Max(0, dailyLimit - sentToday),
            items);
    }

    public async Task<OriginalEmailSendResult?> TrySendAsync(SendOriginalEmailsRequest request, CancellationToken cancellationToken = default)
    {
        if (!await _sendLock.WaitAsync(0)) return null;
        bool batchStarted = false;

        try
        {
            if (request.CampaignId is Guid campaignId && _campaigns.Get(campaignId) is null)
                throw new KeyNotFoundException($"Campaign '{campaignId}' was not found.");

            OriginalEmailPreview preview = Preview(new(
                request.Emails,
                request.Subject,
                request.Attachments,
                request.RecipientOverrides,
                request.AllowResend,
                request.CampaignId,
                request.Body));
            string[] preflightErrors = preview.Recipients
                .SelectMany(item => item.Errors.Select(error => $"{item.Email}: {error}"))
                .ToArray();
            if (preflightErrors.Length > 0) throw new BatchPreflightException(preflightErrors);
            if (!_batch.TryStart("original", preview.Recipients.Count)) return null;
            batchStarted = true;
            Dictionary<string, Recipient> recipients = (request.CampaignId is Guid sourceCampaignId
                    ? GetCampaignRecipients(sourceCampaignId)
                    : _csvReader.ReadRecipients())
                .ToDictionary(recipient => recipient.Email, StringComparer.OrdinalIgnoreCase);
            List<OriginalEmailSendItem> results = [];
            int attempted = 0;
            int sent = 0;
            int failed = 0;

            foreach (OriginalEmailPreviewItem item in preview.Recipients)
            {
                if (_batch.IsCancellationRequested())
                {
                    results.Add(new(item.Email, false, "cancelled"));
                    _batch.RecordResult(false, false, true);
                    continue;
                }
                _batch.SetCurrent(item.Email);
                if (!item.Eligible)
                {
                    results.Add(new(item.Email, false, item.AlreadySent ? "already-sent-resend-not-approved" : "invalid"));
                    _batch.RecordResult(false, false, true);
                    continue;
                }

                if (_tracker.GetSentTodayCount() >= preview.DailyLimit)
                {
                    results.Add(new(item.Email, false, "daily-limit-reached"));
                    _batch.RecordResult(false, false, true);
                    continue;
                }

                // Recheck immediately before dispatch in case local state changed after preview.
                if (_tracker.HasBeenSent(item.Email) && !request.AllowResend)
                {
                    results.Add(new(item.Email, false, "already-sent"));
                    _batch.RecordResult(false, false, true);
                    continue;
                }

                Recipient recipient = recipients[item.Email];
                attempted++;
                EmailDispatchResult delivery = _dispatch.TrySendMail(
                    item.Subject,
                    item.RenderedBody,
                    recipient.Email,
                    recipient.Name,
                            item.Attachments.Select(attachment => new AttachmentSpec(
                                _attachments.Resolve(request.CampaignId, attachment.FileName),
                                string.IsNullOrWhiteSpace(recipient.AttachmentDisplayName) ? null : recipient.AttachmentDisplayName)));

                if (delivery.Success)
                {
                    if (request.CampaignId is Guid deliveryCampaignId)
                    {
                        _campaigns.RecordOriginalDelivery(
                            deliveryCampaignId,
                            recipient,
                            item.Subject,
                            item.Attachments.Select(attachment => attachment.FileName).ToArray(),
                            delivery);
                    }
                    _tracker.RecordSuccessfulDelivery(recipient.Email, "original", delivery);
                    sent++;
                    results.Add(new(item.Email, true, "sent", delivery.ProviderMessageId, delivery.ProviderThreadId));
                    _batch.RecordResult(true, false, false);
                }
                else
                {
                    failed++;
                    results.Add(new(item.Email, false, "send-failed", Error: delivery.Error));
                    _batch.RecordResult(false, true, false);
                }
            }

            return new OriginalEmailSendResult(
                preview.SelectedRecipients,
                attempted,
                sent,
                failed,
                results.Count - attempted,
                results);
        }
        finally
        {
            if (batchStarted) _batch.Complete();
            _sendLock.Release();
        }
    }

    private string GetRequiredSetting(string key) =>
        _config[key] ?? throw new InvalidOperationException($"Configuration '{key}' is missing.");

    private int GetDailyLimit() =>
        int.TryParse(_config["Mail:DailyLimit"], out int limit) && limit > 0
            ? limit
            : throw new InvalidOperationException("Mail:DailyLimit must be a positive integer.");

    private static string FirstNonEmpty(params string?[] values) =>
        values.First(value => !string.IsNullOrWhiteSpace(value))!.Trim();

    private static IEnumerable<string> CombineAttachments(
        IReadOnlyCollection<string>? campaignAttachments,
        IReadOnlyCollection<string>? recipientAttachments,
        IReadOnlyCollection<string>? recipientOverrideAttachments)
    {
        foreach (string path in campaignAttachments ?? []) yield return path;
        foreach (string path in recipientAttachments ?? []) yield return path;
        foreach (string path in recipientOverrideAttachments ?? []) yield return path;
    }

    private List<Recipient> GetCampaignRecipients(Guid campaignId)
    {
        CampaignState campaign = _campaigns.Get(campaignId)
            ?? throw new KeyNotFoundException($"Campaign '{campaignId}' was not found.");
        return campaign.Recipients.Select(recipient =>
        {
            string organizationKey = recipient.Organization.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
            bool isPersistedFallback = string.Equals(recipient.Template, campaign.DefaultTemplate, StringComparison.OrdinalIgnoreCase)
                || string.Equals(recipient.Template, organizationKey, StringComparison.OrdinalIgnoreCase);
            return new Recipient
            {
                Name = recipient.Name,
                Email = recipient.Email,
                Organization = recipient.Organization,
                Subject = recipient.Subject,
                Template = isPersistedFallback ? string.Empty : recipient.Template,
                PresetName = recipient.PresetName,
                HtmlBody = recipient.HtmlBody,
                AttachmentDisplayName = recipient.AttachmentDisplayName,
                Attachments = recipient.Attachments.ToList(),
                AttachmentFileName = recipient.Attachments.FirstOrDefault() ?? string.Empty
            };
        }).ToList();
    }
}
