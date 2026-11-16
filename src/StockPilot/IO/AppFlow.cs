namespace StockPilot;

/// <summary>All data loaded for one run. Deliveries is empty when the command does not use them.</summary>
public sealed record LoadedData(
    IReadOnlyList<RawProduct?> Products,
    IReadOnlyList<OrderRequest?> Orders,
    IReadOnlyList<DeliveryRequest?> Deliveries)
{
    public LoadedData(IReadOnlyList<RawProduct?> products, IReadOnlyList<OrderRequest?> orders)
        : this(products, orders, Array.Empty<DeliveryRequest?>()) { }
}

public static class AppFlow
{
    /// <summary>Loads the files a command needs. The products file is read first; if it fails, the other files are not
    /// even read (Bind skips them). A null path means "this file is not needed" (empty list); an empty path is an error.
    /// The caller receives either ALL the data or a Failure — never half of it.</summary>
    public static Result<LoadedData> Load(string productsPath, string? ordersPath, string? deliveriesPath = null) =>
        DataFile.Load<IReadOnlyList<RawProduct?>>(productsPath, ProductInput.ParseJson)
            .Bind(products => LoadOptional(ordersPath, OrderInput.ParseJson)
                .Bind(orders => LoadOptional(deliveriesPath, DeliveryInput.ParseJson)
                    .Map(deliveries => new LoadedData(products, orders, deliveries))));

    private static Result<IReadOnlyList<T?>> LoadOptional<T>(string? path, Func<string, IReadOnlyList<T?>> parse) where T : class =>
        path is null
            ? Result.Ok<IReadOnlyList<T?>>(Array.Empty<T?>())
            : DataFile.Load(path, parse);
}
