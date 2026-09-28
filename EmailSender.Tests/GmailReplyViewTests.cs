using EmailSender.Services;
using Google.Apis.Gmail.v1.Data;
using System.Text;

namespace EmailSender.Tests;

public sealed class GmailReplyViewTests
{
    [Fact]
    public void CreateReplyView_DecodesBodiesSanitizesHtmlAndListsAttachments()
    {
        Message message = new()
        {
            Id = "reply-1",
            ThreadId = "thread-1",
            InternalDate = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Payload = new MessagePart
            {
                Headers =
                [
                    new MessagePartHeader { Name = "From", Value = "Jane <jane@example.com>" },
                    new MessagePartHeader { Name = "To", Value = "sender@example.com" },
                    new MessagePartHeader { Name = "Subject", Value = "Re: Hello" }
                ],
                Parts =
                [
                    Part("text/plain", "Thanks for reaching out."),
                    Part("text/html", "<p onclick=\"bad()\">Thanks</p><script>alert(1)</script><img src=\"https://tracker.example/pixel\">"),
                    new MessagePart { Filename = "details.pdf", MimeType = "application/pdf", Body = new MessagePartBody { Size = 1234 } }
                ]
            }
        };

        var view = GmailThreadReader.CreateReplyView(message, "fallback");

        Assert.Equal("Thanks for reaching out.", view.PlainTextBody);
        Assert.Contains("<p>Thanks</p>", view.SanitizedHtmlBody);
        Assert.DoesNotContain("script", view.SanitizedHtmlBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onclick", view.SanitizedHtmlBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://", view.SanitizedHtmlBody, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("details.pdf", Assert.Single(view.Attachments).FileName);
    }

    private static MessagePart Part(string mimeType, string body) => new()
    {
        MimeType = mimeType,
        Body = new MessagePartBody { Data = Convert.ToBase64String(Encoding.UTF8.GetBytes(body)).TrimEnd('=').Replace('+', '-').Replace('/', '_') }
    };
}
