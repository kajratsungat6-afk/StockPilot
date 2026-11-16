namespace StockPilot;

public sealed record ScoreConfig(int DeficitWeight, long ExpensivePriceThreshold, int ExpensiveBonus);

public static class Scoring
{
    /// <summary>Curried function: Configuration -> (Product -> Score). The configuration is fixed by the first call;
    /// the returned function takes the remaining argument (the product).
    /// score = DeficitWeight * (ReorderLevel - Quantity) + (price >= threshold ? bonus : 0)</summary>
    public static readonly Func<ScoreConfig, Func<Product, long>> CreateScore =
        config => product =>
            (long)config.DeficitWeight * (product.ReorderLevel - product.Quantity)
            + (product.UnitPriceMinor >= config.ExpensivePriceThreshold ? config.ExpensiveBonus : 0);

    // Partial application: two configured functions made from the same curried function.
    // The configuration is an immutable record captured once; nothing is switched through a mutable global.
    public static readonly Func<Product, long> Urgent = CreateScore(new ScoreConfig(3, 10_000, 50));
    public static readonly Func<Product, long> Economy = CreateScore(new ScoreConfig(1, 50_000, 0));
}
