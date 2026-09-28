using EmailSender.Models;
using EmailSender.Services;

namespace EmailSender.Tests;

public sealed class CampaignStoreServiceTests
{
    [Fact]
    public void Create_PersistsAndReloadsCampaign()
    {
        using TestWorkspace workspace = new();
        CampaignStoreService store = new(workspace.Configuration());

        CampaignState created = store.Create(
            new("First campaign", "Custom subject", null, ["resume.pdf", "resume.pdf"]),
            "Fallback subject",
            "default");

        CampaignState reloaded = new CampaignStoreService(workspace.Configuration()).Get(created.Id)!;

        Assert.Equal("First campaign", reloaded.Name);
        Assert.Equal("Custom subject", reloaded.DefaultSubject);
        Assert.Equal("default", reloaded.DefaultTemplate);
        Assert.Single(reloaded.DefaultAttachments);
    }

    [Fact]
    public void GetAll_RejectsUnknownSchemaVersion()
    {
        using TestWorkspace workspace = new();
        File.WriteAllText(workspace.CampaignPath, "{\"schemaVersion\":99,\"campaigns\":[]}");

        CampaignStoreService store = new(workspace.Configuration());

        Assert.Throws<InvalidDataException>(() => store.GetAll());
    }

    [Fact]
    public void UpdateDefaults_PersistsCampaignAttachmentsWithoutAffectingOtherCampaigns()
    {
        using TestWorkspace workspace = new();
        CampaignStoreService store = new(workspace.Configuration());
        CampaignState first = store.Create(new("First", null, null, null), "Subject", "default");
        CampaignState second = store.Create(new("Second", null, null, ["second.pdf"]), "Subject", "default");

        store.UpdateDefaults(first.Id, new("Updated", "custom", ["first.pdf", "first.pdf"]));

        Assert.Equal(["first.pdf"], store.Get(first.Id)!.DefaultAttachments);
        Assert.Equal(["second.pdf"], store.Get(second.Id)!.DefaultAttachments);
        Assert.Contains("campaign 'First' common attachments", store.GetAttachmentReferences(first.Id, "first.pdf"));
    }

    [Fact]
    public void RenameDuplicateAndDelete_PreserveSafeSemantics()
    {
        using TestWorkspace workspace = new();
        CampaignStoreService store = new(workspace.Configuration());
        CampaignState campaign = store.Create(new("Original", "Subject", "default", ["resume.pdf"]), "Fallback", "default");
        CampaignRecipientState recipient = store.AddRecipient(campaign.Id,
            new("Jane Doe", "jane@example.com", "Example", "Custom", null, ["resume.pdf"]));
        store.RecordOriginalDelivery(campaign.Id,
            new Recipient { Name = recipient.Name, Email = recipient.Email, Organization = recipient.Organization },
            "Custom", ["resume.pdf"], new(true, "message-1", "thread-1"));

        CampaignState renamed = store.Rename(campaign.Id, "Renamed");
        CampaignState copy = store.Duplicate(campaign.Id);
        store.Delete(campaign.Id);

        Assert.Equal("Renamed", renamed.Name);
        Assert.Null(store.Get(campaign.Id));
        CampaignRecipientState copiedRecipient = Assert.Single(copy.Recipients);
        Assert.NotEqual(recipient.Id, copiedRecipient.Id);
        Assert.Null(copiedRecipient.OriginalMessage);
        Assert.Equal("not-checked", copiedRecipient.ReplyCheck.Status);
        Assert.Empty(copiedRecipient.FollowUps);
    }
}
