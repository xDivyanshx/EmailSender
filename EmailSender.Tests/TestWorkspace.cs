using Microsoft.Extensions.Configuration;

namespace EmailSender.Tests;

internal sealed class TestWorkspace : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "cold-mail-sender-tests", Guid.NewGuid().ToString("N"));
    public string Resources => Path.Combine(Root, "resources");
    public string CsvPath => Path.Combine(Resources, "master.csv");
    public string SentPath => Path.Combine(Resources, "sent.json");
    public string CampaignPath => Path.Combine(Resources, "campaigns.json");
    public string DeliveryLedgerPath => Path.Combine(Resources, "delivery-ledger.json");
    public string SettingsPath => Path.Combine(Resources, "settings.json");
    public string OrganizationTrackingPath => Path.Combine(Resources, "organization-tracking.json");
    public string Attachments => Path.Combine(Resources, "attachments");
    public string GmailCredentialsPath => Path.Combine(Resources, "google-oauth-client.json");
    public string GmailTokenDirectory => Path.Combine(Resources, "google-tokens");

    public TestWorkspace()
    {
        Directory.CreateDirectory(Resources);
        Directory.CreateDirectory(Attachments);
        File.WriteAllText(Path.Combine(Resources, "default.html"), "Hi {{name}}");
        File.WriteAllText(CsvPath, "Name,Organization,Email,Subject,Attachment\n");
    }

    public IConfiguration Configuration(int dailyLimit = 20) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["FilePaths:MasterCsv"] = CsvPath,
            ["FilePaths:Templates"] = Resources,
            ["FilePaths:MailTemplate"] = "default",
            ["FilePaths:SentMailList"] = SentPath,
            ["FilePaths:CampaignStore"] = CampaignPath,
            ["FilePaths:DeliveryLedger"] = DeliveryLedgerPath,
            ["FilePaths:ApplicationSettings"] = SettingsPath,
            ["FilePaths:OrganizationTracking"] = OrganizationTrackingPath,
            ["FilePaths:Attachments"] = Attachments,
            ["Mail:SenderName"] = "Test Sender",
            ["Mail:Subject"] = "Configured subject",
            ["Mail:DailyLimit"] = dailyLimit.ToString(),
            ["Gmail:Account"] = "sender@example.com"
            ,["Gmail:CredentialsPath"] = GmailCredentialsPath
            ,["Gmail:TokenDirectory"] = GmailTokenDirectory
        }).Build();

    public void WriteCsv(params string[] rows) =>
        File.WriteAllLines(CsvPath, ["Name,Organization,Email,Subject,Attachment", .. rows]);

    public void Dispose()
    {
        if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
    }
}
