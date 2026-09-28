namespace EmailSender.Models;

public sealed record AttachmentFileInfo(string FileName, long Size, DateTimeOffset LastModified);

public sealed class BatchPreflightException : Exception
{
    public IReadOnlyCollection<string> Errors { get; }

    public BatchPreflightException(IEnumerable<string> errors)
        : base("Batch preflight failed. No email was sent.") => Errors = errors.ToArray();
}
