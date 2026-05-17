namespace ToolEngine.Core.Domain.Common;

/// <summary>
/// Non-generic result for operations that do not return a value.
/// Use Result.Success() or Result.Failure(error).
/// </summary>
public class Result
{
    protected Result(bool isSuccess, Error error)
    {
        if (isSuccess && error != Error.None)
            throw new InvalidOperationException("A successful result cannot carry an error.");
        if (!isSuccess && error == Error.None)
            throw new InvalidOperationException("A failed result must carry an error.");

        IsSuccess = isSuccess;
        Error     = error;
    }

    public bool  IsSuccess { get; }
    public bool  IsFailure => !IsSuccess;
    public Error Error     { get; }

    public static Result    Success()            => new(true,  Error.None);
    public static Result    Failure(Error error) => new(false, error);

    public static Result<T> Success<T>(T value)  => Result<T>.Success(value);
    public static Result<T> Failure<T>(Error e)  => Result<T>.Failure(e);
}

/// <summary>
/// Generic result for operations that return a value on success.
/// </summary>
public sealed class Result<T> : Result
{
    private readonly T? _value;

    private Result(bool isSuccess, T? value, Error error)
        : base(isSuccess, error) => _value = value;

    /// <summary>
    /// The success value. Throws InvalidOperationException if result is a failure.
    /// </summary>
    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("Cannot access Value of a failed Result.");

    public static Result<T> Success(T value) => new(true,  value, Error.None);
    public new static Result<T> Failure(Error e) => new(false, default, e);

    /// <summary>Map over a successful result; propagate failure unchanged.</summary>
    public Result<TOut> Map<TOut>(Func<T, TOut> mapper) =>
        IsSuccess ? Result<TOut>.Success(mapper(Value)) : Result<TOut>.Failure(Error);

    /// <summary>Bind (flatMap) over a successful result.</summary>
    public Result<TOut> Bind<TOut>(Func<T, Result<TOut>> binder) =>
        IsSuccess ? binder(Value) : Result<TOut>.Failure(Error);

    /// <summary>Execute an action on success, return the original result.</summary>
    public Result<T> Tap(Action<T> action)
    {
        if (IsSuccess) action(Value);
        return this;
    }
}
