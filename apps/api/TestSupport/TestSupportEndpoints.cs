using Astryx.Infrastructure.Http;
using Astryx.Infrastructure.Idempotency;
using Astryx.SharedKernel.Finance;
using Astryx.SharedKernel.Results;
using FluentValidation;

namespace Astryx.Api.TestSupport;

/// <summary>
/// Endpoints that exercise the shared pipeline from integration tests. Mapped only when the environment is
/// <c>Testing</c>, which is never deployed.
/// </summary>
public static class TestSupportEndpoints
{
    public sealed record EchoRequest(string Symbol, decimal Amount, Price Entry);

    public sealed class EchoRequestValidator : AbstractValidator<EchoRequest>
    {
        public EchoRequestValidator()
        {
            RuleFor(r => r.Symbol).Matches("^[A-Z0-9.\\-]{1,12}$").WithMessage("Symbol must be 1–12 upper-case characters.");
            RuleFor(r => r.Amount).GreaterThan(0m).WithMessage("Amount must be greater than zero.");
        }
    }

    public sealed record TestOrderRequest(string Symbol, decimal Quantity, int DelayMs = 0);

    /// <summary>Counts how many times the idempotent test endpoint actually executed.</summary>
    public sealed class TestOrderCounter
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        /// <summary>The next N executions throw (simulates a server error before any effect).</summary>
        public int FailuresRemaining { get; set; }

        public int Next() => Interlocked.Increment(ref _count);
    }

    public static IServiceCollection AddTestSupport(this IServiceCollection services) =>
        services.AddScoped<IValidator<EchoRequest>, EchoRequestValidator>()
            .AddSingleton<TestOrderCounter>();

    public static void MapTestSupportEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/_test");
        group.MapPost("/echo", (EchoRequest request) => Results.Ok(request)).WithValidation<EchoRequest>();
        group.MapGet("/throw", IResult () => throw new InvalidOperationException("boom: internal detail that must not leak"));
        group.MapGet("/error/{code}", (string code) => new Error(code, $"Simulated {code}.").ToProblem());
        group.MapPost("/orders", async (TestOrderRequest request, TestOrderCounter counter, CancellationToken cancellationToken) =>
        {
            if (request.DelayMs > 0)
            {
                await Task.Delay(request.DelayMs, cancellationToken);
            }

            if (counter.FailuresRemaining > 0)
            {
                counter.FailuresRemaining--;
                throw new InvalidOperationException("simulated server failure");
            }

            var number = counter.Next();
            return Results.Created($"/_test/orders/{number}", new { orderNumber = number, request.Symbol, request.Quantity });
        }).RequireIdempotency();
    }
}
