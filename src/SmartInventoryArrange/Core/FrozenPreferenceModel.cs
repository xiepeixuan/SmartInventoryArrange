using System.Text.Json;
namespace SmartInventoryArrange.Core;

/// <summary>Frozen explanatory model. Only breaks equal explicit geometry ranks;
/// no model coefficient can bypass item safety or veto the approved visual policy.</summary>
internal sealed class FrozenPreferenceModel
{
    private readonly string[] _names;
    private readonly double[] _scale;
    private readonly double[] _weights;
    private FrozenPreferenceModel(string[] names, double[] scale, double[] weights)
    { _names = names; _scale = scale; _weights = weights; }
    private static readonly Lazy<FrozenPreferenceModel> Embedded = new(LoadCore);
    public static FrozenPreferenceModel LoadEmbedded() => Embedded.Value;
    private static FrozenPreferenceModel LoadCore()
    {
        using var stream = typeof(FrozenPreferenceModel).Assembly.GetManifestResourceStream("SmartInventoryArrange.directional21.json") ?? throw new InvalidOperationException("Frozen preference resource missing.");
        using var doc = JsonDocument.Parse(stream); var r = doc.RootElement;
        if (r.GetProperty("kind").GetString() != "nonnegative-regularized-pair-logistic" || r.GetProperty("profile").GetString() != "semantic") throw new InvalidOperationException("Frozen model schema mismatch.");
        var names = r.GetProperty("featureNames").EnumerateArray().Select(q => q.GetString()!).ToArray(); var scale = r.GetProperty("scale").EnumerateArray().Select(q => q.GetDouble()).ToArray(); var weights = r.GetProperty("weights").EnumerateArray().Select(q => q.GetDouble()).ToArray();
        if (names.Length != scale.Length || names.Length != weights.Length || names.Distinct().Count() != names.Length || scale.Any(v => !double.IsFinite(v) || v <= 0) || weights.Any(v => !double.IsFinite(v) || v < 0)) throw new InvalidOperationException("Frozen model numeric domain invalid.");
        return new(names, scale, weights);
    }
    public double Value(IReadOnlyDictionary<string, double> features)
    {
        double value = 0; for (int i = 0; i < _names.Length; i++)
        { if (!features.TryGetValue(_names[i], out var f) || !double.IsFinite(f)) throw new InvalidOperationException("Preference feature missing or not finite: " + _names[i]); value += f / _scale[i] * _weights[i]; }
        return value;
    }
}
