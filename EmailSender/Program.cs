using EmailSender.Services;
using EmailSender.Models;

// Keep all configured relative file paths deterministic regardless of where
// the `dotnet run` command or packaged application is launched from.
Directory.SetCurrentDirectory(AppContext.BaseDirectory);

WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
    WebRootPath = "wwwroot"
});

builder.Configuration
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
builder.Configuration.AddJsonFile(
    builder.Configuration["FilePaths:ApplicationSettings"] ?? "../../../../resources/app-data/settings.json",
    optional: true,
    reloadOnChange: true);

builder.Services.AddSingleton<TemplateService>();
builder.Services.AddSingleton<AttachmentService>();
builder.Services.AddSingleton<SentMailTrackerService>();
builder.Services.AddSingleton<DeliveryLedgerService>();
builder.Services.AddSingleton<BatchOperationService>();
builder.Services.AddSingleton<GmailMimeMessageFactory>();
builder.Services.AddSingleton<IEmailDispatchService, GmailEmailDispatchService>();
builder.Services.AddSingleton<CsvReaderService>();
builder.Services.AddSingleton<OriginalEmailService>();
builder.Services.AddSingleton<CampaignStoreService>();
builder.Services.AddSingleton<GmailConnectionService>();
builder.Services.AddSingleton<IGmailThreadReader, GmailThreadReader>();
builder.Services.AddSingleton<ReplyCheckService>();
builder.Services.AddSingleton<FollowUpService>();
builder.Services.AddSingleton<ApplicationSettingsService>();
builder.Services.AddSingleton<OrganizationTrackerService>();

WebApplication app = builder.Build();

app.UseExceptionHandler(exceptionHandler => exceptionHandler.Run(async context =>
{
    Exception exception = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()!.Error;
    int status;
    object body;
    if (exception is BatchPreflightException preflight)
    {
        status = StatusCodes.Status400BadRequest;
        body = new { error = preflight.Message, errors = preflight.Errors };
    }
    else if (exception is InvalidDataException or ArgumentException or InvalidOperationException)
    {
        status = StatusCodes.Status400BadRequest;
        body = new { error = exception.Message };
    }
    else if (exception is KeyNotFoundException)
    {
        status = StatusCodes.Status404NotFound;
        body = new { error = exception.Message };
    }
    else if (exception is IOException)
    {
        status = StatusCodes.Status409Conflict;
        body = new { error = exception.Message };
    }
    else
    {
        status = StatusCodes.Status500InternalServerError;
        body = new { error = "An unexpected local application error occurred." };
    }

    context.Response.StatusCode = status;
    await context.Response.WriteAsJsonAsync(body);
}));

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/status", (IConfiguration config) => Results.Ok(new
{
    application = "Cold Mail Sender",
    mode = "localhost",
    manualSendingOnly = true,
    dailyLimit = config["Mail:DailyLimit"]
}));

app.MapGet("/api/settings", (ApplicationSettingsService settings) => Results.Ok(settings.Get()));
app.MapPut("/api/settings", (UpdateApplicationSettingsRequest request, ApplicationSettingsService settings) =>
    Results.Ok(settings.Update(request)));

app.MapGet("/api/recipients", (CsvReaderService csvReader, SentMailTrackerService tracker) =>
{
    var recipients = csvReader.ReadRecipients()
        .Select(recipient => new
        {
            recipient.Name,
            recipient.Email,
            recipient.Organization,
            recipient.Subject,
            recipient.AttachmentFileName,
            sent = tracker.HasBeenSent(recipient.Email)
        });

    return Results.Ok(recipients);
});

app.MapPost("/api/recipients/import", async (
    IFormFile file,
    CsvReaderService csvReader,
    CancellationToken cancellationToken) =>
{
    if (file.Length == 0) return Results.BadRequest(new { error = "Select a non-empty CSV file." });
    await using Stream stream = file.OpenReadStream();
    int imported = await csvReader.ReplaceMasterFileAsync(stream, cancellationToken);
    return Results.Ok(new { imported });
}).DisableAntiforgery();

app.MapGet("/api/templates", (TemplateService templates) =>
    Results.Ok(templates.GetTemplateNames()));
app.MapPost("/api/templates/reload", (TemplateService templates) =>
    Results.Ok(new { templates = templates.Reload(), names = templates.GetTemplateNames() }));
app.MapPost("/api/templates/upload", async (IFormFile file, TemplateService templates, CancellationToken cancellationToken) => Results.Ok(new { name = await templates.UploadAsync(file, cancellationToken), names = templates.GetTemplateNames() })).DisableAntiforgery();

app.MapGet("/api/batch/status", (BatchOperationService batch) => Results.Ok(batch.GetStatus()));
app.MapPost("/api/batch/cancel", (BatchOperationService batch) =>
    batch.RequestCancellation()
        ? Results.Accepted(value: new { message = "Cancellation requested. The current provider call will finish first." })
        : Results.Conflict(new { error = "No batch is currently running." }));

app.MapGet("/api/campaigns/{campaignId:guid}/attachments", (Guid campaignId, AttachmentService attachments, CampaignStoreService campaigns) =>
    campaigns.Get(campaignId) is null ? Results.NotFound() : Results.Ok(attachments.List(campaignId)));

app.MapPost("/api/campaigns/{campaignId:guid}/attachments", async (
    Guid campaignId,
    IFormFile file,
    AttachmentService attachments,
    CampaignStoreService campaigns,
    bool overwrite,
    CancellationToken cancellationToken) =>
{
    if (campaigns.Get(campaignId) is null) return Results.NotFound(new { error = "Campaign was not found." });
    if (file.Length == 0) return Results.BadRequest(new { error = "Select a non-empty attachment." });
    AttachmentFileInfo uploaded = await attachments.UploadAsync(campaignId, file, overwrite, cancellationToken);
    return Results.Ok(uploaded);
}).DisableAntiforgery();

app.MapDelete("/api/campaigns/{campaignId:guid}/attachments/{fileName}", (
    Guid campaignId,
    string fileName,
    AttachmentService attachments,
    CampaignStoreService campaigns) =>
{
    if (campaigns.Get(campaignId) is null) return Results.NotFound(new { error = "Campaign was not found." });
    IReadOnlyCollection<string> references = campaigns.GetAttachmentReferences(campaignId, fileName);
    if (references.Count > 0)
        return Results.Conflict(new { error = $"Attachment is referenced by {string.Join("; ", references)} and cannot be deleted." });
    attachments.Delete(campaignId, fileName);
    return Results.NoContent();
});

app.MapPost("/api/original-emails/preview", (
    OriginalEmailSelection selection,
    OriginalEmailService service) => Results.Ok(service.Preview(selection)));

app.MapPost("/api/original-emails/send", async (
    SendOriginalEmailsRequest request,
    OriginalEmailService service,
    CancellationToken cancellationToken) =>
{
    if (!request.Confirm || !string.Equals(
            request.ConfirmationText,
            OriginalEmailService.RequiredConfirmationText,
            StringComparison.Ordinal))
    {
        return Results.BadRequest(new
        {
            error = "Explicit confirmation is required.",
            requiredConfirmationText = OriginalEmailService.RequiredConfirmationText
        });
    }

    if (request.Emails is not { Count: > 0 })
    {
        return Results.BadRequest(new { error = "Select at least one recipient email." });
    }

    OriginalEmailSendResult? result = await service.TrySendAsync(request, cancellationToken);
    return result is null
        ? Results.Conflict(new { error = "Another original-email send operation is already running." })
        : Results.Ok(result);
});

app.MapGet("/api/campaigns", (CampaignStoreService campaigns) => Results.Ok(campaigns.GetAll()));

app.MapGet("/api/organizations", (OrganizationTrackerService organizations) => Results.Ok(organizations.GetAll()));
app.MapPut("/api/organizations/tracking", (
    UpdateOrganizationTrackingRequest request,
    OrganizationTrackerService organizations) => Results.Ok(organizations.Update(request)));
app.MapPost("/api/organizations/replies/check", async (
    ReplyCheckService replies,
    CancellationToken cancellationToken) =>
{
    WorkspaceReplyCheckResult? result = await replies.CheckAllAsync(cancellationToken);
    return result is null
        ? Results.Conflict(new { error = "Another reply check is already running." })
        : Results.Ok(result);
});

app.MapGet("/api/campaigns/{id:guid}", (Guid id, CampaignStoreService campaigns) =>
    campaigns.Get(id) is { } campaign ? Results.Ok(campaign) : Results.NotFound());

app.MapPost("/api/campaigns", (
    CreateCampaignRequest request,
    CampaignStoreService campaigns,
    AttachmentService attachments,
    IConfiguration config) =>
{
    CampaignState campaign = campaigns.Create(
        request,
        config["Mail:Subject"] ?? string.Empty,
        config["FilePaths:MailTemplate"] ?? string.Empty);
    attachments.List(campaign.Id);
    return Results.Created($"/api/campaigns/{campaign.Id}", campaign);
});

app.MapPut("/api/campaigns/{id:guid}", (Guid id, RenameCampaignRequest request, CampaignStoreService campaigns) =>
    Results.Ok(campaigns.Rename(id, request.Name)));

app.MapPut("/api/campaigns/{id:guid}/defaults", (Guid id, UpdateCampaignDefaultsRequest request, CampaignStoreService campaigns) =>
    Results.Ok(campaigns.UpdateDefaults(id, request)));

app.MapPost("/api/campaigns/{id:guid}/duplicate", (Guid id, CampaignStoreService campaigns, AttachmentService attachments) =>
{
    CampaignState copy = campaigns.Duplicate(id);
    attachments.CopyCampaign(id, copy.Id);
    return Results.Created($"/api/campaigns/{copy.Id}", copy);
});

app.MapDelete("/api/campaigns/{id:guid}", (Guid id, CampaignStoreService campaigns, AttachmentService attachments) =>
{
    campaigns.Delete(id);
    attachments.DeleteCampaign(id);
    return Results.NoContent();
});

app.MapGet("/api/campaigns/{id:guid}/export", (Guid id, CampaignStoreService campaigns) =>
{
    CampaignState campaign = campaigns.Get(id) ?? throw new KeyNotFoundException("Campaign was not found.");
    return Results.Json(campaign, contentType: "application/json");
});

app.MapPost("/api/campaigns/{id:guid}/recipients", (
    Guid id,
    SaveCampaignRecipientRequest request,
    CampaignStoreService campaigns) =>
{
    CampaignRecipientState recipient = campaigns.AddRecipient(id, request);
    return Results.Created($"/api/campaigns/{id}/recipients/{recipient.Id}", recipient);
});

app.MapPut("/api/campaigns/{id:guid}/recipients/{recipientId:guid}", (
    Guid id,
    Guid recipientId,
    SaveCampaignRecipientRequest request,
    CampaignStoreService campaigns) => Results.Ok(campaigns.UpdateRecipient(id, recipientId, request)));

app.MapDelete("/api/campaigns/{id:guid}/recipients/{recipientId:guid}", (
    Guid id,
    Guid recipientId,
    CampaignStoreService campaigns) =>
{
    campaigns.DeleteRecipient(id, recipientId);
    return Results.NoContent();
});

app.MapPost("/api/campaigns/{id:guid}/recipients/import", async (
    Guid id,
    IFormFile file,
    CsvReaderService csvReader,
    CampaignStoreService campaigns,
    CancellationToken cancellationToken) =>
{
    if (file.Length == 0) return Results.BadRequest(new { error = "Select a non-empty CSV file." });
    await using Stream stream = file.OpenReadStream();
    List<Recipient> imported = csvReader.ReadRecipients(stream);
    int count = campaigns.ImportRecipients(id, imported);
    return Results.Ok(new { imported = count });
}).DisableAntiforgery();

app.MapGet("/api/gmail/status", (GmailConnectionService gmail) =>
    Results.Ok(gmail.GetStatus()));

app.MapPost("/api/gmail/connect", async (
    GmailConnectionService gmail,
    CancellationToken cancellationToken) =>
{
    try
    {
        GmailConnectionStatus? status = await gmail.ConnectAsync(cancellationToken);
        return status is null
            ? Results.Conflict(new { error = "A Gmail authorization operation is already running." })
            : Results.Ok(status);
    }
    catch (Exception exception)
    {
        return Results.BadRequest(new { error = $"Gmail authorization failed: {exception.Message}" });
    }
});

app.MapPost("/api/campaigns/{id:guid}/replies/check", async (
    Guid id,
    ReplyCheckService replies,
    CampaignStoreService campaigns,
    CancellationToken cancellationToken) =>
{
    if (campaigns.Get(id) is null) return Results.NotFound(new { error = "Campaign was not found." });
    CampaignReplyCheckResult? result = await replies.CheckAsync(id, cancellationToken);
    return result is null
        ? Results.Conflict(new { error = "Another reply check is already running." })
        : Results.Ok(result);
});

app.MapGet("/api/campaigns/{id:guid}/recipients/{recipientId:guid}/reply", async (
    Guid id,
    Guid recipientId,
    CampaignStoreService campaigns,
    IGmailThreadReader gmail,
    CancellationToken cancellationToken) =>
{
    CampaignState campaign = campaigns.Get(id) ?? throw new KeyNotFoundException("Campaign was not found.");
    CampaignRecipientState recipient = campaign.Recipients.SingleOrDefault(item => item.Id == recipientId)
        ?? throw new KeyNotFoundException("Campaign recipient was not found.");
    string messageId = recipient.ReplyCheck.MatchedMessageId
        ?? throw new InvalidOperationException("No matched reply message is available for this recipient.");
    return Results.Ok(await gmail.GetMessageAsync(messageId, cancellationToken));
});

app.MapPost("/api/campaigns/{id:guid}/follow-ups/preview", (
    Guid id,
    FollowUpPreviewRequest request,
    FollowUpService followUps,
    CampaignStoreService campaigns) =>
    campaigns.Get(id) is null
        ? Results.NotFound(new { error = "Campaign was not found." })
        : Results.Ok(followUps.Preview(id, request)));

app.MapPost("/api/campaigns/{id:guid}/follow-ups/send", async (
    Guid id,
    SendFollowUpsRequest request,
    FollowUpService followUps,
    CampaignStoreService campaigns,
    CancellationToken cancellationToken) =>
{
    if (campaigns.Get(id) is null) return Results.NotFound(new { error = "Campaign was not found." });
    if (!request.Confirm || !string.Equals(
            request.ConfirmationText,
            FollowUpService.RequiredConfirmationText,
            StringComparison.Ordinal))
    {
        return Results.BadRequest(new
        {
            error = "Explicit confirmation is required.",
            requiredConfirmationText = FollowUpService.RequiredConfirmationText
        });
    }
    if (request.Emails is not { Count: > 0 })
        return Results.BadRequest(new { error = "Select at least one follow-up recipient." });

    FollowUpSendResult? result = await followUps.TrySendAsync(id, request, cancellationToken);
    return result is null
        ? Results.Conflict(new { error = "Another follow-up send operation is already running." })
        : Results.Ok(result);
});

app.MapFallbackToFile("index.html");

app.Run();

public partial class Program;
