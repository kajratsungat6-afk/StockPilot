namespace StockPilot;

public sealed class Catalog
{
    private readonly ImmutableDictionary<string, Product> _items;

    private Catalog(ImmutableDictionary<string, Product> items) => _items = items;

    public static Catalog FromProducts(IEnumerable<Product> products)
    {
        ArgumentNullException.ThrowIfNull(products);
        var builder = ImmutableDictionary.CreateBuilder<string, Product>(StringComparer.Ordinal);
        foreach (var p in products) builder[p.Sku] = p;
        return new Catalog(builder.ToImmutable());
    }

    public int Count => _items.Count;

    public IEnumerable<Product> Products => _items.Values.OrderBy(p => p.Sku, StringComparer.Ordinal);

    public Maybe<Product> Find(string sku)
    {
        if (sku is not null && _items.TryGetValue(sku, out var product)) return Maybe.Some(product);
        return Maybe.None<Product>();
    }

    public Maybe<int> StockOf(string sku)
    {
        if (sku is not null && _items.TryGetValue(sku, out var product)) return Maybe.Some(product.Quantity);
        return Maybe.None<int>();
    }

    public Catalog WithQuantity(string sku, int newQuantity)
    {
        if (newQuantity < 0) throw new ArgumentOutOfRangeException(nameof(newQuantity), "Stock cannot be negative.");
        if (!_items.TryGetValue(sku, out var product)) throw new ArgumentException($"Unknown sku '{sku}'.", nameof(sku));
        return new Catalog(_items.SetItem(sku, product with { Quantity = newQuantity }));
    }
}
