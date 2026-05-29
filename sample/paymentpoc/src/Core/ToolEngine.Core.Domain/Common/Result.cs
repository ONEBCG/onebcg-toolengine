namespace ToolEngine.Core.Domain.Common;

public sealed class Result<T>
{
    private readonly T?     _value;
    private readonly Error? _error;

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;

    public T Value =>
        IsSuccess ? _value! : throw new InvalidOperationException("Cannot access Value on a failed result.");
    public Error Error =>
        IsFailure ? _error! : throw new InvalidOperationException("Cannot access Error on a successful result.");

    private Result(T value)     { _value = value; IsSuccess = true; }
    private Result(Error error) { _error = error; IsSuccess = false; }

    public static Result<T> Success(T value)    => new(value);
    public static Result<T> Failure(Error error) => new(error);

    public Result<TOut> Map<TOut>(Func<T, TOut> f) =>
        IsSuccess ? Result<TOut>.Success(f(Value)) : Result<TOut>.Failure(Error);

    public Result<TOut> Bind<TOut>(Func<T, Result<TOut>> f) =>
        IsSuccess ? f(Value) : Result<TOut>.Failure(Error);
}

public static class Result
{
    public static Result<T> Success<T>(T value)    => Result<T>.Success(value);
    public static Result<T> Failure<T>(Error error) => Result<T>.Failure(error);
}
