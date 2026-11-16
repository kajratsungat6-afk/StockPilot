namespace StockPilot;

/// <summary>Console output. Every method only WRITES text that was computed before; no business decision is made here.</summary>
public static class Views
{
    public static readonly (string Name, Func<Product, long> Fn)[] NamedPolicies =
    {
        ("Conservative", Policies.Conservative),
        ("ValueWeighted", Policies.ValueWeighted),
        ("Urgent (curried)", Scoring.Urgent),
        ("Economy (curried)", Scoring.Economy),
    };

    public static void Intake(TextWriter w, IntakeResult intake)
    {
        w.WriteLine($"Loaded: {intake.Valid.Length} valid, {intake.Issues.Length} rejected");
        foreach (var issue in intake.Issues)
            w.WriteLine($"  rejected row {issue.Row}: {issue.Message}");
    }

    public static void RestockPlan(TextWriter w, string policyName, RestockPlan plan)
    {
        w.WriteLine();
        w.WriteLine($"=== Policy: {policyName} (total suggested units: {plan.TotalSuggestedUnits}) ===");
        if (plan.Items.IsEmpty) w.WriteLine("  nothing to restock");
        foreach (var r in plan.Items)
            w.WriteLine($"  {r.Sku,-6} {r.Name,-10} qty={r.Quantity,3}/{r.ReorderLevel,-3} score={r.Score,5} order={r.SuggestedOrderQty}");
    }

    public static void PurchasingList(TextWriter w, PurchasingReport report)
    {
        w.WriteLine();
        w.WriteLine("=== Report 1: Purchasing list (low stock) ===");
        foreach (var l in report.Lines)
            w.WriteLine($"  {l.Sku,-6} {l.Name,-10} qty={l.Quantity,3}/{l.ReorderLevel,-3} deficit={l.Deficit}");
        w.WriteLine($"  total deficit: {report.TotalDeficit}");
    }

    public static void ValueByCategory(TextWriter w, ValueReport report)
    {
        w.WriteLine();
        w.WriteLine("=== Report 2: Stock value by category ===");
        foreach (var r in report.Rows)
            w.WriteLine($"  {r.CategoryId,-9} products={r.ProductCount} units={r.TotalUnits,3} value={r.TotalValueMinor}");
        w.WriteLine($"  grand total: {report.GrandTotalMinor}");
    }

    public static void NativeCheck(TextWriter w, IFeeCalculator fees)
    {
        w.WriteLine();
        w.WriteLine("=== Native library check ===");
        w.WriteLine("  handling fee for total 10000, quantity 3: " + fees.Calculate(10000, 3).Match(
            fee => $"{fee} (expected 410)",
            error => $"FAILED [{error.Code}] {error.Message}"));
    }

    public static void StockLookup(TextWriter w, Catalog catalog)
    {
        w.WriteLine();
        w.WriteLine("=== Stock lookup ===");
        foreach (var sku in new[] { "A-100", "B-200", "Z-999" })
        {
            var text = OrderWorkflow.StockLevel(catalog, sku).Match(
                quantity => $"{quantity} in stock",
                error => $"[{error.Code}] {error.Message}");
            w.WriteLine($"  {sku}: {text}");
        }
    }

    public static void Orders(TextWriter w, OrderBatch batch)
    {
        w.WriteLine();
        w.WriteLine("=== Orders (validate -> limit -> fee -> confirm -> receipt) ===");
        var n = 0;
        foreach (var o in batch.Outcomes)
        {
            n++;
            var text = o.Result.Match(
                receipt => receipt.Summary,
                error => $"REJECTED [{error.Code}] {error.Message}");
            w.WriteLine($"  #{n} {o.Request?.Quantity} x {o.Request?.Sku ?? "(no sku)"}: {text}");
        }
    }

    public static void Stock(TextWriter w, string title, Catalog catalog)
    {
        w.WriteLine();
        w.WriteLine($"=== {title} ===");
        foreach (var p in catalog.Products)
            w.WriteLine($"  {p.Sku,-6} {p.Name,-10} qty={p.Quantity}");
    }

    public static void InvalidSequences(TextWriter w, OrderBatch batch, DateTimeOffset at)
    {
        w.WriteLine();
        w.WriteLine("=== Invalid sequences ===");
        var pending = OrderWorkflow.Place(new OrderRequest("C-200", 1), batch.Catalog);
        var first = OrderWorkflow.ConfirmReservation(pending, batch.Catalog, at);
        w.WriteLine($"  Confirm a pending order            -> {Describe(first)}");
        var again = first.Bind(c => OrderWorkflow.ConfirmReservation(c.State, c.Catalog, at));
        w.WriteLine($"  Confirm the same order again       -> {Describe(again)}");
        var firstRejected = batch.Outcomes.FirstOrDefault(o => o.State is Reservation.Rejected);
        if (firstRejected is not null)
            w.WriteLine($"  Confirm a rejected order           -> {Describe(OrderWorkflow.ConfirmReservation(firstRejected.State, batch.Catalog, at))}");
    }

    public static void Deliveries(TextWriter w, DeliveryBatch batch)
    {
        w.WriteLine();
        w.WriteLine("=== Deliveries (validate -> apply to stock -> receipt) ===");
        var n = 0;
        foreach (var o in batch.Outcomes)
        {
            n++;
            var text = o.Result.Match(
                receipt => receipt.Summary,
                error => $"REJECTED [{error.Code}] {error.Message}");
            w.WriteLine($"  #{n} {o.Request?.Reference ?? "(no ref)"} {o.Request?.Quantity} x {o.Request?.Sku ?? "(no sku)"}: {text}");
        }
    }

    public static void SavedOrFailed(TextWriter w, Result<string> saved, string verb) =>
        w.WriteLine("  " + saved.Match(path => $"{verb} {path}", error => $"FAILED [{error.Code}] {error.Message}"));

    private static string Describe(Result<OrderConfirmation> result) => result.Match(
        c => $"OK: confirmed {c.State.Order.Quantity} x {c.State.Order.Sku}",
        e => $"FAILED [{e.Code}] {e.Message}");
}
