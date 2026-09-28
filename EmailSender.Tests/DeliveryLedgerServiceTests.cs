using EmailSender.Services;

namespace EmailSender.Tests;

public sealed class DeliveryLedgerServiceTests
{
    [Fact]
    public void LegacyHistory_IsImportedOnce()
    {
        using TestWorkspace workspace = new();
        File.WriteAllText(workspace.SentPath, "{\"jane@example.com\":\"2026-08-16T09:00:00+05:30\"}");

        _ = new SentMailTrackerService(workspace.Configuration());
        _ = new SentMailTrackerService(workspace.Configuration());

        DeliveryLedgerService ledger = new(workspace.Configuration());
        Assert.Single(ledger.GetAll());
        Assert.Equal("legacy-import", ledger.GetAll().Single().MessageType);
    }

    [Fact]
    public void ApprovedResends_CountAsSeparateDeliveries()
    {
        using TestWorkspace workspace = new();
        SentMailTrackerService tracker = new(workspace.Configuration());

        tracker.RecordSuccessfulDelivery("jane@example.com", "original", new(true, "message-1", "thread-1"));
        tracker.RecordSuccessfulDelivery("jane@example.com", "original", new(true, "message-2", "thread-2"));

        Assert.Equal(2, tracker.GetSentTodayCount());
        Assert.Equal(2, new DeliveryLedgerService(workspace.Configuration()).GetAll().Count);
    }

    [Fact]
    public void DeliveryCounts_DistinguishRollingLocalAndUtcWindows()
    {
        using TestWorkspace workspace = new();
        DeliveryLedgerService ledger = new(workspace.Configuration());
        DateTimeOffset now = new(2026, 8, 21, 1, 0, 0, TimeSpan.FromHours(5.5));

        ledger.Record(new("rolling@example.com", now.AddHours(-23), "original"));
        ledger.Record(new("local-only@example.com", new DateTimeOffset(2026, 8, 21, 0, 30, 0, TimeSpan.FromHours(5.5)), "original"));
        ledger.Record(new("utc-only@example.com", new DateTimeOffset(2026, 8, 20, 2, 0, 0, TimeSpan.FromHours(-4)), "follow-up"));
        ledger.Record(new("old@example.com", now.AddHours(-25), "original"));

        Assert.Equal(3, ledger.CountLast24Hours(now));
        Assert.Equal(1, ledger.CountOn(now.Date));
        Assert.Equal(2, ledger.CountOnUtc(now.UtcDateTime.Date));
    }
}
