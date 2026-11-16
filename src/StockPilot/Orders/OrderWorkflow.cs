namespace StockPilot;

public sealed record OrderConfirmation(Reservation.Confirmed State, Catalog Catalog);

public sealed record OrderReceipt(
    string Sku, string Name, int Quantity, long TotalMinor, long FeeMinor, DateTimeOffset ConfirmedAt,
    Reservation.Confirmed State, Catalog Catalog)
{
    public string Summary => $"CONFIRMED {Quantity} x {Sku}, total={TotalMinor}, fee={FeeMinor}";
}

public sealed record OrderOutcome(OrderRequest? Request, Reservation State, Result<OrderReceipt> Result);

public sealed record OrderStep(OrderOutcome Outcome, Catalog CatalogAfter);

public sealed record OrderBatch(Catalog Catalog, ImmutableArray<OrderOutcome> Outcomes);

public static class OrderWorkflow
{
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

    public const long MaxOrderTotalMinor = 100_000_000;

  
    public static Result<OrderReceipt> Process(OrderRequest? request, Catalog catalog, IFeeCalculator fees, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(fees);
        return ValidatedOrder.Create(request, catalog)
            .Ensure(order => order.TotalMinor <= MaxOrderTotalMinor,
                    new Error(ErrorCodes.OrderTooLarge, $"Order total exceeds the limit of {MaxOrderTotalMinor} minor units."))
            .Bind(order => fees.Calculate(order.TotalMinor, order.Quantity)                              
                .Bind(fee => ConfirmReservation(new Reservation.Pending(order), catalog, at)             
                    .Map(confirmation => ToReceipt(confirmation, fee))));                               
    }

    private static OrderReceipt ToReceipt(OrderConfirmation c, long fee) =>
        new(c.State.Order.Sku, c.State.Order.Name, c.State.Order.Quantity, c.State.Order.TotalMinor, fee,
            c.State.ConfirmedAt, c.State, c.Catalog);

    public static Result<int> StockLevel(Catalog catalog, string sku) =>
        catalog.StockOf(sku).ToResult(new Error(ErrorCodes.ItemNotFound, $"Unknown sku '{sku}'."));


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
