using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace Astryx.Modules.Notifications.Infrastructure.Push;

/// <summary>One push to one device. <paramref name="Data"/> carries the deep link the app opens on tap.</summary>
public sealed record PushMessage(string To, string Title, string Body, IReadOnlyDictionary<string, string> Data);

/// <summary>What happened to one message. <c>DeviceNotRegistered</c> means the token is dead and must be revoked.</summary>
public sealed record PushOutcome(string To, bool Ok, string? Error);

/// <summary>Sends pushes (SYSTEM_DESIGN.md §7.15). <c>Push:Provider</c>: <c>expo</c> in deployments, <c>fake</c> for dev and tests.</summary>
public interface IPushGateway
{
    Task<IReadOnlyList<PushOutcome>> SendAsync(IReadOnlyList<PushMessage> messages, CancellationToken cancellationToken);
}

/// <summary>
/// Expo's push API (<c>POST https://exp.host/--/api/v2/push/send</c>), one call per batch, tickets read inline. Receipts
/// (delivery reports fetched later) are <c>NOT IMPLEMENTED</c>; immediate <c>DeviceNotRegistered</c> tickets are handled.
/// </summary>
public sealed class ExpoPushGateway(HttpClient http, ILogger<ExpoPushGateway> logger) : IPushGateway
{
    public async Task<IReadOnlyList<PushOutcome>> SendAsync(IReadOnlyList<PushMessage> messages, CancellationToken cancellationToken)
    {
        var body = messages.Select(m => new { to = m.To, title = m.Title, body = m.Body, data = m.Data, sound = "default" }).ToList();
        using var response = await http.PostAsJsonAsync("--/api/v2/push/send", body, cancellationToken);
        response.EnsureSuccessStatusCode(); // a failed call throws: the job retries
        var tickets = (await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken))?["data"]?.AsArray() ?? [];
        var outcomes = new List<PushOutcome>(messages.Count);
        for (var i = 0; i < messages.Count; i++)
        {
            var ticket = i < tickets.Count ? tickets[i] : null;
            var ok = (string?)ticket?["status"] == "ok";
            var error = ok ? null : (string?)ticket?["details"]?["error"] ?? (string?)ticket?["message"] ?? "unknown";
            if (!ok)
            {
                logger.LogWarning("Expo push to a device failed: {Error}", error); // never log the token
            }

            outcomes.Add(new PushOutcome(messages[i].To, ok, error));
        }

        return outcomes;
    }
}

/// <summary>Records pushes instead of sending them; tokens listed in <see cref="Unregistered"/> fail like Expo would.</summary>
public sealed class FakePushGateway : IPushGateway
{
    public ConcurrentQueue<PushMessage> Sent { get; } = new();

    public ConcurrentDictionary<string, bool> Unregistered { get; } = new();

    public Task<IReadOnlyList<PushOutcome>> SendAsync(IReadOnlyList<PushMessage> messages, CancellationToken cancellationToken)
    {
        var outcomes = new List<PushOutcome>();
        foreach (var m in messages)
        {
            if (Unregistered.ContainsKey(m.To))
            {
                outcomes.Add(new PushOutcome(m.To, false, "DeviceNotRegistered"));
                continue;
            }

            Sent.Enqueue(m);
            outcomes.Add(new PushOutcome(m.To, true, null));
        }

        return Task.FromResult<IReadOnlyList<PushOutcome>>(outcomes);
    }
}
