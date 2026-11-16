namespace StockPilot;


public static class ProductFormats
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static string ToJson(IEnumerable<Product> products)
    {
        ArgumentNullException.ThrowIfNull(products);
        var lines = products
            .OrderBy(p => p.Sku, StringComparer.Ordinal)
            .Select(p => "  " + JsonSerializer.Serialize(p, Options))
            .ToArray();
        return lines.Length == 0 ? "[]\n" : "[\n" + string.Join(",\n", lines) + "\n]\n";
    }
}
