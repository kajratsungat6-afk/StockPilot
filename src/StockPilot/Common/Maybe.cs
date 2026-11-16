namespace StockPilot;

/// <summary>Maybe<T> models a lookup that may find nothing (lecture naming: Something / Nothing).
/// Something carries a value that is allowed to be 0, false or "" (these are data, not absence).
/// Only code in this assembly can add new cases (internal constructor).</summary>
public abstract record Maybe<T>
{
    internal Maybe() { }
}

public sealed record Something<T> : Maybe<T>
{
    public T Value { get; }

    public Something(T value)
    {
        if (value is null) throw new ArgumentNullException(nameof(value), "Something must carry a value; use Nothing for absence.");
        Value = value;
    }
}

public sealed record Nothing<T> : Maybe<T>;

public static class Maybe
{
    public static Maybe<T> Some<T>(T value) => new Something<T>(value);
    public static Maybe<T> None<T>() => new Nothing<T>();
}

public static class MaybeExtensions
{
    public static TOut Match<T, TOut>(this Maybe<T> maybe, Func<T, TOut> some, Func<TOut> none) => maybe switch
    {
        Something<T> s => some(s.Value),
        Nothing<T> => none(),
        _ => throw new InvalidOperationException("Unknown Maybe case."),
    };

    /// <summary>Required item: absence becomes a meaningful failure.</summary>
    public static Result<T> ToResult<T>(this Maybe<T> maybe, Error whenNothing) => maybe switch
    {
        Something<T> s => Result.Ok(s.Value),
        Nothing<T> => Result.Fail<T>(whenNothing),
        _ => Result.Fail<T>(new Error(ErrorCodes.Internal, "Unknown Maybe case.")),
    };
}
