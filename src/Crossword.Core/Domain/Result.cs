namespace Crossword.Core.Domain;

/// <summary>Success value or domain error. Used instead of exceptions for expected rule violations.</summary>
public readonly record struct Result<TValue, TError>
{
    private readonly TValue? _value;
    private readonly TError? _error;

    public bool IsOk { get; }

    private Result(TValue? value, TError? error, bool isOk)
    {
        _value = value;
        _error = error;
        IsOk = isOk;
    }

    public static Result<TValue, TError> Ok(TValue value) => new(value, default, true);

    public static Result<TValue, TError> Fail(TError error) => new(default, error, false);

    public TValue Value => IsOk ? _value! : throw new InvalidOperationException($"Result is an error: {_error}");

    public TError Error => !IsOk ? _error! : throw new InvalidOperationException("Result is not an error.");
}
