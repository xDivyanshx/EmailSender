namespace EmailSender.Models;

public sealed record BatchOperationStatus(
    bool Running,
    string? OperationType,
    int Total,
    int Processed,
    int Sent,
    int Failed,
    int Skipped,
    string? CurrentEmail,
    bool CancellationRequested,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt);
