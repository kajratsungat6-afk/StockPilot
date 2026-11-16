namespace StockPilot;

public interface IFeeCalculator
{
    Result<long> Calculate(long orderTotalMinor, int quantity);
}


public sealed class NativeFeeCalculator : IFeeCalculator
{
    public const long MaxTotalMinor = 1_000_000_000;
    public const int MaxQuantity = 10_000;

    private readonly Func<int, int, int> _call;

    public NativeFeeCalculator() : this(NativeMethods.CalcHandlingFee) { }

    public NativeFeeCalculator(Func<int, int, int> call)
    {
        ArgumentNullException.ThrowIfNull(call);
        _call = call;
    }

    public Result<long> Calculate(long orderTotalMinor, int quantity)
    {
        if (orderTotalMinor < 0 || orderTotalMinor > MaxTotalMinor)
            return Result.Fail<long>(ErrorCodes.InvalidFeeInput, $"Order total {orderTotalMinor} is outside 0..{MaxTotalMinor}.");
        if (quantity < 1 || quantity > MaxQuantity)
            return Result.Fail<long>(ErrorCodes.InvalidFeeInput, $"Quantity {quantity} is outside 1..{MaxQuantity}.");

        int fee;
        try
        {
            fee = _call((int)orderTotalMinor, quantity);
        }
        catch (DllNotFoundException ex)
        {
            return Result.Fail<long>(ErrorCodes.NativeUnavailable, $"Native library could not be loaded: {ex.Message}");
        }
        catch (EntryPointNotFoundException ex)
        {
            return Result.Fail<long>(ErrorCodes.NativeUnavailable, $"Native function not found in the library: {ex.Message}");
        }
        catch (BadImageFormatException ex)
        {
            return Result.Fail<long>(ErrorCodes.NativeUnavailable, $"Native library has the wrong architecture: {ex.Message}");
        }

        return fee < 0
            ? Result.Fail<long>(ErrorCodes.NativeRejected, $"Native library rejected the input (total {orderTotalMinor}, quantity {quantity}).")
            : Result.Ok((long)fee);
    }
}
