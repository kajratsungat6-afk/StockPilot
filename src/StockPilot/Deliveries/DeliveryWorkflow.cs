namespace StockPilot;

public sealed record DeliveryReceipt(
    string Reference, string Sku, string Name, int Quantity, int StockBefore, int StockAfter,
    long ValueMinor, Catalog Catalog, DeliveryLedger Ledger)
{
    public string Summary => $"RECEIVED {Reference}: {Quantity} x {Sku}, stock {StockBefore} -> {StockAfter}, value={ValueMinor}";
}

public sealed record DeliveryOutcome(DeliveryRequest? Request, Result<DeliveryReceipt> Result);

public sealed record DeliveryBatch(Catalog Catalog, DeliveryLedger Ledger, ImmutableArray<DeliveryOutcome> Outcomes);

public static class DeliveryWorkflow
{
    public static Result<ValidatedDelivery> ToResult(this DeliveryValidation validation) => validation switch
    {
        DeliveryValidation.Valid v => Result.Ok(v.Delivery),
        DeliveryValidation.Invalid i => Result.Fail<ValidatedDelivery>(ErrorList.Combine(i.Errors)),
        _ => Result.Fail<ValidatedDelivery>(new Error(ErrorCodes.Internal, "Unknown validation outcome.")),
    };


    public static Result<DeliveryReceipt> Receive(DeliveryRequest? request, Catalog catalog, DeliveryLedger ledger)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(ledger);
        return ValidatedDelivery.Create(request, catalog, ledger)
            .ToResult()                                                                                
            .Bind(delivery => catalog.Find(delivery.Sku)                                              
                .ToResult(new Error(ErrorCodes.ItemNotFound, $"Unknown sku '{delivery.Sku}'."))          
                .Map(product => ToReceipt(delivery, product, catalog, ledger)));                       
    }

    private static DeliveryReceipt ToReceipt(ValidatedDelivery delivery, Product product, Catalog catalog, DeliveryLedger ledger)
    {
        var after = product.Quantity + delivery.Quantity;
        return new DeliveryReceipt(
            delivery.Reference, delivery.Sku, delivery.Name, delivery.Quantity, product.Quantity, after,
            delivery.ValueMinor, catalog.WithQuantity(delivery.Sku, after), ledger.With(delivery.Reference));
    }

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
