namespace StockPilot;

public sealed record RawProduct(
    string? Sku, string? Name, string? CategoryId,
    int? Quantity, long? UnitPriceMinor, int? ReorderLevel);

public sealed record Product(
    string Sku, string Name, string CategoryId,
    int Quantity, long UnitPriceMinor, int ReorderLevel);

public sealed record ScoredProduct(Product Product, long Score);

public sealed record Recommendation(
    string Sku, string Name, int Quantity, int ReorderLevel,
    long Score, int SuggestedOrderQty);

public sealed record RestockPlan(ImmutableArray<Recommendation> Items, int TotalSuggestedUnits);

public sealed record ValidationIssue(int Row, string Message);

public sealed record IntakeResult(ImmutableArray<Product> Valid, ImmutableArray<ValidationIssue> Issues);
