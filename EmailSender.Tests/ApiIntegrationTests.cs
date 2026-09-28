using EmailSender.Models;
using EmailSender.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Net;
using System.Net.Http.Json;

namespace EmailSender.Tests;

public sealed class ApiIntegrationTests : IDisposable
{
    private readonly TestWorkspace _workspace = new();
    private readonly RecordingEmailDispatcher _dispatcher = new();
    private readonly WebApplicationFactory<Program> _factory;

    public ApiIntegrationTests()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FilePaths:MasterCsv"] = _workspace.CsvPath,
                ["FilePaths:Templates"] = _workspace.Resources,
                ["FilePaths:MailTemplate"] = "default",
                ["FilePaths:SentMailList"] = _workspace.SentPath,
                ["FilePaths:CampaignStore"] = _workspace.CampaignPath,
                ["FilePaths:Attachments"] = _workspace.Attachments,
                ["FilePaths:DeliveryLedger"] = _workspace.DeliveryLedgerPath,
                ["FilePaths:ApplicationSettings"] = _workspace.SettingsPath,
                ["Mail:Subject"] = "Configured subject",
                ["Mail:SenderName"] = "Test Sender",
                ["Mail:DailyLimit"] = "20",
                ["Gmail:Account"] = "sender@example.com"
            }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IEmailDispatchService>();
                services.AddSingleton<IEmailDispatchService>(_dispatcher);
            });
        });
    }

    [Fact]
    public async Task CampaignRecipientAndManualHtmlPreview_WorkThroughHttp()
    {
        using HttpClient client = _factory.CreateClient();
        HttpResponseMessage created = await client.PostAsJsonAsync("/api/campaigns", new CreateCampaignRequest("HTTP campaign", null, null, null));
        CampaignState campaign = (await created.Content.ReadFromJsonAsync<CampaignState>())!;
        await client.PostAsJsonAsync($"/api/campaigns/{campaign.Id}/recipients",
            new SaveCampaignRecipientRequest("Jane Doe", "jane@example.com", "Example", null, null, null));

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/original-emails/preview", new OriginalEmailSelection(
            ["jane@example.com"], null, null, null, false, campaign.Id, "<p>Hi {{name}} at {organization}</p>"));
        OriginalEmailPreview preview = (await response.Content.ReadFromJsonAsync<OriginalEmailPreview>())!;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        OriginalEmailPreviewItem recipient = Assert.Single(preview.Recipients);
        Assert.Equal("manual-html", recipient.BodySource);
        Assert.Equal("<p>Hi Jane at Example</p>", recipient.RenderedBody);
        Assert.Empty(_dispatcher.Messages);
    }

    [Fact]
    public async Task UnsafeSendAndIdleCancellation_AreRejectedWithoutDispatch()
    {
        using HttpClient client = _factory.CreateClient();
        HttpResponseMessage send = await client.PostAsJsonAsync("/api/original-emails/send", new SendOriginalEmailsRequest(
            null, ["jane@example.com"], null, null, null, false, false, string.Empty));
        HttpResponseMessage cancel = await client.PostAsync("/api/batch/cancel", null);

        Assert.Equal(HttpStatusCode.BadRequest, send.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, cancel.StatusCode);
        Assert.Empty(_dispatcher.Messages);
    }

    [Fact]
    public async Task ConfirmedSend_UsesFakeProviderAndPersistsCompletedOperation()
    {
        using HttpClient client = _factory.CreateClient();
        CampaignState campaign = (await (await client.PostAsJsonAsync("/api/campaigns",
            new CreateCampaignRequest("Send campaign", null, null, null))).Content.ReadFromJsonAsync<CampaignState>())!;
        await client.PostAsJsonAsync($"/api/campaigns/{campaign.Id}/recipients",
            new SaveCampaignRecipientRequest("Jane Doe", "jane@example.com", "Example", null, null, null));

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/original-emails/send", new SendOriginalEmailsRequest(
            campaign.Id,
            ["jane@example.com"],
            "HTTP subject",
            null,
            null,
            false,
            true,
            OriginalEmailService.RequiredConfirmationText,
            "<p>Hi {{name}}</p>"));
        BatchOperationStatus status = (await client.GetFromJsonAsync<BatchOperationStatus>("/api/batch/status"))!;
        CampaignState saved = (await client.GetFromJsonAsync<CampaignState>($"/api/campaigns/{campaign.Id}"))!;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(_dispatcher.Messages);
        Assert.False(status.Running);
        Assert.Equal(1, status.Sent);
        Assert.Equal("message-test", Assert.Single(saved.Recipients).OriginalMessage!.ProviderMessageId);
        Assert.Single(new DeliveryLedgerService(_workspace.Configuration()).GetAll());
    }

    [Fact]
    public async Task CampaignManagement_RenamesDuplicatesExportsAndDeletesThroughHttp()
    {
        using HttpClient client = _factory.CreateClient();
        CampaignState campaign = (await (await client.PostAsJsonAsync("/api/campaigns",
            new CreateCampaignRequest("Managed campaign", "Default subject", "default", ["resume.pdf"])))
            .Content.ReadFromJsonAsync<CampaignState>())!;
        await client.PostAsJsonAsync($"/api/campaigns/{campaign.Id}/recipients",
            new SaveCampaignRecipientRequest("Jane Doe", "jane@example.com", "Example", "Custom subject", "example", ["resume.pdf"]));

        HttpResponseMessage renamedResponse = await client.PutAsJsonAsync($"/api/campaigns/{campaign.Id}",
            new RenameCampaignRequest("Renamed campaign"));
        CampaignState renamed = (await renamedResponse.Content.ReadFromJsonAsync<CampaignState>())!;
        HttpResponseMessage duplicateResponse = await client.PostAsync($"/api/campaigns/{campaign.Id}/duplicate", null);
        CampaignState duplicate = (await duplicateResponse.Content.ReadFromJsonAsync<CampaignState>())!;
        CampaignState exported = (await client.GetFromJsonAsync<CampaignState>($"/api/campaigns/{duplicate.Id}/export"))!;
        HttpResponseMessage deletedResponse = await client.DeleteAsync($"/api/campaigns/{campaign.Id}");
        HttpResponseMessage missingResponse = await client.GetAsync($"/api/campaigns/{campaign.Id}");

        Assert.Equal(HttpStatusCode.OK, renamedResponse.StatusCode);
        Assert.Equal("Renamed campaign", renamed.Name);
        Assert.Equal(HttpStatusCode.Created, duplicateResponse.StatusCode);
        Assert.NotEqual(campaign.Id, duplicate.Id);
        Assert.Equal("Renamed campaign Copy", duplicate.Name);
        CampaignRecipientState copiedRecipient = Assert.Single(duplicate.Recipients);
        Assert.Equal("jane@example.com", copiedRecipient.Email);
        Assert.Null(copiedRecipient.OriginalMessage);
        Assert.Empty(copiedRecipient.FollowUps);
        Assert.Equal(duplicate.Id, exported.Id);
        Assert.Equal(HttpStatusCode.NoContent, deletedResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missingResponse.StatusCode);
    }

    [Fact]
    public async Task LocalSettings_CanBeReadAndUpdatedThroughHttp()
    {
        using HttpClient client = _factory.CreateClient();

        ApplicationSettings initial = (await client.GetFromJsonAsync<ApplicationSettings>("/api/settings"))!;
        HttpResponseMessage response = await client.PutAsJsonAsync("/api/settings",
            new UpdateApplicationSettingsRequest("Updated Sender", "Updated subject", "updated-template", 75));
        ApplicationSettings updated = (await response.Content.ReadFromJsonAsync<ApplicationSettings>())!;

        Assert.Equal("Test Sender", initial.SenderName);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Updated Sender", updated.SenderName);
        Assert.Equal(75, updated.DailyLimit);
        Assert.True(File.Exists(_workspace.SettingsPath));
    }

    public void Dispose()
    {
        _factory.Dispose();
        _workspace.Dispose();
    }
}
