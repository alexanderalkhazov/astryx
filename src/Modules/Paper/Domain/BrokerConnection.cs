using Astryx.Infrastructure.Security;

namespace Astryx.Modules.Paper.Domain;

/// <summary>
/// Row in <c>trading.broker_connections</c> (SYSTEM_DESIGN.md §10.4, §17.6): a user's Alpaca key pair, envelope-encrypted.
/// Only the key ID's last four characters are stored in clear, for display.
/// </summary>
public sealed class BrokerConnection : IUserOwned
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string Broker { get; set; } = BrokerCode.Alpaca;

    /// <summary><c>paper</c>; <c>live</c> is rejected by a check constraint while the §17.9 gates are open.</summary>
    public string Environment { get; set; } = BrokerEnvironment.Paper;

    public string Status { get; set; } = BrokerConnectionStatus.Connected;

    /// <summary>Alpaca account number, shown in Settings.</summary>
    public string? AccountRef { get; set; }

    public required string KeySuffix { get; set; }

    public required byte[] CredentialsCiphertext { get; set; }

    public required string CredentialsKeyId { get; set; }

    public DateTimeOffset ConnectedAt { get; set; }

    public DateTimeOffset? LastSyncAt { get; set; }

    public string? LastError { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

public static class BrokerCode
{
    public const string Mock = "mock";
    public const string Alpaca = "alpaca";
}

public static class BrokerEnvironment
{
    public const string Paper = "paper";
    public const string Live = "live";
}

public static class BrokerConnectionStatus
{
    public const string Connected = "connected";
    public const string Degraded = "degraded";
    public const string Revoked = "revoked";
}
