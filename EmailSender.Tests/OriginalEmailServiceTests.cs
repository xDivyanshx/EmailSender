using EmailSender.Models;
using EmailSender.Services;

namespace EmailSender.Tests;

public sealed class OriginalEmailServiceTests
{
    [Fact]
    public void Preview_AppliesSubjectPrecedenceAndCombinesAttachments()
    {
        using TestWorkspace workspace = new();
        workspace.WriteCsv("Jane Doe,Example,jane@example.com,CSV subject,csv.pdf");
        File.WriteAllText(Path.Combine(workspace.Resources, "Example.html"), "Hello {{name}}");
        File.WriteAllText(Path.Combine(workspace.Attachments, "campaign.pdf"), "x");
        File.WriteAllText(Path.Combine(workspace.Attachments, "csv.pdf"), "x");
        File.WriteAllText(Path.Combine(workspace.Attachments, "person.pdf"), "x");
        RecordingEmailDispatcher dispatcher = new();
        OriginalEmailService service = CreateService(workspace, dispatcher);

        OriginalEmailPreview preview = service.Preview(new(
            ["jane@example.com"],
            "Request subject",
            ["campaign.pdf"],
            [new("jane@example.com", "Recipient subject", ["person.pdf"])]));

        OriginalEmailPreviewItem item = Assert.Single(preview.Recipients);
        Assert.Equal("Recipient subject", item.Subject);
        Assert.Equal("Example", item.Template);
        Assert.Equal(3, item.Attachments.Count);
        Assert.All(item.Attachments, attachment => Assert.True(attachment.Exists));
        Assert.Contains(item.Attachments, attachment => attachment.FileName == "campaign.pdf");
        Assert.DoesNotContain(item.Attachments, attachment => Path.IsPathRooted(attachment.FileName));
        Assert.True(item.Eligible);
    }

    [Fact]
    public void Preview_AppliesRecipientPresetBodyOverride()
    {
        using TestWorkspace workspace = new();
        workspace.WriteCsv("Jane Doe,Example,jane@example.com,,");
        OriginalEmailService service = CreateService(workspace, new());

        OriginalEmailPreview preview = service.Preview(new(
            ["jane@example.com"], null, null,
            [new("jane@example.com", "Preset subject", null, "<p>Preset for {{name}} at {{organization}}</p>")]));

        OriginalEmailPreviewItem item = Assert.Single(preview.Recipients);
        Assert.Equal("Preset subject", item.Subject);
        Assert.Equal("recipient-preset", item.BodySource);
        Assert.Equal("<p>Preset for Jane at Example</p>", item.RenderedBody);
    }

    [Fact]
    public async Task Send_MissingAttachmentRejectsWholeBatchBeforeDispatch()
    {
        using TestWorkspace workspace = new();
        workspace.WriteCsv(
            "Jane Doe,Example,jane@example.com,,",
            "John Doe,Example,john@example.com,,missing.pdf");
        RecordingEmailDispatcher dispatcher = new();
        OriginalEmailService service = CreateService(workspace, dispatcher);

        BatchPreflightException error = await Assert.ThrowsAsync<BatchPreflightException>(() => service.TrySendAsync(new(
            null,
            ["jane@example.com", "john@example.com"],
            null,
            null,
            null,
            false,
            true,
            OriginalEmailService.RequiredConfirmationText)));

        Assert.Contains(error.Errors, item => item.Contains("john@example.com", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(dispatcher.Messages);
    }

    [Theory]
    [InlineData("../resume.pdf")]
    [InlineData("folder/resume.pdf")]
    [InlineData("folder\\resume.pdf")]
    public void Preview_RejectsAttachmentPaths(string attachment)
    {
        using TestWorkspace workspace = new();
        workspace.WriteCsv("Jane Doe,Example,jane@example.com,,");
        OriginalEmailService service = CreateService(workspace, new());

        Assert.Throws<InvalidDataException>(() => service.Preview(new(
            ["jane@example.com"], null, [attachment], null)));
    }

    [Fact]
    public void Preview_MissingAttachmentMakesRecipientIneligible()
    {
        using TestWorkspace workspace = new();
        workspace.WriteCsv("Jane Doe,Example,jane@example.com,,missing.pdf");
        OriginalEmailService service = CreateService(workspace, new());

        OriginalEmailPreviewItem item = Assert.Single(service.Preview(new(null, null, null, null)).Recipients);

        Assert.False(item.Eligible);
        Assert.Contains(item.Errors, error => error.Contains("attachments", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ManualHtml_IsPersonalizedPreviewedAndDispatched()
    {
        using TestWorkspace workspace = new();
        workspace.WriteCsv("Jane Doe,Example Corp,jane@example.com,,");
        RecordingEmailDispatcher dispatcher = new();
        OriginalEmailService service = CreateService(workspace, dispatcher);
        const string body = "<p>Hi {{name}} from {organization}</p>";

        OriginalEmailPreviewItem preview = Assert.Single(service.Preview(new(
            ["jane@example.com"], null, null, null, false, null, body)).Recipients);
        OriginalEmailSendResult result = (await service.TrySendAsync(new(
            null, ["jane@example.com"], null, null, null, false, true,
            OriginalEmailService.RequiredConfirmationText, body)))!;

        Assert.Equal("manual-html", preview.BodySource);
        Assert.Equal("<p>Hi Jane from Example Corp</p>", preview.RenderedBody);
        Assert.Equal(preview.RenderedBody, Assert.Single(dispatcher.Messages).Body);
        Assert.Equal(1, result.Sent);
    }

    [Fact]
    public void RecipientTemplate_TakesPrecedenceOverManualHtml()
    {
        using TestWorkspace workspace = new();
        File.WriteAllText(workspace.CsvPath,
            "Name,Organization,Email,Subject,Attachment,Template\nJane Doe,Example Corp,jane@example.com,,,Special\n");
        File.WriteAllText(Path.Combine(workspace.Resources, "Special.html"), "<p>Template for {{name}}</p>");
        OriginalEmailService service = CreateService(workspace, new());

        OriginalEmailPreviewItem preview = Assert.Single(service.Preview(new(
            ["jane@example.com"], null, null, null, false, null, "<p>Manual</p>")).Recipients);

        Assert.Equal("template:Special", preview.BodySource);
        Assert.Equal("<p>Template for Jane</p>", preview.RenderedBody);
    }

    [Fact]
    public void CampaignPersistedFallbackTemplate_DoesNotBlockManualHtml()
    {
        using TestWorkspace workspace = new();
        IConfiguration config = workspace.Configuration();
        CampaignStoreService campaigns = new(config);
        CampaignState campaign = campaigns.Create(new("Campaign", null, "default", null), "Subject", "default");
        campaigns.AddRecipient(campaign.Id, new("Jane Doe", "jane@example.com", "Example", null, "default", null));
        OriginalEmailService service = new(
            config,
            new CsvReaderService(config),
            new TemplateService(config),
            new SentMailTrackerService(config),
            new RecordingEmailDispatcher(),
            campaigns,
            new AttachmentService(config),
            new BatchOperationService());

        OriginalEmailPreviewItem preview = Assert.Single(service.Preview(new(
            ["jane@example.com"], null, null, null, false, campaign.Id, "<p>Manual for {{name}}</p>")).Recipients);

        Assert.Equal("manual-html", preview.BodySource);
        Assert.Equal("<p>Manual for Jane</p>", preview.RenderedBody);
    }

    [Fact]
    public async Task Send_UsesFakeDispatcherAndPersistsSuccessImmediately()
    {
        using TestWorkspace workspace = new();
        workspace.WriteCsv("Jane Doe,Example,jane@example.com,,");
        RecordingEmailDispatcher dispatcher = new();
        OriginalEmailService service = CreateService(workspace, dispatcher);
        SendOriginalEmailsRequest request = new(
            null, ["jane@example.com"], null, null, null, false, true, OriginalEmailService.RequiredConfirmationText);

        OriginalEmailSendResult result = (await service.TrySendAsync(request))!;

        Assert.Equal(1, result.Sent);
        Assert.Single(dispatcher.Messages);
        Assert.True(new SentMailTrackerService(workspace.Configuration()).HasBeenSent("JANE@example.com"));
    }

    [Fact]
    public async Task Send_SkipsAlreadySentRecipient()
    {
        using TestWorkspace workspace = new();
        workspace.WriteCsv("Jane Doe,Example,jane@example.com,,");
        SentMailTrackerService tracker = new(workspace.Configuration());
        tracker.MarkAsSentAndSave("jane@example.com");
        RecordingEmailDispatcher dispatcher = new();
        OriginalEmailService service = CreateService(workspace, dispatcher, tracker);

        OriginalEmailSendResult result = (await service.TrySendAsync(new(
            null, ["jane@example.com"], null, null, null, false, true, OriginalEmailService.RequiredConfirmationText)))!;

        Assert.Equal(0, result.Attempted);
        Assert.Empty(dispatcher.Messages);
        Assert.Equal("already-sent-resend-not-approved", Assert.Single(result.Results).Status);
    }

    [Fact]
    public async Task Send_ResendsPreviouslySentRecipientOnlyWhenExplicitlyApproved()
    {
        using TestWorkspace workspace = new();
        workspace.WriteCsv("Jane Doe,Example,jane@example.com,,");
        SentMailTrackerService tracker = new(workspace.Configuration());
        tracker.MarkAsSentAndSave("jane@example.com");
        DateTimeOffset originalSentAt = tracker.GetSentAt("jane@example.com")!.Value;
        RecordingEmailDispatcher dispatcher = new();
        OriginalEmailService service = CreateService(workspace, dispatcher, tracker);

        OriginalEmailPreviewItem preview = Assert.Single(service.Preview(new(
            ["jane@example.com"], null, null, null, true)).Recipients);
        OriginalEmailSendResult result = (await service.TrySendAsync(new(
            null, ["jane@example.com"], null, null, null, true, true, OriginalEmailService.RequiredConfirmationText)))!;

        Assert.True(preview.AlreadySent);
        Assert.True(preview.ResendApproved);
        Assert.True(preview.Eligible);
        Assert.Equal(1, result.Sent);
        Assert.Single(dispatcher.Messages);
        Assert.True(tracker.GetSentAt("jane@example.com") >= originalSentAt);
    }

    [Fact]
    public async Task Send_WithCampaignPersistsProviderMessageAndThreadIds()
    {
        using TestWorkspace workspace = new();
        workspace.WriteCsv("Jane Doe,Example,jane@example.com,,");
        IConfiguration config = workspace.Configuration();
        CampaignStoreService campaigns = new(config);
        CampaignState campaign = campaigns.Create(new("Campaign", null, null, null), "Subject", "default");
        campaigns.AddRecipient(campaign.Id, new(
            "Jane Doe",
            "jane@example.com",
            "Example",
            null,
            null,
            null));
        RecordingEmailDispatcher dispatcher = new();
        OriginalEmailService service = new(
            config,
            new CsvReaderService(config),
            new TemplateService(config),
            new SentMailTrackerService(config),
            dispatcher,
            campaigns,
            new AttachmentService(config),
            new BatchOperationService());

        OriginalEmailSendResult result = (await service.TrySendAsync(new(
            campaign.Id,
            ["jane@example.com"],
            null,
            null,
            null,
            false,
            true,
            OriginalEmailService.RequiredConfirmationText)))!;

        CampaignRecipientState recipient = Assert.Single(campaigns.Get(campaign.Id)!.Recipients);
        Assert.Equal(1, result.Sent);
        Assert.Equal("message-test", recipient.OriginalMessage!.ProviderMessageId);
        Assert.Equal("thread-test", recipient.OriginalMessage.ProviderThreadId);
    }

    [Fact]
    public async Task Send_WithUnknownCampaignDoesNotCallDispatcher()
    {
        using TestWorkspace workspace = new();
        workspace.WriteCsv("Jane Doe,Example,jane@example.com,,");
        RecordingEmailDispatcher dispatcher = new();
        OriginalEmailService service = CreateService(workspace, dispatcher);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.TrySendAsync(new(
            Guid.NewGuid(),
            ["jane@example.com"],
            null,
            null,
            null,
            false,
            true,
            OriginalEmailService.RequiredConfirmationText)));

        Assert.Empty(dispatcher.Messages);
    }

    private static OriginalEmailService CreateService(
        TestWorkspace workspace,
        RecordingEmailDispatcher dispatcher,
        SentMailTrackerService? tracker = null)
    {
        IConfiguration config = workspace.Configuration();
        return new(
            config,
            new CsvReaderService(config),
            new TemplateService(config),
            tracker ?? new SentMailTrackerService(config),
            dispatcher,
            new CampaignStoreService(config),
            new AttachmentService(config),
            new BatchOperationService());
    }
}
