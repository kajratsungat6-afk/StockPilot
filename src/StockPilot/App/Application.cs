namespace StockPilot;

/// <summary>The application boundary. The final outcome is handled ONCE here. The clock, the fee calculator and the
/// output writers are passed in, so the whole application can be run in a test without a real console.</summary>
public static class Application
{
    public static int Run(
        string[] args, string baseDirectory, TextWriter output, TextWriter error,
        Func<DateTimeOffset> clock, IFeeCalculator fees)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(fees);

        return CommandLine.Parse(args, Path.Combine(baseDirectory, "data")).Match(
            options => options.Command == CommandLine.Help
                ? Help(output)
                : AppFlow.Load(
                        options.ProductsPath,
                        CommandLine.NeedsOrders(options.Command) ? options.OrdersPath : null,
                        CommandLine.NeedsDeliveries(options.Command) ? options.DeliveriesPath : null)
                    .Match(
                        data => Execute(options, data, output, clock(), fees),
                        failure => Fail(error, failure)),
            failure =>
            {
                error.WriteLine(CommandLine.Usage);
                return Fail(error, failure);
            });
    }

    private static int Help(TextWriter output)
    {
        output.WriteLine(CommandLine.Usage);
        return 0;
    }

    private static int Fail(TextWriter error, Error failure)
    {
        error.WriteLine($"ERROR [{failure.Code}] {failure.Message}");
        return 1;
    }

    private static int Execute(AppOptions options, LoadedData data, TextWriter w, DateTimeOffset at, IFeeCalculator fees)
    {
        var intake = ProductInput.ValidateAll(data.Products);
        Views.Intake(w, intake);

        return options.Command switch
        {
            CommandLine.Restock => RestockCommand(w, intake),
            CommandLine.Reports => ReportsCommand(w, intake, options.OutputDir),
            CommandLine.Orders => OrdersCommand(w, intake, data, options.OutputDir, at, fees),
            CommandLine.Deliveries => DeliveriesCommand(w, intake, data, options.OutputDir),
            _ => DemoCommand(w, intake, data, options.OutputDir, at, fees),
        };
    }

    // ---- restock pipeline: four policies (two plain, two partially applied from the curried score) ----
    private static int RestockCommand(TextWriter w, IntakeResult intake)
    {
        foreach (var (name, fn) in Views.NamedPolicies)
            Views.RestockPlan(w, name, RestockPipeline.Run(intake.Valid, fn));
        return 0;
    }

    // ---- two LINQ reports (pure), then export (boundary) ----
    private static int ReportsCommand(TextWriter w, IntakeResult intake, string outputDir)
    {
        var purchasing = Reports.PurchasingList(intake.Valid);
        var value = Reports.ValueByCategory(intake.Valid);
        Views.PurchasingList(w, purchasing);
        Views.ValueByCategory(w, value);
        return ExportReports(w, purchasing, value, outputDir);
    }

    // ---- order workflow: validate -> limit -> native fee -> confirm -> receipt ----
    private static int OrdersCommand(
        TextWriter w, IntakeResult intake, LoadedData data, string outputDir, DateTimeOffset at, IFeeCalculator fees)
    {
        var catalog = Catalog.FromProducts(intake.Valid);
        Views.NativeCheck(w, fees);
        Views.StockLookup(w, catalog);
        var batch = OrderWorkflow.ProcessAll(data.Orders, catalog, fees, at);
        Views.Orders(w, batch);
        Views.Stock(w, "Stock after orders", batch.Catalog);
        Views.InvalidSequences(w, batch, at);
        return SaveStock(w, batch.Catalog, outputDir);
    }

    // ---- delivery workflow: validate -> apply to stock -> receipt ----
    private static int DeliveriesCommand(TextWriter w, IntakeResult intake, LoadedData data, string outputDir)
    {
        var catalog = Catalog.FromProducts(intake.Valid);
        var batch = DeliveryWorkflow.ReceiveAll(data.Deliveries, catalog, DeliveryLedger.Empty);
        Views.Deliveries(w, batch);
        Views.Stock(w, "Stock after deliveries", batch.Catalog);
        return SaveStock(w, batch.Catalog, outputDir);
    }

    // ---- everything: restock -> reports -> orders -> export -> deliveries -> updated stock saved ----
    private static int DemoCommand(
        TextWriter w, IntakeResult intake, LoadedData data, string outputDir, DateTimeOffset at, IFeeCalculator fees)
    {
        RestockCommand(w, intake);

        var purchasing = Reports.PurchasingList(intake.Valid);
        var value = Reports.ValueByCategory(intake.Valid);
        Views.PurchasingList(w, purchasing);
        Views.ValueByCategory(w, value);

        var catalog = Catalog.FromProducts(intake.Valid);
        Views.NativeCheck(w, fees);
        Views.StockLookup(w, catalog);

        var orders = OrderWorkflow.ProcessAll(data.Orders, catalog, fees, at);
        Views.Orders(w, orders);
        Views.Stock(w, "Stock after orders", orders.Catalog);
        Views.InvalidSequences(w, orders, at);

        // the same restock question again, now on the stock left by the orders: the loop restock -> order -> deliver
        var deliveries = DeliveryWorkflow.ReceiveAll(data.Deliveries, orders.Catalog, DeliveryLedger.Empty);
        Views.Deliveries(w, deliveries);
        Views.Stock(w, "Stock after deliveries", deliveries.Catalog);
        var afterOrders = Reports.PurchasingList(orders.Catalog.Products);
        var afterDeliveries = Reports.PurchasingList(deliveries.Catalog.Products);
        w.WriteLine();
        w.WriteLine("=== Purchasing list: total deficit before and after the deliveries ===");
        w.WriteLine($"  after orders: {afterOrders.TotalDeficit}, after deliveries: {afterDeliveries.TotalDeficit}");

        var exit = ExportReports(w, purchasing, value, outputDir);
        return Math.Max(exit, SaveStock(w, deliveries.Catalog, outputDir));
    }

    private static int ExportReports(TextWriter w, PurchasingReport purchasing, ValueReport value, string outputDir)
    {
        w.WriteLine();
        w.WriteLine($"=== Export to '{outputDir}' ===");
        var exitCode = 0;
        var exports = new (string File, string Csv)[]
        {
            ("purchasing_list.csv", ReportFormats.PurchasingCsv(purchasing)),
            ("value_by_category.csv", ReportFormats.ValueByCategoryCsv(value)),
        };
        foreach (var (file, csv) in exports)
        {
            var saved = ReportFile.Save(Path.Combine(outputDir, file), csv);
            Views.SavedOrFailed(w, saved, "exported");
            if (saved is Failure<string>) exitCode = 1;
        }
        return exitCode;
    }

    /// <summary>Saves the updated stock in the same JSON schema as the input, so it can be loaded again.</summary>
    private static int SaveStock(TextWriter w, Catalog catalog, string outputDir)
    {
        w.WriteLine();
        w.WriteLine($"=== Save updated stock to '{outputDir}' ===");
        var saved = ReportFile.Save(Path.Combine(outputDir, "products_after.json"), ProductFormats.ToJson(catalog.Products));
        Views.SavedOrFailed(w, saved, "saved");
        return saved is Failure<string> ? 1 : 0;
    }
}
