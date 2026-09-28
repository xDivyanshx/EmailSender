using EmailSender.Models;
using EmailSender.Services;

namespace EmailSender.Tests;

public sealed class AttachmentServiceTests
{
    [Fact]
    public void CampaignLibraries_AreIsolatedByCampaignId()
    {
        using TestWorkspace workspace = new();
        AttachmentService attachments = new(workspace.Configuration());
        Guid first = Guid.NewGuid();
        Guid second = Guid.NewGuid();

        string firstPath = attachments.Resolve(first, "resume.pdf");
        string secondPath = attachments.Resolve(second, "resume.pdf");
        Directory.CreateDirectory(Path.GetDirectoryName(firstPath)!);
        File.WriteAllText(firstPath, "first");

        Assert.True(File.Exists(attachments.Resolve(first, "resume.pdf")));
        Assert.False(File.Exists(secondPath));
        Assert.Single(attachments.List(first));
        Assert.Empty(attachments.List(second));
    }

    [Fact]
    public void CampaignReferences_DoNotBlockDeletionInAnotherCampaign()
    {
        using TestWorkspace workspace = new();
        CampaignStoreService campaigns = new(workspace.Configuration());
        CampaignState first = campaigns.Create(new("First", null, null, ["resume.pdf"]), "Subject", "default");
        CampaignState second = campaigns.Create(new("Second", null, null, null), "Subject", "default");

        Assert.NotEmpty(campaigns.GetAttachmentReferences(first.Id, "resume.pdf"));
        Assert.Empty(campaigns.GetAttachmentReferences(second.Id, "resume.pdf"));
    }
}
