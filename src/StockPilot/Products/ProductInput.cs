namespace StockPilot;

public static class ProductInput
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };


    public static IReadOnlyList<RawProduct?> ParseJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        return JsonSerializer.Deserialize<List<RawProduct?>>(json, Options)
               ?? throw new JsonException("JSON root must be an array of products.");
    }

    public static bool TryValidate(
        RawProduct? raw,
        [NotNullWhen(true)] out Product? product,
        [NotNullWhen(false)] out string? error)
    {
        product = null;
        error = null;

        if (raw is null) { error = "Record is missing."; return false; }
        if (string.IsNullOrWhiteSpace(raw.Sku)) { error = "Sku is required."; return false; }
        if (string.IsNullOrWhiteSpace(raw.Name)) { error = "Name is required."; return false; }
        if (string.IsNullOrWhiteSpace(raw.CategoryId)) { error = "Category is required."; return false; }

        if (raw.Quantity is not { } qty) { error = "Quantity is required."; return false; }
        if (qty < 0) { error = "Quantity must be >= 0."; return false; }

        if (raw.UnitPriceMinor is not { } price) { error = "Unit price is required."; return false; }
        if (price < 0) { error = "Unit price must be >= 0."; return false; }

        if (raw.ReorderLevel is not { } level) { error = "Reorder level is required."; return false; }
        if (level < 0) { error = "Reorder level must be >= 0."; return false; }

        product = new Product(raw.Sku.Trim(), raw.Name.Trim(), raw.CategoryId.Trim(), qty, price, level);
        return true;
    }

    public static IntakeResult ValidateAll(IEnumerable<RawProduct?> raws)
    {
        ArgumentNullException.ThrowIfNull(raws);
        var valid = ImmutableArray.CreateBuilder<Product>();
        var issues = ImmutableArray.CreateBuilder<ValidationIssue>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var row = 0;
        foreach (var raw in raws)
        {
            row++;
            if (!TryValidate(raw, out var p, out var err)) issues.Add(new ValidationIssue(row, err));
            else if (!seen.Add(p.Sku)) issues.Add(new ValidationIssue(row, $"Duplicate sku '{p.Sku}'."));
            else valid.Add(p);
        }
        return new IntakeResult(valid.ToImmutable(), issues.ToImmutable());
    }
}
