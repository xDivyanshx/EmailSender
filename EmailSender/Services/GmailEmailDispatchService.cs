using Google.Apis.Auth.OAuth2;
using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using MimeKit;
using EmailSender.Models;

namespace EmailSender.Services;

public sealed class GmailEmailDispatchService : IEmailDispatchService
{
    private static readonly string[] Scopes = [GmailService.Scope.GmailSend, GmailService.Scope.GmailReadonly];
    private readonly string _credentialsPath;
    private readonly string _tokenDirectory;
    private readonly string _account;
    private readonly GmailMimeMessageFactory _messages;

    public GmailEmailDispatchService(IConfiguration config, GmailMimeMessageFactory messages)
    {
        _credentialsPath = config["Gmail:CredentialsPath"]
            ?? "../../../../resources/app-data/google-oauth-client.json";
        _tokenDirectory = config["Gmail:TokenDirectory"]
            ?? "../../../../resources/app-data/google-tokens";
        _account = config["Gmail:Account"] ?? throw new InvalidOperationException("Gmail account is missing.");
        _messages = messages;
    }

    public EmailDispatchResult TrySendMail(
        string subject,
        string body,
        string toEmailAddress,
        string name,
        IEnumerable<AttachmentSpec>? attachments = null)
    {
        try
        {
            MimeMessage mime = _messages.Create(subject, body, toEmailAddress, name, attachments);
            UserCredential credential = GetCredentialAsync().GetAwaiter().GetResult();
            using GmailService gmail = new(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "Cold Mail Sender"
            });
            Message sent = gmail.Users.Messages.Send(new Message
            {
                Raw = GmailMimeMessageFactory.ToBase64Url(mime)
            }, "me").Execute();
            string? internetMessageId = GetInternetMessageId(gmail, sent.Id);
            return new(true, sent.Id, sent.ThreadId, internetMessageId);
        }
        catch (Exception exception)
        {
            return new(false, Error: exception.Message);
        }
    }

    public EmailDispatchResult TrySendReply(
        string subject,
        string body,
        string toEmailAddress,
        string name,
        string providerThreadId,
        string inReplyTo,
        IEnumerable<string> references,
        IEnumerable<string>? attachmentPaths = null)
    {
        try
        {
            MimeMessage mime = _messages.Create(
                subject,
                body,
                toEmailAddress,
                name,
                attachmentPaths?.Select(path => new AttachmentSpec(path)),
                inReplyTo,
                references);
            UserCredential credential = GetCredentialAsync().GetAwaiter().GetResult();
            using GmailService gmail = new(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "Cold Mail Sender"
            });
            Message sent = gmail.Users.Messages.Send(new Message
            {
                Raw = GmailMimeMessageFactory.ToBase64Url(mime),
                ThreadId = providerThreadId
            }, "me").Execute();
            string? internetMessageId = GetInternetMessageId(gmail, sent.Id);
            return new(true, sent.Id, sent.ThreadId, internetMessageId);
        }
        catch (Exception exception)
        {
            return new(false, Error: exception.Message);
        }
    }

    private static string? GetInternetMessageId(GmailService gmail, string messageId)
    {
        UsersResource.MessagesResource.GetRequest request = gmail.Users.Messages.Get("me", messageId);
        request.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Metadata;
        request.MetadataHeaders = new[] { "Message-ID" };
        Message message = request.Execute();
        return message.Payload?.Headers?
            .LastOrDefault(header => string.Equals(header.Name, "Message-ID", StringComparison.OrdinalIgnoreCase))?
            .Value;
    }

    private async Task<UserCredential> GetCredentialAsync()
    {
        if (!File.Exists(_credentialsPath))
            throw new FileNotFoundException("Google OAuth client file was not found.", _credentialsPath);
        if (!Directory.Exists(_tokenDirectory) || !Directory.EnumerateFiles(_tokenDirectory).Any())
            throw new InvalidOperationException("Gmail is not connected. Complete local OAuth authorization first.");
        if (GmailAuthorizationPolicy.IsExpired(_tokenDirectory))
            throw new InvalidOperationException("Daily Gmail authorization has expired. Reconnect Gmail before sending.");

        GoogleClientSecrets secrets = GoogleClientSecrets.FromFile(_credentialsPath);
        return await GoogleWebAuthorizationBroker.AuthorizeAsync(
            secrets.Secrets,
            Scopes,
            _account,
            CancellationToken.None,
            new FileDataStore(_tokenDirectory, fullPath: true));
    }
}
