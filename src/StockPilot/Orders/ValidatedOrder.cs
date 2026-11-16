namespace StockPilot;

/// <summary>A validated order. The only way to get one is ValidatedOrder.Create.</summary>
public sealed class ValidatedOrder
{
    public string Sku { get; }
    public string Name { get; }
    public int Quantity { get; }
    public long UnitPriceMinor { get; }
    public long TotalMinor { get; }

    private ValidatedOrder(Product product, int quantity)
    {
        Sku = product.Sku;
        Name = product.Name;
        Quantity = quantity;
        UnitPriceMinor = product.UnitPriceMinor;
        TotalMinor = quantity * product.UnitPriceMinor;
    }

    /// <summary>Rules: (1) sku present and found (Maybe lookup), (2) quantity present and > 0,
    /// (3) quantity not above stock. All broken rules are reported, each with its own error code.</summary>
    public static OrderValidation Create(OrderRequest? request, Catalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (request is null)
            return new OrderValidation.Invalid(ImmutableArray.Create(new Error(ErrorCodes.InvalidRequest, "Request is missing.")));

        var errors = ImmutableArray.CreateBuilder<Error>();

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

        if (request.Quantity is not { } qty) errors.Add(new Error(ErrorCodes.InvalidQuantity, "Quantity is required."));
        else if (qty <= 0) errors.Add(new Error(ErrorCodes.InvalidQuantity, "Quantity must be > 0."));
        else if (product is not null && qty > product.Quantity)
            errors.Add(new Error(ErrorCodes.InsufficientStock, $"Insufficient stock for '{sku}': requested {qty}, available {product.Quantity}."));

        if (errors.Count == 0 && product is not null && request.Quantity is { } quantity)
            return new OrderValidation.Valid(new ValidatedOrder(product, quantity));

        if (errors.Count == 0) errors.Add(new Error(ErrorCodes.Internal, "Order could not be validated.")); // defensive
        return new OrderValidation.Invalid(errors.ToImmutable());
    }
}
