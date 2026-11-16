namespace StockPilot;

/// <summary>Validation outcome. Valid carries the validated value; Invalid carries structured errors.
/// Meaning: "is this REQUEST acceptable?" (a decision about input).</summary>
public abstract record OrderValidation
{
    private OrderValidation() { }
    public sealed record Valid(ValidatedOrder Order) : OrderValidation;
    public sealed record Invalid(ImmutableArray<Error> Errors) : OrderValidation
    {
        public ImmutableArray<string> Reasons => Errors.Select(e => e.Message).ToImmutableArray();
    }
}

/// <summary>Workflow state: "where is this ORDER in its life?" (Pending, Confirmed or Rejected).
/// This is different from a validation outcome and from a Result (success/failure of ONE operation).</summary>
public abstract record Reservation
{
    private Reservation() { }
    public sealed record Pending(ValidatedOrder Order) : Reservation;
    public sealed record Confirmed(ValidatedOrder Order, DateTimeOffset ConfirmedAt) : Reservation;
    public sealed record Rejected(ImmutableArray<string> Reasons) : Reservation;
}

public abstract record TransitionOutcome
{
    private TransitionOutcome() { }
    public sealed record Applied(Reservation Next, Catalog Catalog) : TransitionOutcome;
    public sealed record NotAllowed(Error Error) : TransitionOutcome
    {
        public string Reason => Error.Message;
    }
}
