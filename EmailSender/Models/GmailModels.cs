namespace EmailSender.Models;

public sealed record GmailConnectionStatus(
    bool CredentialsConfigured,
    bool TokenStored,
    string Account,
    string Status,
    string? Error = null,
    DateTimeOffset? AuthorizationExpiresAt = null);
