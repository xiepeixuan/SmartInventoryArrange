namespace SmartInventoryArrange.Core;

public readonly record struct Cell(int X, int Y);

public sealed class ShapeMask
{
    public int Width { get; }
    public int Height { get; }
    public IReadOnlyList<Cell> Cells { get; }
    public int Area => Cells.Count;
    public string Signature { get; }

    public ShapeMask(IEnumerable<Cell> cells)
    {
        var normalized = cells.Distinct().OrderBy(c => c.Y).ThenBy(c => c.X).ToArray();
        if (normalized.Length == 0) throw new ArgumentException("Shape must contain at least one cell.");
        int minX = normalized.Min(c => c.X), minY = normalized.Min(c => c.Y);
        var shifted = normalized.Select(c => new Cell(c.X - minX, c.Y - minY)).OrderBy(c => c.Y).ThenBy(c => c.X).ToArray();
        Cells = shifted;
        Width = shifted.Max(c => c.X) + 1;
        Height = shifted.Max(c => c.Y) + 1;
        Signature = string.Join(";", shifted.Select(c => $"{c.X},{c.Y}"));
    }

    public ShapeMask Rotate90() => new(Cells.Select(c => new Cell(Height - 1 - c.Y, c.X)));
}

public sealed record Orientation(int GameOrientation, ShapeMask Mask, bool Rotated);

public sealed class LayoutItem
{
    public string StableId { get; init; } = "";
    public string ItemId { get; init; } = "";
    public string PrimaryCategory { get; init; } = "~unknown";
    public IReadOnlyList<string> Categories { get; init; } = Array.Empty<string>();
    public IReadOnlyList<Orientation> Orientations { get; init; } = Array.Empty<Orientation>();
    public ShapeMask? OriginalMask { get; init; }
    public int OriginalOrientation { get; init; }
    public int OriginalX { get; init; }
    public int OriginalY { get; init; }
    public int VisualPriority { get; init; } = 1000;
    public int Area => Orientations[0].Mask.Area;
    public int Complexity => Orientations[0].Mask.Width * Orientations[0].Mask.Height - Area;
}

public sealed record Placement(string StableId, int X, int Y, int GameOrientation, ShapeMask Mask, string Category, bool Rotated);

public sealed class LayoutProblem
{
    public int Width { get; init; }
    public int Height { get; init; }
    public bool[,] Usable { get; init; } = new bool[0, 0];
    public IReadOnlyList<Placement> Fixed { get; init; } = Array.Empty<Placement>();
    public IReadOnlyList<LayoutItem> Items { get; init; } = Array.Empty<LayoutItem>();
    public IReadOnlyDictionary<string, int> CategoryOrder { get; init; } = new Dictionary<string, int>(StringComparer.Ordinal);
}

public readonly record struct LayoutScore(
    int PlacedCount, int PlacedArea,
    int NegativeTypeRuns, int SameTypeRowAlignment,
    int SameTypeHorizontalContact,
    int NegativeCohortRuns, int CohortRowAlignment,
    int CohortHorizontalContact, int OrientationConsistency,
    int VerticalPreference, int PriorityOrderAlignment,
    int CategoryOrderAlignment,
    int LargeFirstAlignment, int SameTypeContact,
    int SameCategoryContact, int TopLeftCompactness,
    int RowRegularity, int NegativeFragments,
    int NegativeInterleaving, int NegativeRotations,
    string StableTie) : IComparable<LayoutScore>
{
    public int CompareTo(LayoutScore o)
    {
        int[] a = { PlacedCount, PlacedArea,
            NegativeTypeRuns, SameTypeRowAlignment,
            SameTypeHorizontalContact,
            NegativeCohortRuns, CohortRowAlignment,
            CohortHorizontalContact, OrientationConsistency,
            VerticalPreference, PriorityOrderAlignment,
            CategoryOrderAlignment,
            LargeFirstAlignment, SameTypeContact,
            SameCategoryContact, TopLeftCompactness,
            RowRegularity, NegativeFragments,
            NegativeInterleaving, NegativeRotations };
        int[] b = { o.PlacedCount, o.PlacedArea,
            o.NegativeTypeRuns, o.SameTypeRowAlignment,
            o.SameTypeHorizontalContact,
            o.NegativeCohortRuns, o.CohortRowAlignment,
            o.CohortHorizontalContact, o.OrientationConsistency,
            o.VerticalPreference, o.PriorityOrderAlignment,
            o.CategoryOrderAlignment,
            o.LargeFirstAlignment, o.SameTypeContact,
            o.SameCategoryContact, o.TopLeftCompactness,
            o.RowRegularity, o.NegativeFragments,
            o.NegativeInterleaving, o.NegativeRotations };
        for (int i = 0; i < a.Length; i++)
        {
            int c = a[i].CompareTo(b[i]);
            if (c != 0) return c;
        }
        return -StringComparer.Ordinal.Compare(StableTie, o.StableTie);
    }
}
public sealed record LayoutResult(bool Complete, IReadOnlyList<Placement> Placements, LayoutScore Score,
    bool UsedFallback, bool TimedOut, TimeSpan Elapsed, int Holes, int NarrowFragments);



/// <summary>Native unique ID is stable across child-list reordering. An absent or
/// duplicated native ID uses an explicit snapshot index, never an object address.</summary>
public static class InstanceIdentity
{
    public static string From(string itemId,int uniqueId,int index,int multiplicity)
    {
        string key=$"{itemId}\u001f{uniqueId:D10}";
        return uniqueId!=0&&multiplicity==1?key:$"{key}\u001f{index:D6}";
    }
}
