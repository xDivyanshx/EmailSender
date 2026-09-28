using EmailSender.Models;

namespace EmailSender.Services;

public sealed class FollowUpService
{
    public const string RequiredConfirmationText = "SEND FOLLOW-UP EMAILS";

    private readonly CampaignStoreService _campaigns;
    private readonly IGmailThreadReader _threads;
    private readonly IEmailDispatchService _dispatch;
    private readonly AttachmentService _attachments;
    private readonly DeliveryLedgerService _ledger;
    private readonly BatchOperationService _batch;
    private readonly string _senderEmail;
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public FollowUpService(
        CampaignStoreService campaigns,
        IGmailThreadReader threads,
        IEmailDispatchService dispatch,
        IConfiguration config,
        AttachmentService attachments,
        DeliveryLedgerService ledger,
        BatchOperationService batch)
    {
        _campaigns = campaigns;
        _threads = threads;
        _dispatch = dispatch;
        _attachments = attachments;
        _ledger = ledger;
        _batch = batch;
        _senderEmail = config["Gmail:Account"]
            ?? throw new InvalidOperationException("Sender email is missing.");
    }

    public FollowUpPreview Preview(Guid campaignId, FollowUpPreviewRequest request)
    {
        CampaignState campaign = _campaigns.Get(campaignId)
            ?? throw new KeyNotFoundException($"Campaign '{campaignId}' was not found.");
        HashSet<string>? selection = request.Emails is { Count: > 0 }
            ? new(request.Emails, StringComparer.OrdinalIgnoreCase)
            : null;
        Dictionary<string, RecipientMessageOverride> overrides = request.RecipientOverrides?
            .Where(item => !string.IsNullOrWhiteSpace(item.Email))
            .GroupBy(item => item.Email, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase)
            ?? new(StringComparer.OrdinalIgnoreCase);
        List<CampaignRecipientState> selected = selection is null
            ? campaign.Recipients
            : campaign.Recipients.Where(recipient => selection.Contains(recipient.Email)).ToList();

        List<FollowUpPreviewItem> items = selected.Select(recipient =>
        {
            List<string> errors = [];
            MessageDeliveryState? original = recipient.OriginalMessage;
            if (original is null || string.IsNullOrWhiteSpace(original.ProviderThreadId))
                errors.Add("Original Gmail thread is unavailable.");
            if (!string.Equals(recipient.ReplyCheck.Status, "no-reply", StringComparison.Ordinal))
                errors.Add($"Recipient reply status is '{recipient.ReplyCheck.Status}'. Run Check Replies first.");
            if (string.IsNullOrWhiteSpace(request.Body)) errors.Add("Follow-up body is required.");

            overrides.TryGetValue(recipient.Email, out RecipientMessageOverride? recipientOverride);
            string originalSubject = original?.Subject ?? recipient.Subject;
            string defaultReplySubject = originalSubject.StartsWith("Re:", StringComparison.OrdinalIgnoreCase)
                ? originalSubject
                : $"Re: {originalSubject}";
            string subject = FirstNonEmpty(recipientOverride?.Subject, request.Subject, defaultReplySubject);
            bool subjectChanged = !SubjectsMatch(subject, originalSubject);
            if (subjectChanged && !request.AllowSubjectChange)
                errors.Add("Custom subject may break Gmail threading and requires explicit approval.");

            string[] attachmentNames = CombineAttachments(request.Attachments, recipientOverride?.Attachments)
                .Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            List<AttachmentPreview> attachments = attachmentNames
                .Select(name => new AttachmentPreview(name, File.Exists(_attachments.Resolve(campaignId, name))))
                .ToList();
            string[] missingAttachments = attachments.Where(attachment => !attachment.Exists)
                .Select(attachment => attachment.FileName).ToArray();
            if (missingAttachments.Length > 0)
                errors.Add($"Missing attachments: {string.Join(", ", missingAttachments)}.");
            DateTimeOffset lastOutboundAt = recipient.FollowUps.Count > 0
                ? recipient.FollowUps.Max(message => message.SentAt)
                : original?.SentAt ?? campaign.CreatedAt;
            double elapsedHours = Math.Max(0, (DateTimeOffset.Now - lastOutboundAt).TotalHours);

            return new FollowUpPreviewItem(
                recipient.Name,
                recipient.Email,
                recipient.Organization,
                recipient.ReplyCheck.Status,
                lastOutboundAt,
                elapsedHours,
                originalSubject,
                subject,
                FormatBody(request.Body, recipient),
                subjectChanged,
                subjectChanged && request.AllowSubjectChange,
                attachments,
                errors.Count == 0,
                errors);
        }).ToList();

        return new(campaignId, campaign.Recipients.Count, items.Count, items.Count(item => item.Eligible), items);
    }

    public async Task<FollowUpSendResult?> TrySendAsync(
        Guid campaignId,
        SendFollowUpsRequest request,
        CancellationToken cancellationToken)
    {
        if (!await _sendLock.WaitAsync(0, cancellationToken)) return null;
        bool batchStarted = false;
        try
        {
            FollowUpPreview preview = Preview(campaignId, new(
                request.Emails,
                request.Body,
                request.Subject,
                request.Attachments,
                request.RecipientOverrides,
                request.AllowSubjectChange));
            string[] attachmentErrors = preview.Recipients
                .SelectMany(item => item.Errors
                    .Where(error => error.Contains("attachment", StringComparison.OrdinalIgnoreCase))
                    .Select(error => $"{item.Email}: {error}"))
                .ToArray();
            if (attachmentErrors.Length > 0) throw new BatchPreflightException(attachmentErrors);
            if (!_batch.TryStart("follow-up", preview.Recipients.Count)) return null;
            batchStarted = true;
            CampaignState campaign = _campaigns.Get(campaignId)!;
            Dictionary<string, CampaignRecipientState> recipients = campaign.Recipients
                .ToDictionary(item => item.Email, StringComparer.OrdinalIgnoreCase);
            List<FollowUpSendItem> results = [];
            int attempted = 0;
            int sent = 0;
            int failed = 0;

            foreach (FollowUpPreviewItem item in preview.Recipients)
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
                    results.Add(new(item.Email, false, "not-eligible"));
                    _batch.RecordResult(false, false, true);
                    continue;
                }

                CampaignRecipientState recipient = recipients[item.Email];
                MessageDeliveryState original = recipient.OriginalMessage!;
                GmailThreadSnapshot thread = await _threads.GetThreadAsync(original.ProviderThreadId!, cancellationToken);
                RecipientReplyCheckResult current = ReplyCheckService.Classify(recipient, thread, _senderEmail);
                _campaigns.RecordReplyChecks(campaignId, DateTimeOffset.Now, [current]);
                if (current.Status != "no-reply")
                {
                    results.Add(new(item.Email, false, $"suppressed-{current.Status}"));
                    _batch.RecordResult(false, false, true);
                    continue;
                }

                GmailThreadMessage? originalThreadMessage = thread.Messages.SingleOrDefault(message =>
                    string.Equals(message.Id, original.ProviderMessageId, StringComparison.Ordinal));
                string? inReplyTo = originalThreadMessage?.InternetMessageId ?? original.InternetMessageId;
                if (string.IsNullOrWhiteSpace(inReplyTo))
                {
                    results.Add(new(item.Email, false, "missing-internet-message-id"));
                    _batch.RecordResult(false, false, true);
                    continue;
                }

                string[] references = recipient.FollowUps
                    .Select(message => message.InternetMessageId)
                    .Prepend(inReplyTo)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Cast<string>()
                    .ToArray();
                string[] attachmentNames = item.Attachments.Select(attachment => attachment.FileName).ToArray();
                string[] attachments = attachmentNames.Select(name => _attachments.Resolve(campaignId, name)).ToArray();
                attempted++;
                EmailDispatchResult delivery = _dispatch.TrySendReply(
                    item.Subject,
                    FormatBody(request.Body, recipient),
                    recipient.Email,
                    recipient.Name,
                    original.ProviderThreadId!,
                    inReplyTo,
                    references,
                    attachments);

                if (delivery.Success)
                {
                    _campaigns.RecordFollowUp(campaignId, recipient.Email, item.Subject, attachmentNames, delivery);
                    _ledger.Record(new(recipient.Email, DateTimeOffset.Now, "follow-up", delivery.ProviderMessageId, delivery.ProviderThreadId));
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

            return new(campaignId, preview.SelectedRecipients, attempted, sent, failed, results.Count - attempted, results);
        }
        finally
        {
            if (batchStarted) _batch.Complete();
            _sendLock.Release();
        }
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.First(value => !string.IsNullOrWhiteSpace(value))!.Trim();

    private static bool SubjectsMatch(string replySubject, string originalSubject) =>
        NormalizeSubject(replySubject).Equals(NormalizeSubject(originalSubject), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeSubject(string subject)
    {
        string normalized = subject.Trim();
        while (normalized.StartsWith("Re:", StringComparison.OrdinalIgnoreCase)) normalized = normalized[3..].Trim();
        return normalized;
    }

    private static string FirstName(string name) =>
        string.IsNullOrWhiteSpace(name) ? string.Empty : name.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];

    private static string FormatBody(string body, CampaignRecipientState recipient)
    {
        string formatted = body
            .Replace("{{name}}", FirstName(recipient.Name), StringComparison.Ordinal)
            .Replace("{name}", FirstName(recipient.Name), StringComparison.Ordinal)
            .Replace("{{organization}}", recipient.Organization, StringComparison.Ordinal)
            .Replace("{organization}", recipient.Organization, StringComparison.Ordinal);
        return formatted;
    }

    private static IEnumerable<string> CombineAttachments(
        IReadOnlyCollection<string>? common,
        IReadOnlyCollection<string>? recipient)
    {
        foreach (string path in common ?? []) yield return path;
        foreach (string path in recipient ?? []) yield return path;
    }
}
