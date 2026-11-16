namespace StockPilot;

public static class RestockPipeline
{
    public static RestockPlan Run(IEnumerable<Product> products, Func<Product, long> urgencyPolicy)
    {
        ArgumentNullException.ThrowIfNull(products);
        ArgumentNullException.ThrowIfNull(urgencyPolicy);

        var candidates = Stages.SelectRestockCandidates(products);
        var scored = Stages.ScoreCandidates(candidates, urgencyPolicy);
        return Stages.BuildPlan(scored);
    }
}
