using Astryx.Infrastructure.Http;
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

    public static IServiceCollection AddTestSupport(this IServiceCollection services) =>
        services.AddScoped<IValidator<EchoRequest>, EchoRequestValidator>();

    public static void MapTestSupportEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/_test");
        group.MapPost("/echo", (EchoRequest request) => Results.Ok(request)).WithValidation<EchoRequest>();
        group.MapGet("/throw", IResult () => throw new InvalidOperationException("boom: internal detail that must not leak"));
        group.MapGet("/error/{code}", (string code) => new Error(code, $"Simulated {code}.").ToProblem());
    }
}
