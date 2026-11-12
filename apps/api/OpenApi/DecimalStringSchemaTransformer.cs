using Astryx.SharedKernel.Finance;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Astryx.Api.OpenApi;

/// <summary>
/// Describes values as the API actually writes them, for generated clients: decimals and the financial value objects
/// as decimal strings (<c>"186.40"</c>, ADR-003) so money is never a JavaScript number; integers as plain integers
/// (the web defaults also <i>accept</i> numeric strings, which is not what we send); instants and dates as strings.
/// </summary>
public sealed class DecimalStringSchemaTransformer : IOpenApiSchemaTransformer
{
    private static readonly HashSet<Type> DecimalTypes = [typeof(decimal), typeof(Price), typeof(Quantity), typeof(Percent)];
    private static readonly HashSet<Type> IntegerTypes = [typeof(int), typeof(long), typeof(short)];
    private static readonly HashSet<Type> StringTypes = [typeof(DateTimeOffset), typeof(DateTime), typeof(DateOnly), typeof(Guid)];

    public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        var type = context.JsonTypeInfo.Type;
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        var nullable = underlying != type ? JsonSchemaType.Null : 0;
        if (DecimalTypes.Contains(underlying))
        {
            schema.Type = JsonSchemaType.String | nullable;
            schema.Format = "decimal";
            schema.Pattern = @"^-?\d+(\.\d+)?$";
            schema.Properties?.Clear();
        }
        else if (IntegerTypes.Contains(underlying))
        {
            schema.Type = JsonSchemaType.Integer | nullable;
            schema.Pattern = null;
        }
        else if (StringTypes.Contains(underlying))
        {
            schema.Type = JsonSchemaType.String | nullable;
        }

        return Task.CompletedTask;
    }
}
