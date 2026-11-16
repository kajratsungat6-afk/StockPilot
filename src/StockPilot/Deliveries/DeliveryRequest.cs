namespace StockPilot;

/// <summary>Raw delivery line as it arrives from JSON. A null Reference, Sku or Quantity means "missing".</summary>
public sealed record DeliveryRequest(string? Reference, string? Sku, int? Quantity);

/// <summary>Immutable record of the delivery references that were already received.
/// It makes a repeated delivery detectable: receiving the same reference twice is rejected, not applied twice.
/// Every change returns a NEW ledger.</summary>
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
