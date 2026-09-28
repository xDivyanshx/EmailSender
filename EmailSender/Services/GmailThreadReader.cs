using EmailSender.Models;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Gmail.v1;
using Google.Apis.Services;
using Google.Apis.Util.Store;

namespace EmailSender.Services;

public sealed class GmailThreadReader : IGmailThreadReader
{
    private static readonly string[] Scopes = [GmailService.Scope.GmailSend, GmailService.Scope.GmailReadonly];
    private readonly string _credentialsPath;
    private readonly string _tokenDirectory;
    private readonly string _account;

    public GmailThreadReader(IConfiguration config)
    {
        _credentialsPath = config["Gmail:CredentialsPath"]
            ?? "../../../../resources/app-data/google-oauth-client.json";
        _tokenDirectory = config["Gmail:TokenDirectory"]
            ?? "../../../../resources/app-data/google-tokens";
        _account = config["Gmail:Account"] ?? throw new InvalidOperationException("Gmail account is missing.");
    }

    public async Task<GmailThreadSnapshot> GetThreadAsync(string threadId, CancellationToken cancellationToken)
    {
        UserCredential credential = await GetCredentialAsync(cancellationToken);
        using GmailService gmail = new(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "Cold Mail Sender"
        });
        UsersResource.ThreadsResource.GetRequest request = gmail.Users.Threads.Get("me", threadId);
        request.Format = UsersResource.ThreadsResource.GetRequest.FormatEnum.Metadata;
        request.MetadataHeaders = new[] { "From", "Subject", "Message-ID", "Auto-Submitted", "Precedence", "X-Autoreply" };
        Google.Apis.Gmail.v1.Data.Thread thread = await request.ExecuteAsync(cancellationToken);

        List<GmailThreadMessage> messages = thread.Messages?.Select(message =>
        {
            Dictionary<string, string> headers = message.Payload?.Headers?
                .GroupBy(header => header.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Last().Value ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                ?? new(StringComparer.OrdinalIgnoreCase);
            DateTimeOffset? date = message.InternalDate is long milliseconds
                ? DateTimeOffset.FromUnixTimeMilliseconds(milliseconds)
                : null;
            return new GmailThreadMessage(
                message.Id,
                date,
                GetHeader(headers, "From"),
                GetHeader(headers, "Subject"),
                GetOptionalHeader(headers, "Message-ID"),
                GetOptionalHeader(headers, "Auto-Submitted"),
                GetOptionalHeader(headers, "Precedence"),
                GetOptionalHeader(headers, "X-Autoreply"));
        }).ToList() ?? [];

        return new(thread.Id ?? threadId, messages);
    }

    public async Task<GmailReplyView> GetMessageAsync(string messageId, CancellationToken cancellationToken)
    {
        UserCredential credential = await GetCredentialAsync(cancellationToken);
        using GmailService gmail = new(new BaseClientService.Initializer { HttpClientInitializer = credential, ApplicationName = "Cold Mail Sender" });
        var request = gmail.Users.Messages.Get("me", messageId);
        request.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Full;
        var message = await request.ExecuteAsync(cancellationToken);
        return CreateReplyView(message, messageId);
    }

    public static GmailReplyView CreateReplyView(Google.Apis.Gmail.v1.Data.Message message, string fallbackMessageId)
    {
        Dictionary<string, string> headers = message.Payload?.Headers?
            .GroupBy(header => header.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last().Value ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            ?? new(StringComparer.OrdinalIgnoreCase);
        (string plain, string html) = ExtractBodies(message.Payload);
        IReadOnlyCollection<GmailAttachmentMetadata> attachments = EnumerateParts(message.Payload)
            .Where(part => !string.IsNullOrWhiteSpace(part.Filename))
            .Select(part => new GmailAttachmentMetadata(part.Filename!, part.MimeType ?? "application/octet-stream", part.Body?.Size ?? 0))
            .ToArray();
        DateTimeOffset? received = message.InternalDate is long milliseconds ? DateTimeOffset.FromUnixTimeMilliseconds(milliseconds) : null;
        return new(message.Id ?? fallbackMessageId, message.ThreadId, Header(headers, "From"), Header(headers, "To"), Header(headers, "Subject"), received, plain, SanitizeHtml(html), attachments);
    }

    private static (string Plain, string Html) ExtractBodies(Google.Apis.Gmail.v1.Data.MessagePart? payload)
    {
        List<string> plain = [], html = [];
        foreach (var part in EnumerateParts(payload).Append(payload).Where(part => part is not null))
        {
            string? data = part!.Body?.Data;
            if (string.IsNullOrWhiteSpace(data)) continue;
            string decoded = Decode(data);
            if (string.Equals(part.MimeType, "text/html", StringComparison.OrdinalIgnoreCase)) html.Add(decoded);
            else if (string.Equals(part.MimeType, "text/plain", StringComparison.OrdinalIgnoreCase)) plain.Add(decoded);
        }
        return (string.Join("\n", plain), string.Join("\n", html));
    }

    private static IEnumerable<Google.Apis.Gmail.v1.Data.MessagePart> EnumerateParts(Google.Apis.Gmail.v1.Data.MessagePart? part)
    {
        if (part is null) yield break;
        foreach (var child in part.Parts ?? [])
        {
            yield return child;
            foreach (var nested in EnumerateParts(child)) yield return nested;
        }
    }

    private static string Decode(string value)
    {
        string normalized = value.Replace('-', '+').Replace('_', '/');
        normalized = normalized.PadRight(normalized.Length + ((4 - normalized.Length % 4) % 4), '=');
        return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(normalized));
    }

    private static string SanitizeHtml(string html)
    {
        const System.Text.RegularExpressions.RegexOptions options = System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline;
        string sanitized = System.Text.RegularExpressions.Regex.Replace(html, "<\\s*(script|style|iframe|object|embed)[^>]*>.*?<\\s*/\\s*\\1\\s*>", string.Empty, options);
        sanitized = System.Text.RegularExpressions.Regex.Replace(sanitized, "\\s+on[a-z]+\\s*=\\s*(\\\"[^\\\"]*\\\"|'[^']*'|[^\\s>]+)", string.Empty, options);
        sanitized = System.Text.RegularExpressions.Regex.Replace(sanitized, "\\s+(src|href)\\s*=\\s*([\"'])\\s*(javascript:|https?://)[^\"']*\\2", string.Empty, options);
        return sanitized;
    }

    private static string Header(IReadOnlyDictionary<string, string> headers, string name) => headers.TryGetValue(name, out string? value) ? value : string.Empty;

    private async Task<UserCredential> GetCredentialAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_credentialsPath)) throw new FileNotFoundException("Google OAuth client file was not found.", _credentialsPath);
        if (!Directory.Exists(_tokenDirectory) || !Directory.EnumerateFiles(_tokenDirectory).Any())
            throw new InvalidOperationException("Gmail is not connected.");
        if (GmailAuthorizationPolicy.IsExpired(_tokenDirectory))
            throw new InvalidOperationException("Daily Gmail authorization has expired. Reconnect Gmail before checking replies.");
        GoogleClientSecrets secrets = GoogleClientSecrets.FromFile(_credentialsPath);
        return await GoogleWebAuthorizationBroker.AuthorizeAsync(
            secrets.Secrets,
            Scopes,
            _account,
            cancellationToken,
            new FileDataStore(_tokenDirectory, fullPath: true));
    }

    private static string GetHeader(IReadOnlyDictionary<string, string> headers, string name) =>
        headers.TryGetValue(name, out string? value) ? value : string.Empty;

    private static string? GetOptionalHeader(IReadOnlyDictionary<string, string> headers, string name) =>
        headers.TryGetValue(name, out string? value) && !string.IsNullOrWhiteSpace(value) ? value : null;
}
