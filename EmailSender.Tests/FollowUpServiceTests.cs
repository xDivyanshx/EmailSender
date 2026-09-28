using EmailSender.Models;
using EmailSender.Services;

namespace EmailSender.Tests;

public sealed class FollowUpServiceTests
{
    [Fact]
    public void Preview_CustomSubjectRequiresExplicitApproval()
    {
        using TestWorkspace workspace = new();
        TestContext context = CreateContext(workspace);

        FollowUpPreviewItem item = Assert.Single(context.Service.Preview(context.CampaignId, new(
            [context.Email],
            "Following up",
            "A completely different subject",
            null,
            null,
            false)).Recipients);

        Assert.True(item.SubjectChanged);
        Assert.False(item.Eligible);
        Assert.Contains(item.Errors, error => error.Contains("threading", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Send_RechecksThreadAndPersistsThreadedFollowUp()
    {
        using TestWorkspace workspace = new();
        TestContext context = CreateContext(workspace);

        FollowUpSendResult result = (await context.Service.TrySendAsync(context.CampaignId, new(
            [context.Email],
            "<p>Hi {name}, Following up</p>",
            null,
            null,
            null,
            false,
            true,
            FollowUpService.RequiredConfirmationText), CancellationToken.None))!;

        Assert.Equal(1, result.Sent);
        RecordedReply reply = Assert.Single(context.Dispatcher.Replies);
        Assert.Equal("thread-1", reply.ThreadId);
        Assert.Equal("<original@example.com>", reply.InReplyTo);
        Assert.Contains("Hi Recipient, Following up", Assert.Single(context.Dispatcher.Messages).Body);
        FollowUpPreviewItem preview = Assert.Single(context.Service.Preview(context.CampaignId, new(
            [context.Email], "Body", null, null, null, false)).Recipients);
        Assert.Equal("Example", preview.Organization);
        Assert.True(preview.ElapsedHours >= 0);
        MessageDeliveryState persisted = Assert.Single(context.Campaigns.Get(context.CampaignId)!.Recipients.Single().FollowUps);
        Assert.Equal("followup-test", persisted.ProviderMessageId);
        Assert.Equal("thread-1", persisted.ProviderThreadId);
        DeliveryLedgerEntry ledgerEntry = Assert.Single(new DeliveryLedgerService(workspace.Configuration()).GetAll());
        Assert.Equal("follow-up", ledgerEntry.MessageType);
    }

    [Fact]
    public async Task Send_SuppressesRecipientWhoRepliedAfterPreview()
    {
        using TestWorkspace workspace = new();
        TestContext context = CreateContext(workspace);
        context.Reader.Threads["thread-1"] = new("thread-1",
        [
            OriginalMessage(),
            new("reply-1", DateTimeOffset.Now, $"Recipient <{context.Email}>", "Re: Subject", "<reply@example.com>", null, null, null)
        ]);

        FollowUpSendResult result = (await context.Service.TrySendAsync(context.CampaignId, new(
            [context.Email],
            "<p>Following up</p>",
            null,
            null,
            null,
            false,
            true,
            FollowUpService.RequiredConfirmationText), CancellationToken.None))!;

        Assert.Equal(0, result.Attempted);
        Assert.Equal("suppressed-replied", Assert.Single(result.Results).Status);
        Assert.Empty(context.Dispatcher.Replies);
        Assert.Equal("replied", context.Campaigns.Get(context.CampaignId)!.Recipients.Single().ReplyCheck.Status);
    }

    private static TestContext CreateContext(TestWorkspace workspace)
    {
        const string email = "recipient@example.com";
        IConfiguration config = new ConfigurationBuilder()
            .AddConfiguration(workspace.Configuration())
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Gmail:Account"] = "sender@example.com" })
            .Build();
        CampaignStoreService campaigns = new(config);
        CampaignState campaign = campaigns.Create(new("Follow ups", null, null, null), "Subject", "default");
        campaigns.RecordOriginalDelivery(
            campaign.Id,
            new Recipient { Name = "Recipient", Email = email, Organization = "Example" },
            "Subject",
            [],
            new(true, "original-1", "thread-1", "<original@example.com>"));
        campaigns.RecordReplyChecks(campaign.Id, DateTimeOffset.Now,
            [new(email, "no-reply", null, "No reply detected.")]);
        FakeGmailThreadReader reader = new();
        reader.Threads["thread-1"] = new("thread-1", [OriginalMessage()]);
        RecordingEmailDispatcher dispatcher = new();
        FollowUpService service = new(
            campaigns,
            reader,
            dispatcher,
            config,
            new AttachmentService(config),
            new DeliveryLedgerService(config),
            new BatchOperationService());
        return new(campaign.Id, email, campaigns, reader, dispatcher, service);
    }

    private static GmailThreadMessage OriginalMessage() =>
        new("original-1", DateTimeOffset.Now.AddMinutes(-10), "Sender <sender@example.com>", "Subject", "<original@example.com>", null, null, null);

    private sealed record TestContext(
        Guid CampaignId,
        string Email,
        CampaignStoreService Campaigns,
        FakeGmailThreadReader Reader,
        RecordingEmailDispatcher Dispatcher,
        FollowUpService Service);
}
