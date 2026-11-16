using System.Collections;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using StockPilot;
using Xunit;

namespace StockPilot.Tests;

public class OrderTests
{
    private static readonly Product Hammer = new("A-100", "Hammer", "TOOLS", 2, 1500, 10);
    private static readonly Product Drill  = new("A-200", "Drill", "TOOLS", 5, 25000, 6);
    private static readonly Product Nails  = new("B-100", "Nails box", "HARDWARE", 10, 300, 10);
    private static readonly Product Screws = new("B-200", "Screws", "HARDWARE", 0, 200, 20);   // stock 0 is legitimate
    private static readonly Product Tape   = new("C-100", "Tape", "SUPPLIES", 50, 400, 10);
    private static readonly Product Glue   = new("C-200", "Glue", "SUPPLIES", 30, 350, 5);

    private static readonly DateTimeOffset At = new(2026, 10, 12, 10, 0, 0, TimeSpan.Zero);

    private static Catalog NewCatalog() => Catalog.FromProducts(new[] { Hammer, Drill, Nails, Screws, Tape, Glue });

    private static int Stock(Catalog catalog, string sku)
    {
        var found = Assert.IsType<Something<Product>>(catalog.Find(sku));
        return found.Value.Quantity;
    }

    private static int[] AllStocks(Catalog catalog) => catalog.Products.Select(p => p.Quantity).ToArray();

    // ---------- FACTORY SUCCESS ----------
    [Fact]
    public void Factory_ValidRequest_CreatesUsableValidatedOrder()
    {
        var result = ValidatedOrder.Create(new OrderRequest("A-100", 2), NewCatalog());

        var valid = Assert.IsType<OrderValidation.Valid>(result);
        Assert.Equal("A-100", valid.Order.Sku);
        Assert.Equal("Hammer", valid.Order.Name);
        Assert.Equal(2, valid.Order.Quantity);
        Assert.Equal(1500, valid.Order.UnitPriceMinor);
        Assert.Equal(3000, valid.Order.TotalMinor);
    }

    // ---------- FACTORY REJECTION (each rule) ----------
    [Theory]
    [InlineData(null, 1, "Sku is required.")]
    [InlineData("Z-999", 1, "Unknown sku 'Z-999'.")]
    [InlineData("A-100", null, "Quantity is required.")]
    [InlineData("A-100", 0, "Quantity must be > 0.")]
    [InlineData("A-100", -3, "Quantity must be > 0.")]
    [InlineData("A-100", 3, "Insufficient stock for 'A-100': requested 3, available 2.")]
    [InlineData("B-200", 1, "Insufficient stock for 'B-200': requested 1, available 0.")]
    public void Factory_RejectsEachRule_WithReason(string? sku, int? quantity, string expectedReason)
    {
        var result = ValidatedOrder.Create(new OrderRequest(sku, quantity), NewCatalog());

        var invalid = Assert.IsType<OrderValidation.Invalid>(result);
        Assert.Equal(new[] { expectedReason }, invalid.Reasons.ToArray());
    }

    [Fact]
    public void Factory_ReportsAllBrokenRulesTogether_AndRejectsNullRequest()
    {
        var both = Assert.IsType<OrderValidation.Invalid>(ValidatedOrder.Create(new OrderRequest("Z-999", 0), NewCatalog()));
        Assert.Equal(2, both.Reasons.Length);

        var missing = Assert.IsType<OrderValidation.Invalid>(ValidatedOrder.Create(null, NewCatalog()));
        Assert.Equal(new[] { "Request is missing." }, missing.Reasons.ToArray());
    }

    // ---------- BOUNDARY VALUE ----------
    [Fact]
    public void Boundary_QuantityEqualToStock_IsValid_AndOneAboveIsNot()
    {
        var catalog = NewCatalog(); // A-200 has stock 5

        Assert.IsType<OrderValidation.Valid>(ValidatedOrder.Create(new OrderRequest("A-200", 5), catalog));
        Assert.IsType<OrderValidation.Invalid>(ValidatedOrder.Create(new OrderRequest("A-200", 6), catalog));
        Assert.IsType<OrderValidation.Valid>(ValidatedOrder.Create(new OrderRequest("A-200", 1), catalog)); // lower limit
    }

    // ---------- DISTINCT CASES + PATTERN MATCHING ----------
    [Fact]
    public void Place_MapsEachValidationCase_ToItsReservationState()
    {
        var catalog = NewCatalog();

        var pending = Assert.IsType<Reservation.Pending>(OrderWorkflow.Place(new OrderRequest("A-100", 1), catalog));
        Assert.Equal("A-100", pending.Order.Sku);

        var rejected = Assert.IsType<Reservation.Rejected>(OrderWorkflow.Place(new OrderRequest("Z-999", 1), catalog));
        Assert.Equal(new[] { "Unknown sku 'Z-999'." }, rejected.Reasons.ToArray());
    }

    [Fact]
    public void Confirm_ReachesEveryReservationCase()
    {
        var catalog = NewCatalog();
        var pending = OrderWorkflow.Place(new OrderRequest("A-100", 1), catalog);
        var rejected = OrderWorkflow.Place(new OrderRequest("Z-999", 1), catalog);
        var applied = Assert.IsType<TransitionOutcome.Applied>(OrderWorkflow.Confirm(pending, catalog, At));

        Assert.IsType<TransitionOutcome.Applied>(OrderWorkflow.Confirm(pending, catalog, At));          // Pending
        Assert.IsType<TransitionOutcome.NotAllowed>(OrderWorkflow.Confirm(applied.Next, applied.Catalog, At)); // Confirmed
        Assert.IsType<TransitionOutcome.NotAllowed>(OrderWorkflow.Confirm(rejected, catalog, At));      // Rejected
    }

    // ---------- NORMAL OPERATION ----------
    [Fact]
    public void Confirm_Pending_ProducesConfirmed_AndTakesStock()
    {
        var catalog = NewCatalog();
        var pending = Assert.IsType<Reservation.Pending>(OrderWorkflow.Place(new OrderRequest("A-100", 2), catalog));

        var applied = Assert.IsType<TransitionOutcome.Applied>(OrderWorkflow.Confirm(pending, catalog, At));

        var confirmed = Assert.IsType<Reservation.Confirmed>(applied.Next);
        Assert.Equal(At, confirmed.ConfirmedAt);
        Assert.Equal(3000, confirmed.Order.TotalMinor);
        Assert.Equal(0, Stock(applied.Catalog, "A-100"));
    }

    // ---------- MISSING PREREQUISITE / INVALID SEQUENCE ----------
    [Fact]
    public void Confirm_OnRejectedReservation_IsNotAllowed()
    {
        var catalog = NewCatalog();
        var rejected = OrderWorkflow.Place(new OrderRequest("A-100", 99), catalog);

        var outcome = OrderWorkflow.Confirm(rejected, catalog, At);

        var notAllowed = Assert.IsType<TransitionOutcome.NotAllowed>(outcome);
        Assert.Contains("rejected reservation cannot be confirmed", notAllowed.Reason);
    }

    // ---------- REPEATED OPERATION ----------
    [Fact]
    public void Confirm_Repeated_IsRejected_AndStockIsNotTakenTwice()
    {
        var catalog = NewCatalog();
        var pending = OrderWorkflow.Place(new OrderRequest("A-200", 2), catalog);
        var first = Assert.IsType<TransitionOutcome.Applied>(OrderWorkflow.Confirm(pending, catalog, At));
        Assert.Equal(3, Stock(first.Catalog, "A-200"));

        var second = OrderWorkflow.Confirm(first.Next, first.Catalog, At);

        var notAllowed = Assert.IsType<TransitionOutcome.NotAllowed>(second);
        Assert.Contains("already confirmed", notAllowed.Reason);
        Assert.Equal(3, Stock(first.Catalog, "A-200")); // unchanged by the repeated call
    }

    // ---------- PREREQUISITE CHANGED BETWEEN PLACE AND CONFIRM ----------
    [Fact]
    public void Confirm_WhenStockWasTakenMeanwhile_IsNotAllowed_AndPendingStaysUsable()
    {
        var catalog = NewCatalog(); // A-200 stock 5
        var p1 = OrderWorkflow.Place(new OrderRequest("A-200", 3), catalog);
        var p2 = Assert.IsType<Reservation.Pending>(OrderWorkflow.Place(new OrderRequest("A-200", 3), catalog));

        var first = Assert.IsType<TransitionOutcome.Applied>(OrderWorkflow.Confirm(p1, catalog, At));
        var second = OrderWorkflow.Confirm(p2, first.Catalog, At);

        var notAllowed = Assert.IsType<TransitionOutcome.NotAllowed>(second);
        Assert.Contains("Insufficient stock", notAllowed.Reason);
        Assert.Equal(2, Stock(first.Catalog, "A-200"));
        Assert.Equal(3, p2.Order.Quantity); // the pending reservation itself was not changed
    }

    // ---------- STATE PRESERVATION ----------
    [Fact]
    public void FailedOperations_AndSuccessfulOnes_NeverModifyTheOriginalCatalog()
    {
        var catalog = NewCatalog();
        var before = AllStocks(catalog);

        ValidatedOrder.Create(new OrderRequest("A-100", 99), catalog);                 // failed construction
        var rejected = OrderWorkflow.Place(new OrderRequest("Z-999", 1), catalog);
        OrderWorkflow.Confirm(rejected, catalog, At);                                   // failed transition
        var pending = OrderWorkflow.Place(new OrderRequest("A-100", 2), catalog);
        OrderWorkflow.Confirm(pending, catalog, At);                                    // successful transition (returns a NEW catalog)

        Assert.Equal(before, AllStocks(catalog));
    }

    // ---------- COMPLETE WORKFLOWS ----------
    [Fact]
    public void Workflow_NormalPath_FromJson_ToConfirmedAndRejectedOrders()
    {
        const string json = """
        [
          { "sku": "A-100", "quantity": 2 },
          { "sku": "A-200", "quantity": 5 },
          { "sku": "B-200", "quantity": 1 },
          { "sku": "Z-999", "quantity": 1 },
          { "sku": "C-100", "quantity": 0 },
          { "sku": "C-100", "quantity": 40 },
          { "sku": "C-100", "quantity": 20 },
          { "quantity": 1 }
        ]
        """;

        var batch = OrderWorkflow.ProcessAll(OrderInput.ParseJson(json), NewCatalog(), TestFees.None, At);

        var kinds = batch.Outcomes.Select(o => o.State is Reservation.Confirmed ? "C" : "R").ToArray();
        Assert.Equal(new[] { "C", "C", "R", "R", "R", "C", "R", "R" }, kinds);

        var reasons = batch.Outcomes.Select(o => o.State is Reservation.Rejected r ? string.Join(" ", r.Reasons) : "").ToArray();
        Assert.Equal("Insufficient stock for 'B-200': requested 1, available 0.", reasons[2]);
        Assert.Equal("Unknown sku 'Z-999'.", reasons[3]);
        Assert.Equal("Quantity must be > 0.", reasons[4]);
        Assert.Equal("Insufficient stock for 'C-100': requested 20, available 10.", reasons[6]); // order #6 took the stock
        Assert.Equal("Sku is required.", reasons[7]);

        // A-100, A-200, B-100, B-200, C-100, C-200
        Assert.Equal(new[] { 0, 0, 10, 0, 10, 30 }, AllStocks(batch.Catalog));
    }

    [Fact]
    public void Workflow_FailurePath_ReportsProblems_AndLeavesCatalogConsistent()
    {
        var catalog = NewCatalog();
        var before = AllStocks(catalog);
        var requests = new OrderRequest?[] { new("Z-999", 1), new("A-100", 0), new("A-100", 99), null };

        var batch = OrderWorkflow.ProcessAll(requests, catalog, TestFees.None, At);

        Assert.All(batch.Outcomes, o => Assert.IsType<Reservation.Rejected>(o.State));
        Assert.Equal(4, batch.Outcomes.Length);
        Assert.Equal(before, AllStocks(batch.Catalog));
        Assert.Equal(before, AllStocks(catalog));
    }

    [Fact]
    public void Workflow_EmptyOrderList_ChangesNothing()
    {
        var catalog = NewCatalog();

        var batch = OrderWorkflow.ProcessAll(Array.Empty<OrderRequest?>(), catalog, TestFees.None, At);

        Assert.Empty(batch.Outcomes);
        Assert.Equal(AllStocks(catalog), AllStocks(batch.Catalog));
    }

    // ---------- INPUT BOUNDARY ----------
    [Fact]
    public void OrderJson_MissingQuantityIsNotZero_AndMalformedJsonIsReported()
    {
        var requests = OrderInput.ParseJson("""[ { "sku": "A-100" }, { "sku": "A-100", "quantity": 0 } ]""");

        Assert.Null(requests[0]!.Quantity);   // missing
        Assert.Equal(0, requests[1]!.Quantity); // legitimate zero value (then rejected by the factory rule)

        Assert.Throws<JsonException>(() => OrderInput.ParseJson("{ not json"));
        Assert.Throws<JsonException>(() => OrderInput.ParseJson("null"));
    }

    [Fact]
    public void ProductIntake_DuplicateSku_IsRejected_SoTheCatalogIsUnambiguous()
    {
        var raws = new RawProduct?[]
        {
            new("A-100", "Hammer", "TOOLS", 2, 1500, 10),
            new("A-100", "Hammer copy", "TOOLS", 9, 1500, 10),
        };

        var result = ProductInput.ValidateAll(raws);

        Assert.Single(result.Valid);
        Assert.Equal("Hammer", result.Valid[0].Name);
        Assert.Equal("Duplicate sku 'A-100'.", Assert.Single(result.Issues).Message);
    }
}
