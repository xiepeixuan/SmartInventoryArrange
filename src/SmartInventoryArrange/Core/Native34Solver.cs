using System.Diagnostics;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace SmartInventoryArrange.Core;

/// <summary>Pure managed native port of the approved whole-family visual policy.
/// The Python laboratory's historical experiments are not runtime dependencies.
/// Original game masks, stable instances and transaction application stay separate.
/// All search caps count deterministic work, never wall-clock selection decisions.
/// </summary>
public sealed partial class Native34Solver
{
    public int NodeLimit { get; init; } = 250000;
    public int MaxPasses { get; init; } = 8;
    public string Diagnostic { get; private set; } = "";
    private LayoutProblem _p = null!;
    private FrozenPreferenceModel _model = null!;
    private Dictionary<string, LayoutItem> _docs = null!;
    private List<Family> _families = null!;
    private Dictionary<string,int> _currentOrientations = new(StringComparer.Ordinal);
    private Dictionary<string, List<Template>> _templates = new(StringComparer.Ordinal);
    private readonly List<string> _runs = new();
    // All caches are solve-local. Geometry metrics do not depend on the current pass;
    // direction-ranked options/templates do and are cleared at every pass boundary.
    private readonly Dictionary<string, Metrics> _metricCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool> _interlockCache = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Id, string Style), Orientation[]> _optionCache = new();
    private readonly Dictionary<Template, PackedTemplate> _packed = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, Dictionary<string, Orientation>> _legalOrientations = new(StringComparer.Ordinal);
    private readonly List<StageTiming> _performance = new();
    private int _pass, _lastNodes;
    public sealed record StageTiming(int Pass, string Stage, double Milliseconds, long AllocatedBytes, int Nodes);
    public IReadOnlyList<StageTiming> Performance => _performance;
    private void RunStage(string name, Action action)
    {
        long ticks = Stopwatch.GetTimestamp(), bytes = GC.GetAllocatedBytesForCurrentThread(); _lastNodes = 0;
        action();
        _performance.Add(new(_pass, name, (Stopwatch.GetTimestamp() - ticks) * 1000.0 / Stopwatch.Frequency,
            GC.GetAllocatedBytesForCurrentThread() - bytes, _lastNodes));
    }
    private sealed record PackedTemplate(Template T, BigInteger Bits, Cell First);
    private readonly record struct SearchState(BigInteger Occupied, BigInteger Remaining);
    private sealed class SearchChoice
    {
        public int Gi, Count, X, Y, Offset, Anchor, Continuation, Sequence;
        public PackedTemplate Pattern = null!;
        public BigInteger Bits;
        public string? TieKey;
    }

    private sealed record Family(string Key, LayoutItem[] Items);
    private sealed record Template(Placement[] Items, int Width, int Height, int Rows, int Landscape);
    private sealed record GroupMetric(string Id, int Count, int Regions, int DetachedArea, int Rows,
        int Mixed, int Portrait, bool Interlock, int Left, int Top, int Bottom, int ItemArea);
    private sealed record Metrics(GroupMetric[] Groups, int Regions, int Detached, int ExtraRows,
        int Mixed, int Landscape, int PortraitInterlock, int Overlaps, int Left, int Inversions, int Middle,
        int LargestMiddle, int Height, double AnchorY, int AnchorArea, double Priority, int CohortRows, double RowDebt, int AnchorIntrusion);
    public sealed record CandidateTrace(string Signature, string[] Generators, bool Valid, string RejectedReason,
        string Metrics, bool Selected);
    public IReadOnlyList<CandidateTrace> CandidateRecords { get; private set; } = Array.Empty<CandidateTrace>();
    private readonly Dictionary<string, (Placement[] Q, SortedSet<string> Generators)> _pool = new(StringComparer.Ordinal);

    public static string Type(LayoutItem i) => i.ItemId.Length > 0 ? i.ItemId : i.StableId.Split('\u001f')[0];
    private string Type(Placement q) => _docs.TryGetValue(q.StableId, out var i) ? Type(i) : "~fixed:" + q.StableId;
    private bool Anchor(LayoutItem i) => i.Area >= 12 || i.Categories.Append(i.PrimaryCategory).Any(c =>
        new[] { "container", "storage", "backpack", "bag", "machine", "device", "appliance" }
            .Any(s => c.Contains(s, StringComparison.OrdinalIgnoreCase)));
    private static string ShapeKey(LayoutItem i) => string.Join("/", i.Orientations.Select(o => o.Mask.Signature).Distinct().OrderBy(s => s, StringComparer.Ordinal));
    public static string Key(IEnumerable<Placement> q) => string.Join("|", q.OrderBy(a => a.StableId, StringComparer.Ordinal)
        .Select(a => $"{a.StableId}@{a.X},{a.Y},{a.GameOrientation}:{a.Mask.Signature}"));
    public static bool SameLayout(IEnumerable<Placement> a, IEnumerable<Placement> b) => StringComparer.Ordinal.Equals(Key(a), Key(b));
    private static string Hash(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)));
    public static bool ValidateLayout(LayoutProblem p, IReadOnlyList<Placement> q)
    {
        try
        {
            if (q.Count != p.Items.Count || q.Select(x => x.StableId).Distinct(StringComparer.Ordinal).Count() != q.Count) return false;
            var docs = p.Items.ToDictionary(i => i.StableId, StringComparer.Ordinal);
            if (p.Fixed.Any(f => docs.ContainsKey(f.StableId)) || p.Fixed.Select(f => f.StableId).Distinct().Count() != p.Fixed.Count) return false;
            var used = new bool[p.Width, p.Height];
            foreach (var a in p.Fixed.Concat(q))
            {
                if (docs.TryGetValue(a.StableId, out var i))
                {
                    // Original symmetric numeric orientations can have equivalent masks deduplicated.
                    bool legal = i.Orientations.Any(o => o.GameOrientation == a.GameOrientation && o.Mask.Signature == a.Mask.Signature);
                    if (!legal && !(a.GameOrientation == i.OriginalOrientation && i.OriginalMask != null && i.OriginalMask.Signature == a.Mask.Signature)) return false;
                    if (a.Category != i.PrimaryCategory) return false;
                }
                else if (!p.Fixed.Any(f => f == a)) return false;
                foreach (var c in a.Mask.Cells)
                {
                    int x = a.X + c.X, y = a.Y + c.Y;
                    if (x < 0 || y < 0 || x >= p.Width || y >= p.Height || !p.Usable[x, y] || used[x, y]) return false;
                    used[x, y] = true;
                }
            }
            return q.All(a => docs.ContainsKey(a.StableId));
        }
        catch (Exception e) when (e is ArgumentException or IndexOutOfRangeException or NullReferenceException) { return false; }
    }
    private static Placement At(LayoutItem i, int x, int y, Orientation o) => new(i.StableId, x, y, o.GameOrientation, o.Mask, i.PrimaryCategory, o.Mask.Width > o.Mask.Height);
    private static HashSet<Cell> Cells(IEnumerable<Placement> q) => q.SelectMany(a => a.Mask.Cells.Select(c => new Cell(a.X + c.X, a.Y + c.Y))).ToHashSet();
    private bool Fits(IEnumerable<Placement> q, int x, int y, HashSet<Cell> used) => q.All(a => a.Mask.Cells.All(c =>
    {
        int xx = x + a.X + c.X, yy = y + a.Y + c.Y;
        return xx >= 0 && yy >= 0 && xx < _p.Width && yy < _p.Height && _p.Usable[xx, yy] && !used.Contains(new Cell(xx, yy));
    }));
    private static Placement[] Move(IEnumerable<Placement> q, int x, int y) => q.Select(a => a with { X = a.X + x, Y = a.Y + y }).ToArray();
    private Orientation[] Options(LayoutItem i, string style)
    {
        if (_optionCache.TryGetValue((i.StableId, style), out var cached)) return cached;
        // Legal masks/codes and stable IDs only: current positions/directions never
        // decide which fresh global layout is generated.
        var result = i.Orientations
            .GroupBy(o => o.Mask.Signature, StringComparer.Ordinal).Select(g => g.OrderBy(o => o.GameOrientation).First())
            .OrderBy(o => o.Mask.Width > o.Mask.Height).ThenBy(o => o.GameOrientation)
            .ThenBy(o => o.Mask.Signature, StringComparer.Ordinal).ToArray();
        _optionCache[(i.StableId, style)] = result; return result;
    }
    private bool Interlocking(LayoutItem i)
    {
        if (_interlockCache.TryGetValue(i.StableId, out bool cached)) return cached;
        var result = InterlockingCore(i); _interlockCache[i.StableId] = result; return result;
    }
    private bool InterlockingCore(LayoutItem i)
    {
        var opts = Options(i, "portrait"); var portrait = opts.Where(o => o.Mask.Width <= o.Mask.Height).ToArray();
        if (portrait.Length > 0) opts = portrait;
        foreach (var a in opts) foreach (var b in opts)
        {
            var ac = a.Mask.Cells.ToHashSet();
            for (int dx = 0; dx < a.Mask.Width; dx++)
                if (!b.Mask.Cells.Any(c => ac.Contains(new Cell(c.X + dx, c.Y))) &&
                    Math.Max(a.Mask.Width, dx + b.Mask.Width) < a.Mask.Width + b.Mask.Width) return true;
        }
        return false;
    }
    private List<Template> InterlockRows(LayoutItem[] items, int beam = 6)
    {
        var states = new List<Template> { new(Array.Empty<Placement>(), 0, 0, 1, 0) };
        foreach (var i in items)
        {
            var os = Options(i, "portrait"); var portrait = os.Where(o => o.Mask.Width <= o.Mask.Height).ToArray();
            if (portrait.Length > 0) os = portrait;
            var next = new List<Template>();
            foreach (var state in states) foreach (var o in os)
            {
                if (o.Mask.Height > _p.Height || o.Mask.Width > _p.Width) continue;
                var used = Cells(state.Items); int start = state.Items.Length > 0 ? state.Items[^1].X : 0;
                for (int x = start; x <= state.Width && x + o.Mask.Width <= _p.Width; x++)
                {
                    var q = At(i, x, 0, o);
                    if (q.Mask.Cells.Any(c => used.Contains(new Cell(c.X + x, c.Y)))) continue;
                    next.Add(new(state.Items.Append(q).ToArray(), Math.Max(state.Width, x + o.Mask.Width), Math.Max(state.Height, o.Mask.Height), 1, state.Landscape + (o.Mask.Width > o.Mask.Height ? 1 : 0))); break;
                }
            }
            states = next.OrderBy(t => t.Width).ThenBy(t => t.Height).ThenBy(t => Key(t.Items), StringComparer.Ordinal).Take(beam).ToList();
            if (states.Count == 0) break;
        }
        return states;
    }
    private List<Template> Templates(LayoutItem[] members, string style)
    {
        string ck = string.Join("/", members.Select(i => i.StableId)) + ":" + style;
        if (_templates.TryGetValue(ck, out var cached)) return cached;
        var result = new List<Template>();
        if (members.Length > 1 && Interlocking(members[0]))
        {
            result.AddRange(InterlockRows(members));
            for (int per = members.Length - 1; per >= 1; per--)
            {
                var qs = new List<Placement>(); int y = 0, width = 0; bool valid = true;
                for (int off = 0; off < members.Length; off += per)
                {
                    var ts = InterlockRows(members.Skip(off).Take(per).ToArray(), 1);
                    if (ts.Count == 0) { valid = false; break; }
                    var t = ts[0]; qs.AddRange(Move(t.Items, 0, y)); y += t.Height; width = Math.Max(width, t.Width);
                }
                if (valid && y <= _p.Height) result.Add(new(qs.ToArray(), width, y, qs.Select(a => a.Y).Distinct().Count(), qs.Count(a => a.Mask.Width > a.Mask.Height)));
            }
        }
        else
        {
            var common = members[0].Orientations.Select(o => o.Mask.Signature).ToHashSet(StringComparer.Ordinal);
            foreach (var m in members) common.IntersectWith(m.Orientations.Select(o => o.Mask.Signature));
            foreach (var mask in common.OrderBy(x => x, StringComparer.Ordinal))
            {
                var os = members.Select(i => Options(i, style).First(o => o.Mask.Signature == mask)).ToArray(); int w = os[0].Mask.Width, h = os[0].Mask.Height;
                for (int per = Math.Min(_p.Width / w, members.Length); per >= 1; per--)
                {
                    int rows = (members.Length + per - 1) / per; if (rows * h > _p.Height) continue;
                    var qs = members.Select((i, k) => At(i, k % per * w, k / per * h, os[k])).ToArray();
                    result.Add(new(qs, Math.Min(per, members.Length) * w, rows * h, rows, qs.Count(q => q.Mask.Width > q.Mask.Height)));
                }
            }
        }
        result = result.GroupBy(t => Key(t.Items), StringComparer.Ordinal).Select(g => g.First()).OrderBy(t => t.Rows)
            .ThenBy(t => t.Landscape)
            .ThenBy(t => t.Height).ThenBy(t => -t.Width).ThenBy(t => Key(t.Items), StringComparer.Ordinal).ToList();
        _templates[ck] = result; return result;
    }
    private void Add(IEnumerable<Placement> q, string generator)
    {
        var array = q.OrderBy(a => a.StableId, StringComparer.Ordinal).ToArray();
        if (!ValidateLayout(_p, array)) throw new InvalidOperationException("Generated candidate violated immutable geometry: " + generator);
        string key = Key(array);
        if (!_pool.TryGetValue(key, out var record)) record = (array, new SortedSet<string>(StringComparer.Ordinal));
        record.Generators.Add(generator); _pool[key] = record;
    }
    private bool UnitTail(LayoutItem[] ms, List<Placement> placed)
    {
        var used = Cells(placed); var free = new HashSet<Cell>();
        for (int y = 0; y < _p.Height; y++) for (int x = 0; x < _p.Width; x++) if (_p.Usable[x, y] && !used.Contains(new(x, y))) free.Add(new(x, y));
        var parts = Components(free); var part = parts.Where(c => c.Count >= ms.Length).OrderByDescending(c => c.Count).ThenBy(c => c.Min(z => z.Y * _p.Width + z.X)).FirstOrDefault();
        if (part == null) return false;
        var frontier = new HashSet<Cell> { part.OrderBy(c => c.Y).ThenBy(c => c.X).First() }; var seen = new HashSet<Cell>();
        foreach (var m in ms)
        {
            var c = frontier.OrderBy(c => c.Y).ThenBy(c => c.X).First(); frontier.Remove(c); seen.Add(c);
            placed.Add(At(m, c.X, c.Y, m.Orientations.OrderBy(o => o.GameOrientation).First()));
            foreach (var n in Neighbours(c)) if (part.Contains(n) && !seen.Contains(n)) frontier.Add(n);
        }
        return true;
    }
    private void Shelf(string order, string style, string layout)
    {
        var groups = OrderedFamilies(order, layout == "unit-tail");
        var placed = _p.Fixed.ToList(); int floor = 0, bandTop = -1, bandHeight = 0; string? failed = null;
        foreach (var group in groups)
        {
            var ms = group.Items; bool anchor = ms.Any(Anchor);
            if (layout == "unit-tail" && ms[0].Area == 1 && _families.Count(g => g.Items[0].Area == 1) == 1)
            { if (!UnitTail(ms, placed)) { failed = group.Key; break; } else continue; }
            var choices = new List<(int Rows, int Penalty, int Y, int X, int H, int W, Placement[] Q)>(); var occ = Cells(placed);
            foreach (var t in Templates(ms, style))
            {
                int penalty = t.Landscape;
                int startY = anchor || layout != "shelf" ? 0 : bandTop >= 0 && t.Height == bandHeight && t.Rows == 1 ? bandTop : floor; bool found = false;
                for (int y = startY; y + t.Height <= _p.Height && !found; y++) for (int x = 0; x + t.Width <= _p.Width; x++)
                {
                    if (!Fits(t.Items, x, y, occ)) continue;
                    choices.Add((t.Rows, penalty, y, x, t.Height, t.Width, Move(t.Items, x, y))); found = true; break;
                }
            }
            if (choices.Count == 0) { failed = group.Key; break; }
            var best = choices.OrderBy(t => t.Rows).ThenBy(t => t.Penalty).ThenBy(t => t.Y).ThenBy(t => t.X).ThenBy(t => t.H).ThenBy(t => t.W).ThenBy(t => Key(t.Q), StringComparer.Ordinal).First();
            placed.AddRange(best.Q); floor = Math.Max(floor, best.Y + best.H); if (!anchor) { bandTop = best.Y; bandHeight = best.H; }
        }
        string gid = $"global:{layout}:{order}:{style}"; _runs.Add($"{gid}:complete={failed == null}:placed={placed.Count - _p.Fixed.Count}:failed={failed ?? "none"}");
        if (failed == null) Add(placed.Where(q => _docs.ContainsKey(q.StableId)), gid);
    }
    private static Cell[] Neighbours(Cell c) => new[] { new Cell(c.X - 1, c.Y), new Cell(c.X + 1, c.Y), new Cell(c.X, c.Y - 1), new Cell(c.X, c.Y + 1) };
    private static List<HashSet<Cell>> Components(HashSet<Cell> input)
    {
        var left = new HashSet<Cell>(input); var result = new List<HashSet<Cell>>();
        while (left.Count > 0)
        {
            var seed = left.OrderBy(c => c.Y).ThenBy(c => c.X).First(); left.Remove(seed); var part = new HashSet<Cell> { seed }; var todo = new Stack<Cell>(); todo.Push(seed);
            while (todo.Count > 0) foreach (var n in Neighbours(todo.Pop())) if (left.Remove(n)) { part.Add(n); todo.Push(n); }
            result.Add(part);
        }
        return result;
    }
    private Metrics Measure(IReadOnlyList<Placement> movable)
    {
        string key = Key(movable);
        if (_metricCache.TryGetValue(key, out var cached)) return cached;
        var result = MeasureCore(movable); _metricCache[key] = result; return result;
    }
    private Metrics MeasureCore(IReadOnlyList<Placement> movable)
    {
        var all = _p.Fixed.Concat(movable).ToArray(); var bands = new List<GroupMetric>();
        foreach (var g in movable.GroupBy(Type, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var qs = g.ToArray(); var parts = Components(Cells(qs)); var maxArea = parts.Max(c => c.Count); bool interlock = qs.Length > 1 && Interlocking(_docs[qs[0].StableId]);
            int mixed = qs.GroupBy(q => ShapeKey(_docs[q.StableId]), StringComparer.Ordinal)
                .Sum(g => Interlocking(_docs[g.First().StableId]) ? 0 : Math.Max(0, g.Select(q => q.Mask.Signature).Distinct(StringComparer.Ordinal).Count() - 1));
            bands.Add(new(g.Key, qs.Length, parts.Count, qs.Sum(q => q.Mask.Area) - maxArea, qs.Select(q => q.Y).Distinct().Count(), mixed, qs.Count(q => q.Mask.Width <= q.Mask.Height), interlock, qs.Min(q => q.X), qs.Min(q => q.Y), qs.Max(q => q.Y + q.Mask.Height), qs.Max(q => q.Mask.Area)));
        }
        var occupied = Cells(all); var rows = occupied.GroupBy(c => c.Y).ToDictionary(g => g.Key, g => (lo: g.Min(c => c.X), hi: g.Max(c => c.X))); var cols = occupied.GroupBy(c => c.X).ToDictionary(g => g.Key, g => (lo: g.Min(c => c.Y), hi: g.Max(c => c.Y))); var middle = new HashSet<Cell>();
        for (int y = 0; y < _p.Height; y++) for (int x = 0; x < _p.Width; x++) if (_p.Usable[x, y] && !occupied.Contains(new(x, y)) && ((rows.TryGetValue(y, out var r) && r.lo < x && x < r.hi) || (cols.TryGetValue(x, out var c) && c.lo < y && y < c.hi))) middle.Add(new(x, y));
        int overlaps = 0, inversions = 0;
        for (int i = 0; i < bands.Count; i++) for (int j = i + 1; j < bands.Count; j++)
        {
            var a = bands[i]; var b = bands[j]; if (Math.Min(a.Bottom, b.Bottom) > Math.Max(a.Top, b.Top) && (a.Top, a.Bottom) != (b.Top, b.Bottom)) overlaps++;
            if (a.ItemArea > b.ItemArea && a.Top > b.Top || b.ItemArea > a.ItemArea && b.Top > a.Top) inversions++;
        }
        int cohortRows = movable.Where(q => q.Mask.Area >= 4).GroupBy(q => (q.Category, q.Mask.Signature))
            .Where(g => g.Select(Type).Distinct(StringComparer.Ordinal).Count() > 1)
            .Sum(g => Math.Max(0, g.Select(q => q.Y).Distinct().Count() - 1));
        double rowDebt = bands.Sum(g => Math.Max(0, g.Rows - (int)Math.Ceiling(g.Count * _docs[movable.First(q=>Type(q)==g.Id).StableId].Orientations.Min(o=>o.Mask.Width) / (double)_p.Width)) / (double)g.Count);
        var anchors = movable.Where(q => Anchor(_docs[q.StableId])).ToArray(); int area = anchors.Sum(q => q.Mask.Area);
        int anchorBottom = anchors.Where(q=>_interlockAnchors.Contains(q.StableId)).Select(q=>q.Y+q.Mask.Height).DefaultIfEmpty().Max();
        int intrusion = movable.Where(q=>!Anchor(_docs[q.StableId])).Sum(q=>q.Mask.Cells.Count(c=>q.Y+c.Y<anchorBottom));
        return new(bands.ToArray(), bands.Sum(g => Math.Max(0, g.Regions - 1)), bands.Sum(g => g.DetachedArea), bands.Sum(g => Math.Max(0, g.Rows - 1)), bands.Sum(g => g.Mixed), movable.Count(q => q.Mask.Width > q.Mask.Height), bands.Where(g => g.Interlock).Sum(g => g.Portrait), overlaps, bands.Sum(g => g.Left), inversions, middle.Count, LargestRectangle(middle), all.Length > 0 ? all.Max(q => q.Y + q.Mask.Height) : 0, anchors.Sum(q => (double)q.Mask.Area * q.Y), area, movable.Sum(q => (double)q.Mask.Area * (q.Y * _p.Width + q.X)), cohortRows, rowDebt, intrusion);
    }
    private int LargestRectangle(HashSet<Cell> cells)
    {
        var heights = new int[_p.Width]; int largest = 0;
        for (int y = 0; y < _p.Height; y++)
        {
            for (int x = 0; x < _p.Width; x++) heights[x] = cells.Contains(new(x, y)) ? heights[x] + 1 : 0;
            var stack = new Stack<int>();
            for (int x = 0; x <= _p.Width; x++)
            {
                int height = x == _p.Width ? 0 : heights[x];
                while (stack.Count > 0 && heights[stack.Peek()] > height) { int i = stack.Pop(); int width = stack.Count > 0 ? x - stack.Peek() - 1 : x; largest = Math.Max(largest, heights[i] * width); }
                stack.Push(x);
            }
        }
        return largest;
    }
    // Only invariant geometry is a hard admission gate. Old positions, old score,
    // row counts, and aesthetic anchors do not freeze movable objects in place.
    private string Guard(Placement[] candidate) => ValidateLayout(_p, candidate) ? "" : "hard-validation";
    private double Value(Metrics m)
    {
        int n = Math.Max(1, _p.Items.Count), groups = Math.Max(1, m.Groups.Length), A = _p.Width * _p.Height;
        double pairs = Math.Max(1, groups * (groups - 1) / 2.0);
        int totalArea = _p.Items.Sum(i=>i.Area), usable=0;foreach(bool cell in _p.Usable)if(cell)usable++;
        double occupancy = (totalArea + _p.Fixed.Sum(q=>q.Mask.Area)) / (double)Math.Max(1,usable);
        // Calibrated against the recorded five-machine counterexample: pair counts
        // must be normalized by pairs, not grow without bound with family count.
        // No term is a comparison with the incoming layout.
        return 1000.0 * m.Regions / n + 200.0 * m.Detached / Math.Max(1, _p.Items.Sum(i => i.Area))
            + 50.0 * m.RowDebt / groups + 30.0 * m.Mixed / n + 8.0 * m.Landscape / n
            + 6.0 * m.AnchorY / Math.Max(1, m.AnchorArea) / _p.Height
            + 2.0 * m.Height / _p.Height + 3.0 * m.Middle / A + 2.0 * m.LargestMiddle / A
            + 3.0 * m.CohortRows / n + 4.0 * (1-occupancy) * m.AnchorIntrusion / Math.Max(1,totalArea-m.AnchorArea) + .1 * m.Overlaps / pairs + .1 * m.Inversions / pairs
            + .1 * m.Left / (_p.Width * groups) + .1 * m.Priority / Math.Max(1, _p.Items.Sum(i => i.Area)) / A
            + .5 * (n - m.PortraitInterlock) / n;
    }
    private static string Summary(Metrics m) => $"regions={m.Regions},detached={m.Detached},extraRows={m.ExtraRows},mixed={m.Mixed},portraitInterlock={m.PortraitInterlock},anchorY={m.AnchorY},bandOverlaps={m.Overlaps},familyLeft={m.Left},largeInversions={m.Inversions},middle={m.Middle},middleRect={m.LargestMiddle},height={m.Height},cohortRows={m.CohortRows},rowDebt={m.RowDebt:F5},anchorIntrusion={m.AnchorIntrusion}";
    public LayoutResult Solve(LayoutProblem p, IReadOnlyList<Placement>? original = null)
    {
        var session = CreateSession(p, original);
        var progress = session.Advance(NodeLimit, 64);
        return progress.Result;
    }

    // Same routes, DFS traversal order, memo equivalence and node caps as v3.12.
    // Occupancy + mixed-radix remaining counts is an exact, collision-checked key.
    private PackedTemplate Pack(Template t)
    {
        if (_packed.TryGetValue(t, out var cached)) return cached;
        var cells = Cells(t.Items); var first = cells.OrderBy(c => c.Y).ThenBy(c => c.X).First();
        BigInteger bits = 0;
        foreach (var c in cells) bits |= BigInteger.One << (c.Y * _p.Width + c.X);
        var result = new PackedTemplate(t, bits, first); _packed[t] = result; return result;
    }
    private static int LowestBit(BigInteger value)
    {
        Span<byte> small = stackalloc byte[128];
        ReadOnlySpan<byte> bytes = value.TryWriteBytes(small, out int length, isUnsigned: true) ? small[..length] : value.ToByteArray(isUnsigned: true);
        for (int i = 0; i < bytes.Length; i++) if (bytes[i] != 0) return i * 8 + BitOperations.TrailingZeroCount((uint)bytes[i]);
        return -1;
    }
    private static int CountBits(BigInteger value) { int result = 0; foreach (var b in value.ToByteArray(isUnsigned: true)) result += BitOperations.PopCount((uint)b); return result; }
    // Native counterpart of preference_features/engine.features. Exposed for cross-language contracts.
    public IReadOnlyDictionary<string,double> FeatureVector(IReadOnlyList<Placement> movable)
    {
        var qs = _p.Fixed.Concat(movable).ToArray(); int W = _p.Width, H = _p.Height, gridArea = W * H, n = Math.Max(1, qs.Length), usable = 0; foreach (bool v in _p.Usable) if (v) usable++; usable = Math.Max(1, usable);
        var m = Measure(movable); var byCell = new Dictionary<Cell,Placement>(); foreach (var q in qs) foreach (var c in q.Mask.Cells) byCell[new(q.X + c.X, q.Y + c.Y)] = q;
        int typeEdges = 0, categoryEdges = 0, interleaved = 0, cohort = 0, runBreaks = 0, rowGaps = 0, extraRows = 0, wraps = 0, colBreaks = 0, seams = 0, rowDrift = 0; double upperGaps = 0;
        var left = new List<int>(); var right = new List<int>(); var starts = new SortedDictionary<int,HashSet<int>>(); var family = qs.GroupBy(Type,StringComparer.Ordinal).ToArray();
        foreach (var q in qs) { if(!starts.TryGetValue(q.Y,out var xs))starts[q.Y]=xs=new(); foreach(var c in q.Mask.Cells)xs.Add(q.X+c.X); }
        var boundaries=starts.Select(kv=>(Y:kv.Key,Left:kv.Value.Min(),Right:kv.Value.Max()+1)).ToArray();for(int i=1;i<boundaries.Length;i++)rowDrift+=Math.Abs(boundaries[i].Left-boundaries[i-1].Left)+Math.Abs(boundaries[i].Right-boundaries[i-1].Right);
        foreach (var kv in byCell) foreach (var point in new[]{new Cell(kv.Key.X+1,kv.Key.Y),new Cell(kv.Key.X,kv.Key.Y+1)})
        {
            if(!byCell.TryGetValue(point,out var z)||z.StableId==kv.Value.StableId)continue;var q=kv.Value;
            if(Type(q)==Type(z))typeEdges++; if(q.Category==z.Category)categoryEdges++;else interleaved++;
            if(Type(q)!=Type(z)&&!q.Category.StartsWith('~')&&q.Category==z.Category&&q.Y==z.Y&&q.Mask.Signature==z.Mask.Signature)cohort++;
        }
        for(int y=0;y<H;y++)
        {
            var runs=new Dictionary<string,int>(StringComparer.Ordinal);string? prev=null;var xs=new List<int>();
            for(int x=0;x<W;x++){string key=byCell.TryGetValue(new(x,y),out var q)?Type(q):"~empty";if(key!=prev){runs[key]=runs.GetValueOrDefault(key)+1;prev=key;}if(q!=null)xs.Add(x);}
            runBreaks+=runs.Where(kv=>kv.Key!="~empty").Sum(kv=>Math.Max(0,kv.Value-1));
            if(xs.Count>0){left.Add(xs[0]);right.Add(xs[^1]);int gap=Enumerable.Range(xs[0],xs[^1]-xs[0]+1).Count(x=>_p.Usable[x,y]&&!byCell.ContainsKey(new(x,y)));rowGaps+=gap;upperGaps+=gap*(H-y)/(double)H;}
        }
        foreach(var g in family)
        {
            var rows=g.GroupBy(q=>q.Y).OrderBy(g=>g.Key).ToArray();if(g.Count()>1)extraRows+=rows.Length-1;for(int i=1;i<rows.Length;i++)if(rows[i].Key!=rows[i-1].Max(q=>q.Y+q.Mask.Height))wraps++;
            var eligible=g.Where(q=>q.Mask.Area>=4||q.Category=="MODULE").GroupBy(q=>q.X);
            foreach(var col in eligible){var items=col.OrderBy(q=>q.Y).ThenBy(q=>q.StableId,StringComparer.Ordinal).ToArray();for(int i=1;i<items.Length;i++)colBreaks+=(items[i-1].Mask.Width!=items[i].Mask.Width?1:0)+(items[i].Y!=items[i-1].Y+items[i-1].Mask.Height?1:0);}
        }
        var eligibleAll=qs.Where(q=>q.Mask.Area>=4||q.Category=="MODULE").ToArray();foreach(var a in eligibleAll){var below=eligibleAll.Where(b=>b.Y==a.Y+a.Mask.Height&&b.Mask.Width==a.Mask.Width&&Math.Max(a.X,b.X)<Math.Min(a.X+a.Mask.Width,b.X+b.Mask.Width)).ToArray();if(below.Length>0)seams+=below.Min(b=>Math.Abs(a.X-b.X));}
        int typeRegions=family.Sum(g=>Math.Max(0,InstanceComponents(g.ToArray())-1));int categoryRegions=qs.GroupBy(q=>q.Category,StringComparer.Ordinal).Sum(g=>Math.Max(0,InstanceComponents(g.ToArray())-1));
        double compact=qs.GroupBy(q=>q.Category,StringComparer.Ordinal).Select(g=>g.Sum(q=>q.Mask.Area)/(double)Math.Max(1,(g.Max(q=>q.X+q.Mask.Width)-g.Min(q=>q.X))*(g.Max(q=>q.Y+q.Mask.Height)-g.Min(q=>q.Y)))).DefaultIfEmpty().Average();
        int uniform=family.Sum(g=>g.GroupBy(q=>q.Mask.Width>q.Mask.Height?"landscape":q.Mask.Width<q.Mask.Height?"portrait":"square").Max(g=>g.Count()));
        var free=new HashSet<Cell>();for(int y=0;y<H;y++)for(int x=0;x<W;x++)if(_p.Usable[x,y]&&!byCell.ContainsKey(new(x,y)))free.Add(new(x,y));int holes=Components(free).Where(c=>!c.Any(z=>z.X==0||z.Y==0||z.X==W-1||z.Y==H-1)).Sum(c=>c.Count);
        int bottomBlank=0;for(int y=H-1;y>=0;y--){if(Enumerable.Range(0,W).All(x=>free.Contains(new(x,y))))bottomBlank+=W;else break;}
        var bottomCells=new HashSet<Cell>();for(int x=0;x<W;x++)for(int y=H-1;y>=0&&free.Contains(new(x,y));y--)bottomCells.Add(new(x,y));int bottomRect=LargestRectangle(bottomCells);
        double contour=0;for(int i=1;i<left.Count;i++)contour+=Math.Abs(left[i]-left[i-1])+Math.Abs(right[i]-right[i-1]);int totalArea=Math.Max(1,qs.Sum(q=>q.Mask.Area));var anchors=movable.Where(q=>_docs[q.StableId].VisualPriority<100).ToArray();int anchorArea=Math.Max(1,anchors.Sum(q=>q.Mask.Area));
        return new Dictionary<string,double>(StringComparer.Ordinal){["typeConnectivity"]=-typeRegions/(double)n,["typeRowContinuity"]=-extraRows/(double)n,["adjacentWrapContinuity"]=-wraps/(double)n,["middleSpace"]=-m.Middle/(double)usable,["largestMiddleSpace"]=-m.LargestMiddle/(double)usable,["columnSeamAlignment"]=-seams/(double)(W*n),["rowBoundaryAlignment"]=-rowDrift/(double)(2*W*Math.Max(1,starts.Count-1)),["columnContinuity"]=-colBreaks/(double)n,["categoryConnectivity"]=-categoryRegions/(double)n,["categoryCompactness"]=compact,["categorySeparation"]=-interleaved/(double)gridArea,["typeContact"]=typeEdges/(double)gridArea,["categoryContact"]=categoryEdges/(double)gridArea,["sameShapeCohortRowContact"]=cohort/(double)usable,["rowRunContinuity"]=-runBreaks/(double)n,["directionUniformity"]=uniform/(double)n,["uprightPreference"]=-qs.Count(q=>q.Mask.Width>q.Mask.Height)/(double)n,["enclosedSpace"]=-holes/(double)gridArea,["bottomBlank"]=bottomBlank/(double)gridArea,["bottomEmptyRectangle"]=bottomRect/(double)gridArea,["interiorRowContinuity"]=-rowGaps/(double)gridArea,["upperRowContinuity"]=-upperGaps/gridArea,["contourAlignment"]=-contour/gridArea,["areaTopPriority"]=-qs.Sum(q=>(double)q.Mask.Area*(q.Y*W+q.X))/gridArea/totalArea,["anchorTopPriority"]=-anchors.Sum(q=>(double)q.Mask.Area*(q.Y*W+q.X))/gridArea/anchorArea};
    }
    private static int InstanceComponents(Placement[] items)
    {
        if(items.Length==0)return 0;var parent=Enumerable.Range(0,items.Length).ToArray();int Find(int i){while(parent[i]!=i){parent[i]=parent[parent[i]];i=parent[i];}return i;}
        var owner=new Dictionary<Cell,int>();for(int i=0;i<items.Length;i++)foreach(var c in items[i].Mask.Cells)owner[new(items[i].X+c.X,items[i].Y+c.Y)]=i;
        foreach(var kv in owner)foreach(var next in Neighbours(kv.Key))if(owner.TryGetValue(next,out int b)){int a=Find(kv.Value),bb=Find(b);if(a!=bb)parent[bb]=a;}
        return Enumerable.Range(0,items.Length).Select(Find).Distinct().Count();
    }
}
