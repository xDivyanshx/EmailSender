namespace EmailSender.Models;

public sealed record ApplicationSettings(
    string SenderName,
    string DefaultSubject,
    string DefaultTemplate,
    int DailyLimit,
    string GmailAccount,
    int SentLast24Hours,
    int SentTodayLocal,
    int SentTodayUtc);

public sealed record UpdateApplicationSettingsRequest(
    string SenderName,
    string DefaultSubject,
    string DefaultTemplate,
    int DailyLimit);
