namespace StockPilot;

public static class Program
{
    public static int Main(string[] args) =>
        Application.Run(
            args, AppContext.BaseDirectory, Console.Out, Console.Error,
            () => DateTimeOffset.UtcNow, new NativeFeeCalculator());
}
