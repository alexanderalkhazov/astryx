using System.Collections.Concurrent;
using Astryx.Contracts.Email;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace Astryx.IntegrationTests;

/// <summary>Boots the real API against a throwaway PostgreSQL container (SYSTEM_DESIGN.md §21.1).</summary>
public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17").Build();

    /// <summary>Whether the API applies migrations on startup (the Development default).</summary>
    protected virtual bool MigrateOnStartup => true;

    public string ConnectionString => _postgres.GetConnectionString();

    /// <summary>A throwaway ES256 key ring for this test run.</summary>
    public static readonly string SigningKeys = Astryx.Modules.Identity.Application.JwtKeyRing.Generate("test-key");

    /// <summary>Every email the API sent; replaces SMTP in tests.</summary>
    public CapturingEmailSender Emails { get; } = new();

    public Task InitializeAsync() => _postgres.StartAsync();

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Postgres", _postgres.GetConnectionString());
        builder.UseSetting("Database:MigrateOnStartup", MigrateOnStartup ? "true" : "false");
        builder.UseSetting("Ai:Ollama:BaseUrl", "http://127.0.0.1:9");
        builder.UseSetting("Identity:PasswordHashIterations", "10000"); // production uses 600,000
        builder.UseSetting("Jwt:SigningKeys", SigningKeys);
        builder.ConfigureTestServices(services => services.AddSingleton<IEmailSender>(Emails));
    }
}

/// <summary>An API whose database has never been migrated.</summary>
public sealed class UnmigratedApiFactory : ApiFactory
{
    protected override bool MigrateOnStartup => false;
}

/// <summary>Records outgoing email; can be told to fail the next N sends to simulate an SMTP outage.</summary>
public sealed class CapturingEmailSender : IEmailSender
{
    public ConcurrentQueue<EmailMessage> Sent { get; } = new();

    public int FailuresRemaining { get; set; }

    public IReadOnlyList<EmailMessage> To(string address) => Sent.Where(m => string.Equals(m.To, address, StringComparison.OrdinalIgnoreCase)).ToList();

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        if (FailuresRemaining > 0)
        {
            FailuresRemaining--;
            throw new InvalidOperationException("SMTP unavailable");
        }

        Sent.Enqueue(message);
        return Task.CompletedTask;
    }
}
