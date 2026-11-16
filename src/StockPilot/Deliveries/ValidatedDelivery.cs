namespace StockPilot;

/// <summary>Validation outcome of a delivery. Valid carries the validated value; Invalid carries structured errors.</summary>
public abstract record DeliveryValidation
{
    private DeliveryValidation() { }
    public sealed record Valid(ValidatedDelivery Delivery) : DeliveryValidation;
    public sealed record Invalid(ImmutableArray<Error> Errors) : DeliveryValidation
    {
        public ImmutableArray<string> Reasons => Errors.Select(e => e.Message).ToImmutableArray();
    }
}

/// <summary>A validated delivery. The only way to get one is ValidatedDelivery.Create (private constructor).</summary>
public sealed class ValidatedDelivery
{
    /// <summary>Warehouse capacity per product: stock after a delivery may be at most this many units (inclusive).</summary>
    public const int MaxStockPerSku = 10_000;

    public string Reference { get; }
    public string Sku { get; }
    public string Name { get; }
    public int Quantity { get; }
    public long ValueMinor { get; }

    private ValidatedDelivery(string reference, Product product, int quantity)
    {
        Reference = reference;
        Sku = product.Sku;
        Name = product.Name;
        Quantity = quantity;
        ValueMinor = quantity * product.UnitPriceMinor;
    }

    /// <summary>Rules: (1) reference present and not received before (ledger), (2) sku present and found (Maybe lookup),
    /// (3) quantity present and > 0, (4) stock after the delivery not above MaxStockPerSku.
    /// All broken rules are reported, each with its own error code.</summary>
    public static DeliveryValidation Create(DeliveryRequest? request, Catalog catalog, DeliveryLedger ledger)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(ledger);
        if (request is null)
            return new DeliveryValidation.Invalid(ImmutableArray.Create(new Error(ErrorCodes.InvalidRequest, "Request is missing.")));

        var errors = ImmutableArray.CreateBuilder<Error>();

        var reference = request.Reference?.Trim();
        if (string.IsNullOrEmpty(reference))
            errors.Add(new Error(ErrorCodes.ReferenceRequired, "Delivery reference is required."));
        else if (ledger.Contains(reference))
            errors.Add(new Error(ErrorCodes.DuplicateDelivery, $"Delivery '{reference}' was already received; repeated delivery is rejected."));

        Product? product = null;
        var sku = request.Sku?.Trim();
        if (string.IsNullOrEmpty(sku))
        {
            errors.Add(new Error(ErrorCodes.SkuRequired, "Sku is required."));
        }
        else
        {
            switch (catalog.Find(sku))
            {
                case Something<Product> found:
                    product = found.Value;
                    break;
                case Nothing<Product>:
                    errors.Add(new Error(ErrorCodes.ItemNotFound, $"Unknown sku '{sku}'."));
                    break;
                default:
                    errors.Add(new Error(ErrorCodes.Internal, "Unknown lookup result."));
                    break;
            }
        }

        if (request.Quantity is not { } qty)
            errors.Add(new Error(ErrorCodes.InvalidQuantity, "Quantity is required."));
        else if (qty <= 0)
            errors.Add(new Error(ErrorCodes.InvalidQuantity, "Quantity must be > 0."));
        else if (product is not null && (long)product.Quantity + qty > MaxStockPerSku)
            errors.Add(new Error(ErrorCodes.CapacityExceeded,
                $"Delivery of {qty} x '{sku}' would raise stock from {product.Quantity} to {(long)product.Quantity + qty}, above the limit of {MaxStockPerSku}."));

        if (errors.Count == 0 && !string.IsNullOrEmpty(reference) && product is not null && request.Quantity is { } quantity)
            return new DeliveryValidation.Valid(new ValidatedDelivery(reference, product, quantity));

        if (errors.Count == 0) errors.Add(new Error(ErrorCodes.Internal, "Delivery could not be validated.")); // defensive
        return new DeliveryValidation.Invalid(errors.ToImmutable());
    }
}
