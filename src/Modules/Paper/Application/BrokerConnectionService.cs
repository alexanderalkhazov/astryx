using System.Security.Cryptography;
using System.Text.Json;
using Astryx.Contracts.Audit;
using Astryx.Contracts.Portfolio;
using Astryx.Infrastructure.Alpaca;
using Astryx.Infrastructure.Security;
using Astryx.Modules.Paper.Domain;
using Astryx.Modules.Paper.Infrastructure.Alpaca;
using Astryx.Modules.Paper.Infrastructure.Persistence;
using Astryx.SharedKernel;
using Astryx.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Astryx.Modules.Paper.Application;

/// <summary>N8 body. The secret travels once, over TLS, and is never logged or returned.</summary>
public sealed record ConnectBrokerRequest(string Broker, string Environment, string KeyId, string SecretKey);

/// <summary>
/// N7 item (SYSTEM_DESIGN.md §11, §17.8): what Settings shows. Alpaca API keys always carry trading permission, and
/// money movement is never requested.
/// </summary>
/// <param name="BuyingPower">
/// Alpaca's buying power, cash plus margin, for information only: Astryx sizes and trades with cash and never uses margin
/// (§17.4). Null when Alpaca couldn't be asked.
/// </param>
public sealed record BrokerConnectionDto(
    Guid Id, string Broker, string Environment, string Label, string KeySuffix, string? AccountRef, string Status,
    BrokerPermissionsDto Permissions, DateTimeOffset ConnectedAt, DateTimeOffset? LastSyncAt, string? LastError, decimal? BuyingPower = null);

public sealed record BrokerPermissionsDto(bool ReadPortfolio, string PlaceOrders, string DepositsWithdrawals);

public sealed record BrokerConnectionPageDto(IReadOnlyList<BrokerConnectionDto> Items);

/// <summary>
/// N7–N9 (TASKS.md T-141, SYSTEM_DESIGN.md §17.6): connect an Alpaca paper account with the user's key pair, list it,
/// disconnect it. Connecting validates the keys against the paper account, seals them with <see cref="CredentialVault"/>
/// and attaches the user's <c>alpaca_paper</c> portfolio, so new orders execute at Alpaca.
/// </summary>
public sealed class BrokerConnectionService(
    PaperDbContext db,
    ICurrentUser user,
    CredentialVault vault,
    IAlpacaTradingClient alpaca,
    IBrokerPortfolios portfolios,
    BrokerCredentials credentials,
    IMemoryCache cache,
    TimeProvider timeProvider)
{
    public static readonly Error CredentialsInvalid = new("broker_credentials_invalid", "Alpaca didn't accept that key ID and secret for a paper account. Check you copied the paper keys from the Alpaca dashboard.");
    public static readonly Error LiveDisabled = new("live_trading_disabled", "Live trading isn't available yet. Connect an Alpaca paper account.");
    public static readonly Error Unsupported = new("bad_request", "Only Alpaca is supported.");
    public static readonly Error AlreadyConnected = new("conflict", "An Alpaca paper account is already connected. Disconnect it first.");
    public static readonly Error NotConfigured = new("broker_unavailable", "Broker connections aren't set up on this server (no credentials master key).");
    public static readonly Error AccountUnusable = new("broker_credentials_invalid", "That Alpaca account can't trade right now (blocked or not active).");
    public static readonly Error NotFound = new("not_found", "No broker connection with that id.");

    public async Task<BrokerConnectionPageDto> ListAsync(CancellationToken cancellationToken)
    {
        var rows = await db.BrokerConnections.AsNoTracking().Where(c => c.Status != BrokerConnectionStatus.Revoked).OrderBy(c => c.ConnectedAt).ToListAsync(cancellationToken);
        var items = new List<BrokerConnectionDto>(rows.Count);
        foreach (var row in rows)
        {
            items.Add(ToDto(row) with { BuyingPower = await BuyingPowerAsync(row, cancellationToken) });
        }

        return new BrokerConnectionPageDto(items);
    }

    /// <summary>From the account snapshot the readiness checks cache for a minute; null when Alpaca can't be asked.</summary>
    private async Task<decimal?> BuyingPowerAsync(BrokerConnection connection, CancellationToken cancellationToken)
    {
        if (connection.Status != BrokerConnectionStatus.Connected || await credentials.OpenAsync(connection.Id, cancellationToken) is not var (_, keys))
        {
            return null;
        }

        try
        {
            var account = await cache.GetOrCreateAsync(("alpaca-account", connection.Id), entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1L);
                return alpaca.GetAccountAsync(keys, cancellationToken);
            });
            return account?.BuyingPower;
        }
        catch (AlpacaApiException)
        {
            return null;
        }
    }

    public async Task<Result<BrokerConnectionDto>> ConnectAsync(ConnectBrokerRequest request, CancellationToken cancellationToken)
    {
        if (!string.Equals(request.Broker, BrokerCode.Alpaca, StringComparison.OrdinalIgnoreCase))
        {
            return Unsupported;
        }

        if (!string.Equals(request.Environment, BrokerEnvironment.Paper, StringComparison.OrdinalIgnoreCase))
        {
            return LiveDisabled; // §17.9: no live connection until every gate passes
        }

        if (!vault.IsConfigured)
        {
            return NotConfigured;
        }

        var userId = user.RequireUserId();
        if (await db.BrokerConnections.AnyAsync(c => c.Broker == BrokerCode.Alpaca && c.Environment == BrokerEnvironment.Paper && c.Status != BrokerConnectionStatus.Revoked, cancellationToken))
        {
            return AlreadyConnected;
        }

        var credentials = new AlpacaCredentials(request.KeyId.Trim(), request.SecretKey.Trim());
        AlpacaAccount account;
        try
        {
            account = await alpaca.GetAccountAsync(credentials, cancellationToken); // the paper host: live keys fail here
        }
        catch (AlpacaApiException ex) when (ex.Failure is AlpacaFailure.CredentialsInvalid or AlpacaFailure.Forbidden or AlpacaFailure.NotFound)
        {
            return CredentialsInvalid;
        }
        catch (AlpacaApiException ex)
        {
            return new Error("broker_unavailable", $"Alpaca is unavailable right now ({ex.Failure}). Try again shortly.");
        }

        if (account.AccountBlocked || account.TradingBlocked || !string.Equals(account.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
        {
            return AccountUnusable;
        }

        var now = timeProvider.GetUtcNow();
        var id = Ids.New(timeProvider);
        var sealedSecret = vault.Seal(JsonSerializer.Serialize(new StoredCredentials(credentials.KeyId, credentials.SecretKey)), id);
        var connection = new BrokerConnection
        {
            Id = id,
            UserId = userId,
            AccountRef = account.AccountNumber,
            KeySuffix = credentials.KeySuffix,
            CredentialsCiphertext = sealedSecret.Ciphertext,
            CredentialsKeyId = sealedSecret.KeyId,
            ConnectedAt = now,
            LastSyncAt = now,
            UpdatedAt = now,
        };
        db.BrokerConnections.Add(connection);
        await portfolios.AttachAsync(userId, id, account.Cash, now, cancellationToken);
        db.Audit(new AuditEntry("broker.connected", AuditActor.User, userId, "broker_connection", id.ToString(), Metadata: new
        {
            broker = BrokerCode.Alpaca,
            environment = BrokerEnvironment.Paper,
            keySuffix = connection.KeySuffix,
            accountRef = account.AccountNumber,
        }));
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(connection);
    }

    public async Task<Result<bool>> DisconnectAsync(Guid id, CancellationToken cancellationToken)
    {
        var connection = await db.BrokerConnections.FirstOrDefaultAsync(c => c.Id == id && c.Status != BrokerConnectionStatus.Revoked, cancellationToken);
        if (connection is null)
        {
            return NotFound;
        }

        var now = timeProvider.GetUtcNow();
        connection.Status = BrokerConnectionStatus.Revoked;
        connection.RevokedAt = now;
        connection.UpdatedAt = now;
        connection.CredentialsCiphertext = []; // §17.6: disconnect deletes the secret
        await portfolios.DetachAsync(connection.UserId, cancellationToken);
        db.Audit(new AuditEntry("broker.disconnected", AuditActor.User, connection.UserId, "broker_connection", id.ToString(), Metadata: new { broker = connection.Broker, keySuffix = connection.KeySuffix }));
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static BrokerConnectionDto ToDto(BrokerConnection c) => new(
        c.Id, c.Broker, c.Environment, "Alpaca · paper account", c.KeySuffix, c.AccountRef, c.Status,
        new BrokerPermissionsDto(ReadPortfolio: true, PlaceOrders: "paper_only", DepositsWithdrawals: "never_requested"),
        c.ConnectedAt, c.LastSyncAt, c.LastError);

    internal sealed record StoredCredentials(string KeyId, string SecretKey);
}

/// <summary>Opens a connection's sealed key pair for one Alpaca call. The plaintext lives only in memory.</summary>
public sealed class BrokerCredentials(PaperDbContext db, CredentialVault vault)
{
    /// <summary>The active connection and its credentials, or null when it is revoked or the secret can't be opened.</summary>
    public async Task<(BrokerConnection Connection, AlpacaCredentials Credentials)?> OpenAsync(Guid connectionId, CancellationToken cancellationToken)
    {
        var connection = await db.BrokerConnections.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == connectionId && c.Status != BrokerConnectionStatus.Revoked, cancellationToken);
        if (connection is null)
        {
            return null;
        }

        try
        {
            var stored = JsonSerializer.Deserialize<BrokerConnectionService.StoredCredentials>(vault.Open(new SealedSecret(connection.CredentialsCiphertext, connection.CredentialsKeyId), connection.Id))!;
            return (connection, new AlpacaCredentials(stored.KeyId, stored.SecretKey));
        }
        catch (CryptographicException)
        {
            return null;
        }
    }
}
