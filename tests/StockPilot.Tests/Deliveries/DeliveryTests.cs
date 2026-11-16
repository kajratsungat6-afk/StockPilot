using System.Collections.Immutable;
using System.Text.Json;
using StockPilot;
using Xunit;

namespace StockPilot.Tests;

/// <summary>Workflow 3: receive a delivery. Factory, Result chain, repeated delivery, capacity boundary, state preservation.</summary>
public sealed class DeliveryTests
{
    private static readonly Product Hammer = new("A-100", "Hammer", "TOOLS", 2, 1500, 10);
    private static readonly Product Drill  = new("A-200", "Drill", "TOOLS", 5, 25000, 6);
    private static readonly Product Screws = new("B-200", "Screws", "HARDWARE", 0, 200, 20); // stock 0 is legitimate data

    private static Catalog NewCatalog() => Catalog.FromProducts(new[] { Hammer, Drill, Screws });
    private static int[] Stocks(Catalog c) => c.Products.Select(p => p.Quantity).ToArray();
    private static DeliveryRequest Req(string? reference, string? sku, int? quantity) => new(reference, sku, quantity);

    // ---------- factory ----------
    [Fact]
    public void Factory_ValidRequest_CreatesValidatedDeliveryWithExpectedData()
    {
        var result = ValidatedDelivery.Create(Req(" D-1 ", " A-100 ", 18), NewCatalog(), DeliveryLedger.Empty);

        var valid = Assert.IsType<DeliveryValidation.Valid>(result);
        Assert.Equal("D-1", valid.Delivery.Reference);
        Assert.Equal("A-100", valid.Delivery.Sku);
        Assert.Equal("Hammer", valid.Delivery.Name);
        Assert.Equal(18, valid.Delivery.Quantity);
        Assert.Equal(27000, valid.Delivery.ValueMinor);
    }

    [Theory]
    [InlineData("D-1", "A-100", null, ErrorCodes.InvalidQuantity)]
    [InlineData("D-1", "A-100", 0, ErrorCodes.InvalidQuantity)]
    [InlineData("D-1", "A-100", -5, ErrorCodes.InvalidQuantity)]
    [InlineData("D-1", "Z-999", 5, ErrorCodes.ItemNotFound)]
    [InlineData("D-1", null, 5, ErrorCodes.SkuRequired)]
    [InlineData("D-1", "  ", 5, ErrorCodes.SkuRequired)]
    [InlineData(null, "A-100", 5, ErrorCodes.ReferenceRequired)]
    [InlineData("  ", "A-100", 5, ErrorCodes.ReferenceRequired)]
    [InlineData("D-1", "A-100", 9999, ErrorCodes.CapacityExceeded)]
    public void Factory_RejectsEachRule_WithItsOwnErrorCode(string? reference, string? sku, int? quantity, string expectedCode)
    {
        var result = ValidatedDelivery.Create(Req(reference, sku, quantity), NewCatalog(), DeliveryLedger.Empty);

        var invalid = Assert.IsType<DeliveryValidation.Invalid>(result);
        Assert.Contains(expectedCode, invalid.Errors.Select(e => e.Code).ToArray());
    }

    [Fact]
    public void Factory_MissingRequest_IsInvalid_AndNullArgumentsAreRejected()
    {
        var result = ValidatedDelivery.Create(null, NewCatalog(), DeliveryLedger.Empty);

        var invalid = Assert.IsType<DeliveryValidation.Invalid>(result);
        Assert.Equal(ErrorCodes.InvalidRequest, Assert.Single(invalid.Errors).Code);
        Assert.Throws<ArgumentNullException>(() => ValidatedDelivery.Create(Req("D-1", "A-100", 1), null!, DeliveryLedger.Empty));
        Assert.Throws<ArgumentNullException>(() => ValidatedDelivery.Create(Req("D-1", "A-100", 1), NewCatalog(), null!));
    }

    [Fact]
    public void Factory_ReportsEveryBrokenRule_NotOnlyTheFirst()
    {
        var result = ValidatedDelivery.Create(Req(null, "Z-999", 0), NewCatalog(), DeliveryLedger.Empty);

        var invalid = Assert.IsType<DeliveryValidation.Invalid>(result);
        Assert.Equal(
            new[] { ErrorCodes.ReferenceRequired, ErrorCodes.ItemNotFound, ErrorCodes.InvalidQuantity },
            invalid.Errors.Select(e => e.Code).ToArray());
        Assert.Equal(3, invalid.Reasons.Length);
    }

    // ---------- boundary ----------
    [Fact]
    public void Boundary_StockAfterDeliveryExactlyAtTheLimit_IsAccepted_OneAboveIsRejected()
    {
        // Hammer has 2 units: 2 + 9998 = 10000 (the limit) is fine, 2 + 9999 = 10001 is not.
        var atLimit = DeliveryWorkflow.Receive(Req("D-1", "A-100", ValidatedDelivery.MaxStockPerSku - 2), NewCatalog(), DeliveryLedger.Empty);
        var above = DeliveryWorkflow.Receive(Req("D-2", "A-100", ValidatedDelivery.MaxStockPerSku - 1), NewCatalog(), DeliveryLedger.Empty);

        var receipt = Assert.IsType<Success<DeliveryReceipt>>(atLimit).Value;
        Assert.Equal(10_000, receipt.StockAfter);
        var failure = Assert.IsType<Failure<DeliveryReceipt>>(above);
        Assert.Equal(ErrorCodes.CapacityExceeded, failure.Error.Code);
        Assert.Contains("10001", failure.Error.Message);
    }

    [Fact]
    public void Boundary_VeryLargeQuantity_DoesNotOverflow()
    {
        var result = DeliveryWorkflow.Receive(Req("D-1", "A-100", int.MaxValue), NewCatalog(), DeliveryLedger.Empty);

        Assert.Equal(ErrorCodes.CapacityExceeded, Assert.IsType<Failure<DeliveryReceipt>>(result).Error.Code);
    }

    // ---------- the Result chain (Bind + Map) ----------
    [Fact]
    public void Receive_NormalPath_ProducesReceiptNewCatalogAndNewLedger()
    {
        var catalog = NewCatalog();

        var result = DeliveryWorkflow.Receive(Req("D-1", "A-100", 18), catalog, DeliveryLedger.Empty);

        var receipt = Assert.IsType<Success<DeliveryReceipt>>(result).Value;
        Assert.Equal(2, receipt.StockBefore);
        Assert.Equal(20, receipt.StockAfter);
        Assert.Equal(27000, receipt.ValueMinor);
        Assert.Equal("RECEIVED D-1: 18 x A-100, stock 2 -> 20, value=27000", receipt.Summary);
        Assert.Equal(new[] { 20, 5, 0 }, Stocks(receipt.Catalog));
        Assert.True(receipt.Ledger.Contains("D-1"));
    }

    [Fact]
    public void Receive_DoesNotModifyTheCallersCatalogOrLedger()
    {
        var catalog = NewCatalog();
        var ledger = DeliveryLedger.Empty;

        var result = DeliveryWorkflow.Receive(Req("D-1", "A-100", 18), catalog, ledger);

        Assert.IsType<Success<DeliveryReceipt>>(result);
        Assert.Equal(new[] { 2, 5, 0 }, Stocks(catalog));
        Assert.Equal(0, ledger.Count);
    }

    [Fact]
    public void Receive_ProductWithZeroStock_IsLegitimate_NotAbsent()
    {
        var result = DeliveryWorkflow.Receive(Req("D-1", "B-200", 5), NewCatalog(), DeliveryLedger.Empty);

        var receipt = Assert.IsType<Success<DeliveryReceipt>>(result).Value;
        Assert.Equal(0, receipt.StockBefore);
        Assert.Equal(5, receipt.StockAfter);
    }

    [Fact]
    public void Receive_FailureHasUsefulError_AndReturnsNoCatalog()
    {
        var result = DeliveryWorkflow.Receive(Req("D-1", "Z-999", 5), NewCatalog(), DeliveryLedger.Empty);

        var failure = Assert.IsType<Failure<DeliveryReceipt>>(result);
        Assert.Equal(ErrorCodes.ItemNotFound, failure.Error.Code);
        Assert.Contains("Z-999", failure.Error.Message);
    }

    [Fact]
    public void ToResult_InvalidBecomesOneFailure_WithFirstCodeAndAllMessages()
    {
        var validation = ValidatedDelivery.Create(Req(null, "Z-999", 5), NewCatalog(), DeliveryLedger.Empty);

        var failure = Assert.IsType<Failure<ValidatedDelivery>>(validation.ToResult());

        Assert.Equal(ErrorCodes.ReferenceRequired, failure.Error.Code);
        Assert.Contains("Delivery reference is required.", failure.Error.Message);
        Assert.Contains("Unknown sku 'Z-999'.", failure.Error.Message);
    }

    // ---------- repeated operation ----------
    [Fact]
    public void RepeatedDelivery_IsRejected_AndStateIsUnchanged()
    {
        var first = Assert.IsType<Success<DeliveryReceipt>>(
            DeliveryWorkflow.Receive(Req("D-1", "A-100", 18), NewCatalog(), DeliveryLedger.Empty)).Value;

        var again = DeliveryWorkflow.Receive(Req("D-1", "A-100", 18), first.Catalog, first.Ledger);

        var failure = Assert.IsType<Failure<DeliveryReceipt>>(again);
        Assert.Equal(ErrorCodes.DuplicateDelivery, failure.Error.Code);
        Assert.Equal(new[] { 20, 5, 0 }, Stocks(first.Catalog));
        Assert.Equal(1, first.Ledger.Count);
    }

    // ---------- batch ----------
    [Fact]
    public void ReceiveAll_RejectedDeliveriesChangeNothing_AcceptedOnesAccumulate()
    {
        var catalog = NewCatalog();
        var requests = new[]
        {
            Req("D-1", "A-100", 8),     // ok:    2 -> 10
            Req("D-1", "A-200", 1),     // duplicate reference
            Req("D-2", "Z-999", 1),     // unknown sku
            Req("D-3", "A-100", 0),     // invalid quantity
            Req("D-4", "A-100", 5),     // ok:    10 -> 15 (sees the stock left by D-1)
        };

        var batch = DeliveryWorkflow.ReceiveAll(requests, catalog, DeliveryLedger.Empty);

        Assert.Equal(5, batch.Outcomes.Length);
        Assert.Equal(
            new[] { true, false, false, false, true },
            batch.Outcomes.Select(o => o.Result is Success<DeliveryReceipt>).ToArray());
        Assert.Equal(new[] { 15, 5, 0 }, Stocks(batch.Catalog));
        Assert.Equal(new[] { "D-1", "D-4" }, batch.Ledger.References.ToArray());
        Assert.Equal(new[] { 2, 5, 0 }, Stocks(catalog)); // the input catalog is untouched
    }

    [Fact]
    public void ReceiveAll_EmptyDataset_ReturnsTheSameCatalogAndNoOutcomes()
    {
        var catalog = NewCatalog();

        var batch = DeliveryWorkflow.ReceiveAll(Array.Empty<DeliveryRequest?>(), catalog, DeliveryLedger.Empty);

        Assert.Empty(batch.Outcomes);
        Assert.Same(catalog, batch.Catalog);
        Assert.Equal(0, batch.Ledger.Count);
    }

    [Fact]
    public void ReceiveAll_NullArguments_AreRejected()
    {
        Assert.Throws<ArgumentNullException>(() => DeliveryWorkflow.ReceiveAll(null!, NewCatalog(), DeliveryLedger.Empty));
        Assert.Throws<ArgumentNullException>(() => DeliveryWorkflow.ReceiveAll(Array.Empty<DeliveryRequest?>(), null!, DeliveryLedger.Empty));
        Assert.Throws<ArgumentNullException>(() => DeliveryWorkflow.ReceiveAll(Array.Empty<DeliveryRequest?>(), NewCatalog(), null!));
    }

    // ---------- ledger ----------
    [Fact]
    public void Ledger_IsImmutable_WithReturnsANewLedger()
    {
        var empty = DeliveryLedger.Empty;

        var one = empty.With("D-1");

        Assert.Equal(0, empty.Count);
        Assert.Equal(1, one.Count);
        Assert.True(one.Contains("D-1"));
        Assert.False(empty.Contains("D-1"));
        Assert.Throws<ArgumentException>(() => empty.With("  "));
    }

    // ---------- input ----------
    [Fact]
    public void ParseJson_ReadsDeliveries_AndKeepsMissingFieldsAsNull()
    {
        var parsed = DeliveryInput.ParseJson("""[ { "reference": "D-1", "sku": "A-100", "quantity": 3 }, { "sku": "A-200" } ]""");

        Assert.Equal(2, parsed.Count);
        Assert.Equal(new DeliveryRequest("D-1", "A-100", 3), parsed[0]);
        Assert.Equal(new DeliveryRequest(null, "A-200", null), parsed[1]);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{ not json")]
    public void ParseJson_RootNullOrMalformed_ThrowsJsonException(string json)
    {
        Assert.Throws<JsonException>(() => DeliveryInput.ParseJson(json));
    }

    [Fact]
    public void ParseJson_EmptyArray_IsAValidEmptyDataset()
    {
        Assert.Empty(DeliveryInput.ParseJson("[]"));
    }

    // ---------- saved stock ----------
    [Fact]
    public void ProductFormats_ToJson_RoundTrips_SortedBySku_WithUnixLineEndings()
    {
        var json = ProductFormats.ToJson(new[] { Screws, Hammer, Drill });

        Assert.DoesNotContain("\r", json);
        var intake = ProductInput.ValidateAll(ProductInput.ParseJson(json));
        Assert.Empty(intake.Issues);
        Assert.Equal(new[] { Hammer, Drill, Screws }, intake.Valid.ToArray());
    }

    [Fact]
    public void ProductFormats_ToJson_EmptyDataset_IsAnEmptyArray()
    {
        Assert.Equal("[]\n", ProductFormats.ToJson(Array.Empty<Product>()));
        Assert.Throws<ArgumentNullException>(() => ProductFormats.ToJson(null!));
    }
}
