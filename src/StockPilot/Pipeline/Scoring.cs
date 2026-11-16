namespace StockPilot;

public sealed record ScoreConfig(int DeficitWeight, long ExpensivePriceThreshold, int ExpensiveBonus);

public static class Scoring
{

    public static readonly Func<ScoreConfig, Func<Product, long>> CreateScore =
        config => product =>
            (long)config.DeficitWeight * (product.ReorderLevel - product.Quantity)
            + (product.UnitPriceMinor >= config.ExpensivePriceThreshold ? config.ExpensiveBonus : 0);


    public static readonly Func<Product, long> Urgent = CreateScore(new ScoreConfig(3, 10_000, 50));
    public static readonly Func<Product, long> Economy = CreateScore(new ScoreConfig(1, 50_000, 0));
}
