using SmartInventoryArrange.Core;
namespace SmartInventoryArrange;

/// <summary>Input-only constraint. Never edits item/native rotation flags or solver strategy.</summary>
public static class SupplyCrateOrientationPolicy
{
    public static IReadOnlyList<Orientation> Allowed(IReadOnlyList<string> categories, IReadOnlyList<Orientation> nativeOrientations)
    {
        if (!categories.Contains("SUPPLY_CRATE", StringComparer.Ordinal)) return nativeOrientations;
        // GameGridShapeBuilder uses absolute game orientation0 for the original
        // horizontal supply-crate mask, even when the captured item is vertical.
        var original = nativeOrientations.Where(o => o.GameOrientation == 0).ToArray();
        if (original.Length != 1 || original[0].Mask.Width < original[0].Mask.Height) throw new InvalidOperationException("Supply crate has no unique native default-horizontal orientation; captured inventory unchanged.");
        return original;
    }
}
