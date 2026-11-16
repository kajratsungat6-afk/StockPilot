namespace StockPilot;

/// <summary>Pure text formatting of reports (CSV). Writing the text to a file happens in ReportFile.</summary>
public static class ReportFormats
{
    public static string PurchasingCsv(PurchasingReport report)
    {
        var sb = new StringBuilder("sku,name,quantity,reorder_level,deficit\n");
        foreach (var l in report.Lines)
            sb.Append(FormattableString.Invariant($"{Field(l.Sku)},{Field(l.Name)},{l.Quantity},{l.ReorderLevel},{l.Deficit}\n"));
        return sb.ToString();
    }

    public static string ValueByCategoryCsv(ValueReport report)
    {
        var sb = new StringBuilder("category,products,units,value_minor\n");
        foreach (var r in report.Rows)
            sb.Append(FormattableString.Invariant($"{Field(r.CategoryId)},{r.ProductCount},{r.TotalUnits},{r.TotalValueMinor}\n"));
        return sb.ToString();
    }

    private static string Field(string value) =>
        value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
}
