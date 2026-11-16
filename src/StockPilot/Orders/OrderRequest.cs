namespace StockPilot;

public sealed record OrderRequest(string? Sku, int? Quantity);
