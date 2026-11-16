using System.Collections;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using StockPilot;
using Xunit;

namespace StockPilot.Tests;

public sealed class ReportsAndNativeTests : IDisposable
{
    private static readonly Product Hammer = new("A-100", "Hammer", "TOOLS", 2, 1500, 10);
    private static readonly Product Drill  = new("A-200", "Drill", "TOOLS", 5, 25000, 6);
    private static readonly Product Nails  = new("B-100", "Nails box", "HARDWARE", 10, 300, 10);
    private static readonly Product Screws = new("B-200", "Screws", "HARDWARE", 0, 200, 20);
    private static readonly Product Tape   = new("C-100", "Tape", "SUPPLIES", 50, 400, 10);
    private static readonly Product Glue   = new("C-200", "Glue", "SUPPLIES", 30, 350, 5);
    private static readonly DateTimeOffset At = new(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);

    private static Product[] AllProducts() => new[] { Hammer, Drill, Nails, Screws, Tape, Glue };
    private static Catalog NewCatalog() => Catalog.FromProducts(AllProducts());
    private static int[] Stocks(Catalog c) => c.Products.Select(p => p.Quantity).ToArray();
    private static string[] Skus(RestockPlan p) => p.Items.Select(i => i.Sku).ToArray();

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "stockpilot-reports-" + Guid.NewGuid().ToString("N"));
    public ReportsAndNativeTests() => Directory.CreateDirectory(_dir);
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

    private const string ProductsJson = """
    [
      { "sku": "A-100", "name": "Hammer",    "categoryId": "TOOLS",    "quantity": 2,  "unitPriceMinor": 1500,  "reorderLevel": 10 },
      { "sku": "A-200", "name": "Drill",     "categoryId": "TOOLS",    "quantity": 5,  "unitPriceMinor": 25000, "reorderLevel": 6 },
      { "sku": "B-100", "name": "Nails box", "categoryId": "HARDWARE", "quantity": 10, "unitPriceMinor": 300,   "reorderLevel": 10 },
      { "sku": "B-200", "name": "Screws",    "categoryId": "HARDWARE", "quantity": 0,  "unitPriceMinor": 200,   "reorderLevel": 20 },
      { "sku": "C-100", "name": "Tape",      "categoryId": "SUPPLIES", "quantity": 50, "unitPriceMinor": 400,   "reorderLevel": 10 },
      { "sku": "C-200", "name": "Glue",      "categoryId": "SUPPLIES", "quantity": 30, "unitPriceMinor": 350,   "reorderLevel": 5 },
      { "sku": "D-100", "name": "Saw",       "categoryId": "TOOLS",    "quantity": 3,  "unitPriceMinor": -500,  "reorderLevel": 5 },
      { "name": "Pliers", "categoryId": "TOOLS", "quantity": 1, "unitPriceMinor": 900, "reorderLevel": 5 }
    ]
    """;

    private const string OrdersJson = """
    [
      { "sku": "A-100", "quantity": 2 }, { "sku": "A-200", "quantity": 5 }, { "sku": "B-200", "quantity": 1 },
      { "sku": "Z-999", "quantity": 1 }, { "sku": "C-100", "quantity": 0 }, { "sku": "C-100", "quantity": 40 },
      { "sku": "C-100", "quantity": 20 }, { "quantity": 1 }
    ]
    """;

    // ================= REPORTS =================
    [Fact]
    public void Report1_PurchasingList_NormalResult()
    {
        var report = Reports.PurchasingList(AllProducts());

        Assert.Equal(new[] { "B-200", "A-100", "A-200", "B-100" }, report.Lines.Select(l => l.Sku).ToArray());
        Assert.Equal(new[] { 20, 8, 1, 0 }, report.Lines.Select(l => l.Deficit).ToArray());
        Assert.Equal(29, report.TotalDeficit);
    }

    [Fact]
    public void Report1_EmptyDataset_AndNoMatches()
    {
        var empty = Reports.PurchasingList(Array.Empty<Product>());
        Assert.Empty(empty.Lines);
        Assert.Equal(0, empty.TotalDeficit);

        var none = Reports.PurchasingList(new[] { Tape, Glue }); // both well above their reorder level
        Assert.Empty(none.Lines);
        Assert.Equal(0, none.TotalDeficit);
    }

    [Fact]
    public void Report1_TiedDeficits_AreOrderedBySku_InAnyInputOrder()
    {
        var b = Hammer with { Sku = "B", Quantity = 0, ReorderLevel = 5 };
        var a = Hammer with { Sku = "A", Quantity = 0, ReorderLevel = 5 };

        Assert.Equal(new[] { "A", "B" }, Reports.PurchasingList(new[] { b, a }).Lines.Select(l => l.Sku).ToArray());
        Assert.Equal(new[] { "A", "B" }, Reports.PurchasingList(new[] { a, b }).Lines.Select(l => l.Sku).ToArray());
    }

    [Fact]
    public void Report2_ValueByCategory_GroupingAndTotals()
    {
        var report = Reports.ValueByCategory(AllProducts());

        Assert.Equal(new[] { "TOOLS", "SUPPLIES", "HARDWARE" }, report.Rows.Select(r => r.CategoryId).ToArray());
        Assert.Equal(new[] { 2, 2, 1 }, report.Rows.Select(r => r.ProductCount).ToArray());       // Screws (0 in stock) is excluded
        Assert.Equal(new[] { 7, 80, 10 }, report.Rows.Select(r => r.TotalUnits).ToArray());
        Assert.Equal(new long[] { 128000, 30500, 3000 }, report.Rows.Select(r => r.TotalValueMinor).ToArray());
        Assert.Equal(161500, report.GrandTotalMinor);
    }

    [Fact]
    public void Report2_Empty_NoMatches_AndTiedValues()
    {
        var empty = Reports.ValueByCategory(Array.Empty<Product>());
        Assert.Empty(empty.Rows);
        Assert.Equal(0, empty.GrandTotalMinor);

        var allZero = Reports.ValueByCategory(new[] { Screws });   // nothing in stock -> no rows
        Assert.Empty(allZero.Rows);

        var z = Hammer with { Sku = "Z1", CategoryId = "Z-CAT", Quantity = 1, UnitPriceMinor = 1000 };
        var a = Hammer with { Sku = "A1", CategoryId = "A-CAT", Quantity = 1, UnitPriceMinor = 1000 };
        var tied = Reports.ValueByCategory(new[] { z, a });
        Assert.Equal(new[] { "A-CAT", "Z-CAT" }, tied.Rows.Select(r => r.CategoryId).ToArray());   // same value -> by category
    }

    [Fact]
    public void Reports_UseOnlyValidatedRows_InvalidRowsNeverReachThem()
    {
        var intake = ProductInput.ValidateAll(ProductInput.ParseJson(ProductsJson));   // 6 valid, 2 invalid (negative price, no sku)

        var purchasing = Reports.PurchasingList(intake.Valid);
        var value = Reports.ValueByCategory(intake.Valid);

        Assert.Equal(2, intake.Issues.Length);
        Assert.DoesNotContain("D-100", purchasing.Lines.Select(l => l.Sku));    // the invalid Saw is not in the report
        Assert.Equal(161500, value.GrandTotalMinor);
    }

    // ================= EXTENSIONS =================
    [Fact]
    public void Extension_LowStock_IncludesThreshold_ExcludesAbove_AndDoesNotChangeTheSource()
    {
        var source = AllProducts();
        var snapshot = source.ToArray();

        var low = source.LowStock().Select(p => p.Sku).ToArray();

        Assert.Equal(new[] { "A-100", "A-200", "B-100", "B-200" }, low);   // B-100 is exactly at its threshold
        Assert.True(snapshot.SequenceEqual(source));
        Assert.Throws<ArgumentNullException>(() => ((IEnumerable<Product>)null!).LowStock());
    }

    [Fact]
    public void Extension_Ensure_KeepsValue_ReturnsGivenError_AndSkipsPredicateOnFailure()
    {
        var error = new Error("TooSmall", "value must be positive");

        Assert.Equal(5, Assert.IsType<Success<int>>(Result.Ok(5).Ensure(x => x > 0, error)).Value);
        Assert.Same(error, Assert.IsType<Failure<int>>(Result.Ok(-5).Ensure(x => x > 0, error)).Error);

        var calls = 0;
        var failed = Result.Fail<int>("Other", "earlier failure").Ensure(x => { calls++; return true; }, error);
        Assert.Equal(0, calls);
        Assert.Equal("Other", Assert.IsType<Failure<int>>(failed).Error.Code);   // the earlier error is kept
    }

    [Fact]
    public void Extensions_AreUsedByApplicationCode()
    {
        // LowStock is the filter inside restock pipeline stage 1
        Assert.Equal(
            AllProducts().LowStock().Select(p => p.Sku).ToArray(),
            Stages.SelectRestockCandidates(AllProducts()).Select(p => p.Sku).ToArray());

        // Ensure is the limit rule inside OrderWorkflow.Process
        var big = new Product("BIG", "Big", "X", 5, 600_000_000, 1);
        var result = OrderWorkflow.Process(new OrderRequest("BIG", 2), Catalog.FromProducts(new[] { big }), TestFees.None, At);
        Assert.Equal(ErrorCodes.OrderTooLarge, Assert.IsType<Failure<OrderReceipt>>(result).Error.Code);
    }

    // ================= CURRIED / PARTIALLY APPLIED FUNCTIONS =================
    [Fact]
    public void CurriedScore_TwoPartialApplications_DifferOnTheSameInput()
    {
        Assert.Equal(53, Scoring.Urgent(Drill));    // 3 * (6 - 5) + 50 bonus (price 25000 >= 10000)
        Assert.Equal(1, Scoring.Economy(Drill));    // 1 * (6 - 5) + 0   (price 25000 < 50000)
        Assert.Equal(24, Scoring.Urgent(Hammer));   // 3 * 8, no bonus (1500 < 10000)
        Assert.Equal(8, Scoring.Economy(Hammer));

        // the curried form: configuration first, product in a second call
        Assert.Equal(53, Scoring.CreateScore(new ScoreConfig(3, 10_000, 50))(Drill));

        // the threshold is inclusive
        Assert.Equal(53, Scoring.Urgent(Drill with { UnitPriceMinor = 10_000 }));
        Assert.Equal(3, Scoring.Urgent(Drill with { UnitPriceMinor = 9_999 }));
    }

    [Fact]
    public void ConfiguredScores_PassedIntoThePipeline_ChangeTheOrder()
    {
        var urgent = RestockPipeline.Run(AllProducts(), Scoring.Urgent);
        var economy = RestockPipeline.Run(AllProducts(), Scoring.Economy);

        Assert.Equal(new[] { "B-200", "A-200", "A-100", "B-100" }, Skus(urgent));
        Assert.Equal(new[] { "B-200", "A-100", "A-200", "B-100" }, Skus(economy));
        Assert.NotEqual(Skus(urgent), Skus(economy));
    }

    // ================= DEFERRED EXECUTION =================
    [Fact]
    public void Deferred_NothingHappensBeforeEnumeration_AndOneOrderPerStep()
    {
        var probe = new Probe<OrderRequest?>(new OrderRequest?[] { new("A-100", 2), new("A-200", 1), new("Z-999", 1) });
        var fees = new CountingFee();
        var catalog = NewCatalog();

        var lazy = OrderWorkflow.ProcessLazily(probe, catalog, fees, At);

        Assert.Equal(0, probe.Enumerations);   // creating the enumerable did not start it
        Assert.Equal(0, probe.Pulled);
        Assert.Equal(0, fees.Calls);

        var first = lazy.First();              // iteration triggers the processing of ONE order

        Assert.Equal(1, probe.Pulled);
        Assert.Equal(1, fees.Calls);
        Assert.IsType<Success<OrderReceipt>>(first.Outcome.Result);
        Assert.Equal(new[] { 0, 5, 10, 0, 50, 30 }, Stocks(first.CatalogAfter));
        Assert.Equal(new[] { 2, 5, 10, 0, 50, 30 }, Stocks(catalog));   // the original catalog is untouched
    }

    [Fact]
    public void Deferred_ArgumentsAreCheckedImmediately_NotAtFirstIteration()
    {
        Assert.Throws<ArgumentNullException>(() => OrderWorkflow.ProcessLazily(null!, NewCatalog(), TestFees.None, At));
        Assert.Throws<ArgumentNullException>(() => OrderWorkflow.ProcessLazily(Array.Empty<OrderRequest?>(), NewCatalog(), null!, At));
    }

    [Fact]
    public void Materialisation_RepeatedEnumeration_BeforeAndAfter()
    {
        var requests = new OrderRequest?[] { new("A-100", 2), new("A-200", 1), new("Z-999", 1) };

        // BEFORE materialisation: every enumeration reads the source and processes the orders again
        var probeBefore = new Probe<OrderRequest?>(requests);
        var lazy = OrderWorkflow.ProcessLazily(probeBefore, NewCatalog(), TestFees.None, At);
        lazy.ToArray();
        lazy.ToArray();
        Assert.Equal(2, probeBefore.Enumerations);
        Assert.Equal(6, probeBefore.Pulled);

        // AFTER materialisation (ProcessAll): the source is read once; the snapshot can be reused any number of times
        var probeAfter = new Probe<OrderRequest?>(requests);
        var batch = OrderWorkflow.ProcessAll(probeAfter, NewCatalog(), TestFees.None, At);
        var firstUse = batch.Outcomes.Select(o => o.Result is Success<OrderReceipt>).ToArray();
        var secondUse = batch.Outcomes.Select(o => o.Result is Success<OrderReceipt>).ToArray();

        Assert.Equal(1, probeAfter.Enumerations);
        Assert.Equal(3, probeAfter.Pulled);
        Assert.Equal(new[] { true, true, false }, firstUse);
        Assert.Equal(firstUse, secondUse);
        Assert.Equal(new[] { 0, 4, 10, 0, 50, 30 }, Stocks(batch.Catalog));
    }

    // ================= RESOURCE LIFETIME =================
    [Fact]
    public void Save_Success_ReleasesStream_WritesFile_AndLeavesNoTemporaryFile()
    {
        var path = Path.Combine(_dir, "out", "report.csv");   // the folder does not exist yet
        TrackedFileStream? tracked = null;

        var result = ReportFile.Save(path, "a,b\n1,2\n", file => tracked = new TrackedFileStream(file));

        Assert.IsType<Success<string>>(result);
        Assert.NotNull(tracked);
        Assert.True(tracked.Disposed);                         // owned resource released after success
        Assert.Equal("a,b\n1,2\n", File.ReadAllText(path));
        Assert.False(File.Exists(path + ".tmp"));
        File.Delete(path);                                     // would fail on Windows if the handle were still open
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Save_FailureWhileWriting_ReleasesStream_RemovesTempFile_AndKeepsTheOldFile()
    {
        var path = Write("report.csv", "old content");
        TrackedFileStream? tracked = null;

        var result = ReportFile.Save(path, "new content", file => tracked = new TrackedFileStream(file) { ThrowOnWrite = true });

        var failure = Assert.IsType<Failure<string>>(result);
        Assert.Equal(ErrorCodes.IoError, failure.Error.Code);
        Assert.Contains(path, failure.Error.Message);
        Assert.NotNull(tracked);
        Assert.True(tracked.Disposed);                         // owned resource released after the exception path
        Assert.False(File.Exists(path + ".tmp"));
        Assert.Equal("old content", File.ReadAllText(path));   // no partial update
    }

    [Fact]
    public void Save_ReplacesAnExistingFile_Atomically()
    {
        var path = Write("report.csv", "first");

        var result = ReportFile.Save(path, "second", null);

        Assert.IsType<Success<string>>(result);
        Assert.Equal("second", File.ReadAllText(path));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void Load_ConvenienceApi_ReleasesTheFile_AfterSuccessAndAfterFailure()
    {
        var good = Write("good.json", "[]");
        var bad = Write("bad.json", "{ not json");

        Assert.IsType<Success<IReadOnlyList<OrderRequest?>>>(DataFile.Load<IReadOnlyList<OrderRequest?>>(good, OrderInput.ParseJson));
        Assert.IsType<Failure<IReadOnlyList<OrderRequest?>>>(DataFile.Load<IReadOnlyList<OrderRequest?>>(bad, OrderInput.ParseJson));

        File.Delete(good);   // observable cleanup: ReadAllText left no open handle, even on the failure path
        File.Delete(bad);
        Assert.False(File.Exists(good));
        Assert.False(File.Exists(bad));
    }

    // ================= NATIVE INTEGRATION =================
    [Fact]
    public void Native_RealCall_ReturnsTheManuallyCalculatedFee()
    {
        var native = new NativeFeeCalculator();   // calls the real C library

        Assert.Equal(410, Assert.IsType<Success<long>>(native.Calculate(10000, 3)).Value);        // 100 + 20*3 + 250
        Assert.Equal(371, Assert.IsType<Success<long>>(native.Calculate(10020, 1)).Value);        // 2.5% = 250.5 -> rounds half up to 251
        Assert.Equal(370, Assert.IsType<Success<long>>(native.Calculate(10019, 1)).Value);        // 250.475 -> 250
        Assert.Equal(120, Assert.IsType<Success<long>>(native.Calculate(0, 1)).Value);            // lower bound
        Assert.Equal(25_200_100, Assert.IsType<Success<long>>(native.Calculate(1_000_000_000, 10_000)).Value);   // upper bound
    }

    [Fact]
    public void Adapter_RejectsInvalidInput_BeforeCallingNative()
    {
        var calls = 0;
        var adapter = new NativeFeeCalculator((total, quantity) => { calls++; return 1; });

        foreach (var (total, quantity) in new[] { (-1L, 1), (1_000_000_001L, 1), (100L, 0), (100L, 10_001) })
        {
            var failure = Assert.IsType<Failure<long>>(adapter.Calculate(total, quantity));
            Assert.Equal(ErrorCodes.InvalidFeeInput, failure.Error.Code);
        }
        Assert.Equal(0, calls);   // the native function was never called
    }

    [Fact]
    public void Adapter_LoadingAndBindingFailures_BecomeClearFailures()
    {
        // a binding to a library that does not exist: a real, fresh load failure
        var missing = Assert.IsType<Failure<long>>(new NativeFeeCalculator(MissingLibrary.CalcHandlingFee).Calculate(10000, 3));
        Assert.Equal(ErrorCodes.NativeUnavailable, missing.Error.Code);
        Assert.Contains("could not be loaded", missing.Error.Message);

        // a binding to the real library with a wrong function name
        var wrongName = Assert.IsType<Failure<long>>(new NativeFeeCalculator(WrongEntryPoint.Call).Calculate(10000, 3));
        Assert.Equal(ErrorCodes.NativeUnavailable, wrongName.Error.Code);

        // the native function answering -1
        var rejected = Assert.IsType<Failure<long>>(new NativeFeeCalculator((total, quantity) => -1).Calculate(100, 1));
        Assert.Equal(ErrorCodes.NativeRejected, rejected.Error.Code);
    }

    // ================= COMPLETE WORKFLOWS =================
    [Fact]
    public void Workflow_NormalPath_ReportsAndRealNativeCall_ThenExport()
    {
        var intake = ProductInput.ValidateAll(ProductInput.ParseJson(ProductsJson));
        var catalog = Catalog.FromProducts(intake.Valid);
        var purchasing = Reports.PurchasingList(intake.Valid);
        var value = Reports.ValueByCategory(intake.Valid);

        var batch = OrderWorkflow.ProcessAll(OrderInput.ParseJson(OrdersJson), catalog, new NativeFeeCalculator(), At);   // REAL native fee

        var summaries = batch.Outcomes.Select(o => o.Result.Match(r => r.Summary, e => e.Code)).ToArray();
        Assert.Equal(new[]
        {
            "CONFIRMED 2 x A-100, total=3000, fee=215",       // 100 + 40 + 75
            "CONFIRMED 5 x A-200, total=125000, fee=3325",    // 100 + 100 + 3125
            ErrorCodes.InsufficientStock,
            ErrorCodes.ItemNotFound,
            ErrorCodes.InvalidQuantity,
            "CONFIRMED 40 x C-100, total=16000, fee=1300",    // 100 + 800 + 400
            ErrorCodes.InsufficientStock,
            ErrorCodes.SkuRequired,
        }, summaries);
        Assert.Equal(new[] { 0, 0, 10, 0, 10, 30 }, Stocks(batch.Catalog));

        // export both reports
        var p1 = ReportFile.Save(Path.Combine(_dir, "purchasing_list.csv"), ReportFormats.PurchasingCsv(purchasing));
        var p2 = ReportFile.Save(Path.Combine(_dir, "value_by_category.csv"), ReportFormats.ValueByCategoryCsv(value));
        Assert.IsType<Success<string>>(p1);
        Assert.IsType<Success<string>>(p2);
        Assert.Equal("sku,name,quantity,reorder_level,deficit\nB-200,Screws,0,20,20\nA-100,Hammer,2,10,8\nA-200,Drill,5,6,1\nB-100,Nails box,10,10,0\n",
            File.ReadAllText(Path.Combine(_dir, "purchasing_list.csv")));
        Assert.Equal("category,products,units,value_minor\nTOOLS,2,7,128000\nSUPPLIES,2,80,30500\nHARDWARE,1,10,3000\n",
            File.ReadAllText(Path.Combine(_dir, "value_by_category.csv")));
    }

    [Fact]
    public void Workflow_FailurePath_NativeUnavailable_KeepsStateConsistent_AndReportsTheProblem()
    {
        var catalog = NewCatalog();
        var before = Stocks(catalog);
        var unavailable = new NativeFeeCalculator(MissingLibrary.CalcHandlingFee);

        var batch = OrderWorkflow.ProcessAll(OrderInput.ParseJson(OrdersJson), catalog, unavailable, At);

        var codes = batch.Outcomes.Select(o => o.Result.Match(_ => "OK", e => e.Code)).ToArray();
        Assert.Equal(new[]
        {
            ErrorCodes.NativeUnavailable, ErrorCodes.NativeUnavailable, ErrorCodes.InsufficientStock, ErrorCodes.ItemNotFound,
            ErrorCodes.InvalidQuantity, ErrorCodes.NativeUnavailable, ErrorCodes.NativeUnavailable, ErrorCodes.SkuRequired,
        }, codes);
        Assert.Equal(before, Stocks(batch.Catalog));   // no order was confirmed without its fee
        Assert.Equal(before, Stocks(catalog));

        // a failed export keeps the previous report file
        var path = Write("report.csv", "previous report");
        var export = ReportFile.Save(path, "new report", file => new TrackedFileStream(file) { ThrowOnWrite = true });
        Assert.Equal(ErrorCodes.IoError, Assert.IsType<Failure<string>>(export).Error.Code);
        Assert.Equal("previous report", File.ReadAllText(path));
    }

    [Fact]
    public void Chain_OrderLimit_IsInclusive_AndStopsBeforeTheNativeCall()
    {
        var big = new Product("BIG", "Big", "X", 5, 50_000_000, 1);
        var catalog = Catalog.FromProducts(new[] { big });
        var fees = new CountingFee();

        var atLimit = OrderWorkflow.Process(new OrderRequest("BIG", 2), catalog, fees, At);        // 100 000 000 = limit
        Assert.IsType<Success<OrderReceipt>>(atLimit);
        Assert.Equal(1, fees.Calls);

        var overLimit = OrderWorkflow.Process(new OrderRequest("BIG", 3), catalog, fees, At);      // 150 000 000 > limit
        var failure = Assert.IsType<Failure<OrderReceipt>>(overLimit);
        Assert.Equal(ErrorCodes.OrderTooLarge, failure.Error.Code);
        Assert.Equal(1, fees.Calls);                       // the fee was not calculated for the rejected order
    }

    [Fact]
    public void Csv_FieldsWithCommasAndQuotes_AreEscaped()
    {
        var tricky = Hammer with { Sku = "A", Name = "Nuts, \"big\"", Quantity = 0, ReorderLevel = 1 };

        var csv = ReportFormats.PurchasingCsv(Reports.PurchasingList(new[] { tricky }));

        Assert.Equal("sku,name,quantity,reorder_level,deficit\nA,\"Nuts, \"\"big\"\"\",0,1,1\n", csv);
    }
}
