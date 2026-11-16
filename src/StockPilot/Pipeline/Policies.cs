namespace StockPilot;

public static class Policies
{
    public static readonly Func<Product, long> Conservative =
        p => p.ReorderLevel - p.Quantity;

    public static readonly Func<Product, long> ValueWeighted =
        p => (long)(p.ReorderLevel - p.Quantity) * p.UnitPriceMinor / 100;
}
