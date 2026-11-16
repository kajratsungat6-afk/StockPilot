namespace StockPilot;

/// <summary>What the person asked for: a command and the file paths it should use.</summary>
public sealed record AppOptions(string Command, string ProductsPath, string OrdersPath, string DeliveriesPath, string OutputDir);

/// <summary>Pure command-line parsing: no Console, no exceptions. Wrong arguments become a Failure(InvalidArguments).</summary>
public static class CommandLine
{
    public const string Demo = "demo";
    public const string Restock = "restock";
    public const string Reports = "reports";
    public const string Orders = "orders";
    public const string Deliveries = "deliveries";
    public const string Help = "help";

    private static readonly ImmutableArray<string> Commands =
        ImmutableArray.Create(Demo, Restock, Reports, Orders, Deliveries, Help);

    public const string Usage = """
        Usage: StockPilot [command] [options]

        Commands:
          demo         everything in one run (default)
          restock      restock plans under four policies
          reports      two LINQ reports, exported as CSV
          orders       process orders (native handling fee), save the stock afterwards
          deliveries   receive deliveries, save the stock afterwards
          help         show this text

        Options (each takes a value):
          --products <file>     default: data/products.json
          --orders <file>       default: data/orders.json
          --deliveries <file>   default: data/deliveries.json
          --out <folder>        default: output
        """;

    public static Result<AppOptions> Parse(string[] args, string dataDirectory)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(dataDirectory);

        var command = Demo;
        var rest = args.AsSpan();
        if (rest.Length > 0 && !rest[0].StartsWith("--", StringComparison.Ordinal))
        {
            command = rest[0];
            rest = rest[1..];
        }

        if (command == "--help" || command == "-h") command = Help;
        if (!Commands.Contains(command))
            return Result.Fail<AppOptions>(ErrorCodes.InvalidArguments, $"Unknown command '{command}'.");

        var products = Path.Combine(dataDirectory, "products.json");
        var orders = Path.Combine(dataDirectory, "orders.json");
        var deliveries = Path.Combine(dataDirectory, "deliveries.json");
        var output = "output";

        for (var i = 0; i < rest.Length; i += 2)
        {
            var option = rest[i];
            if (option == "--help") { command = Help; continue; }
            if (i + 1 >= rest.Length)
                return Result.Fail<AppOptions>(ErrorCodes.InvalidArguments, $"Option '{option}' needs a value.");

            var value = rest[i + 1];
            switch (option)
            {
                case "--products": products = value; break;
                case "--orders": orders = value; break;
                case "--deliveries": deliveries = value; break;
                case "--out": output = value; break;
                default:
                    return Result.Fail<AppOptions>(ErrorCodes.InvalidArguments, $"Unknown option '{option}'.");
            }
        }

        return Result.Ok(new AppOptions(command, products, orders, deliveries, output));
    }

    /// <summary>Which files a command reads. A command never opens a file it does not need.</summary>
    public static bool NeedsOrders(string command) => command is Demo or Orders;

    public static bool NeedsDeliveries(string command) => command is Demo or Deliveries;
}
