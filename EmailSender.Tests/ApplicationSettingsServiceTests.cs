using EmailSender.Models;
using EmailSender.Services;

namespace EmailSender.Tests;

public sealed class ApplicationSettingsServiceTests
{
    [Fact]
    public void Update_ValidatesAndPersistsSafeLocalSettings()
    {
        using TestWorkspace workspace = new();
        IConfiguration config = workspace.Configuration();
        ApplicationSettingsService service = new(config, new DeliveryLedgerService(config));

        ApplicationSettings updated = service.Update(new UpdateApplicationSettingsRequest(
            "A Local Sender", "New subject", "custom-template", 125));

        Assert.Equal("A Local Sender", updated.SenderName);
        Assert.Equal(125, updated.DailyLimit);
        Assert.Equal("New subject", config["Mail:Subject"]);
        Assert.Contains("custom-template", File.ReadAllText(workspace.SettingsPath));
    }

    [Fact]
    public void Update_RejectsInvalidDailyLimit()
    {
        using TestWorkspace workspace = new();
        IConfiguration config = workspace.Configuration();
        ApplicationSettingsService service = new(config, new DeliveryLedgerService(config));

        Assert.Throws<ArgumentException>(() => service.Update(
            new UpdateApplicationSettingsRequest("Sender", "Subject", "template", 0)));
    }
}
