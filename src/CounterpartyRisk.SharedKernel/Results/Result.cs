namespace CounterpartyRisk.SharedKernel.Results;

/// <summary>
/// Outcome of an operation that can fail in an expected way.
/// Exceptions remain reserved for unexpected (technical) failures.
/// </summary>
public class Result
{
    protected Result(bool isSuccess, Error? error)
    {
        if (isSuccess == error is not null)
        {
            throw new ArgumentException("A success has no error and a failure must have one.", nameof(error));
        }

        IsSuccess = isSuccess;
        ErrorOrNull = error;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    /// <summary>
    /// The error of a failed result. Throws when the result is a success.
    /// </summary>
    public Error Error => ErrorOrNull ?? throw new InvalidOperationException("A successful result has no error.");

    protected Error? ErrorOrNull { get; }

    public static Result Success() => new(true, null);

    public static Result Failure(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Result(false, error);
    }

    public static Result<TValue> Success<TValue>(TValue value) => new(value, true, null);

    public static Result<TValue> Failure<TValue>(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Result<TValue>(default, false, error);
    }
}

/// <summary>
/// Outcome carrying a value on success.
/// </summary>
/// <typeparam name="TValue">Type of the value.</typeparam>
public sealed class Result<TValue> : Result
{
    private readonly TValue? _value;

    internal Result(TValue? value, bool isSuccess, Error? error)
        : base(isSuccess, error)
    {
        _value = value;
    }

    /// <summary>
    /// The value of a successful result. Throws when the result is a failure.
    /// </summary>
    public TValue Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("A failed result has no value.");

    public static implicit operator Result<TValue>(TValue value) => Success(value);

    public static implicit operator Result<TValue>(Error error) => Failure<TValue>(error);
}
