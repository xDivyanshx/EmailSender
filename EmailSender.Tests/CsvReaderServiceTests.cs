using EmailSender.Services;
using EmailSender.Models;

namespace EmailSender.Tests;

public sealed class CsvReaderServiceTests
{
    [Fact]
    public async Task ReplaceMasterFile_AtomicallyImportsValidCsv()
    {
        using TestWorkspace workspace = new();
        CsvReaderService service = new(workspace.Configuration());
        using MemoryStream csv = new(System.Text.Encoding.UTF8.GetBytes(
            "Name,Organization,Email,Subject,Attachment\nJane Doe,Example,jane@example.com,,\n"));

        int imported = await service.ReplaceMasterFileAsync(csv, CancellationToken.None);

        Assert.Equal(1, imported);
        Assert.Equal("jane@example.com", Assert.Single(service.ReadRecipients()).Email);
    }

    [Fact]
    public async Task ReplaceMasterFile_InvalidHeadersPreserveExistingCsv()
    {
        using TestWorkspace workspace = new();
        workspace.WriteCsv("Jane Doe,Example,jane@example.com,,");
        CsvReaderService service = new(workspace.Configuration());
        using MemoryStream csv = new(System.Text.Encoding.UTF8.GetBytes("Wrong,Headers\nA,B\n"));

        await Assert.ThrowsAsync<InvalidDataException>(() => service.ReplaceMasterFileAsync(csv, CancellationToken.None));

        Assert.Equal("jane@example.com", Assert.Single(service.ReadRecipients()).Email);
    }

    [Fact]
    public void ReadRecipients_SupportsQuotedCommasAndOptionalTemplate()
    {
        using TestWorkspace workspace = new();
        CsvReaderService service = new(workspace.Configuration());
        using MemoryStream csv = new(System.Text.Encoding.UTF8.GetBytes(
            "Name,Organization,Email,Subject,Attachment,Template\n\"Doe, Jane\",\"Example, Inc.\",jane@example.com,Subject,resume.pdf,Custom\n"));

        Recipient recipient = Assert.Single(service.ReadRecipients(csv));

        Assert.Equal("Doe, Jane", recipient.Name);
        Assert.Equal("Example, Inc.", recipient.Organization);
        Assert.Equal("Custom", recipient.Template);
    }

    [Fact]
    public void ReadRecipients_SplitsPipeSeparatedAttachments()
    {
        using TestWorkspace workspace = new();
        CsvReaderService service = new(workspace.Configuration());
        using MemoryStream csv = new(System.Text.Encoding.UTF8.GetBytes(
            "Name,Organization,Email,Attachment\nJane Doe,Example,jane@example.com, resume.pdf | cover-letter.pdf | resume.pdf\n"));

        Recipient recipient = Assert.Single(service.ReadRecipients(csv));

        Assert.Equal(["resume.pdf", "cover-letter.pdf"], recipient.Attachments);
        Assert.Equal("resume.pdf", recipient.AttachmentFileName);
    }

    [Fact]
    public void ReadRecipients_ReadsOptionalPresetName()
    {
        using TestWorkspace workspace = new();
        CsvReaderService service = new(workspace.Configuration());
        using MemoryStream csv = new(System.Text.Encoding.UTF8.GetBytes(
            "Name,Organization,Email,PresetName\nJane Doe,Example,jane@example.com,Company A Backend\n"));

        Recipient recipient = Assert.Single(service.ReadRecipients(csv));

        Assert.Equal("Company A Backend", recipient.PresetName);
    }
}
