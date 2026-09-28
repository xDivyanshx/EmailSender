using Microsoft.Extensions.Configuration;
using System.Text.Json;

namespace EmailSender.Services
{
    /// <summary>
    /// Tracks which emails have been sent to avoid duplicates and enforce daily limits.
    /// </summary>
    public class SentMailTrackerService
    {
        private readonly string _sentMailFilePath;
        private readonly Dictionary<string, DateTime> _sentMap;
        private readonly DeliveryLedgerService _ledger;
        private readonly object _sync = new();
        private readonly JsonSerializerOptions _serializerOptions = new() { WriteIndented = true };

        public int TotalSentCount { get { lock (_sync) return _sentMap.Count; } }

        public SentMailTrackerService(IConfiguration config) : this(config, new DeliveryLedgerService(config))
        {
        }

        public SentMailTrackerService(IConfiguration config, DeliveryLedgerService ledger)
        {
            string? filePath = config["FilePaths:SentMailList"];
            if (string.IsNullOrWhiteSpace(filePath))
            {
                // Throws a more descriptive error message naming the missing setting
                throw new ArgumentException("SentMailList file path is missing in configuration.");
            }

            _sentMailFilePath = filePath;
            _ledger = ledger;
            _sentMap = LoadSentMap();
            if (_ledger.IsEmpty && _sentMap.Count > 0)
            {
                _ledger.RecordRange(_sentMap.Select(entry => new EmailSender.Models.DeliveryLedgerEntry(
                    entry.Key,
                    entry.Value,
                    "legacy-import")));
            }
        }

        private Dictionary<string, DateTime> LoadSentMap()
        {
            if (File.Exists(_sentMailFilePath))
            {
                string json = File.ReadAllText(_sentMailFilePath);
                Dictionary<string, DateTime>? deserializedMap = JsonSerializer.Deserialize<Dictionary<string, DateTime>>(json);

                if (deserializedMap != null)
                {
                    // 2. Wrap the deserialized map in a case-insensitive dictionary
                    return new Dictionary<string, DateTime>(deserializedMap, StringComparer.OrdinalIgnoreCase);
                }
            }

            // Return an empty case-insensitive dictionary
            return new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        }

        public bool HasBeenSent(string email)
        {
            lock (_sync) return _sentMap.ContainsKey(email)
                || _ledger.GetAll().Any(entry => string.Equals(entry.Email, email, StringComparison.OrdinalIgnoreCase));
        }

        public DateTimeOffset? GetSentAt(string email)
        {
            lock (_sync)
            {
                DateTimeOffset? legacy = _sentMap.TryGetValue(email, out DateTime sentAt) ? sentAt : null;
                DateTimeOffset? latest = _ledger.GetAll()
                    .Where(entry => string.Equals(entry.Email, email, StringComparison.OrdinalIgnoreCase))
                    .Select(entry => (DateTimeOffset?)entry.SentAt)
                    .OrderByDescending(value => value)
                    .FirstOrDefault();
                return latest ?? legacy;
            }
        }

        public void MarkAsSent(string email)
        {
            lock (_sync)
            {
                if (!_sentMap.ContainsKey(email)) _sentMap.Add(email, DateTime.Now);
            }
        }

        public void MarkAsSentAndSave(string email)
        {
            lock (_sync)
            {
                _sentMap[email] = DateTime.Now;
                SaveChangesCore();
            }
        }

        public int GetSentTodayCount()
        {
            lock (_sync)
            {
                return _ledger.CountOn(DateTime.Today);
            }
        }

        public void RecordSuccessfulDelivery(string email, string messageType, EmailDispatchResult delivery)
        {
            if (!delivery.Success) throw new ArgumentException("Only successful deliveries can be recorded.");
            _ledger.Record(new(email, DateTimeOffset.Now, messageType, delivery.ProviderMessageId, delivery.ProviderThreadId));
            MarkAsSentAndSave(email);
        }

        /// <summary>
        /// Saves the updated list of sent emails back to the JSON file.
        /// </summary>
        public void SaveChanges()
        {
            lock (_sync) SaveChangesCore();
        }

        private void SaveChangesCore()
        {
            string? directory = Path.GetDirectoryName(Path.GetFullPath(_sentMailFilePath));
            if (directory is not null) Directory.CreateDirectory(directory);

            string temporaryPath = _sentMailFilePath + ".tmp";
            string json = JsonSerializer.Serialize(_sentMap, _serializerOptions);
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, _sentMailFilePath, overwrite: true);
        }
    }
}
