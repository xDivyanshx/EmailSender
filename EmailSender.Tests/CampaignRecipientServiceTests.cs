using EmailSender.Models;
using EmailSender.Services;

namespace EmailSender.Tests;

public sealed class CampaignRecipientServiceTests
{
    [Fact]
    public void AddUpdateDelete_ManagesCampaignRecipient()
    {
        using TestWorkspace workspace = new();
        CampaignStoreService store = new(workspace.Configuration());
        CampaignState campaign = store.Create(new("Campaign", null, null, null), "Subject", "default");

        CampaignRecipientState added = store.AddRecipient(campaign.Id, new(
            "Jane Doe", "jane@example.com", "Example", null, null, ["resume.pdf"]));
        CampaignRecipientState updated = store.UpdateRecipient(campaign.Id, added.Id, new(
            "Jane Smith", "jane@example.com", "Updated Org", "Custom", "Template", ["portfolio.pdf"]));

        Assert.Equal("Jane Smith", updated.Name);
        Assert.Equal("Updated Org", updated.Organization);
        Assert.Equal("Custom", updated.Subject);
        Assert.Equal("Template", updated.Template);
        Assert.Equal("portfolio.pdf", Assert.Single(updated.Attachments));

        store.DeleteRecipient(campaign.Id, added.Id);
        Assert.Empty(store.Get(campaign.Id)!.Recipients);
    }

    [Fact]
    public void Import_UpsertsByEmailAndPreservesDeliveryHistory()
    {
        using TestWorkspace workspace = new();
        CampaignStoreService store = new(workspace.Configuration());
        CampaignState campaign = store.Create(new("Campaign", null, null, null), "Subject", "default");
        CampaignRecipientState recipient = store.AddRecipient(campaign.Id, new(
            "Jane Doe", "jane@example.com", "Example", null, null, null));
        store.RecordOriginalDelivery(
            campaign.Id,
            new Recipient { Name = recipient.Name, Email = recipient.Email, Organization = recipient.Organization },
            "Subject",
            [],
            new(true, "message-id", "thread-id", "<internet@example.com>"));

        store.ImportRecipients(campaign.Id,
        [
            new Recipient
            {
                Name = "Jane Updated",
                Email = "JANE@example.com",
                Organization = "New Organization",
                Subject = "New Subject"
            }
        ]);

        CampaignRecipientState imported = Assert.Single(store.Get(campaign.Id)!.Recipients);
        Assert.Equal("Jane Updated", imported.Name);
        Assert.Equal("New Organization", imported.Organization);
        Assert.Equal("message-id", imported.OriginalMessage!.ProviderMessageId);
    }

    [Fact]
    public void Add_RejectsDuplicateEmailIgnoringCase()
    {
        using TestWorkspace workspace = new();
        CampaignStoreService store = new(workspace.Configuration());
        CampaignState campaign = store.Create(new("Campaign", null, null, null), "Subject", "default");
        store.AddRecipient(campaign.Id, new("Jane", "jane@example.com", "Example", null, null, null));

        Assert.Throws<InvalidOperationException>(() => store.AddRecipient(
            campaign.Id,
            new("Another Jane", "JANE@example.com", "Other", null, null, null)));
    }
}
