namespace EmailSender.Models;

public sealed record DeliveryLedgerEntry(
    string Email,
    DateTimeOffset SentAt,
    string MessageType,
    string? ProviderMessageId = null,
    string? ProviderThreadId = null);
