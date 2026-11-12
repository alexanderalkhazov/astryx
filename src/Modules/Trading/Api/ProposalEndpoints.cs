using Astryx.Infrastructure.Http;
using Astryx.Modules.Trading.Application;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Astryx.Modules.Trading.Api;

public sealed class CreateProposalRequestValidator : AbstractValidator<CreateProposalRequest>
{
    public CreateProposalRequestValidator() =>
        RuleFor(r => r.Source).Must(s => s is "signal" or "analysis").WithMessage("source must be signal or analysis.");
}

public sealed class RejectProposalRequestValidator : AbstractValidator<RejectProposalRequest>
{
    public RejectProposalRequestValidator() => RuleFor(r => r.Reason).MaximumLength(200);
}

/// <summary>T1–T3 and T5 (SYSTEM_DESIGN.md §11.7). T4 (assessments) arrives with the risk engine.</summary>
public static class ProposalEndpoints
{
    public static IEndpointRouteBuilder MapProposalEndpoints(this IEndpointRouteBuilder app)
    {
        var proposals = app.MapGroup("/api/v1/proposals").WithTags("Proposals").RequireAuthorization("Authenticated");

        proposals.MapPost("/", async (CreateProposalRequest request, ProposalService service, CancellationToken cancellationToken) =>
                (await service.CreateAsync(request, cancellationToken)).Match(
                    outcome => outcome.Created ? Results.Created($"/api/v1/proposals/{outcome.Proposal.Id}", outcome.Proposal) : Results.Ok(outcome.Proposal),
                    error => error.ToProblem()))
            .WithName("CreateProposal")
            .WithValidation<CreateProposalRequest>()
            .Produces<ProposalDto>(StatusCodes.Status201Created)
            .Produces<ProposalDto>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        proposals.MapGet("/{id:guid}", async (Guid id, ProposalService service, CancellationToken cancellationToken) =>
                (await service.GetAsync(id, cancellationToken)).Match(Results.Ok, error => error.ToProblem()))
            .WithName("GetProposal")
            .Produces<ProposalDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        proposals.MapGet("/", async (string? status, int? limit, string? cursor, ProposalService service, CancellationToken cancellationToken) =>
                (await service.ListAsync(status, limit, cursor, cancellationToken)).Match(Results.Ok, error => error.ToProblem()))
            .WithName("ListProposals")
            .Produces<ProposalPageDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        proposals.MapPost("/{id:guid}/reject", async (Guid id, RejectProposalRequest? request, ProposalService service, CancellationToken cancellationToken) =>
                (await service.RejectAsync(id, request?.Reason, cancellationToken)).Match(Results.Ok, error => error.ToProblem()))
            .WithName("RejectProposal")
            .Produces<ProposalDto>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }
}
