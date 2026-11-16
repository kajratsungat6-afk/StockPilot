using System.Collections.Immutable;
using StockPilot;
using Xunit;

namespace StockPilot.Tests;

/// <summary>Command line parsing and the whole application, run without a real console:
/// output goes to a StringWriter, files live in a controlled temporary folder that is removed afterwards.</summary>
public sealed class ApplicationTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 9, 0, 0, TimeSpan.Zero);

    private const string Products = """
        [
          { "sku": "A-100", "name": "Hammer", "categoryId": "TOOLS", "quantity": 2, "unitPriceMinor": 1500, "reorderLevel": 10 },
          { "sku": "B-200", "name": "Screws", "categoryId": "HARDWARE", "quantity": 0, "unitPriceMinor": 200, "reorderLevel": 20 },
          { "sku": "C-100", "name": "Tape", "categoryId": "SUPPLIES", "quantity": 50, "unitPriceMinor": 400, "reorderLevel": 10 }
        ]
        """;

    private const string Orders = """[ { "sku": "A-100", "quantity": 2 }, { "sku": "Z-999", "quantity": 1 } ]""";

    private const string Deliveries = """
        [
          { "reference": "D-1", "sku": "A-100", "quantity": 18 },
          { "reference": "D-1", "sku": "B-200", "quantity": 5 },
          { "reference": "D-2", "sku": "Z-999", "quantity": 5 }
        ]
        """;

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "stockpilot-app-" + Guid.NewGuid().ToString("N"));

    public ApplicationTests() => Directory.CreateDirectory(_dir);

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

    private string Out => Path.Combine(_dir, "out");

    private (int Exit, string Output, string Error) Run(IFeeCalculator? fees, params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = Application.Run(args, _dir, output, error, () => Now, fees ?? new NativeFeeCalculator());
        return (exit, output.ToString(), error.ToString());
    }

    // ---------- command line ----------
    [Fact]
    public void CommandLine_NoArguments_MeansDemo_WithDefaultPaths()
    {
        var parsed = CommandLine.Parse(Array.Empty<string>(), "data");

        var options = Assert.IsType<Success<AppOptions>>(parsed).Value;
        Assert.Equal(CommandLine.Demo, options.Command);
        Assert.Equal(Path.Combine("data", "products.json"), options.ProductsPath);
        Assert.Equal(Path.Combine("data", "orders.json"), options.OrdersPath);
        Assert.Equal(Path.Combine("data", "deliveries.json"), options.DeliveriesPath);
        Assert.Equal("output", options.OutputDir);
    }

    [Fact]
    public void CommandLine_CommandAndOptions_AreRead()
    {
        var parsed = CommandLine.Parse(new[] { "deliveries", "--products", "p.json", "--deliveries", "d.json", "--out", "o" }, "data");

        var options = Assert.IsType<Success<AppOptions>>(parsed).Value;
        Assert.Equal(CommandLine.Deliveries, options.Command);
        Assert.Equal("p.json", options.ProductsPath);
        Assert.Equal("d.json", options.DeliveriesPath);
        Assert.Equal("o", options.OutputDir);
    }

    [Theory]
    [InlineData("frobnicate", null, null)]
    [InlineData("orders", "--colour", "red")]
    [InlineData("orders", "--orders", null)]
    public void CommandLine_WrongArguments_BecomeInvalidArguments_WithoutAnException(string a, string? b, string? c)
    {
        var args = new[] { a, b, c }.Where(x => x is not null).Select(x => x!).ToArray();

        var parsed = CommandLine.Parse(args, "data");

        Assert.Equal(ErrorCodes.InvalidArguments, Assert.IsType<Failure<AppOptions>>(parsed).Error.Code);
    }

    [Fact]
    public void CommandLine_EachCommandOpensOnlyTheFilesItNeeds()
    {
        Assert.True(CommandLine.NeedsOrders(CommandLine.Orders));
        Assert.True(CommandLine.NeedsDeliveries(CommandLine.Deliveries));
        Assert.False(CommandLine.NeedsOrders(CommandLine.Deliveries));
        Assert.False(CommandLine.NeedsDeliveries(CommandLine.Orders));
        Assert.False(CommandLine.NeedsOrders(CommandLine.Restock));
        Assert.True(CommandLine.NeedsOrders(CommandLine.Demo) && CommandLine.NeedsDeliveries(CommandLine.Demo));
    }

    // ---------- complete workflows ----------
    [Fact]
    public void Deliveries_NormalPath_PrintsReceipts_AndSavesStockThatCanBeLoadedAgain()
    {
        var products = Write("products.json", Products);
        var deliveries = Write("deliveries.json", Deliveries);

        var (exit, output, error) = Run(null, "deliveries", "--products", products, "--deliveries", deliveries, "--out", Out);

        Assert.Equal(0, exit);
        Assert.Equal("", error);
        Assert.Contains("RECEIVED D-1: 18 x A-100, stock 2 -> 20, value=27000", output);
        Assert.Contains("REJECTED [DuplicateDelivery]", output);
        Assert.Contains("REJECTED [ItemNotFound]", output);

        var saved = Path.Combine(Out, "products_after.json");
        Assert.True(File.Exists(saved));
        var reloaded = AppFlow.Load(saved, null);
        var data = Assert.IsType<Success<LoadedData>>(reloaded).Value;
        var intake = ProductInput.ValidateAll(data.Products);
        Assert.Empty(intake.Issues);
        Assert.Equal(new[] { 20, 0, 50 }, intake.Valid.Select(p => p.Quantity).ToArray()); // A-100 received, the rest unchanged
    }

    [Fact]
    public void Orders_WithRealNativeLibrary_ConfirmsAndSavesStock_AndSkipsTheDeliveriesFile()
    {
        var products = Write("products.json", Products);
        var orders = Write("orders.json", Orders);

        // --deliveries points to a file that does not exist: the orders command must not open it
        var (exit, output, _) = Run(null, "orders", "--products", products, "--orders", orders,
            "--deliveries", Path.Combine(_dir, "nothing.json"), "--out", Out);

        Assert.Equal(0, exit);
        Assert.Contains("handling fee for total 10000, quantity 3: 410 (expected 410)", output);
        Assert.Contains("CONFIRMED 2 x A-100, total=3000, fee=215", output);
        Assert.Contains("REJECTED [ItemNotFound]", output);
        Assert.Contains("A-100  Hammer     qty=0", output);
        Assert.True(File.Exists(Path.Combine(Out, "products_after.json")));
    }

    [Fact]
    public void Demo_FullFlow_RunsEverySection_WithExitCodeZero()
    {
        var products = Write("products.json", Products);
        var orders = Write("orders.json", Orders);
        var deliveries = Write("deliveries.json", Deliveries);

        var (exit, output, error) = Run(null, "--products", products, "--orders", orders, "--deliveries", deliveries, "--out", Out);

        Assert.Equal(0, exit);
        Assert.Equal("", error);
        foreach (var section in new[]
        {
            "=== Policy: Conservative", "=== Policy: Economy (curried)", "=== Report 1: Purchasing list",
            "=== Report 2: Stock value by category", "=== Native library check", "=== Orders",
            "=== Invalid sequences", "=== Deliveries", "=== Export to", "=== Save updated stock to",
        })
            Assert.Contains(section, output);
        Assert.True(File.Exists(Path.Combine(Out, "purchasing_list.csv")));
        Assert.True(File.Exists(Path.Combine(Out, "value_by_category.csv")));
        Assert.True(File.Exists(Path.Combine(Out, "products_after.json")));
    }

    [Fact]
    public void Reports_ExportCsv_WithTheDocumentedHeaderAndRows()
    {
        var products = Write("products.json", Products);

        var (exit, output, _) = Run(null, "reports", "--products", products, "--out", Out);

        Assert.Equal(0, exit);
        Assert.Contains("total deficit: 28", output);
        var csv = File.ReadAllText(Path.Combine(Out, "purchasing_list.csv"));
        Assert.Equal("sku,name,quantity,reorder_level,deficit\nB-200,Screws,0,20,20\nA-100,Hammer,2,10,8\n", csv);
    }

    [Fact]
    public void Help_PrintsUsage_AndExitsWithZero()
    {
        var (exit, output, _) = Run(null, "help");

        Assert.Equal(0, exit);
        Assert.Contains("Usage: StockPilot", output);
    }

    // ---------- failure paths: clear message, exit code 1, nothing half written ----------
    [Fact]
    public void UnknownCommand_ShowsUsageAndError_WithExitCodeOne()
    {
        var (exit, output, error) = Run(null, "frobnicate");

        Assert.Equal(1, exit);
        Assert.Equal("", output);
        Assert.Contains("ERROR [InvalidArguments] Unknown command 'frobnicate'.", error);
        Assert.Contains("Usage: StockPilot", error);
    }

    [Fact]
    public void MissingProductsFile_IsReported_AndNothingIsWritten()
    {
        var (exit, output, error) = Run(null, "restock", "--products", Path.Combine(_dir, "missing.json"), "--out", Out);

        Assert.Equal(1, exit);
        Assert.Equal("", output);
        Assert.Contains("ERROR [FileMissing]", error);
        Assert.Contains("missing.json", error);
        Assert.False(Directory.Exists(Out));
    }

    [Fact]
    public void MalformedDeliveriesFile_IsReported_AndNothingIsWritten()
    {
        var products = Write("products.json", Products);
        var deliveries = Write("deliveries.json", "{ this is not json");

        var (exit, output, error) = Run(null, "deliveries", "--products", products, "--deliveries", deliveries, "--out", Out);

        Assert.Equal(1, exit);
        Assert.Equal("", output);
        Assert.Contains("ERROR [MalformedData]", error);
        Assert.Contains("deliveries.json", error);
        Assert.False(Directory.Exists(Out));
    }

    [Fact]
    public void Orders_WhenTheNativeLibraryCannotBeLoaded_OrdersAreRejected_AndSavedStockIsUnchanged()
    {
        var products = Write("products.json", Products);
        var orders = Write("orders.json", Orders);
        var failing = new NativeFeeCalculator(MissingLibrary.CalcHandlingFee); // a real binding to a library that does not exist

        var (exit, output, _) = Run(failing, "orders", "--products", products, "--orders", orders, "--out", Out);

        Assert.Equal(0, exit); // a rejected order is a domain outcome, not an operational failure
        Assert.Contains("FAILED [NativeUnavailable]", output);
        Assert.Contains("REJECTED [NativeUnavailable]", output);
        var originalIntake = ProductInput.ValidateAll(ProductInput.ParseJson(Products));
        Assert.Equal(ProductFormats.ToJson(originalIntake.Valid), File.ReadAllText(Path.Combine(Out, "products_after.json")));
    }

    [Fact]
    public void SavingStockFails_ExitCodeIsOne_AndTheMessageNamesTheProblem()
    {
        var products = Write("products.json", Products);
        var deliveries = Write("deliveries.json", Deliveries);
        var notAFolder = Write("out-is-a-file", "I am a file, not a folder");

        var (exit, output, _) = Run(null, "deliveries", "--products", products, "--deliveries", deliveries, "--out", notAFolder);

        Assert.Equal(1, exit);
        Assert.Contains("FAILED [IoError]", output);
        Assert.Equal("I am a file, not a folder", File.ReadAllText(notAFolder)); // untouched
    }

    [Fact]
    public void Run_NullArguments_AreRejected()
    {
        Assert.Throws<ArgumentNullException>(() => Application.Run(Array.Empty<string>(), _dir, null!, TextWriter.Null, () => Now, new NativeFeeCalculator()));
        Assert.Throws<ArgumentNullException>(() => Application.Run(Array.Empty<string>(), _dir, TextWriter.Null, TextWriter.Null, null!, new NativeFeeCalculator()));
        Assert.Throws<ArgumentNullException>(() => Application.Run(Array.Empty<string>(), _dir, TextWriter.Null, TextWriter.Null, () => Now, null!));
    }
}
