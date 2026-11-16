using System.Collections;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using StockPilot;
using Xunit;

namespace StockPilot.Tests;

// ---------------------------------------------------------------------
// Test helpers (test doubles are used ONLY where a real native call is not the point of the test)
// ---------------------------------------------------------------------
internal sealed class FixedFee : IFeeCalculator
{
    private readonly long _fee;
    public FixedFee(long fee) => _fee = fee;
    public Result<long> Calculate(long orderTotalMinor, int quantity) => Result.Ok(_fee);
}

internal static class TestFees
{
    public static readonly IFeeCalculator None = new FixedFee(0);
}

internal sealed class CountingFee : IFeeCalculator
{
    public int Calls { get; private set; }
    public Result<long> Calculate(long orderTotalMinor, int quantity)
    {
        Calls++;
        return Result.Ok(10L);
    }
}

/// <summary>Real bindings that fail to load: genuine fresh failures (not mocks).</summary>
internal static class MissingLibrary
{
    [DllImport("stockpilot_library_that_does_not_exist", EntryPoint = "calc_handling_fee", CallingConvention = CallingConvention.Cdecl)]
    public static extern int CalcHandlingFee(int orderTotalMinor, int quantity);
}

internal static class WrongEntryPoint
{
    [DllImport("stockpilot_native", EntryPoint = "no_such_function", CallingConvention = CallingConvention.Cdecl)]
    public static extern int Call(int orderTotalMinor, int quantity);
}

/// <summary>Test probe: counts how often the sequence is enumerated and how many items are pulled from it.
/// The counting lives in the test, not in production code.</summary>
internal sealed class Probe<T> : IEnumerable<T>
{
    private readonly IEnumerable<T> _items;
    public int Enumerations { get; private set; }
    public int Pulled { get; private set; }

    public Probe(IEnumerable<T> items) => _items = items;

    public IEnumerator<T> GetEnumerator()
    {
        Enumerations++;
        foreach (var item in _items)
        {
            Pulled++;
            yield return item;
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

internal sealed class TrackedFileStream : FileStream
{
    public bool Disposed { get; private set; }
    public bool ThrowOnWrite { get; set; }

    public TrackedFileStream(string path) : base(path, FileMode.Create, FileAccess.Write, FileShare.None) { }

    protected override void Dispose(bool disposing)
    {
        Disposed = true;
        base.Dispose(disposing);
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        if (ThrowOnWrite) throw new IOException("simulated write failure");
        base.Write(buffer);
    }

    public override void Write(byte[] array, int offset, int count)
    {
        if (ThrowOnWrite) throw new IOException("simulated write failure");
        base.Write(array, offset, count);
    }
}
