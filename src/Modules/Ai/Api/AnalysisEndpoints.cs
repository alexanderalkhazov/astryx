using Astryx.Infrastructure.Http;
using Astryx.Infrastructure.Idempotency;
using Astryx.Modules.Ai.Application.Jobs;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Astryx.Modules.Ai.Api;

public sealed class CreateAnalysisRequestValidator : AbstractValidator<CreateAnalysisRequest>
{
    public CreateAnalysisRequestValidator()
    {
        RuleFor(r => r.Symbol).NotEmpty().MaximumLength(12);
        RuleFor(r => r.Kind).MaximumLength(32);
        RuleFor(r => r.Timeframe).MaximumLength(8);
    }
}

/// <summary>AI1–AI5 (SYSTEM_DESIGN.md §11.4, §19.4): jobs answer immediately; the worker does the model call.</summary>
public static class AnalysisEndpoints
{
    public static IEndpointRouteBuilder MapAnalysisEndpoints(this IEndpointRouteBuilder app)
    {
        var analyses = app.MapGroup("/api/v1/analyses").WithTags("Analyses").RequireAuthorization("Authenticated");

        analyses.MapPost("/", async (CreateAnalysisRequest request, AnalysisJobService service, HttpContext http, CancellationToken cancellationToken) =>
            {
                var key = http.Request.Headers[IdempotencyFilter.HeaderName].ToString();
                var result = await service.CreateAsync(request, key, cancellationToken);
                if (!result.IsSuccess)
                {
                    return result.Error.ToProblem();
                }

                var outcome = result.Value;
                if (outcome.ActiveJobId is { } active)
                {
                    return Results.Problem(
                        statusCode: StatusCodes.Status409Conflict,
                        title: "duplicate",
                        detail: "An analysis for this symbol is already running.",
                        type: ErrorHttpMapping.TypeUri("duplicate"),
                        extensions: new Dictionary<string, object?> { ["code"] = "duplicate", ["jobId"] = active });
                }

                var job = outcome.Job!;
                return outcome.Created ? Results.Accepted($"/api/v1/analyses/jobs/{job.Id}", job) : Results.Ok(job);
            })
            .WithName("CreateAnalysis")
            .WithValidation<CreateAnalysisRequest>()
            .RequireIdempotency()
            .RequireRateLimiting(RateLimitPolicies.Analysis)
            .Produces<AnalysisJobDto>(StatusCodes.Status202Accepted)
            .Produces<AnalysisJobDto>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        analyses.MapGet("/jobs/{jobId:guid}", async (Guid jobId, AnalysisJobService service, CancellationToken cancellationToken) =>
                (await service.GetJobAsync(jobId, cancellationToken)).Match(Results.Ok, error => error.ToProblem()))
            .WithName("GetAnalysisJob")
            .Produces<AnalysisJobDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        analyses.MapDelete("/jobs/{jobId:guid}", async (Guid jobId, AnalysisJobService service, CancellationToken cancellationToken) =>
                (await service.CancelAsync(jobId, cancellationToken)).Match(job => Results.Accepted(null, job), error => error.ToProblem()))
            .WithName("CancelAnalysisJob")
            .Produces<AnalysisJobDto>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        analyses.MapGet("/{id:guid}", async (Guid id, AnalysisJobService service, CancellationToken cancellationToken) =>
                (await service.GetResultAsync(id, cancellationToken)).Match(Results.Ok, error => error.ToProblem()))
            .WithName("GetAnalysis")
            .Produces<AnalysisResultDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        analyses.MapGet("/", async (string? symbol, int? limit, string? cursor, AnalysisJobService service, CancellationToken cancellationToken) =>
                (await service.ListAsync(symbol, limit, cursor, cancellationToken)).Match(Results.Ok, error => error.ToProblem()))
            .WithName("ListAnalyses")
            .Produces<AnalysisPageDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        return app;
    }
}
