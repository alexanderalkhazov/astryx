using Astryx.Infrastructure.Http;
using Astryx.Modules.Watchlists.Application;
using Astryx.Modules.Watchlists.Domain;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Astryx.Modules.Watchlists.Api;

public sealed record WatchlistsResponse(IReadOnlyList<WatchlistDto> Items);

public sealed record CreateWatchlistRequest(string Name);

public sealed record UpdateWatchlistRequest(string? Name, int? SortOrder);

public sealed record AddWatchlistItemRequest(string Symbol, string? Note);

public sealed record ReorderWatchlistItemsRequest(IReadOnlyList<string> Symbols);

public sealed class CreateWatchlistRequestValidator : AbstractValidator<CreateWatchlistRequest>
{
    public CreateWatchlistRequestValidator() =>
        RuleFor(r => r.Name).Must(n => n?.Trim().Length is >= 1 and <= Watchlist.MaxNameLength)
            .WithMessage($"Use 1–{Watchlist.MaxNameLength} characters.");
}

public sealed class UpdateWatchlistRequestValidator : AbstractValidator<UpdateWatchlistRequest>
{
    public UpdateWatchlistRequestValidator()
    {
        RuleFor(r => r.Name).Must(n => n is null || n.Trim().Length is >= 1 and <= Watchlist.MaxNameLength)
            .WithMessage($"Use 1–{Watchlist.MaxNameLength} characters.");
        RuleFor(r => r.SortOrder).InclusiveBetween(0, 1000);
    }
}

public sealed class AddWatchlistItemRequestValidator : AbstractValidator<AddWatchlistItemRequest>
{
    public AddWatchlistItemRequestValidator()
    {
        RuleFor(r => r.Symbol).NotEmpty().MaximumLength(12);
        RuleFor(r => r.Note).MaximumLength(WatchlistItem.MaxNoteLength);
    }
}

public sealed class ReorderWatchlistItemsRequestValidator : AbstractValidator<ReorderWatchlistItemsRequest>
{
    public ReorderWatchlistItemsRequestValidator() =>
        RuleFor(r => r.Symbols).NotNull().Must(s => s is null || s.Count <= Watchlist.MaxItems);
}

/// <summary>W1–W8 (SYSTEM_DESIGN.md §11.5). <c>{id}</c> is a list id or <c>default</c>.</summary>
public static class WatchlistEndpoints
{
    public static IEndpointRouteBuilder MapWatchlistEndpoints(this IEndpointRouteBuilder app)
    {
        var lists = app.MapGroup("/api/v1/watchlists").WithTags("Watchlists").RequireAuthorization("Authenticated");

        lists.MapGet("/", async (WatchlistService service, CancellationToken cancellationToken) =>
                Results.Ok(new WatchlistsResponse(await service.ListAsync(cancellationToken))))
            .WithName("ListWatchlists")
            .Produces<WatchlistsResponse>();

        lists.MapPost("/", async (CreateWatchlistRequest request, WatchlistService service, HttpContext http, CancellationToken cancellationToken) =>
                (await service.CreateAsync(request.Name, cancellationToken)).Match(
                    list => WithETag(http, list, Results.Created($"/api/v1/watchlists/{list.Id}", list)), error => error.ToProblem()))
            .WithName("CreateWatchlist")
            .WithValidation<CreateWatchlistRequest>()
            .Produces<WatchlistDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status409Conflict);

        lists.MapPatch("/{id}", async (string id, UpdateWatchlistRequest request, WatchlistService service, HttpContext http, CancellationToken cancellationToken) =>
                (await service.UpdateAsync(id, ETags.ParseIfMatch(http), request.Name, request.SortOrder, cancellationToken)).Match(
                    list => WithETag(http, list, Results.Ok(list)), error => error.ToProblem()))
            .WithName("UpdateWatchlist")
            .WithValidation<UpdateWatchlistRequest>()
            .Produces<WatchlistDto>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesProblem(StatusCodes.Status428PreconditionRequired);

        lists.MapDelete("/{id}", async (string id, WatchlistService service, CancellationToken cancellationToken) =>
                NoContentOr(await service.DeleteAsync(id, cancellationToken)))
            .WithName("DeleteWatchlist")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        lists.MapPost("/{id}/items", async (string id, AddWatchlistItemRequest request, WatchlistService service, CancellationToken cancellationToken) =>
                (await service.AddItemAsync(id, request.Symbol, request.Note, cancellationToken)).Match(
                    item => Results.Created($"/api/v1/watchlists/{id}/items/{item.Symbol}", item), error => error.ToProblem()))
            .WithName("AddWatchlistItem")
            .WithValidation<AddWatchlistItemRequest>()
            .Produces<WatchlistItemDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        lists.MapDelete("/{id}/items/{symbol}", async (string id, string symbol, WatchlistService service, CancellationToken cancellationToken) =>
                NoContentOr(await service.RemoveItemAsync(id, symbol, cancellationToken)))
            .WithName("RemoveWatchlistItem")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        lists.MapPut("/default/items/{symbol}", async (string symbol, WatchlistService service, CancellationToken cancellationToken) =>
                NoContentOr(await service.StarAsync(symbol, cancellationToken)))
            .WithName("StarInstrument")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        lists.MapPut("/{id}/order", async (string id, ReorderWatchlistItemsRequest request, WatchlistService service, HttpContext http, CancellationToken cancellationToken) =>
                (await service.ReorderItemsAsync(id, ETags.ParseIfMatch(http), request.Symbols, cancellationToken)).Match(
                    list => WithETag(http, list, Results.Ok(list)), error => error.ToProblem()))
            .WithName("ReorderWatchlistItems")
            .WithValidation<ReorderWatchlistItemsRequest>()
            .Produces<WatchlistDto>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesProblem(StatusCodes.Status428PreconditionRequired);

        return app;
    }

    private static IResult NoContentOr(Astryx.SharedKernel.Results.Result result) => result.IsSuccess ? Results.NoContent() : result.Error.ToProblem();

    private static IResult WithETag(HttpContext http, WatchlistDto list, IResult result)
    {
        http.Response.Headers.ETag = ETags.Format(list.Version);
        return result;
    }
}
