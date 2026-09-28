using EmailSender.Models;
using EmailSender.Services;

namespace EmailSender.Tests;

public sealed class ReplyCheckServiceTests
{
    [Fact]
    public async Task CheckAll_UpdatesEveryCampaignAndOrganizationLastCheckedTime()
    {
        using TestWorkspace workspace = new();
        IConfiguration config = workspace.Configuration();
        CampaignStoreService campaigns = new(config);
        CampaignState first = campaigns.Create(new("First", null, null, null), "Subject", "default");
        CampaignState second = campaigns.Create(new("Second", null, null, null), "Subject", "default");
        AddRecipient(campaigns, first.Id, "first@example.com", "original-1", "thread-1");
        AddRecipient(campaigns, second.Id, "second@example.com", "original-2", "thread-2");
        FakeGmailThreadReader reader = new();
        reader.Threads["thread-1"] = Thread("thread-1", Original("original-1"), Message("reply-1", "First <first@example.com>"));
        reader.Threads["thread-2"] = Thread("thread-2", Original("original-2"));
        ReplyCheckService service = new(campaigns, reader, config);

        WorkspaceReplyCheckResult result = (await service.CheckAllAsync(CancellationToken.None))!;

        Assert.Equal(2, result.Campaigns);
        Assert.Equal(2, result.TotalRecipients);
        Assert.Equal(1, result.Replied);
        Assert.Equal(1, result.NoReply);
        Assert.All(campaigns.GetAll().SelectMany(campaign => campaign.Recipients), recipient => Assert.NotNull(recipient.ReplyCheck.CheckedAt));
        OrganizationSummary organization = Assert.Single(new OrganizationTrackerService(config, campaigns).GetAll());
        Assert.NotNull(organization.LastCheckedAt);
    }

    [Fact]
    public async Task Check_ClassifiesRepliesAndPersistsResultsWithoutSending()
    {
        using TestWorkspace workspace = new();
        IConfiguration config = workspace.Configuration();
        CampaignStoreService campaigns = new(config);
        CampaignState campaign = campaigns.Create(new("Replies", null, null, null), "Subject", "default");
        AddRecipient(campaigns, campaign.Id, "human@example.com", "original-1", "thread-1");
        AddRecipient(campaigns, campaign.Id, "auto@example.com", "original-2", "thread-2");
        AddRecipient(campaigns, campaign.Id, "bounce@example.com", "original-3", "thread-3");
        AddRecipient(campaigns, campaign.Id, "quiet@example.com", "original-4", "thread-4");

        FakeGmailThreadReader reader = new();
        reader.Threads["thread-1"] = Thread("thread-1", Original("original-1"), Message("reply-1", "Human <human@example.com>"));
        reader.Threads["thread-2"] = Thread("thread-2", Original("original-2"), Message("reply-2", "Auto <auto@example.com>", autoSubmitted: "auto-replied"));
        reader.Threads["thread-3"] = Thread("thread-3", Original("original-3"), Message("bounce-1", "Mail Delivery Subsystem <mailer-daemon@googlemail.com>", subject: "Delivery Status Notification"));
        reader.Threads["thread-4"] = Thread(
            "thread-4",
            Original("original-4"),
            Message("outgoing-1", "Sender <sender@example.com>", "Re: Subject"));
        ReplyCheckService service = new(campaigns, reader, ReplyConfig(config));

        CampaignReplyCheckResult result = (await service.CheckAsync(campaign.Id, CancellationToken.None))!;

        Assert.Equal(1, result.Replied);
        Assert.Equal(1, result.Automated);
        Assert.Equal(1, result.Bounced);
        Assert.Equal(1, result.NoReply);
        CampaignState persisted = new CampaignStoreService(config).Get(campaign.Id)!;
        Assert.Equal("replied", persisted.Recipients.Single(item => item.Email == "human@example.com").ReplyCheck.Status);
        Assert.Equal("no-reply", persisted.Recipients.Single(item => item.Email == "quiet@example.com").ReplyCheck.Status);
        Assert.Equal("bounced", persisted.Recipients.Single(item => item.Email == "bounce@example.com").ReplyCheck.Status);
    }

    [Fact]
    public async Task Check_MarksRecipientWithoutThreadAsMissingThread()
    {
        using TestWorkspace workspace = new();
        IConfiguration config = workspace.Configuration();
        CampaignStoreService campaigns = new(config);
        CampaignState campaign = campaigns.Create(new("Missing", null, null, null), "Subject", "default");
        campaigns.RecordOriginalDelivery(
            campaign.Id,
            new Recipient { Name = "No Thread", Email = "none@example.com", Organization = "Example" },
            "Subject",
            [],
            new(true, "message-only", null));
        ReplyCheckService service = new(campaigns, new FakeGmailThreadReader(), ReplyConfig(config));

        CampaignReplyCheckResult result = (await service.CheckAsync(campaign.Id, CancellationToken.None))!;

        Assert.Equal(1, result.MissingThread);
        Assert.Equal("missing-thread", Assert.Single(result.Recipients).Status);
    }

    private static void AddRecipient(CampaignStoreService campaigns, Guid campaignId, string email, string messageId, string threadId) =>
        campaigns.RecordOriginalDelivery(
            campaignId,
            new Recipient { Name = email, Email = email, Organization = "Example" },
            "Subject",
            [],
            new(true, messageId, threadId));

    private static GmailThreadSnapshot Thread(string id, params GmailThreadMessage[] messages) => new(id, messages);

    private static GmailThreadMessage Original(string id) =>
        new(id, DateTimeOffset.Now.AddMinutes(-5), "Sender <sender@example.com>", "Subject", "<original@example.com>", null, null, null);

    private static GmailThreadMessage Message(
        string id,
        string from,
        string subject = "Re: Subject",
        string? autoSubmitted = null) =>
        new(id, DateTimeOffset.Now, from, subject, $"<{id}@example.com>", autoSubmitted, null, null);

    private static IConfiguration ReplyConfig(IConfiguration baseConfig) =>
        new ConfigurationBuilder()
            .AddConfiguration(baseConfig)
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Gmail:Account"] = "sender@example.com"
            })
            .Build();
}
