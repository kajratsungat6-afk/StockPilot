namespace StockPilot;

/// <summary>Result of a successful confirmation: the new state and the new catalog.</summary>
public sealed record OrderConfirmation(Reservation.Confirmed State, Catalog Catalog);

/// <summary>Display model created by Map from a successful confirmation. FeeMinor comes from the native library.</summary>
public sealed record OrderReceipt(
    string Sku, string Name, int Quantity, long TotalMinor, long FeeMinor, DateTimeOffset ConfirmedAt,
    Reservation.Confirmed State, Catalog Catalog)
{
    public string Summary => $"CONFIRMED {Quantity} x {Sku}, total={TotalMinor}, fee={FeeMinor}";
}

public sealed record OrderOutcome(OrderRequest? Request, Reservation State, Result<OrderReceipt> Result);

/// <summary>One step of the lazy order processing: the outcome and the catalog after this order.</summary>
public sealed record OrderStep(OrderOutcome Outcome, Catalog CatalogAfter);

public sealed record OrderBatch(Catalog Catalog, ImmutableArray<OrderOutcome> Outcomes);

/// <summary>Pure workflow. No Console, no files, no clock: the time is passed in.</summary>
public static class OrderWorkflow
{
    /// <summary>OrderValidation -> Result: Invalid becomes one Failure (code of the first broken rule, all messages joined).</summary>
    public static Result<ValidatedOrder> ToResult(this OrderValidation validation) => validation switch
    {
        OrderValidation.Valid v => Result.Ok(v.Order),
        OrderValidation.Invalid i => Result.Fail<ValidatedOrder>(ErrorList.Combine(i.Errors)),
        _ => Result.Fail<ValidatedOrder>(new Error(ErrorCodes.Internal, "Unknown validation outcome.")),
    };

    public static Reservation Place(OrderRequest? request, Catalog catalog) =>
        ValidatedOrder.Create(request, catalog) switch
        {
            OrderValidation.Valid v => new Reservation.Pending(v.Order),
            OrderValidation.Invalid i => new Reservation.Rejected(i.Reasons),
            _ => new Reservation.Rejected(ImmutableArray.Create("Unknown validation outcome.")),
        };

    /// <summary>Pending -> Confirmed; Confirmed (repeat) and Rejected are NotAllowed (InvalidState).</summary>
    public static TransitionOutcome Confirm(Reservation current, Catalog catalog, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(catalog);

        return current switch
        {
            Reservation.Pending p => ConfirmPending(p, catalog, at),
            Reservation.Confirmed => new TransitionOutcome.NotAllowed(new Error(ErrorCodes.InvalidState,
                "Reservation is already confirmed; repeated confirmation is rejected.")),
            Reservation.Rejected r => new TransitionOutcome.NotAllowed(new Error(ErrorCodes.InvalidState,
                $"A rejected reservation cannot be confirmed: {string.Join(" ", r.Reasons)}")),
            _ => new TransitionOutcome.NotAllowed(new Error(ErrorCodes.InvalidState, "Unknown reservation state.")),
        };
    }

    private static TransitionOutcome ConfirmPending(Reservation.Pending pending, Catalog catalog, DateTimeOffset at)
    {
        var order = pending.Order;
        switch (catalog.Find(order.Sku))
        {
            case Something<Product> found when order.Quantity > found.Value.Quantity:
                return new TransitionOutcome.NotAllowed(new Error(ErrorCodes.InsufficientStock,
                    $"Insufficient stock for '{order.Sku}': requested {order.Quantity}, available {found.Value.Quantity}."));
            case Something<Product> found:
                return new TransitionOutcome.Applied(
                    new Reservation.Confirmed(order, at),
                    catalog.WithQuantity(order.Sku, found.Value.Quantity - order.Quantity));
            case Nothing<Product>:
                return new TransitionOutcome.NotAllowed(new Error(ErrorCodes.ItemNotFound, $"Unknown sku '{order.Sku}'."));
            default:
                return new TransitionOutcome.NotAllowed(new Error(ErrorCodes.Internal, "Unknown lookup result."));
        }
    }

    /// <summary>The state transition as a Result, so that it can be used with Bind.</summary>
    public static Result<OrderConfirmation> ConfirmReservation(Reservation current, Catalog catalog, DateTimeOffset at) =>
        Confirm(current, catalog, at) switch
        {
            TransitionOutcome.Applied { Next: Reservation.Confirmed confirmed } applied
                => Result.Ok(new OrderConfirmation(confirmed, applied.Catalog)),
            TransitionOutcome.Applied
                => Result.Fail<OrderConfirmation>(ErrorCodes.InvalidState, "Confirmation did not produce a confirmed reservation."),
            TransitionOutcome.NotAllowed n => Result.Fail<OrderConfirmation>(n.Error),
            _ => Result.Fail<OrderConfirmation>(ErrorCodes.Internal, "Unknown transition outcome."),
        };

    /// <summary>Domain limit for one order (minor units). Lower than the native library's own limit.</summary>
    public const long MaxOrderTotalMinor = 100_000_000;

    /// <summary>THE RESULT CHAIN: validate (factory) -> Ensure limit (extension) -> Bind fee (native C) ->
    /// Bind confirm -> Map receipt. A failure at any step skips all later steps and is returned unchanged,
    /// so nothing is confirmed unless the fee was calculated.</summary>
    public static Result<OrderReceipt> Process(OrderRequest? request, Catalog catalog, IFeeCalculator fees, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(fees);
        return ValidatedOrder.Create(request, catalog)
            .ToResult()                                                                                  // Result<ValidatedOrder>
            .Ensure(order => order.TotalMinor <= MaxOrderTotalMinor,
                    new Error(ErrorCodes.OrderTooLarge, $"Order total exceeds the limit of {MaxOrderTotalMinor} minor units."))
            .Bind(order => fees.Calculate(order.TotalMinor, order.Quantity)                              // Result<long> (native call)
                .Bind(fee => ConfirmReservation(new Reservation.Pending(order), catalog, at)             // Result<OrderConfirmation>
                    .Map(confirmation => ToReceipt(confirmation, fee))));                                // Result<OrderReceipt>
    }

    private static OrderReceipt ToReceipt(OrderConfirmation c, long fee) =>
        new(c.State.Order.Sku, c.State.Order.Name, c.State.Order.Quantity, c.State.Order.TotalMinor, fee,
            c.State.ConfirmedAt, c.State, c.Catalog);

    /// <summary>Required lookup: a missing product becomes an ItemNotFound failure; zero stock is a valid success.</summary>
    public static Result<int> StockLevel(Catalog catalog, string sku) =>
        catalog.StockOf(sku).ToResult(new Error(ErrorCodes.ItemNotFound, $"Unknown sku '{sku}'."));

    /// <summary>DEFERRED (yield return). Arguments are checked now; NO order is processed until the result is enumerated.
    /// Each iteration step processes exactly one order and yields its outcome. Enumerating the result twice processes the
    /// orders twice (the work is pure and the catalog is immutable, but the source sequence is read again),
    /// so use ProcessAll when a snapshot is needed.</summary>
    public static IEnumerable<OrderStep> ProcessLazily(
        IEnumerable<OrderRequest?> requests, Catalog catalog, IFeeCalculator fees, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(fees);
        return Iterate(requests, catalog, fees, at);
    }

    private static IEnumerable<OrderStep> Iterate(
        IEnumerable<OrderRequest?> requests, Catalog catalog, IFeeCalculator fees, DateTimeOffset at)
    {
        var current = catalog;
        foreach (var request in requests)
        {
            var result = Process(request, current, fees, at);
            Reservation state;
            switch (result)
            {
                case Success<OrderReceipt> success:
                    state = success.Value.State;
                    current = success.Value.Catalog;
                    break;
                case Failure<OrderReceipt> failure:
                    state = new Reservation.Rejected(ImmutableArray.Create(failure.Error.Message));
                    break;
                default:
                    state = new Reservation.Rejected(ImmutableArray.Create("Unknown result."));
                    break;
            }
            yield return new OrderStep(new OrderOutcome(request, state, result), current);
        }
    }

    /// <summary>MATERIALISATION POINT: runs the lazy sequence once, keeps only the outcomes and the LAST catalog
    /// (intermediate catalogs are not retained), and returns a stable snapshot that can be used repeatedly.</summary>
    public static OrderBatch ProcessAll(IEnumerable<OrderRequest?> requests, Catalog catalog, IFeeCalculator fees, DateTimeOffset at)
    {
        var outcomes = ImmutableArray.CreateBuilder<OrderOutcome>();
        var last = catalog;
        foreach (var step in ProcessLazily(requests, catalog, fees, at))
        {
            outcomes.Add(step.Outcome);
            last = step.CatalogAfter;
        }
        return new OrderBatch(last, outcomes.ToImmutable());
    }
}
