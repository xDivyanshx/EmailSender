using MimeKit;
using EmailSender.Models;

namespace EmailSender.Services;

public sealed class GmailMimeMessageFactory
{
    private readonly IConfiguration _config;

    public GmailMimeMessageFactory(IConfiguration config)
    {
        _config = config;
    }

    public MimeMessage Create(
        string subject,
        string htmlBody,
        string toEmailAddress,
        string recipientName,
        IEnumerable<AttachmentSpec>? attachments = null,
        string? inReplyTo = null,
        IEnumerable<string>? references = null)
    {
        MimeMessage message = new();
        string senderEmail = _config["Gmail:Account"]
            ?? throw new InvalidOperationException("Gmail account is missing.");
        string senderName = _config["Mail:SenderName"]
            ?? throw new InvalidOperationException("Mail sender name is missing.");
        message.From.Add(new MailboxAddress(senderName, senderEmail));
        message.To.Add(new MailboxAddress(recipientName, toEmailAddress));
        message.Subject = subject;

        BodyBuilder body = new() { HtmlBody = htmlBody };
        foreach (AttachmentSpec attachment in attachments ?? [])
        {
            if (!File.Exists(attachment.Path)) throw new FileNotFoundException("Attachment was not found.", attachment.Path);
            MimeEntity part = body.Attachments.Add(attachment.Path);
            if (!string.IsNullOrWhiteSpace(attachment.DisplayName) && part is MimePart mimePart)
                mimePart.FileName = attachment.DisplayName;
        }
        message.Body = body.ToMessageBody();

        if (!string.IsNullOrWhiteSpace(inReplyTo)) message.InReplyTo = inReplyTo;
        foreach (string reference in references ?? []) message.References.Add(reference);
        return message;
    }

    public static string ToBase64Url(MimeMessage message)
    {
        using MemoryStream stream = new();
        message.WriteTo(stream);
        return Convert.ToBase64String(stream.ToArray())
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}
