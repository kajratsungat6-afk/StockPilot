using System.Collections;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using StockPilot;
using Xunit;

namespace StockPilot.Tests;

public class PipelineTests
{
    // Same valid rows as data/products.json.
    private static readonly Product Hammer = new("A-100", "Hammer", "TOOLS", 2, 1500, 10);
    private static readonly Product Drill  = new("A-200", "Drill", "TOOLS", 5, 25000, 6);
    private static readonly Product Nails  = new("B-100", "Nails box", "HARDWARE", 10, 300, 10); // boundary
    private static readonly Product Screws = new("B-200", "Screws", "HARDWARE", 0, 200, 20);     // qty 0 is legitimate
    private static readonly Product Tape   = new("C-100", "Tape", "SUPPLIES", 50, 400, 10);
    private static readonly Product Glue   = new("C-200", "Glue", "SUPPLIES", 30, 350, 5);

    private static Product[] Dataset() => new[] { Hammer, Drill, Nails, Screws, Tape, Glue };

    private static string[] Skus(RestockPlan p) => p.Items.Select(i => i.Sku).ToArray();

    [Fact]
    public void NormalWorkflow_Conservative_ProducesDocumentedPlan()
    {
        var plan = RestockPipeline.Run(Dataset(), Policies.Conservative);

        Assert.Equal(new[] { "B-200", "A-100", "A-200", "B-100" }, Skus(plan));
        Assert.Equal(new long[] { 20, 8, 1, 0 }, plan.Items.Select(i => i.Score).ToArray());
        Assert.Equal(new[] { 40, 18, 7, 10 }, plan.Items.Select(i => i.SuggestedOrderQty).ToArray());
        Assert.Equal(75, plan.TotalSuggestedUnits);
    }

    [Fact]
    public void EmptyDataset_ProducesEmptyPlan()
    {
        var plan = RestockPipeline.Run(Array.Empty<Product>(), Policies.Conservative);

        Assert.Empty(plan.Items);
        Assert.Equal(0, plan.TotalSuggestedUnits);
    }

    [Fact]
    public void Boundary_QuantityEqualToReorderLevel_IsIncluded_AndOneAboveIsNot()
    {
        var atLimit = Hammer with { Sku = "X-1", Quantity = 10, ReorderLevel = 10 };
        var above = Hammer with { Sku = "X-2", Quantity = 11, ReorderLevel = 10 };

        var selected = Stages.SelectRestockCandidates(new[] { atLimit, above });

        Assert.Equal(new[] { "X-1" }, selected.Select(p => p.Sku).ToArray());
    }

    [Fact]
    public void ZeroValues_AreValidData_NotMissingInput()
    {
        var raw = new RawProduct("S-1", "Screws", "HARDWARE", 0, 0L, 0);

        var ok = ProductInput.TryValidate(raw, out var product, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.NotNull(product);
        Assert.Equal(0, product.Quantity);
        Assert.Equal(0, product.UnitPriceMinor);
    }

    [Theory]
    [InlineData(null, "Hammer", "TOOLS", 1, 100L, 5, "Sku is required.")]
    [InlineData("  ", "Hammer", "TOOLS", 1, 100L, 5, "Sku is required.")]
    [InlineData("A", "Hammer", "TOOLS", null, 100L, 5, "Quantity is required.")]
    [InlineData("A", "Hammer", "TOOLS", -1, 100L, 5, "Quantity must be >= 0.")]
    [InlineData("A", "Hammer", "TOOLS", 1, -500L, 5, "Unit price must be >= 0.")]
    [InlineData("A", "Hammer", "TOOLS", 1, 100L, null, "Reorder level is required.")]
    public void InvalidFields_AreRejected_WithReason(
        string? sku, string? name, string? cat, int? qty, long? price, int? level, string expected)
    {
        var ok = ProductInput.TryValidate(new RawProduct(sku, name, cat, qty, price, level), out var product, out var error);

        Assert.False(ok);
        Assert.Null(product);
        Assert.Equal(expected, error);
    }

    [Fact]
    public void NullRecord_AndNullDataset_AreRejected()
    {
        Assert.False(ProductInput.TryValidate(null, out var p, out var err));
        Assert.Null(p);
        Assert.NotNull(err);
        Assert.Throws<ArgumentNullException>(() => ProductInput.ValidateAll(null!));
        Assert.Throws<ArgumentNullException>(() => RestockPipeline.Run(null!, Policies.Conservative));
    }

    [Fact]
    public void Json_ValidAndInvalidRows_AreSeparated_AndEmptyArrayIsValid()
    {
        const string json = """
        [
          { "sku": "A-100", "name": "Hammer", "categoryId": "TOOLS", "quantity": 2, "unitPriceMinor": 1500, "reorderLevel": 10 },
          { "sku": "D-100", "name": "Saw", "categoryId": "TOOLS", "quantity": 3, "unitPriceMinor": -500, "reorderLevel": 5 },
          { "name": "Pliers", "categoryId": "TOOLS", "quantity": 1, "unitPriceMinor": 900, "reorderLevel": 5 }
        ]
        """;

        var result = ProductInput.ValidateAll(ProductInput.ParseJson(json));

        Assert.Single(result.Valid);
        Assert.Equal(new[] { 2, 3 }, result.Issues.Select(i => i.Row).ToArray());

        var empty = ProductInput.ValidateAll(ProductInput.ParseJson("[]"));
        Assert.Empty(empty.Valid);
        Assert.Empty(empty.Issues);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("[ { \"sku\": \"A\", \"quantity\": \"abc\" } ]")]
    [InlineData("null")]
    public void MalformedJson_IsReportedAsJsonException(string json)
    {
        Assert.Throws<JsonException>(() => ProductInput.ParseJson(json));
    }

    [Fact]
    public void TwoPolicies_SameInput_DifferentOrder()
    {
        var data = Dataset();

        var conservative = RestockPipeline.Run(data, Policies.Conservative);
        var weighted = RestockPipeline.Run(data, Policies.ValueWeighted);

        Assert.Equal(new[] { "B-200", "A-100", "A-200", "B-100" }, Skus(conservative));
        Assert.Equal(new[] { "A-200", "A-100", "B-200", "B-100" }, Skus(weighted));
        Assert.Equal(new long[] { 250, 120, 40, 0 }, weighted.Items.Select(i => i.Score).ToArray());
        Assert.NotEqual(Skus(conservative), Skus(weighted));
    }

    [Fact]
    public void TiedScores_AreOrderedBySku_RegardlessOfInputOrder()
    {
        var a = Hammer with { Sku = "A", Quantity = 0, ReorderLevel = 5 };
        var b = Hammer with { Sku = "B", Quantity = 0, ReorderLevel = 5 };

        var p1 = RestockPipeline.Run(new[] { b, a }, Policies.Conservative);
        var p2 = RestockPipeline.Run(new[] { a, b }, Policies.Conservative);

        Assert.Equal(new[] { "A", "B" }, Skus(p1));
        Assert.Equal(Skus(p1), Skus(p2));
    }

    [Fact]
    public void Stages_DoNotMutateInput_AndAreRepeatable()
    {
        var data = Dataset();
        var snapshot = data.ToArray();

        var first = RestockPipeline.Run(data, Policies.Conservative);
        var second = RestockPipeline.Run(data, Policies.Conservative);

        Assert.Equal(snapshot.Length, data.Length);
        Assert.True(snapshot.SequenceEqual(data));
        Assert.Equal(Skus(first), Skus(second));
        Assert.Equal(first.TotalSuggestedUnits, second.TotalSuggestedUnits);
    }

    [Fact]
    public void ScoreStage_InvokesDelegate_OncePerCandidate()
    {
        var calls = 0;
        var scored = Stages.ScoreCandidates(new[] { Hammer, Drill, Screws }, p => { calls++; return 1; });

        Assert.Equal(3, calls);
        Assert.Equal(3, scored.Length);
    }
}
