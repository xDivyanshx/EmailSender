using EmailSender.Services;
using EmailSender.Models;
using MimeKit;

namespace EmailSender.Tests;

public sealed class GmailMimeMessageFactoryTests
{
    [Fact]
    public void Create_BuildsHtmlMessageWithMultipleAttachmentsAndReplyHeaders()
    {
        using TestWorkspace workspace = new();
        string first = Path.Combine(workspace.Resources, "first.txt");
        string second = Path.Combine(workspace.Resources, "second.pdf");
        File.WriteAllText(first, "first");
        File.WriteAllText(second, "second");
        IConfiguration config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Gmail:Account"] = "sender@example.com",
            ["Mail:SenderName"] = "Sender"
        }).Build();
        GmailMimeMessageFactory factory = new(config);

        MimeMessage message = factory.Create(
            "Custom subject",
            "<strong>Hello</strong>",
            "recipient@example.com",
            "Recipient",
            [new AttachmentSpec(first), new AttachmentSpec(second)],
            "original@example.com",
            ["original@example.com"]);

        Assert.Equal("Custom subject", message.Subject);
        Assert.Equal("sender@example.com", ((MailboxAddress)Assert.Single(message.From)).Address);
        Assert.Equal("recipient@example.com", ((MailboxAddress)Assert.Single(message.To)).Address);
        Assert.Equal("original@example.com", message.InReplyTo);
        Assert.Contains("original@example.com", message.References);
        Multipart multipart = Assert.IsType<Multipart>(message.Body);
        Assert.Equal(3, multipart.Count);
        Assert.Contains(multipart.OfType<TextPart>(), part => part.HtmlBodyContains("Hello"));
        Assert.Equal(2, multipart.OfType<MimePart>().Count(part => part.IsAttachment));
    }

    [Fact]
    public void ToBase64Url_UsesGmailSafeAlphabetWithoutPadding()
    {
        IConfiguration config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Gmail:Account"] = "sender@example.com",
            ["Mail:SenderName"] = "Sender"
        }).Build();
        GmailMimeMessageFactory factory = new(config);
        MimeMessage message = factory.Create("Subject", "<p>Hello</p>", "recipient@example.com", "Recipient");

        string encoded = GmailMimeMessageFactory.ToBase64Url(message);

        Assert.DoesNotContain('+', encoded);
        Assert.DoesNotContain('/', encoded);
        Assert.DoesNotContain('=', encoded);
    }

    [Fact]
    public void Create_UsesCurrentSenderNameAfterSettingsChange()
    {
        IConfiguration config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Gmail:Account"] = "sender@example.com",
            ["Mail:SenderName"] = "Original Sender"
        }).Build();
        GmailMimeMessageFactory factory = new(config);
        config["Mail:SenderName"] = "Updated Sender";

        MimeMessage message = factory.Create("Subject", "<p>Hello</p>", "recipient@example.com", "Recipient");

        Assert.Equal("Updated Sender", ((MailboxAddress)Assert.Single(message.From)).Name);
    }
}

internal static class TextPartAssertions
{
    public static bool HtmlBodyContains(this TextPart part, string value) =>
        part.IsHtml && part.Text.Contains(value, StringComparison.Ordinal);
}
