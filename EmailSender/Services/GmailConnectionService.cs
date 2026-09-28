using EmailSender.Models;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Gmail.v1;
using Google.Apis.Services;
using Google.Apis.Util.Store;

namespace EmailSender.Services;

public sealed class GmailConnectionService
{
    private static readonly string[] Scopes =
    [
        GmailService.Scope.GmailSend,
        GmailService.Scope.GmailReadonly
    ];

    private readonly string _credentialsPath;
    private readonly string _tokenDirectory;
    private readonly string _account;
    private readonly SemaphoreSlim _connectLock = new(1, 1);

    public GmailConnectionService(IConfiguration config)
    {
        _credentialsPath = config["Gmail:CredentialsPath"]
            ?? "../../../../resources/app-data/google-oauth-client.json";
        _tokenDirectory = config["Gmail:TokenDirectory"]
            ?? "../../../../resources/app-data/google-tokens";
        _account = config["Gmail:Account"] ?? throw new InvalidOperationException("Gmail account is missing.");
    }

    public GmailConnectionStatus GetStatus()
    {
        bool credentialsConfigured = File.Exists(_credentialsPath);
        bool tokenStored = Directory.Exists(_tokenDirectory)
            && Directory.EnumerateFiles(_tokenDirectory).Any();

        bool authorizationExpired = tokenStored && GmailAuthorizationPolicy.IsExpired(_tokenDirectory);
        string status = !credentialsConfigured
            ? "credentials-required"
            : tokenStored
                ? authorizationExpired ? "reauthorization-required" : "connected-locally"
                : "authorization-required";

        return new(credentialsConfigured, tokenStored, _account, status,
            authorizationExpired ? "Daily Gmail authorization has expired. Reconnect Gmail to continue." : null,
            authorizationExpired ? null : GmailAuthorizationPolicy.ExpiresAt(_tokenDirectory));
    }

    public async Task<GmailConnectionStatus?> ConnectAsync(CancellationToken cancellationToken)
    {
        if (!await _connectLock.WaitAsync(0, cancellationToken)) return null;

        try
        {
            if (!File.Exists(_credentialsPath))
            {
                return new(false, false, _account, "credentials-required",
                    $"Google OAuth client file was not found at '{_credentialsPath}'.");
            }

            Directory.CreateDirectory(_tokenDirectory);
            if (GmailAuthorizationPolicy.IsExpired(_tokenDirectory))
                GmailAuthorizationPolicy.ClearExpiredAuthorization(_tokenDirectory);
            GoogleClientSecrets secrets = GoogleClientSecrets.FromFile(_credentialsPath);
            UserCredential credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                secrets.Secrets,
                Scopes,
                _account,
                cancellationToken,
                new FileDataStore(_tokenDirectory, fullPath: true));

            using GmailService gmail = new(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "Cold Mail Sender"
            });

            // A lightweight authenticated call verifies that the stored token works.
            await gmail.Users.GetProfile("me").ExecuteAsync(cancellationToken);
            GmailAuthorizationPolicy.MarkAuthorized(_tokenDirectory);
            return new(true, true, _account, "connected", AuthorizationExpiresAt: GmailAuthorizationPolicy.ExpiresAt(_tokenDirectory));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new(true, false, _account, "authorization-failed", exception.Message);
        }
        finally
        {
            _connectLock.Release();
        }
    }
}
