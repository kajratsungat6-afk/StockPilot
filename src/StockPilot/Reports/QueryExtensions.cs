namespace StockPilot;

public static class QueryExtensions
{
    /// <summary>Receiver: IEnumerable&lt;Product&gt;. Returns the products at or below their reorder level
    /// (Quantity &lt;= ReorderLevel; the threshold is included). Lazy (a LINQ Where), does not change the source.
    /// Belongs on the product sequence because "low stock" is a domain question about a collection of products;
    /// it is shared by the restock pipeline (stage 1) and the purchasing report.</summary>
    public static IEnumerable<Product> LowStock(this IEnumerable<Product> products)
    {
        ArgumentNullException.ThrowIfNull(products);
        return products.Where(p => p.Quantity <= p.ReorderLevel);
    }
}
