using EmailSender.Services;
using EmailSender.Models;

namespace EmailSender.Tests;

public sealed class GmailAuthorizationTests
{
    [Fact]
    public void GetStatus_RequiresReauthorizationAfterTwoHours()
    {
        using TestWorkspace workspace = new();
        File.WriteAllText(workspace.GmailCredentialsPath, "{}");
        Directory.CreateDirectory(workspace.GmailTokenDirectory);
        File.WriteAllText(Path.Combine(workspace.GmailTokenDirectory, ".authorized-at"), DateTimeOffset.UtcNow.AddHours(-3).ToString("O"));

        GmailConnectionStatus status = new GmailConnectionService(workspace.Configuration()).GetStatus();

        Assert.Equal("reauthorization-required", status.Status);
        Assert.Contains("expired", status.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetStatus_ReportsConnectedWithinTwoHours()
    {
        using TestWorkspace workspace = new();
        File.WriteAllText(workspace.GmailCredentialsPath, "{}");
        Directory.CreateDirectory(workspace.GmailTokenDirectory);
        File.WriteAllText(Path.Combine(workspace.GmailTokenDirectory, ".authorized-at"), DateTimeOffset.UtcNow.AddMinutes(-119).ToString("O"));

        GmailConnectionStatus status = new GmailConnectionService(workspace.Configuration()).GetStatus();

        Assert.Equal("connected-locally", status.Status);
        Assert.NotNull(status.AuthorizationExpiresAt);
    }
}
