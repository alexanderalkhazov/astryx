using System.Diagnostics.CodeAnalysis;

namespace Astryx.SharedKernel.Results;

/// <summary>Outcome of an operation whose failures are expected, so no exceptions are used for control flow (SYSTEM_DESIGN.md §9.3).</summary>
public class Result
{
    protected Result(Error? error) => Error = error;

    public Error? Error { get; }

    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess => Error is null;

    public static Result Success() => new(null);

    public static Result Failure(Error error) => new(error);

    public static Result<T> Success<T>(T value) => Result<T>.Success(value);

    public static implicit operator Result(Error error) => Failure(error);
}

public sealed class Result<T> : Result
{
    private readonly T? _value;

    private Result(T? value, Error? error)
        : base(error) => _value = value;

    /// <summary>The value; throws when the result is a failure.</summary>
    public T Value => IsSuccess ? _value! : throw new InvalidOperationException($"Result failed with '{Error.Code}'; there is no value.");

    public static Result<T> Success(T value) => new(value, null);

    public static new Result<T> Failure(Error error) => new(default, error);

    public TOut Match<TOut>(Func<T, TOut> onSuccess, Func<Error, TOut> onFailure) =>
        IsSuccess ? onSuccess(_value!) : onFailure(Error);

    public static implicit operator Result<T>(T value) => Success(value);

    public static implicit operator Result<T>(Error error) => Failure(error);
}
