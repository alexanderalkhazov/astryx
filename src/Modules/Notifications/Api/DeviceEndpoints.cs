using System.Text.RegularExpressions;
using Astryx.Infrastructure.Http;
using Astryx.Modules.Notifications.Application;
using Astryx.Modules.Notifications.Domain;
using Astryx.SharedKernel.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Astryx.Modules.Notifications.Api;

/// <summary>N4/N5 (SYSTEM_DESIGN.md §11.10): push device registration.</summary>
public static partial class DeviceEndpoints
{
    [GeneratedRegex(@"^(Exponent|Expo)PushToken\[[A-Za-z0-9_\-]{10,200}\]$")]
    private static partial Regex ExpoToken();

    public static IEndpointRouteBuilder MapDeviceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/devices").WithTags("Notifications").RequireAuthorization("Authenticated");

        group.MapPost("/", async (RegisterDeviceRequest request, PushDevices devices, CancellationToken ct) =>
            {
                if (string.IsNullOrWhiteSpace(request.DeviceId) || request.DeviceId.Length > 128)
                {
                    return Error.Validation("deviceId is required (at most 128 characters).").ToProblem();
                }

                if (request.PushToken is null || !ExpoToken().IsMatch(request.PushToken))
                {
                    return Error.Validation("pushToken must be an Expo push token.").ToProblem();
                }

                if (request.Platform is not (PushPlatform.Ios or PushPlatform.Android))
                {
                    return Error.Validation("platform must be 'ios' or 'android'.").ToProblem();
                }

                var device = await devices.RegisterAsync(request.DeviceId, request.PushToken, request.Platform, ct);
                return Results.Created($"/api/v1/devices/{Uri.EscapeDataString(device.DeviceId)}", device);
            })
            .WithName("RegisterPushDevice").Produces<PushDeviceDto>(StatusCodes.Status201Created).ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapDelete("/{deviceId}", async (string deviceId, PushDevices devices, CancellationToken ct) =>
            {
                await devices.UnregisterAsync(deviceId, ct);
                return Results.NoContent();
            })
            .WithName("UnregisterPushDevice").Produces(StatusCodes.Status204NoContent);

        return app;
    }
}
