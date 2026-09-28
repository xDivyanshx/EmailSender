namespace EmailSender.Services;

internal static class GmailAuthorizationPolicy
{
    private const string MarkerFileName = ".authorized-at";
    private static readonly TimeSpan Validity = TimeSpan.FromHours(2);

    public static bool IsExpired(string tokenDirectory)
    {
        DateTimeOffset? authorizedAt = GetAuthorizedAt(tokenDirectory);
        return authorizedAt is null || DateTimeOffset.UtcNow - authorizedAt.Value > Validity;
    }

    public static DateTimeOffset? ExpiresAt(string tokenDirectory) =>
        GetAuthorizedAt(tokenDirectory)?.Add(Validity);

    public static void MarkAuthorized(string tokenDirectory)
    {
        Directory.CreateDirectory(tokenDirectory);
        File.WriteAllText(Path.Combine(tokenDirectory, MarkerFileName), DateTimeOffset.UtcNow.ToString("O"));
    }

    public static void ClearExpiredAuthorization(string tokenDirectory)
    {
        if (!Directory.Exists(tokenDirectory)) return;
        foreach (string file in Directory.EnumerateFiles(tokenDirectory)) File.Delete(file);
    }

    private static DateTimeOffset? GetAuthorizedAt(string tokenDirectory)
    {
        string markerPath = Path.Combine(tokenDirectory, MarkerFileName);
        if (File.Exists(markerPath)
            && DateTimeOffset.TryParse(File.ReadAllText(markerPath), out DateTimeOffset markedAt))
            return markedAt;

        if (!Directory.Exists(tokenDirectory)) return null;
        FileInfo? newestToken = Directory.EnumerateFiles(tokenDirectory)
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .FirstOrDefault();
        return newestToken is null ? null : new DateTimeOffset(newestToken.LastWriteTimeUtc, TimeSpan.Zero);
    }
}
