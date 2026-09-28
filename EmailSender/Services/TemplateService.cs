using Microsoft.Extensions.Configuration;
using System.Text.RegularExpressions;

namespace EmailSender.Services
{
    /// <summary>
    /// Manages loading and formatting of HTML email templates.
    /// </summary>
    public class TemplateService
    {
        private readonly Dictionary<string, string> _templates;
        private readonly string? _templateFolderPath;
        private static readonly Regex _minifyRegex = new(@"[\r\n\t]+");

        /// <summary>
        /// Initializes a new instance of the TemplateService.
        /// Reads the resource path from configuration and loads all templates into memory.
        /// </summary>
        public TemplateService(IConfiguration config)
        {
            // Retrieve the path where HTML templates and resources are stored
            _templateFolderPath = config["FilePaths:Templates"];

            // Pre-load all HTML templates into the dictionary for fast access later
            _templates = LoadMailTemplates();
        }

        /// <summary>
        /// Scans the resources directory for HTML files and loads them into a dictionary.
        /// Minifies the HTML by removing newlines and tabs to ensure clean formatting.
        /// </summary>
        private Dictionary<string, string> LoadMailTemplates()
        {
            Dictionary<string, string> templates = new(StringComparer.OrdinalIgnoreCase);
            // Check if the directory exists to prevent runtime directory-not-found exceptions
            if (!Directory.Exists(_templateFolderPath))
            {
                Console.WriteLine($"Warning: Resource path not found at {_templateFolderPath}");
                return templates;
            }

            // Retrieve all HTML file paths in the target directory
            string[] files = Directory.GetFiles(_templateFolderPath, "*.html");

            // Convert the array of file paths into a Dictionary where:
            // Key = File name without extension (e.g., "Template1")
            // Value = The minified HTML content
            foreach (string file in files)
            {
                string key = Path.GetFileNameWithoutExtension(file);
                string body = File.ReadAllText(file);

                // Strip out carriage returns, line feeds, and tabs to minify the HTML string
                body = _minifyRegex.Replace(body, string.Empty);
                templates.Add(key, body);
            }

            return templates;
        }

        /// <summary>
        /// Checks if a template with the specified name exists in the loaded dictionary.
        /// </summary>
        public bool TemplateExists(string templateName) => _templates.ContainsKey(NormalizeName(templateName));

        public IReadOnlyCollection<string> GetTemplateNames() => _templates.Keys.Select(name => name + ".html").OrderBy(name => name).ToArray();

        public int Reload()
        {
            _templates.Clear();
            foreach (var item in LoadMailTemplates()) _templates[item.Key] = item.Value;
            return _templates.Count;
        }

        public async Task<string> UploadAsync(IFormFile file, CancellationToken cancellationToken)
        {
            if (file.Length == 0) throw new InvalidDataException("Select a non-empty HTML file.");
            string name = Path.GetFileName(file.FileName);
            if (!name.EndsWith(".html", StringComparison.OrdinalIgnoreCase) || name.Contains("..")) throw new InvalidDataException("Template filename is invalid.");
            Directory.CreateDirectory(_templateFolderPath!);
            string destination = Path.Combine(_templateFolderPath!, name);
            string temporary = destination + ".upload-" + Guid.NewGuid().ToString("N") + ".tmp";
            await using (FileStream output = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await file.CopyToAsync(output, cancellationToken);
            Exception? last = null;
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    File.Move(temporary, destination, overwrite: true);
                    last = null;
                    break;
                }
                catch (IOException exception)
                {
                    last = exception;
                    await Task.Delay(100 * (attempt + 1), cancellationToken);
                }
            }
            if (last is not null) { File.Delete(temporary); throw last; }
            Reload(); return name;
        }

        /// <summary>
        /// Gets the personalized HTML body for the recipient.
        /// Attempts to find an organization-specific template first, falling back to a default.
        /// </summary>
        public string GetFormattedTemplate(string organizationKey, string defaultTemplateName, string firstName, string organization = "")
        {
            // Check if a specific template exists for the organization.
            string templateBody = _templates.TryGetValue(NormalizeName(organizationKey), out string? orgTemplate)
                ? orgTemplate
                : _templates[NormalizeName(defaultTemplateName)];

            // Replace the {{name}} placeholder with the recipient's actual first name
            return FormatBody(templateBody, firstName, organization);
        }

        public static string FormatBody(string body, string firstName, string organization) => body
            .Replace("{{name}}", firstName, StringComparison.Ordinal)
            .Replace("{name}", firstName, StringComparison.Ordinal)
            .Replace("{{organization}}", organization, StringComparison.Ordinal)
            .Replace("{organization}", organization, StringComparison.Ordinal);

        private static string NormalizeName(string name) => Path.GetFileNameWithoutExtension(name.Trim());

        /// <summary>
        /// Constructs the full physical file path for a specific attachment located in the resources folder.
        /// </summary>
        public string? GetResourcePath(string fileName)
        {
            if (string.IsNullOrEmpty(fileName) || string.IsNullOrEmpty(_templateFolderPath))
                return null;

            // Safely combine the base resource path with the provided file name to get the absolute path
            return Path.Combine(_templateFolderPath, fileName);
        }

        public IReadOnlyCollection<string> GetResourcePaths(IEnumerable<string>? fileNames)
        {
            if (fileNames is null) return [];

            return fileNames
                .Where(fileName => !string.IsNullOrWhiteSpace(fileName))
                .Select(fileName => Path.IsPathRooted(fileName)
                    ? fileName
                    : GetResourcePath(fileName)!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }
}
