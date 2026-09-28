using EmailSender.Models;
using MimeKit;
using System.Net.Http;
using System.Net.Sockets;

namespace EmailSender.Services;

public sealed class ReplyCheckService
{
    private readonly CampaignStoreService _campaigns;
    private readonly IGmailThreadReader _threads;
    private readonly string _senderEmail;
    private readonly SemaphoreSlim _checkLock = new(1, 1);

    public ReplyCheckService(CampaignStoreService campaigns, IGmailThreadReader threads, IConfiguration config)
    {
        _campaigns = campaigns;
        _threads = threads;
        _senderEmail = config["Gmail:Account"]
            ?? throw new InvalidOperationException("Sender email is missing.");
    }

    public async Task<CampaignReplyCheckResult?> CheckAsync(Guid campaignId, CancellationToken cancellationToken)
    {
        if (!await _checkLock.WaitAsync(0, cancellationToken)) return null;
        try
        {
            return await CheckCampaignAsync(campaignId, cancellationToken);
        }
        finally
        {
            _checkLock.Release();
        }
    }

    public async Task<WorkspaceReplyCheckResult?> CheckAllAsync(CancellationToken cancellationToken)
    {
        if (!await _checkLock.WaitAsync(0, cancellationToken)) return null;
        try
        {
            DateTimeOffset startedAt = DateTimeOffset.Now;
            List<CampaignReplyCheckResult> results = [];
            foreach (CampaignState campaign in _campaigns.GetAll().OrderBy(item => item.CreatedAt))
                results.Add(await CheckCampaignAsync(campaign.Id, cancellationToken));

            return new(
                startedAt,
                DateTimeOffset.Now,
                results.Count,
                results.Sum(result => result.TotalRecipients),
                results.Sum(result => result.CheckedThreads),
                results.Sum(result => result.Replied),
                results.Sum(result => result.NoReply),
                results.Sum(result => result.Automated),
                results.Sum(result => result.Bounced),
                results.Sum(result => result.Uncertain),
                results.Sum(result => result.MissingThread),
                results);
        }
        finally
        {
            _checkLock.Release();
        }
    }

    private async Task<CampaignReplyCheckResult> CheckCampaignAsync(Guid campaignId, CancellationToken cancellationToken)
    {
        CampaignState campaign = _campaigns.Get(campaignId)
            ?? throw new KeyNotFoundException($"Campaign '{campaignId}' was not found.");
        DateTimeOffset checkedAt = DateTimeOffset.Now;
        List<RecipientReplyCheckResult> results = [];

        foreach (CampaignRecipientState recipient in campaign.Recipients)
        {
            string? threadId = recipient.OriginalMessage?.ProviderThreadId;
            if (string.IsNullOrWhiteSpace(threadId))
            {
                results.Add(new(recipient.Email, "missing-thread", null, "Original Gmail thread ID is unavailable."));
                continue;
            }

            try
            {
                GmailThreadSnapshot thread = await _threads.GetThreadAsync(threadId, cancellationToken);
                results.Add(Classify(recipient, thread, _senderEmail));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                results.Add(new(recipient.Email, "uncertain", null, DescribeReadFailure(exception)));
            }
        }

        _campaigns.RecordReplyChecks(campaignId, checkedAt, results);
        return new(
            campaignId,
            checkedAt,
            campaign.Recipients.Count,
            results.Count(result => result.Status != "missing-thread"),
            Count(results, "replied"),
            Count(results, "no-reply"),
            Count(results, "automated"),
            Count(results, "bounced"),
            Count(results, "uncertain"),
            Count(results, "missing-thread"),
            results);
    }

    internal static RecipientReplyCheckResult Classify(
        CampaignRecipientState recipient,
        GmailThreadSnapshot thread,
        string senderEmail)
    {
        string? originalId = recipient.OriginalMessage?.ProviderMessageId;
        List<GmailThreadMessage> laterMessages = thread.Messages
            .Where(message => !string.Equals(message.Id, originalId, StringComparison.Ordinal))
            .Where(message => !IsFrom(message.From, senderEmail))
            .OrderBy(message => message.Date)
            .ToList();

        GmailThreadMessage? bounce = laterMessages.LastOrDefault(IsBounce);
        if (bounce is not null) return new(recipient.Email, "bounced", bounce.Id, "Delivery failure message detected.");

        GmailThreadMessage? recipientReply = laterMessages.LastOrDefault(message => IsFrom(message.From, recipient.Email));
        if (recipientReply is not null && IsAutomated(recipientReply))
            return new(recipient.Email, "automated", recipientReply.Id, "Automated reply detected from recipient address.");
        if (recipientReply is not null)
            return new(recipient.Email, "replied", recipientReply.Id, "Reply detected from recipient address.");

        GmailThreadMessage? automated = laterMessages.LastOrDefault(IsAutomated);
        if (automated is not null) return new(recipient.Email, "automated", automated.Id, "Automated message detected in thread.");

        bool hasUnknownIncoming = laterMessages.Any(message => !string.IsNullOrWhiteSpace(message.From));
        return hasUnknownIncoming
            ? new(recipient.Email, "uncertain", laterMessages.Last().Id, "Thread contains a message from an unmatched sender.")
            : new(recipient.Email, "no-reply", null, "No reply detected.");
    }

    private static bool IsFrom(string from, string expectedEmail)
    {
        if (!InternetAddressList.TryParse(from, out InternetAddressList? addresses)) return false;
        return addresses.Mailboxes.Any(mailbox => string.Equals(mailbox.Address, expectedEmail, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsAutomated(GmailThreadMessage message) =>
        (!string.IsNullOrWhiteSpace(message.AutoSubmitted)
            && !string.Equals(message.AutoSubmitted, "no", StringComparison.OrdinalIgnoreCase))
        || ContainsAny(message.Precedence, "auto_reply", "bulk", "junk", "list")
        || !string.IsNullOrWhiteSpace(message.AutoReplyHeader);

    private static bool IsBounce(GmailThreadMessage message) =>
        ContainsAny(message.From, "mailer-daemon", "postmaster")
        || ContainsAny(message.Subject, "undeliverable", "delivery status notification", "delivery failure", "mail delivery failed");

    private static bool ContainsAny(string? value, params string[] candidates) =>
        !string.IsNullOrWhiteSpace(value) && candidates.Any(candidate => value.Contains(candidate, StringComparison.OrdinalIgnoreCase));

    private static int Count(IEnumerable<RecipientReplyCheckResult> results, string status) =>
        results.Count(result => result.Status == status);

    private static string DescribeReadFailure(Exception exception)
    {
        if (ContainsSocketAccessDenied(exception))
        {
            return "Gmail could not be reached because outbound HTTPS access to oauth2.googleapis.com:443 is blocked by this computer or network. Allow the app/dotnet through the firewall or configure the required proxy, then run the reply check again.";
        }

        if (exception is HttpRequestException)
        {
            return "Gmail could not be reached over HTTPS. Check the internet connection, firewall, VPN, or proxy, then run the reply check again.";
        }

        return exception.Message;
    }

    private static bool ContainsSocketAccessDenied(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is SocketException socket && socket.SocketErrorCode == SocketError.AccessDenied)
                return true;
            if (current.Message.Contains("oauth2.googleapis.com:443", StringComparison.OrdinalIgnoreCase)
                && current.Message.Contains("forbidden by its access permissions", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
