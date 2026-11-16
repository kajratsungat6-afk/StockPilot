namespace StockPilot;

public static class DeliveryInput
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static IReadOnlyList<DeliveryRequest?> ParseJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        return JsonSerializer.Deserialize<List<DeliveryRequest?>>(json, Options)
               ?? throw new JsonException("JSON root must be an array of deliveries.");
    }
}
