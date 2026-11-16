namespace StockPilot;

/// <summary>Display model created by Map from a successful delivery. Carries the NEW catalog and the NEW ledger.</summary>
public sealed record DeliveryReceipt(
    string Reference, string Sku, string Name, int Quantity, int StockBefore, int StockAfter,
    long ValueMinor, Catalog Catalog, DeliveryLedger Ledger)
{
    public string Summary => $"RECEIVED {Reference}: {Quantity} x {Sku}, stock {StockBefore} -> {StockAfter}, value={ValueMinor}";
}

public sealed record DeliveryOutcome(DeliveryRequest? Request, Result<DeliveryReceipt> Result);

public sealed record DeliveryBatch(Catalog Catalog, DeliveryLedger Ledger, ImmutableArray<DeliveryOutcome> Outcomes);

/// <summary>Workflow 3: receive a delivery into stock. Pure: no Console, no files, no clock.</summary>
public static class DeliveryWorkflow
{
    public static Result<ValidatedDelivery> ToResult(this DeliveryValidation validation) => validation switch
    {
        DeliveryValidation.Valid v => Result.Ok(v.Delivery),
        DeliveryValidation.Invalid i => Result.Fail<ValidatedDelivery>(ErrorList.Combine(i.Errors)),
        _ => Result.Fail<ValidatedDelivery>(new Error(ErrorCodes.Internal, "Unknown validation outcome.")),
    };

    /// <summary>THE RESULT CHAIN: validate (factory) -> Bind (look the product up again and apply the delivery to the
    /// catalog) -> Map (receipt). A failure at any step skips all later steps and is returned unchanged;
    /// the original catalog and ledger are never modified (a failure returns no new catalog at all).</summary>
    public static Result<DeliveryReceipt> Receive(DeliveryRequest? request, Catalog catalog, DeliveryLedger ledger)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(ledger);
        return ValidatedDelivery.Create(request, catalog, ledger)
            .ToResult()                                                                                  // Result<ValidatedDelivery>
            .Bind(delivery => catalog.Find(delivery.Sku)                                                 // Maybe<Product> ...
                .ToResult(new Error(ErrorCodes.ItemNotFound, $"Unknown sku '{delivery.Sku}'."))          // ... Result<Product>
                .Map(product => ToReceipt(delivery, product, catalog, ledger)));                         // Result<DeliveryReceipt>
    }

    private static DeliveryReceipt ToReceipt(ValidatedDelivery delivery, Product product, Catalog catalog, DeliveryLedger ledger)
    {
        var after = product.Quantity + delivery.Quantity;
        return new DeliveryReceipt(
            delivery.Reference, delivery.Sku, delivery.Name, delivery.Quantity, product.Quantity, after,
            delivery.ValueMinor, catalog.WithQuantity(delivery.Sku, after), ledger.With(delivery.Reference));
    }

    /// <summary>Applies the deliveries one after another. Each delivery sees the catalog and ledger left by the previous
    /// SUCCESSFUL one; a rejected delivery changes nothing. The inputs are not modified.</summary>
    public static DeliveryBatch ReceiveAll(IEnumerable<DeliveryRequest?> requests, Catalog catalog, DeliveryLedger ledger)
    {
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(ledger);

        var outcomes = ImmutableArray.CreateBuilder<DeliveryOutcome>();
        var currentCatalog = catalog;
        var currentLedger = ledger;
        foreach (var request in requests)
        {
            var result = Receive(request, currentCatalog, currentLedger);
            if (result is Success<DeliveryReceipt> success)
            {
                currentCatalog = success.Value.Catalog;
                currentLedger = success.Value.Ledger;
            }
            outcomes.Add(new DeliveryOutcome(request, result));
        }
        return new DeliveryBatch(currentCatalog, currentLedger, outcomes.ToImmutable());
    }
}
