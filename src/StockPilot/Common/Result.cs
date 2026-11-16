namespace StockPilot;

/// <summary>Result<T>: Success carries a value, Failure carries an Error. Never both, never neither.
/// Success rejects a null payload and Failure rejects a null error, so contradictory states cannot be built.</summary>
public abstract record Result<T>
{
    internal Result() { }
}

public sealed record Success<T> : Result<T>
{
    public T Value { get; }

    public Success(T value)
    {
        if (value is null) throw new ArgumentNullException(nameof(value), "Success must carry a value.");
        Value = value;
    }
}

public sealed record Failure<T> : Result<T>
{
    public Error Error { get; }

    public Failure(Error error)
    {
        if (error is null) throw new ArgumentNullException(nameof(error), "Failure must carry an error.");
        Error = error;
    }
}

public static class Result
{
    public static Result<T> Ok<T>(T value) => new Success<T>(value);
    public static Result<T> Fail<T>(Error error) => new Failure<T>(error);
    public static Result<T> Fail<T>(string code, string message) => new Failure<T>(new Error(code, message));
}

public static class ResultExtensions
{
    /// <summary>Map: Result<T> + pure Func<T,U> -> Result<U>. On failure the function is NOT called.</summary>
    public static Result<TOut> Map<T, TOut>(this Result<T> result, Func<T, TOut> function)
    {
        ArgumentNullException.ThrowIfNull(function);
        return result switch
        {
            Success<T> s => Result.Ok(function(s.Value)),
            Failure<T> f => Result.Fail<TOut>(f.Error),
            _ => Result.Fail<TOut>(new Error(ErrorCodes.Internal, "Unknown Result case.")),
        };
    }

    /// <summary>Bind: Result<T> + Func<T, Result<U>> -> Result<U>. The next step can fail itself.
    /// On failure the function is NOT called and the original error is passed on.</summary>
    public static Result<TOut> Bind<T, TOut>(this Result<T> result, Func<T, Result<TOut>> function)
    {
        ArgumentNullException.ThrowIfNull(function);
        return result switch
        {
            Success<T> s => function(s.Value),
            Failure<T> f => Result.Fail<TOut>(f.Error),
            _ => Result.Fail<TOut>(new Error(ErrorCodes.Internal, "Unknown Result case.")),
        };
    }

    /// <summary>Ensure: keeps a successful value only if it satisfies a rule; otherwise returns the given error.
    /// Receiver: Result&lt;T&gt;. Returns: Result&lt;T&gt;. A failure passes through and the predicate is NOT called.</summary>
    public static Result<T> Ensure<T>(this Result<T> result, Func<T, bool> predicate, Error error)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(error);
        return result.Bind(value => predicate(value) ? Result.Ok(value) : Result.Fail<T>(error));
    }

    /// <summary>Handles both cases explicitly. Used once, at the application boundary.</summary>
    public static TOut Match<T, TOut>(this Result<T> result, Func<T, TOut> onSuccess, Func<Error, TOut> onFailure) => result switch
    {
        Success<T> s => onSuccess(s.Value),
        Failure<T> f => onFailure(f.Error),
        _ => throw new InvalidOperationException("Unknown Result case."),
    };
}
