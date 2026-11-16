namespace StockPilot;

public static class Program
{
    /// <summary>Only the real console, the real clock and the real native library are connected here.</summary>
    public static int Main(string[] args) =>
        Application.Run(
            args, AppContext.BaseDirectory, Console.Out, Console.Error,
            () => DateTimeOffset.UtcNow, new NativeFeeCalculator());
}
