namespace StockPilot;

public static class Stages
{

    public static ImmutableArray<Product> SelectRestockCandidates(IEnumerable<Product> products)
    {
        ArgumentNullException.ThrowIfNull(products);
        return products.LowStock().ToImmutableArray();
    }

    public static ImmutableArray<ScoredProduct> ScoreCandidates(
        IEnumerable<Product> candidates, Func<Product, long> urgency)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(urgency);
        return candidates.Select(p => new ScoredProduct(p, urgency(p))).ToImmutableArray();
    }


    public static RestockPlan BuildPlan(IEnumerable<ScoredProduct> scored)
    {
        ArgumentNullException.ThrowIfNull(scored);
        var items = scored
            .OrderByDescending(s => s.Score)
            .ThenBy(s => s.Product.Sku, StringComparer.Ordinal)
            .Select(s => new Recommendation(
                s.Product.Sku, s.Product.Name, s.Product.Quantity, s.Product.ReorderLevel,
                s.Score, 2 * s.Product.ReorderLevel - s.Product.Quantity))
            .ToImmutableArray();
        return new RestockPlan(items, items.Sum(i => i.SuggestedOrderQty));
    }
}
