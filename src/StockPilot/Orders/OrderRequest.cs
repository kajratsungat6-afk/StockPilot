namespace StockPilot;

/// <summary>Raw order request as it arrives from JSON. Null Quantity means "missing".</summary>
public sealed record OrderRequest(string? Sku, int? Quantity);
