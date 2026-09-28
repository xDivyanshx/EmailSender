using EmailSender.Models;

namespace EmailSender.Services;

public sealed class AttachmentService
{
    private readonly string _rootPath;

    public AttachmentService(IConfiguration config)
    {
        _rootPath = Path.GetFullPath(config["FilePaths:Attachments"] ?? "../../../../resources/attachments");
        Directory.CreateDirectory(_rootPath);
    }

    public IReadOnlyCollection<AttachmentFileInfo> List(Guid campaignId) =>
        Directory.EnumerateFiles(GetCampaignPath(campaignId), "*", SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path))
            .OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
            .Select(file => new AttachmentFileInfo(file.Name, file.Length, file.LastWriteTimeUtc))
            .ToArray();

    public IReadOnlyCollection<string> GetResolvedPaths(Guid? campaignId, IEnumerable<string>? fileNames) =>
        (fileNames ?? [])
            .Where(fileName => !string.IsNullOrWhiteSpace(fileName))
            .Select(fileName => Resolve(campaignId, fileName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public string Resolve(Guid? campaignId, string fileName)
    {
        ValidateFileName(fileName);
        string directory = campaignId is Guid id ? GetCampaignPath(id) : _rootPath;
        string fullPath = Path.GetFullPath(Path.Combine(directory, fileName));
        if (!fullPath.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Attachment path escapes the managed attachments directory.");
        return fullPath;
    }

    public async Task<AttachmentFileInfo> UploadAsync(Guid campaignId, IFormFile file, bool overwrite, CancellationToken cancellationToken)
    {
        string path = ResolveCampaignFile(campaignId, file.FileName);
        if (File.Exists(path) && !overwrite)
            throw new IOException($"Attachment '{file.FileName}' already exists. Set overwrite=true to replace it.");
        string temporaryPath = path + ".tmp";
        await using (Stream input = file.OpenReadStream())
        await using (FileStream output = File.Create(temporaryPath))
            await input.CopyToAsync(output, cancellationToken);
        File.Move(temporaryPath, path, overwrite: true);
        FileInfo info = new(path);
        return new(info.Name, info.Length, info.LastWriteTimeUtc);
    }

    public void Delete(Guid campaignId, string fileName)
    {
        string path = ResolveCampaignFile(campaignId, fileName);
        if (File.Exists(path)) File.Delete(path);
    }

    public void CopyCampaign(Guid sourceCampaignId, Guid destinationCampaignId)
    {
        string source = GetCampaignPath(sourceCampaignId);
        string destination = GetCampaignPath(destinationCampaignId);
        foreach (string path in Directory.EnumerateFiles(source, "*", SearchOption.TopDirectoryOnly))
            File.Copy(path, Path.Combine(destination, Path.GetFileName(path)), overwrite: false);
    }

    public void DeleteCampaign(Guid campaignId)
    {
        string path = Path.GetFullPath(Path.Combine(_rootPath, campaignId.ToString("D")));
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
    }

    private string GetCampaignPath(Guid campaignId)
    {
        string path = Path.GetFullPath(Path.Combine(_rootPath, campaignId.ToString("D")));
        Directory.CreateDirectory(path);
        return path;
    }

    private string ResolveCampaignFile(Guid campaignId, string fileName)
    {
        ValidateFileName(fileName);
        return Path.Combine(GetCampaignPath(campaignId), fileName);
    }

    private static void ValidateFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) throw new InvalidDataException("Attachment filename is required.");
        if (Path.IsPathRooted(fileName) || fileName.Contains('/') || fileName.Contains('\\') || fileName.Contains("..", StringComparison.Ordinal))
            throw new InvalidDataException("Attachments must be filenames only, without paths or '..'.");
        if (Path.GetFileName(fileName) != fileName || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new InvalidDataException("Attachment filename is invalid.");
    }
}
