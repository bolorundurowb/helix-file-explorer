using System.Diagnostics.CodeAnalysis;

namespace HelixExplorer.Core.Infrastructure;

/// <summary>
/// A discriminated result carrying either a success value or an expected domain error. Throwing is
/// reserved for unexpected software faults; callers handle the failure branch explicitly via
/// <see cref="Match"/>, <see cref="TryGetValue"/>, or <see cref="Value"/>/<see cref="Error"/>.
/// </summary>
public readonly struct Result<TValue, TError>
{
    private readonly TValue _value;
    private readonly TError _error;
    private readonly bool _isSuccess;

    private Result(TValue value, TError error, bool isSuccess)
    {
        _value = value;
        _error = error;
        _isSuccess = isSuccess;
    }

    public static Result<TValue, TError> Success(TValue value) => new(value, default!, true);

    public static Result<TValue, TError> Failure(TError error) => new(default!, error, false);

    public bool IsSuccess => _isSuccess;

    public bool IsFailure => !_isSuccess;

    public TValue Value => _isSuccess
        ? _value
        : throw new InvalidOperationException("Result is in a failure state and has no success value.");

    public TError Error => !_isSuccess
        ? _error
        : throw new InvalidOperationException("Result is in a success state and has no error.");

    public bool TryGetValue([MaybeNullWhen(false)] out TValue value)
    {
        value = _value;
        return _isSuccess;
    }

    public bool TryGetError([MaybeNullWhen(false)] out TError error)
    {
        error = _error;
        return !_isSuccess;
    }

    public TResult Match<TResult>(Func<TValue, TResult> onSuccess, Func<TError, TResult> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);
        return _isSuccess ? onSuccess(_value) : onFailure(_error);
    }

    public Result<TResult, TError> Map<TResult>(Func<TValue, TResult> map)
    {
        ArgumentNullException.ThrowIfNull(map);
        return _isSuccess
            ? Result<TResult, TError>.Success(map(_value))
            : Result<TResult, TError>.Failure(_error);
    }

    public Result<TValue, TErrorResult> MapError<TErrorResult>(Func<TError, TErrorResult> map)
    {
        ArgumentNullException.ThrowIfNull(map);
        return _isSuccess
            ? Result<TValue, TErrorResult>.Success(_value)
            : Result<TValue, TErrorResult>.Failure(map(_error));
    }

    public override string ToString() => _isSuccess ? $"Success({_value})" : $"Failure({_error})";
}

public static class Result
{
    public static Result<TValue, TError> Success<TValue, TError>(TValue value)
        => Result<TValue, TError>.Success(value);

    public static Result<TValue, TError> Failure<TValue, TError>(TError error)
        => Result<TValue, TError>.Failure(error);
}

/// <summary>Void marker for <see cref="Result{TValue, TError}"/> where there is no success payload.</summary>
public readonly struct Unit : IEquatable<Unit>
{
    public static Unit Value => default;

    public bool Equals(Unit other) => true;

    public override bool Equals(object? obj) => obj is Unit;

    public override int GetHashCode() => 0;

    public override string ToString() => "()";
}
