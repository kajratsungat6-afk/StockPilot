namespace StockPilot;

/// <summary>Native binding. Library "stockpilot_native" is resolved next to the application
/// (stockpilot_native.dll / libstockpilot_native.so). Cdecl matches the C export calc_handling_fee(int32_t, int32_t) -> int32_t.</summary>
internal static class NativeMethods
{
    [DllImport("stockpilot_native", EntryPoint = "calc_handling_fee", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int CalcHandlingFee(int orderTotalMinor, int quantity);
}
