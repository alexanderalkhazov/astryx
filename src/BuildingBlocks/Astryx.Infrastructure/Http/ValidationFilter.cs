using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Astryx.Infrastructure.Http;

/// <summary>Runs the registered FluentValidation validator for a request body and returns 422 on failure (§11.1, §18.7).</summary>
public sealed class ValidationFilter<TRequest> : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var validator = context.HttpContext.RequestServices.GetService<IValidator<TRequest>>()
            ?? throw new InvalidOperationException($"No validator registered for {typeof(TRequest).Name}.");
        var request = context.Arguments.OfType<TRequest>().FirstOrDefault();
        if (request is null)
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, detail: "A request body is required.");
        }

        var result = await validator.ValidateAsync(request, context.HttpContext.RequestAborted);
        if (result.IsValid)
        {
            return await next(context);
        }

        var errors = result.Errors
            .GroupBy(e => char.ToLowerInvariant(e.PropertyName[0]) + e.PropertyName[1..])
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
        return Results.ValidationProblem(
            errors,
            statusCode: StatusCodes.Status422UnprocessableEntity,
            type: ErrorHttpMapping.TypeUri("validation_failed"),
            extensions: new Dictionary<string, object?> { ["code"] = "validation_failed" });
    }
}

public static class ValidationFilterExtensions
{
    public static RouteHandlerBuilder WithValidation<TRequest>(this RouteHandlerBuilder builder) =>
        builder.AddEndpointFilter<ValidationFilter<TRequest>>().ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);
}
