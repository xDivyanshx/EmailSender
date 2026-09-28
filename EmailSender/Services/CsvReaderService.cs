using CsvHelper;
using CsvHelper.Configuration;
using EmailSender.Models;
using Microsoft.Extensions.Configuration;
using System.Globalization;

namespace EmailSender.Services;

public class CsvReaderService
{
    private static readonly string[] RequiredHeaders = ["Name", "Email", "Organization"];
    private readonly string _masterFilePath;

    public CsvReaderService(IConfiguration config)
    {
        _masterFilePath = config["FilePaths:MasterCsv"]
            ?? throw new ArgumentNullException("FilePaths:MasterCsv", "Master CSV file path configuration is missing.");
    }

    public List<Recipient> ReadRecipients()
    {
        if (!File.Exists(_masterFilePath))
            throw new FileNotFoundException($"CSV file was not found at '{_masterFilePath}'.");
        using FileStream stream = File.OpenRead(_masterFilePath);
        return ReadRecipients(stream);
    }

    public List<Recipient> ReadRecipients(Stream stream)
    {
        CsvConfiguration configuration = new(CultureInfo.InvariantCulture)
        {
            TrimOptions = TrimOptions.Trim,
            IgnoreBlankLines = true,
            HeaderValidated = null,
            MissingFieldFound = null
        };
        using StreamReader textReader = new(stream, leaveOpen: true);
        using CsvReader csv = new(textReader, configuration);
        if (!csv.Read() || !csv.ReadHeader()) throw new InvalidDataException("CSV file is empty.");
        string[] headers = csv.HeaderRecord ?? [];
        string[] missing = RequiredHeaders.Where(required => !headers.Contains(required, StringComparer.Ordinal)).ToArray();
        if (missing.Length > 0)
            throw new InvalidDataException($"Required CSV headers are missing: {string.Join(", ", missing)}.");

        bool hasSubject = headers.Contains("Subject", StringComparer.Ordinal);
        bool hasAttachment = headers.Contains("Attachment", StringComparer.Ordinal);
        bool hasTemplate = headers.Contains("Template", StringComparer.Ordinal);
        bool hasPresetName = headers.Contains("PresetName", StringComparer.Ordinal);
        bool hasHtmlBody = headers.Contains("HtmlBody", StringComparer.Ordinal) || headers.Contains("Body", StringComparer.Ordinal);
        bool hasAttachmentDisplayName = headers.Contains("AttachmentDisplayName", StringComparer.Ordinal);
        List<Recipient> recipients = [];
        while (csv.Read())
        {
            string name = csv.GetField("Name")?.Trim() ?? string.Empty;
            string email = csv.GetField("Email")?.Trim() ?? string.Empty;
            string organization = csv.GetField("Organization")?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(email) && string.IsNullOrWhiteSpace(organization))
                continue;
            string attachment = hasAttachment ? csv.GetField("Attachment")?.Trim() ?? string.Empty : string.Empty;
            List<string> attachments = attachment.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            recipients.Add(new()
            {
                Name = name,
                Email = email,
                Organization = organization,
                Subject = hasSubject ? csv.GetField("Subject")?.Trim() ?? string.Empty : string.Empty,
                AttachmentFileName = attachments.FirstOrDefault() ?? string.Empty,
                Attachments = attachments,
                Template = hasTemplate ? csv.GetField("Template")?.Trim() ?? string.Empty : string.Empty,
                PresetName = hasPresetName ? csv.GetField("PresetName")?.Trim() ?? string.Empty : string.Empty,
                HtmlBody = hasHtmlBody
                    ? csv.GetField(headers.Contains("HtmlBody", StringComparer.Ordinal) ? "HtmlBody" : "Body")?.Trim() ?? string.Empty
                    : string.Empty,
                AttachmentDisplayName = hasAttachmentDisplayName ? csv.GetField("AttachmentDisplayName")?.Trim() ?? string.Empty : string.Empty
            });
        }
        return recipients;
    }

    public async Task<int> ReplaceMasterFileAsync(Stream csvStream, CancellationToken cancellationToken)
    {
        string temporaryPath = _masterFilePath + ".tmp";
        string? directory = Path.GetDirectoryName(Path.GetFullPath(_masterFilePath));
        if (directory is not null) Directory.CreateDirectory(directory);
        await using (FileStream output = File.Create(temporaryPath))
            await csvStream.CopyToAsync(output, cancellationToken);

        try
        {
            int rows;
            using (FileStream validationStream = File.OpenRead(temporaryPath))
                rows = ReadRecipients(validationStream).Count;
            File.Move(temporaryPath, _masterFilePath, overwrite: true);
            return rows;
        }
        catch
        {
            File.Delete(temporaryPath);
            throw;
        }
    }
}
