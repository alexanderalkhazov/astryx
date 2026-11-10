namespace Astryx.SharedKernel.Results;

/// <summary>An expected failure with a stable machine-readable code (SYSTEM_DESIGN.md §11.2).</summary>
public sealed record Error(string Code, string Message)
{
    public static Error NotFound(string message = "Resource not found.") => new("not_found", message);

    public static Error Validation(string message) => new("validation_failed", message);

    public static Error Conflict(string code, string message) => new(code, message);
}
