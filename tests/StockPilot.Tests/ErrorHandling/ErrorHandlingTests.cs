using System.Collections;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using StockPilot;
using Xunit;

namespace StockPilot.Tests;

public sealed class ErrorHandlingTests : IDisposable
{
    private static readonly Product Hammer = new("A-100", "Hammer", "TOOLS", 2, 1500, 10);
    private static readonly Product Drill  = new("A-200", "Drill", "TOOLS", 5, 25000, 6);
    private static readonly Product Screws = new("B-200", "Screws", "HARDWARE", 0, 200, 20);   // stock 0 is legitimate
    private static readonly DateTimeOffset At = new(2026, 10, 18, 9, 0, 0, TimeSpan.Zero);

    private static Catalog NewCatalog() => Catalog.FromProducts(new[] { Hammer, Drill, Screws });
    private static int[] Stocks(Catalog c) => c.Products.Select(p => p.Quantity).ToArray();

    // controlled temporary folder, removed after every test
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "stockpilot-tests-" + Guid.NewGuid().ToString("N"));
    public ErrorHandlingTests() => Directory.CreateDirectory(_dir);
    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private string Write(string name, string content)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private const string ValidProducts = """[ { "sku": "A-100", "name": "Hammer", "categoryId": "TOOLS", "quantity": 2, "unitPriceMinor": 1500, "reorderLevel": 10 } ]""";
    private const string ValidOrders = """[ { "sku": "A-100", "quantity": 1 } ]""";

    // ---------- Result model: contradictory states are impossible ----------
    [Fact]
    public void Result_And_Maybe_RejectContradictoryStates()
    {
        Assert.Throws<ArgumentNullException>(() => Result.Ok<string?>(null));      // success without payload
        Assert.Throws<ArgumentNullException>(() => Result.Fail<int>((Error)null!)); // failure without error
        Assert.Throws<ArgumentNullException>(() => Maybe.Some<string?>(null));     // Something without value
    }

    // ---------- MAP ----------
    [Fact]
    public void Map_Success_PassesValueToFunction_AndReturnsNewValue()
    {
        var received = -1;

        var result = Result.Ok(3).Map(x => { received = x; return x * 2; });

        Assert.Equal(3, received);
        Assert.Equal(6, Assert.IsType<Success<int>>(result).Value);
    }

    // ---------- BIND ----------
    [Fact]
    public void Bind_Success_PassesValue_AndPreservesTheNextResult()
    {
        var received = -1;

        var ok = Result.Ok(3).Bind(x => { received = x; return Result.Ok(x.ToString()); });
        var failed = Result.Ok(3).Bind(_ => Result.Fail<string>("Custom", "next step failed"));

        Assert.Equal(3, received);
        Assert.Equal("3", Assert.IsType<Success<string>>(ok).Value);
        Assert.Equal("Custom", Assert.IsType<Failure<string>>(failed).Error.Code); // the next step's failure is preserved
    }

    // ---------- FAILURE PROPAGATION ----------
    [Fact]
    public void Map_And_Bind_OnFailure_PreserveError_AndDoNotCallTheFunction()
    {
        var calls = 0;
        var failure = Result.Fail<int>(ErrorCodes.ItemNotFound, "no such item");

        var mapped = failure.Map(x => { calls++; return x + 1; });
        var bound = failure.Bind(x => { calls++; return Result.Ok(x + 1); });
        var chained = failure.Map(x => { calls++; return x; }).Bind(x => { calls++; return Result.Ok(x); });

        Assert.Equal(0, calls); // none of the supplied functions ran
        Assert.Equal("no such item", Assert.IsType<Failure<int>>(mapped).Error.Message);
        Assert.Equal(ErrorCodes.ItemNotFound, Assert.IsType<Failure<int>>(bound).Error.Code);
        Assert.Equal(ErrorCodes.ItemNotFound, Assert.IsType<Failure<int>>(chained).Error.Code);
    }

    // ---------- LOOKUP PRESENT AND ABSENT ----------
    [Fact]
    public void Lookup_PresentAndAbsent_AndZeroStockIsPresent()
    {
        var catalog = NewCatalog();

        Assert.Equal("Hammer", Assert.IsType<Something<Product>>(catalog.Find("A-100")).Value.Name);
        Assert.IsType<Nothing<Product>>(catalog.Find("Z-999"));
        Assert.IsType<Nothing<Product>>(catalog.Find(""));

        var zero = Assert.IsType<Something<int>>(catalog.StockOf("B-200")); // 0 units is data, not absence
        Assert.Equal(0, zero.Value);
        Assert.IsType<Nothing<int>>(catalog.StockOf("Z-999"));
    }

    [Fact]
    public void Maybe_ZeroFalseAndEmptyString_AreSomething_NotNothing()
    {
        Assert.Equal(0, Assert.IsType<Something<int>>(Maybe.Some(0)).Value);
        Assert.False(Assert.IsType<Something<bool>>(Maybe.Some(false)).Value);
        Assert.Equal("", Assert.IsType<Something<string>>(Maybe.Some("")).Value);
        Assert.IsType<Nothing<int>>(Maybe.None<int>());
    }

    [Fact]
    public void RequiredLookup_MissingItemBecomesFailure_ZeroStockStaysSuccess()
    {
        var catalog = NewCatalog();

        Assert.Equal(2, Assert.IsType<Success<int>>(OrderWorkflow.StockLevel(catalog, "A-100")).Value);
        Assert.Equal(0, Assert.IsType<Success<int>>(OrderWorkflow.StockLevel(catalog, "B-200")).Value);

        var missing = Assert.IsType<Failure<int>>(OrderWorkflow.StockLevel(catalog, "Z-999"));
        Assert.Equal(ErrorCodes.ItemNotFound, missing.Error.Code);
        Assert.Contains("Z-999", missing.Error.Message);
    }

    // ---------- COMPLETE SUCCESS ----------
    [Fact]
    public void Chain_Success_ProducesReceipt_AndNewCatalog_WithoutChangingTheOriginal()
    {
        var catalog = NewCatalog();

        var result = OrderWorkflow.Process(new OrderRequest("A-100", 2), catalog, TestFees.None, At);

        var receipt = Assert.IsType<Success<OrderReceipt>>(result).Value;
        Assert.Equal("A-100", receipt.Sku);
        Assert.Equal(2, receipt.Quantity);
        Assert.Equal(3000, receipt.TotalMinor);
        Assert.Equal(At, receipt.ConfirmedAt);
        Assert.Equal("CONFIRMED 2 x A-100, total=3000, fee=0", receipt.Summary);
        Assert.Equal(new[] { 0, 5, 0 }, Stocks(receipt.Catalog));
        Assert.Equal(new[] { 2, 5, 0 }, Stocks(catalog));
    }

    // ---------- DOMAIN FAILURES (each with a useful error) ----------
    [Theory]
    [InlineData("A-100", 0, ErrorCodes.InvalidQuantity, "Quantity must be > 0.")]
    [InlineData("Z-999", 1, ErrorCodes.ItemNotFound, "Unknown sku 'Z-999'.")]
    [InlineData("A-100", 99, ErrorCodes.InsufficientStock, "Insufficient stock for 'A-100': requested 99, available 2.")]
    [InlineData(null, 1, ErrorCodes.SkuRequired, "Sku is required.")]
    public void Chain_DomainFailures_HaveCodeAndMessage_AndLeaveCatalogUnchanged(
        string? sku, int quantity, string expectedCode, string expectedMessage)
    {
        var catalog = NewCatalog();
        var before = Stocks(catalog);

        var result = OrderWorkflow.Process(new OrderRequest(sku, quantity), catalog, TestFees.None, At);

        var failure = Assert.IsType<Failure<OrderReceipt>>(result);
        Assert.Equal(expectedCode, failure.Error.Code);
        Assert.Equal(expectedMessage, failure.Error.Message);
        Assert.Equal(before, Stocks(catalog));
    }

    [Fact]
    public void Chain_InvalidState_RepeatedAndRejectedConfirmations_AreFailures()
    {
        var catalog = NewCatalog();
        var pending = OrderWorkflow.Place(new OrderRequest("A-200", 1), catalog);
        var first = Assert.IsType<Success<OrderConfirmation>>(OrderWorkflow.ConfirmReservation(pending, catalog, At)).Value;

        var repeated = OrderWorkflow.ConfirmReservation(first.State, first.Catalog, At);
        var rejected = OrderWorkflow.ConfirmReservation(OrderWorkflow.Place(new OrderRequest("Z-999", 1), catalog), catalog, At);

        Assert.Equal(ErrorCodes.InvalidState, Assert.IsType<Failure<OrderConfirmation>>(repeated).Error.Code);
        Assert.Equal(ErrorCodes.InvalidState, Assert.IsType<Failure<OrderConfirmation>>(rejected).Error.Code);
        Assert.Equal(new[] { 2, 4, 0 }, Stocks(first.Catalog)); // stock was taken only once
    }

    [Fact]
    public void ValidationResult_WithSeveralBrokenRules_KeepsFirstCode_AndAllMessages()
    {
        var result = ValidatedOrder.Create(new OrderRequest("Z-999", 0), NewCatalog()).ToResult();

        var failure = Assert.IsType<Failure<ValidatedOrder>>(result);
        Assert.Equal(ErrorCodes.ItemNotFound, failure.Error.Code);
        Assert.Equal("Unknown sku 'Z-999'. Quantity must be > 0.", failure.Error.Message);
    }

    [Fact]
    public void Workflow_Batch_EachOutcomeCarriesItsErrorCode()
    {
        var requests = new OrderRequest?[]
        {
            new("A-100", 2), new("A-200", 5), new("B-200", 1), new("Z-999", 1),
            new("A-200", 0), new(null, 1), null,
        };

        var batch = OrderWorkflow.ProcessAll(requests, NewCatalog(), TestFees.None, At);

        var codes = batch.Outcomes.Select(o => o.Result.Match(_ => "OK", e => e.Code)).ToArray();
        Assert.Equal(new[]
        {
            "OK", "OK", ErrorCodes.InsufficientStock, ErrorCodes.ItemNotFound,
            ErrorCodes.InvalidQuantity, ErrorCodes.SkuRequired, ErrorCodes.InvalidRequest,
        }, codes);
        Assert.Equal(new[] { 0, 0, 0 }, Stocks(batch.Catalog));
    }

    // ---------- MISSING FILE ----------
    [Fact]
    public void File_Missing_BecomesFileMissingFailure_WithThePath_AndNoException()
    {
        var path = Path.Combine(_dir, "nope.json");

        var result = DataFile.Load<IReadOnlyList<OrderRequest?>>(path, OrderInput.ParseJson);

        var failure = Assert.IsType<Failure<IReadOnlyList<OrderRequest?>>>(result);
        Assert.Equal(ErrorCodes.FileMissing, failure.Error.Code);
        Assert.Contains(path, failure.Error.Message);
        Assert.False(File.Exists(path)); // nothing was created or changed
    }

    // ---------- MALFORMED FILE ----------
    [Theory]
    [InlineData("{ not json")]
    [InlineData("null")]
    [InlineData("""[ { "sku": "A-100", "quantity": "many" } ]""")]
    public void File_Malformed_BecomesMalformedDataFailure_WithThePath(string content)
    {
        var path = Write("orders.json", content);

        var result = DataFile.Load<IReadOnlyList<OrderRequest?>>(path, OrderInput.ParseJson);

        var failure = Assert.IsType<Failure<IReadOnlyList<OrderRequest?>>>(result);
        Assert.Equal(ErrorCodes.MalformedData, failure.Error.Code);
        Assert.Contains(path, failure.Error.Message);
        Assert.Equal(content, File.ReadAllText(path)); // the file itself was not touched
    }

    [Fact]
    public void File_Valid_BecomesSuccess_AndUnreadablePathBecomesIoError()
    {
        var good = Write("orders.json", ValidOrders);
        var ok = DataFile.Load<IReadOnlyList<OrderRequest?>>(good, OrderInput.ParseJson);
        Assert.Single(Assert.IsType<Success<IReadOnlyList<OrderRequest?>>>(ok).Value);

        var notAFile = DataFile.Load<IReadOnlyList<OrderRequest?>>(_dir, OrderInput.ParseJson); // a folder, not a file
        Assert.Equal(ErrorCodes.IoError, Assert.IsType<Failure<IReadOnlyList<OrderRequest?>>>(notAFile).Error.Code);

        var empty = DataFile.Load<IReadOnlyList<OrderRequest?>>("  ", OrderInput.ParseJson);
        Assert.Equal(ErrorCodes.IoError, Assert.IsType<Failure<IReadOnlyList<OrderRequest?>>>(empty).Error.Code);
    }

    // ---------- FILE FAILURES INSIDE THE APPLICATION FLOW (consistent state) ----------
    [Fact]
    public void AppFlow_BothFilesValid_ReturnsAllData()
    {
        var products = Write("products.json", ValidProducts);
        var orders = Write("orders.json", ValidOrders);

        var loaded = Assert.IsType<Success<LoadedData>>(AppFlow.Load(products, orders)).Value;

        Assert.Single(loaded.Products);
        Assert.Single(loaded.Orders);
    }

    [Fact]
    public void AppFlow_OneFileBroken_ReturnsFailureAndNoPartialData()
    {
        var products = Write("products.json", ValidProducts);
        var badOrders = Write("orders.json", "{ not json");
        var missingProducts = Path.Combine(_dir, "missing-products.json");
        var missingOrders = Path.Combine(_dir, "missing-orders.json");

        var malformed = Assert.IsType<Failure<LoadedData>>(AppFlow.Load(products, badOrders));
        Assert.Equal(ErrorCodes.MalformedData, malformed.Error.Code);
        Assert.Contains(badOrders, malformed.Error.Message);

        // products fail first: Bind skips the orders file, so the error names the products file only
        var skipped = Assert.IsType<Failure<LoadedData>>(AppFlow.Load(missingProducts, missingOrders));
        Assert.Equal(ErrorCodes.FileMissing, skipped.Error.Code);
        Assert.Contains(missingProducts, skipped.Error.Message);
        Assert.DoesNotContain(missingOrders, skipped.Error.Message);
    }
}
