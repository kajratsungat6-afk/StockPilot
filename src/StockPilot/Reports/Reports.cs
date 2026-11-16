namespace StockPilot;

public sealed record PurchaseLine(string Sku, string Name, int Quantity, int ReorderLevel, int Deficit);
public sealed record PurchasingReport(ImmutableArray<PurchaseLine> Lines, int TotalDeficit);
public sealed record CategoryValue(string CategoryId, int ProductCount, int TotalUnits, long TotalValueMinor);
public sealed record ValueReport(ImmutableArray<CategoryValue> Rows, long GrandTotalMinor);

/// <summary>Pure LINQ reports. No Console, no files. Both materialise their result, so a report does not keep
/// a reference to the source sequence.</summary>
public static class Reports
{
    /// <summary>Report 1 (purchasing clerk): low-stock products, biggest deficit first.
    /// Stages: LowStock (filter) -> Select (projection) -> OrderByDescending + ThenBy (deterministic order) -> Sum.</summary>
    public static PurchasingReport PurchasingList(IEnumerable<Product> products)
    {
        ArgumentNullException.ThrowIfNull(products);
        var lines = products
            .LowStock()
            .Select(p => new PurchaseLine(p.Sku, p.Name, p.Quantity, p.ReorderLevel, p.ReorderLevel - p.Quantity))
            .OrderByDescending(l => l.Deficit)
            .ThenBy(l => l.Sku, StringComparer.Ordinal)
            .ToImmutableArray();
        return new PurchasingReport(lines, lines.Sum(l => l.Deficit));
    }

    /// <summary>Report 2 (warehouse manager): stock value per category, most valuable first.
    /// Stages: Where (only products in stock) -> GroupBy category -> Select (Count and Sum aggregates)
    /// -> OrderByDescending value + ThenBy category -> Sum for the grand total.</summary>
    public static ValueReport ValueByCategory(IEnumerable<Product> products)
    {
        ArgumentNullException.ThrowIfNull(products);
        var rows = products
            .Where(p => p.Quantity > 0)
            .GroupBy(p => p.CategoryId, StringComparer.Ordinal)
            .Select(g => new CategoryValue(
                g.Key,
                g.Count(),
                g.Sum(p => p.Quantity),
                g.Sum(p => (long)p.Quantity * p.UnitPriceMinor)))
            .OrderByDescending(r => r.TotalValueMinor)
            .ThenBy(r => r.CategoryId, StringComparer.Ordinal)
            .ToImmutableArray();
        return new ValueReport(rows, rows.Sum(r => r.TotalValueMinor));
    }
}
