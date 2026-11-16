namespace StockPilot;


internal static class NativeMethods
{
    [DllImport("stockpilot_native", EntryPoint = "calc_handling_fee", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int CalcHandlingFee(int orderTotalMinor, int quantity);
}
