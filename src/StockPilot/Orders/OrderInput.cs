namespace StockPilot;

public static class OrderInput
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static IReadOnlyList<OrderRequest?> ParseJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        return JsonSerializer.Deserialize<List<OrderRequest?>>(json, Options)
               ?? throw new JsonException("JSON root must be an array of orders.");
    }
}
