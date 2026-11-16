namespace StockPilot;

public sealed record DeliveryRequest(string? Reference, string? Sku, int? Quantity);


public sealed class DeliveryLedger
{
    private readonly ImmutableHashSet<string> _references;

    private DeliveryLedger(ImmutableHashSet<string> references) => _references = references;

    public static DeliveryLedger Empty { get; } = new(ImmutableHashSet.Create<string>(StringComparer.Ordinal));

    public int Count => _references.Count;

    public IEnumerable<string> References => _references.OrderBy(r => r, StringComparer.Ordinal);

    public bool Contains(string reference) => reference is not null && _references.Contains(reference);

    public DeliveryLedger With(string reference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        return new DeliveryLedger(_references.Add(reference));
    }
}
