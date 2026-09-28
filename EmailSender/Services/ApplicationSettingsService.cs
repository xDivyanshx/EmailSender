using System.Text.Json;
using EmailSender.Models;

namespace EmailSender.Services;

public sealed class ApplicationSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly IConfiguration _config;
    private readonly DeliveryLedgerService _ledger;
    private readonly string _filePath;
    private readonly object _sync = new();

    public ApplicationSettingsService(IConfiguration config, DeliveryLedgerService ledger)
    {
        _config = config;
        _ledger = ledger;
        _filePath = Path.GetFullPath(config["FilePaths:ApplicationSettings"]
            ?? "../../../../resources/app-data/settings.json");
    }

    public ApplicationSettings Get()
    {
        DateTimeOffset now = DateTimeOffset.Now;
        return new(
            Required("Mail:SenderName"),
            Required("Mail:Subject"),
            Required("FilePaths:MailTemplate"),
            PositiveDailyLimit(),
            Required("Gmail:Account"),
            _ledger.CountLast24Hours(now),
            _ledger.CountOn(now.Date),
            _ledger.CountOnUtc(now.UtcDateTime.Date));
    }

    public ApplicationSettings Update(UpdateApplicationSettingsRequest request)
    {
        string senderName = RequiredValue(request.SenderName, "Sender name");
        string subject = RequiredValue(request.DefaultSubject, "Default subject");
        string template = RequiredValue(request.DefaultTemplate, "Default template");
        if (request.DailyLimit is < 1 or > 2000)
            throw new ArgumentException("Daily limit must be between 1 and 2000.");

        lock (_sync)
        {
            string? directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            string temporaryPath = _filePath + ".tmp";
            var document = new
            {
                FilePaths = new { MailTemplate = template },
                Mail = new { SenderName = senderName, Subject = subject, DailyLimit = request.DailyLimit }
            };
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(document, JsonOptions));
            File.Move(temporaryPath, _filePath, true);

            _config["Mail:SenderName"] = senderName;
            _config["Mail:Subject"] = subject;
            _config["Mail:DailyLimit"] = request.DailyLimit.ToString();
            _config["FilePaths:MailTemplate"] = template;
            return Get();
        }
    }

    private string Required(string key) =>
        _config[key] ?? throw new InvalidOperationException($"Configuration '{key}' is missing.");

    private int PositiveDailyLimit() =>
        int.TryParse(_config["Mail:DailyLimit"], out int value) && value > 0
            ? value
            : throw new InvalidOperationException("Mail:DailyLimit must be a positive integer.");

    private static string RequiredValue(string value, string label) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"{label} is required.") : value.Trim();
}
