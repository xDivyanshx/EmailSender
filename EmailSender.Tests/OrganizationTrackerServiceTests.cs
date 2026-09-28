using EmailSender.Models;
using EmailSender.Services;

namespace EmailSender.Tests;

public sealed class OrganizationTrackerServiceTests
{
    [Fact]
    public void GetAll_MergesOrganizationAcrossCampaignsAndAggregatesActivity()
    {
        using TestWorkspace workspace = new();
        CampaignStoreService campaigns = new(workspace.Configuration());
        CampaignState first = campaigns.Create(new("First", null, null, null), "Subject", "default");
        CampaignState second = campaigns.Create(new("Second", null, null, null), "Subject", "default");
        CampaignRecipientState one = campaigns.AddRecipient(first.Id, new("One", "one@example.com", "Acme Corp", null, null, null));
        CampaignRecipientState two = campaigns.AddRecipient(second.Id, new("Two", "two@example.com", "  acme   corp ", null, null, null));
        campaigns.RecordOriginalDelivery(first.Id, new Recipient { Name = one.Name, Email = one.Email, Organization = one.Organization }, "Hello", [], new(true, "message-1", "thread-1"));
        campaigns.RecordOriginalDelivery(second.Id, new Recipient { Name = two.Name, Email = two.Email, Organization = two.Organization }, "Hello", [], new(true, "message-2", "thread-2"));
        campaigns.RecordReplyChecks(second.Id, DateTimeOffset.Now, [new(two.Email, "replied", "reply-1", "Reply detected.")]);

        OrganizationSummary summary = Assert.Single(new OrganizationTrackerService(workspace.Configuration(), campaigns).GetAll());

        Assert.Equal("Acme Corp", summary.Organization);
        Assert.Equal(2, summary.Contacts);
        Assert.Equal(2, summary.Campaigns);
        Assert.Equal(2, summary.Sent);
        Assert.Equal(1, summary.Replied);
    }

    [Fact]
    public void Update_PersistsManualOutcomeAndNotes()
    {
        using TestWorkspace workspace = new();
        CampaignStoreService campaigns = new(workspace.Configuration());
        CampaignState campaign = campaigns.Create(new("Campaign", null, null, null), "Subject", "default");
        campaigns.AddRecipient(campaign.Id, new("One", "one@example.com", "Acme", null, null, null));
        OrganizationTrackerService service = new(workspace.Configuration(), campaigns);

        service.Update(new("ACME", "not-interested", "Hiring team declined."));
        OrganizationSummary reloaded = Assert.Single(new OrganizationTrackerService(workspace.Configuration(), campaigns).GetAll());

        Assert.Equal("not-interested", reloaded.Outcome);
        Assert.Equal("Hiring team declined.", reloaded.Notes);
        Assert.NotNull(reloaded.TrackingUpdatedAt);
    }
}
