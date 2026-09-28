using EmailSender.Models;
using System.Text.Json;

namespace EmailSender.Services;

public sealed class DeliveryLedgerService
{
    private readonly string _path;
    private readonly object _sync = new();
    private readonly List<DeliveryLedgerEntry> _entries;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public DeliveryLedgerService(IConfiguration config)
    {
        _path = config["FilePaths:DeliveryLedger"] ?? "../../../../resources/app-data/delivery-ledger.json";
        _entries = Load();
    }

    public IReadOnlyCollection<DeliveryLedgerEntry> GetAll()
    {
        lock (_sync) return _entries.ToArray();
    }

    public bool IsEmpty
    {
        get { lock (_sync) return _entries.Count == 0; }
    }

    public void Record(DeliveryLedgerEntry entry)
    {
        lock (_sync)
        {
            _entries.Add(entry with { Email = entry.Email.Trim().ToLowerInvariant() });
            SaveCore();
        }
    }

    public void RecordRange(IEnumerable<DeliveryLedgerEntry> entries)
    {
        lock (_sync)
        {
            _entries.AddRange(entries.Select(entry => entry with { Email = entry.Email.Trim().ToLowerInvariant() }));
            SaveCore();
        }
    }

    public int CountOn(DateTime date)
    {
        lock (_sync) return _entries.Count(entry => entry.SentAt.ToLocalTime().Date == date);
    }

    public int CountLast24Hours(DateTimeOffset now)
    {
        DateTimeOffset cutoff = now - TimeSpan.FromHours(24);
        lock (_sync) return _entries.Count(entry => entry.SentAt >= cutoff && entry.SentAt <= now);
    }

    public int CountOnUtc(DateTime date)
    {
        lock (_sync) return _entries.Count(entry => entry.SentAt.UtcDateTime.Date == date);
    }

    private List<DeliveryLedgerEntry> Load()
    {
        if (!File.Exists(_path)) return [];
        try { return JsonSerializer.Deserialize<List<DeliveryLedgerEntry>>(File.ReadAllText(_path), JsonOptions) ?? []; }
        catch (JsonException) { return []; }
    }

    private void SaveCore()
    {
        string? directory = Path.GetDirectoryName(Path.GetFullPath(_path));
        if (directory is not null) Directory.CreateDirectory(directory);
        string temporaryPath = _path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(_entries, JsonOptions));
        File.Move(temporaryPath, _path, overwrite: true);
    }
}
