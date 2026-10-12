using System.Diagnostics;

namespace SmartInventoryArrange.Core;

public sealed class DeterministicSolver
{
    public int BeamWidth { get; init; } = 32;
    public int CandidatesPerState { get; init; } = 24;
    public TimeSpan TimeBudget { get; init; } = TimeSpan.FromMilliseconds(250);
    public string Diagnostic { get; private set; } = string.Empty;
    private Stopwatch? _activeWatch;
    private bool OutOfTime() => _activeWatch != null && _activeWatch.Elapsed >= TimeBudget;

    public static double OccupancyRatio(LayoutProblem p)
    {
        int usable = 0;
        for (int y = 0; y < p.Height; y++)
        for (int x = 0; x < p.Width; x++)
            if (p.Usable[x, y]) usable++;
        if (usable == 0) return 1.0;
        int occupied = p.Fixed.Sum(x => x.Mask.Area) + p.Items.Sum(x => x.Area);
        return Math.Clamp(occupied / (double)usable, 0.0, 1.0);
    }

    public static string DensityMode(LayoutProblem p)
    {
        double ratio = OccupancyRatio(p);
        return ratio >= 0.92 ? "dense" : ratio >= 0.80 ? "balanced" : "aesthetic";
    }

    private static (string name, int a, int b)[] ComparisonFields(LayoutProblem p, LayoutScore a, LayoutScore b)
    {
        string mode = DensityMode(p);
        if (mode == "dense") return new[]
        {
            ("placedCount",a.PlacedCount,b.PlacedCount),("placedArea",a.PlacedArea,b.PlacedArea),
            ("typeRegions",a.NegativeTypeRuns,b.NegativeTypeRuns),("typeRows",a.SameTypeRowAlignment,b.SameTypeRowAlignment),
            ("typeEdges",a.SameTypeHorizontalContact,b.SameTypeHorizontalContact),("cohortRegions",a.NegativeCohortRuns,b.NegativeCohortRuns),
            ("cohortRows",a.CohortRowAlignment,b.CohortRowAlignment),("categoryEdges",a.SameCategoryContact,b.SameCategoryContact),
            ("categoryInterleaving",a.NegativeInterleaving,b.NegativeInterleaving),("priorityOrder",a.PriorityOrderAlignment,b.PriorityOrderAlignment),
            ("categoryOrder",a.CategoryOrderAlignment,b.CategoryOrderAlignment),("largeFirst",a.LargeFirstAlignment,b.LargeFirstAlignment),
            ("fragments",a.NegativeFragments,b.NegativeFragments),("edgeRegularity",a.RowRegularity,b.RowRegularity),
            ("orientationConsistency",a.OrientationConsistency,b.OrientationConsistency),("verticalPreference",a.VerticalPreference,b.VerticalPreference),
            ("rotations",a.NegativeRotations,b.NegativeRotations),("topLeft",a.TopLeftCompactness,b.TopLeftCompactness)
        };
        if (mode == "balanced") return new[]
        {
            ("placedCount",a.PlacedCount,b.PlacedCount),("placedArea",a.PlacedArea,b.PlacedArea),
            ("typeRegions",a.NegativeTypeRuns,b.NegativeTypeRuns),("typeRows",a.SameTypeRowAlignment,b.SameTypeRowAlignment),
            ("typeEdges",a.SameTypeHorizontalContact,b.SameTypeHorizontalContact),("cohortRegions",a.NegativeCohortRuns,b.NegativeCohortRuns),
            ("fragments",a.NegativeFragments,b.NegativeFragments),("edgeRegularity",a.RowRegularity,b.RowRegularity),
            ("categoryEdges",a.SameCategoryContact,b.SameCategoryContact),("categoryInterleaving",a.NegativeInterleaving,b.NegativeInterleaving),
            ("priorityOrder",a.PriorityOrderAlignment,b.PriorityOrderAlignment),("categoryOrder",a.CategoryOrderAlignment,b.CategoryOrderAlignment),
            ("largeFirst",a.LargeFirstAlignment,b.LargeFirstAlignment),("orientationConsistency",a.OrientationConsistency,b.OrientationConsistency),
            ("verticalPreference",a.VerticalPreference,b.VerticalPreference),("rotations",a.NegativeRotations,b.NegativeRotations),
            ("topLeft",a.TopLeftCompactness,b.TopLeftCompactness)
        };
        return new[]
        {
            ("placedCount",a.PlacedCount,b.PlacedCount),("placedArea",a.PlacedArea,b.PlacedArea),
            ("typeRegions",a.NegativeTypeRuns,b.NegativeTypeRuns),("typeRows",a.SameTypeRowAlignment,b.SameTypeRowAlignment),
            ("typeEdges",a.SameTypeHorizontalContact,b.SameTypeHorizontalContact),("cohortRegions",a.NegativeCohortRuns,b.NegativeCohortRuns),
            ("cohortRows",a.CohortRowAlignment,b.CohortRowAlignment),("cohortEdges",a.CohortHorizontalContact,b.CohortHorizontalContact),
            ("orientationConsistency",a.OrientationConsistency,b.OrientationConsistency),("verticalPreference",a.VerticalPreference,b.VerticalPreference),
            ("priorityOrder",a.PriorityOrderAlignment,b.PriorityOrderAlignment),("categoryOrder",a.CategoryOrderAlignment,b.CategoryOrderAlignment),
            ("largeFirst",a.LargeFirstAlignment,b.LargeFirstAlignment),("typeContact",a.SameTypeContact,b.SameTypeContact),
            ("categoryEdges",a.SameCategoryContact,b.SameCategoryContact),("topLeft",a.TopLeftCompactness,b.TopLeftCompactness),
            ("edgeRegularity",a.RowRegularity,b.RowRegularity),("fragments",a.NegativeFragments,b.NegativeFragments),
            ("categoryInterleaving",a.NegativeInterleaving,b.NegativeInterleaving),("rotations",a.NegativeRotations,b.NegativeRotations)
        };
    }

    public static int CompareForProblem(LayoutProblem p, LayoutScore a, LayoutScore b)
    {
        foreach (var f in ComparisonFields(p,a,b)) { int c=f.a.CompareTo(f.b); if(c!=0)return c; }
        return -StringComparer.Ordinal.Compare(a.StableTie,b.StableTie);
    }

    public static string FirstDifferingMetric(LayoutProblem p, LayoutScore candidate, LayoutScore baseline)
    {
        foreach(var f in ComparisonFields(p,candidate,baseline)) if(f.a!=f.b) return $"{f.name}:candidate={f.a},baseline={f.b}";
        return StringComparer.Ordinal.Equals(candidate.StableTie,baseline.StableTie)?"equal":"stableTie";
    }
    private sealed class State
    {
        public bool[,] Occupied = new bool[0, 0];
        public List<Placement> Placements = new();
        public LayoutScore Score;
    }

    private enum PackingHeuristic { BestShortSideFit, BestAreaFit, BottomLeft, ContactPoint, CategoryContact }

    private readonly record struct FreeRect(int X, int Y, int Width, int Height)
    {
        public int Right => X + Width;
        public int Bottom => Y + Height;
        public int Area => Width * Height;
        public bool Contains(int x, int y, int width, int height) =>
            x >= X && y >= Y && x + width <= Right && y + height <= Bottom;
        public bool Contains(FreeRect other) =>
            other.X >= X && other.Y >= Y && other.Right <= Right && other.Bottom <= Bottom;
    }

    public LayoutResult Solve(LayoutProblem p, IReadOnlyList<Placement>? original = null)
    {
        ValidateProblem(p);
        string densityMode = DensityMode(p);
        double occupancy = OccupancyRatio(p);
        var sw = Stopwatch.StartNew();
        _activeWatch = sw;
        var baseOcc = new bool[p.Width, p.Height];
        for (int y = 0; y < p.Height; y++) for (int x = 0; x < p.Width; x++) baseOcc[x, y] = !p.Usable[x, y];
        foreach (var fixedPlacement in p.Fixed) Mark(baseOcc, fixedPlacement, true);

        var categoryArea = p.Items.GroupBy(i => i.PrimaryCategory, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Sum(i => i.Area), StringComparer.Ordinal);
        var difficultyOrdered = p.Items.OrderBy(i => EstimateLegalPositions(p, baseOcc, i))
            .ThenByDescending(i => i.Complexity).ThenByDescending(i => i.Area)
            .ThenByDescending(i => i.Orientations.Max(o => Math.Max(o.Mask.Width, o.Mask.Height)))
            .ThenBy(i => i.Orientations.Count).ThenBy(i => i.StableId, StringComparer.Ordinal).ToArray();
        // Primary branch follows human reading order: priority group, catalogue
        // category, identical item type, then large-first within that group.
        var ordered = VisualOrder(p, p.Items).ToArray();

        // Construct whole shelf rows before item-level search. When this
        // produces a complete zero-split type layout, no item-wise candidate
        // is allowed to replace it with a visually fragmented arrangement.
        var shelfRowSeeds = new[]
        {
            AtomicSkylineBlocks(p, baseOcc, ordered, portraitOnly: true, out string portraitDiagnostic),
            AtomicSkylineBlocks(p, baseOcc, ordered, portraitOnly: false, out string anyDiagnostic)
        };
        Diagnostic = $"atomic skyline: portrait={portraitDiagnostic}; any={anyDiagnostic}";
        State? perfectShelf = null;
        foreach (var seed in shelfRowSeeds)
        {
            if (seed == null || seed.Length != p.Items.Count) continue;
            var occ = BuildOccupied(p, seed);
            var score = Score(p, p.Fixed.Concat(seed).ToArray(), occ);
            var state = new State { Occupied = occ, Placements = p.Fixed.Concat(seed).ToList(), Score = score };
            if (perfectShelf == null || CompareForProblem(p, score, perfectShelf.Score) > 0) perfectShelf = state;
        }
        if (densityMode == "aesthetic" && perfectShelf != null && perfectShelf.Score.NegativeTypeRuns == 0)
        {
            var shelfMovable = perfectShelf.Placements.Where(x => p.Fixed.All(f => f.StableId != x.StableId))
                .OrderBy(x => x.StableId, StringComparer.Ordinal).ToArray();
            sw.Stop();
            var shelfMetrics = FragmentMetrics(p, perfectShelf.Occupied);
            Diagnostic = $"mode={densityMode}, occupancy={occupancy:P1}, complete atomic mixed-shape skyline-block seed";
            return new(true, shelfMovable, perfectShelf.Score, false, sw.Elapsed >= TimeBudget, sw.Elapsed,
                shelfMetrics.holes, shelfMetrics.narrow);
        }

        var initialPlacements = new List<Placement>(p.Fixed);
        var states = new List<State> { new() { Occupied = baseOcc, Placements = initialPlacements, Score = Score(p, initialPlacements, baseOcc, includeStableTie: false) } };
        bool timedOut = false;
        foreach (var item in ordered)
        {
            if (OutOfTime()) { timedOut = true; break; }
            var next = new List<State>();
            foreach (var state in states)
            {
                if (OutOfTime()) { timedOut = true; break; }
                var candidates = EnumerateCandidates(p, state, item, categoryArea)
                    .Take(CandidatesPerState).ToArray();
                foreach (var candidate in candidates)
                {
                    if (OutOfTime()) { timedOut = true; break; }
                    var occ = (bool[,])state.Occupied.Clone();
                    Mark(occ, candidate, true);
                    var placements = new List<Placement>(state.Placements) { candidate };
                    next.Add(new State { Occupied = occ, Placements = placements, Score = Score(p, placements, occ, includeStableTie: false) });
                }
                if (timedOut) break;
            }
            if (timedOut || next.Count == 0) break;
            states = next.OrderByDescending(s => s.Score).ThenBy(s => PlacementKey(s.Placements), StringComparer.Ordinal)
                .Take(BeamWidth).ToList();
        }

        var best = states.OrderByDescending(s => s.Score).ThenBy(s => PlacementKey(s.Placements), StringComparer.Ordinal).First();
        best.Score = Score(p, best.Placements, best.Occupied);
        var movable = best.Placements.Where(x => p.Fixed.All(f => f.StableId != x.StableId)).ToArray();
        bool complete = movable.Length == p.Items.Count;

        // Fast deterministic whole-layout seeds. Category-first variants create
        // compact visual bands; difficulty-first variants protect hard shapes.
        var categoryFirst = ordered;
        var areaFirst = p.Items.OrderByDescending(i => i.Area).ThenByDescending(i => i.Complexity)
            .ThenBy(i => i.StableId, StringComparer.Ordinal).ToArray();
        var seedCandidates = new List<Placement[]?>
        {
            densityMode == "dense" && original != null ? DenseSlotReassignment(p, original) : null,
            perfectShelf?.Placements.Where(x => p.Fixed.All(f => f.StableId != x.StableId))
                .OrderBy(x => x.StableId, StringComparer.Ordinal).ToArray(),
            // Shelf seeds reserve a complete horizontal run for identical
            // items before allowing unrelated types to consume that row.
            GreedyShelfGroups(p, baseOcc, categoryFirst, allowHorizontal: false),
            GreedyShelfGroups(p, baseOcc, categoryFirst, allowHorizontal: true),
            GreedyFirstFit(p, baseOcc, categoryFirst, false),
            GreedyFirstFit(p, baseOcc, categoryFirst, true)
        };
        if (!OutOfTime())
        {
            foreach (var order in new[] { categoryFirst, difficultyOrdered, areaFirst })
            foreach (PackingHeuristic heuristic in Enum.GetValues<PackingHeuristic>())
            {
                if (OutOfTime()) { timedOut = true; break; }
                seedCandidates.Add(GreedyMaxRects(p, baseOcc, order, heuristic, densityMode == "dense"));
            }
        }

        foreach (var seed in seedCandidates)
        {
            if (seed == null) continue;
            var occ = BuildOccupied(p, seed);
            var score = Score(p, p.Fixed.Concat(seed).ToArray(), occ);
            if (!complete || CompareForProblem(p, score, best.Score) > 0)
            {
                movable = seed; complete = true;
                best = new State { Occupied = occ, Placements = p.Fixed.Concat(seed).ToList(), Score = score };
            }
        }
        if (!OutOfTime() && original != null && original.Count == p.Items.Count)
        {
            var improved = ImproveCompleteLayout(p, original, sw);
            if (improved != null)
            {
                var improvedScore = Evaluate(p, improved);
                if (!complete || CompareForProblem(p, improvedScore, best.Score) > 0)
                {
                    movable = improved;
                    complete = true;
                    var improvedOcc = BuildOccupied(p, improved);
                    best = new State { Occupied = improvedOcc, Placements = p.Fixed.Concat(improved).ToList(), Score = improvedScore };
                }
            }
        }
        if (!complete)
        {
            var fallback = OriginalLayout(p);
            if (fallback != null)
            {
                var occ = (bool[,])baseOcc.Clone(); foreach (var q in fallback) Mark(occ, q, true);
                var all = p.Fixed.Concat(fallback).ToArray();
                sw.Stop();
                var (holes, narrow) = FragmentMetrics(p, occ);
                return new(true, fallback, Score(p, all, occ), true, timedOut, sw.Elapsed, holes, narrow);
            }
        }
        sw.Stop();
        var metrics = FragmentMetrics(p, best.Occupied);
        Diagnostic = $"mode={densityMode}, occupancy={occupancy:P1}; {Diagnostic}";
        return new(complete, movable, best.Score, false, timedOut, sw.Elapsed, metrics.holes, metrics.narrow);
    }

    private sealed record AtomicBlock(string Key, string Category,
        IReadOnlyList<(LayoutItem item, Orientation orientation, int dx, int dy)> Entries,
        int Width, int Height, int Order);

    private sealed class BlockState
    {
        public bool[,] Occupied = new bool[0, 0];
        public List<Placement> Placements = new();
        public int Bottom;
        public int Raggedness;
        public int CategoryPenalty;
        public string Tie = string.Empty;
        public string VisualTie = string.Empty;
    }

    // Exact item types are indivisible visual blocks. Unlike the old shelf
    // planner, every block is placed against the current skyline rather than
    // advancing a global row Y. Short blocks can fill space beside tall blocks.
    private static Placement[]? AtomicSkylineBlocks(LayoutProblem p, bool[,] baseOcc,
        IReadOnlyList<LayoutItem> items, bool portraitOnly, out string diagnostic)
    {
        diagnostic = "building";
        var blocks = new List<AtomicBlock>();
        int order = 0;
        foreach (var exact in items.GroupBy(i => ItemTypeKey(i.StableId), StringComparer.Ordinal))
        {
            // State-dependent copies may share an identifier while exposing
            // different shapes. They remain one contiguous family, but each
            // copy uses its own verified game orientation and mask.
            var members = exact.OrderBy(i => i.StableId, StringComparer.Ordinal).ToArray();
            var entries = new List<(LayoutItem item, Orientation orientation, int dx, int dy)>();
            int x = 0, y = 0, rowHeight = 0, width = 0;
            foreach (var item in members)
            {
                var orientation = PreferredOrientations(item, portraitOnly).FirstOrDefault();
                if (orientation == null) { diagnostic = $"no {(portraitOnly ? "portrait" : "legal")} orientation for {exact.Key}/{item.StableId}"; return null; }
                if (orientation.Mask.Width > p.Width) { diagnostic = $"item wider than container: {exact.Key}/{item.StableId}"; return null; }
                if (x > 0 && x + orientation.Mask.Width > p.Width) { y += rowHeight; x = 0; rowHeight = 0; }
                if (y + orientation.Mask.Height > p.Height) { diagnostic = $"wrapped family taller than container: {exact.Key}"; return null; }
                entries.Add((item, orientation, x, y));
                x += orientation.Mask.Width; width = Math.Max(width, x); rowHeight = Math.Max(rowHeight, orientation.Mask.Height);
            }
            int height = y + rowHeight;
            blocks.Add(new AtomicBlock(exact.Key, members[0].PrimaryCategory, entries, width, height, order++));
        }        diagnostic = $"built {blocks.Count} mixed-shape families";
        var states = new List<BlockState>
        {
            new() { Occupied = (bool[,])baseOcc.Clone() }
        };
        const int blockBeam = 24;
        const int positionsPerState = 18;
        foreach (var block in blocks.OrderBy(b => b.Order))
        {
            var next = new List<BlockState>();
            foreach (var state in states)
            {
                // Only the first legal skyline height for each X can dominate a
                // higher placement at that same X. This cuts a 30x30 scan from
                // hundreds of candidates to at most 30 deterministic frontiers.
                var positions = new List<(int x, int y, int bottom, int holes, int categoryDistance, int edge, string tie)>();
                for (int x = 0; x <= p.Width - block.Width; x++)
                for (int y = 0; y <= p.Height - block.Height; y++)
                {
                    bool valid = true;
                    foreach (var e in block.Entries)
                        if (!Fits(p, state.Occupied, e.orientation.Mask, x + e.dx, y + e.dy))
                        { valid = false; break; }
                    if (!valid) continue;
                    int bottom = y + block.Height;
                    int holes = SkylineHoles(state.Occupied, x, y, block.Width);
                    int categoryDistance = CategoryBlockDistance(state.Placements, block.Category, x, y, block.Width, block.Height);
                    int edge = x == 0 ? 1 : 0;
                    positions.Add((x, y, bottom, holes, categoryDistance, edge, $"{y:D3},{x:D3}"));
                    break;
                }

                foreach (var pos in positions.OrderBy(v => v.categoryDistance).ThenBy(v => v.bottom)
                    .ThenBy(v => v.holes).ThenByDescending(v => v.edge).ThenBy(v => v.y).ThenBy(v => v.x)
                    .ThenBy(v => v.tie, StringComparer.Ordinal).Take(positionsPerState))
                {
                    var occ = (bool[,])state.Occupied.Clone();
                    var placed = new List<Placement>(state.Placements);
                    foreach (var e in block.Entries)
                    {
                        var q = new Placement(e.item.StableId, pos.x + e.dx, pos.y + e.dy,
                            e.orientation.GameOrientation, e.orientation.Mask,
                            e.item.PrimaryCategory, e.orientation.Rotated);
                        Mark(occ, q, true); placed.Add(q);
                    }
                    next.Add(new BlockState
                    {
                        Occupied = occ,
                        Placements = placed,
                        Bottom = Math.Max(state.Bottom, pos.bottom),
                        Raggedness = 0,
                        CategoryPenalty = state.CategoryPenalty + pos.categoryDistance,
                        Tie = string.Empty,
                        VisualTie = state.VisualTie + $"{pos.y:D3},{pos.x:D3};"
                    });
                }
            }
            if (next.Count == 0) { diagnostic = $"no skyline position for {block.Key}"; return null; }
            states = next.OrderBy(s => s.CategoryPenalty).ThenBy(s => s.Bottom)
                .ThenBy(s => s.VisualTie, StringComparer.Ordinal).Take(blockBeam).ToList();
        }
        return states.OrderBy(s => s.CategoryPenalty).ThenBy(s => s.Bottom)
            .ThenBy(s => s.VisualTie, StringComparer.Ordinal).First().Placements
            .OrderBy(x => x.StableId, StringComparer.Ordinal).ToArray();
    }

    private static int CategoryBlockDistance(IReadOnlyList<Placement> placed, string category,
        int x, int y, int width, int height)
    {
        int best = int.MaxValue;
        bool found = false;
        int right = x + width, bottom = y + height;
        foreach (var q in placed)
        {
            if (!StringComparer.Ordinal.Equals(q.Category, category)) continue;
            found = true;
            int qRight = q.X + q.Mask.Width, qBottom = q.Y + q.Mask.Height;
            int dx = Math.Max(0, Math.Max(q.X - right, x - qRight));
            int dy = Math.Max(0, Math.Max(q.Y - bottom, y - qBottom));
            best = Math.Min(best, dx + dy);
        }
        return found ? best : 0;
    }
    private static int SkylineHoles(bool[,] occupied, int x, int y, int width)
    {
        int holes = 0;
        for (int gx = x; gx < x + width; gx++)
            for (int gy = 0; gy < y; gy++)
                if (!occupied[gx, gy]) holes++;
        return holes;
    }
    private static Placement[]? GreedyShelfGroups(LayoutProblem p, bool[,] baseOcc,
        IReadOnlyList<LayoutItem> items, bool allowHorizontal)
    {
        var occ = (bool[,])baseOcc.Clone();
        var result = new List<Placement>();
        foreach (var group in items.GroupBy(ShelfCohortKey, StringComparer.Ordinal))
        {
            var members = group.OrderBy(i => i.StableId, StringComparer.Ordinal).ToArray();
            var row = FindWholeRowPlan(p, occ, members, portraitOnly: true);
            if (row == null && allowHorizontal)
                row = FindWholeRowPlan(p, occ, members, portraitOnly: false);
            if (row != null)
            {
                foreach (var q in row) { Mark(occ, q, true); result.Add(q); }
                continue;
            }

            foreach (var item in members)
            {
                var chosen = BestShelfPlacement(p, occ, result, item, portraitOnly: true);
                if (chosen == null && allowHorizontal)
                    chosen = BestShelfPlacement(p, occ, result, item, portraitOnly: false);
                if (chosen == null) return null;
                Mark(occ, chosen, true); result.Add(chosen);
            }
        }
        return result.OrderBy(x => x.StableId, StringComparer.Ordinal).ToArray();
    }

    private static Placement[]? FindWholeRowPlan(LayoutProblem p, bool[,] occupied,
        IReadOnlyList<LayoutItem> items, bool portraitOnly)
    {
        if (items.Count == 0) return Array.Empty<Placement>();
        var plans = new List<(Placement[] placements, int orientationBand, int y, int x, int rotations)>();
        foreach (var leader in PreferredOrientations(items[0], portraitOnly))
        {
            var selected = new Orientation[items.Count]; selected[0] = leader;
            bool compatible = true;
            for (int i = 1; i < items.Count; i++)
            {
                selected[i] = PreferredOrientations(items[i], portraitOnly)
                    .FirstOrDefault(o => o.Mask.Signature == leader.Mask.Signature)
                    ?? PreferredOrientations(items[i], portraitOnly)
                        .FirstOrDefault(o => o.Mask.Width == leader.Mask.Width && o.Mask.Height == leader.Mask.Height)!;
                if (selected[i] == null) { compatible = false; break; }
            }
            if (!compatible) continue;
            int width = selected.Sum(o => o.Mask.Width);
            if (width > p.Width) continue;
            int height = selected.Max(o => o.Mask.Height);
            for (int y = 0; y <= p.Height - height; y++)
            for (int x = 0; x <= p.Width - width; x++)
            {
                var temp = (bool[,])occupied.Clone();
                var placements = new Placement[items.Count];
                int cursor = x; bool fits = true;
                for (int i = 0; i < items.Count; i++)
                {
                    var o = selected[i];
                    if (!Fits(p, temp, o.Mask, cursor, y)) { fits = false; break; }
                    placements[i] = new Placement(items[i].StableId, cursor, y, o.GameOrientation,
                        o.Mask, items[i].PrimaryCategory, o.Rotated);
                    Mark(temp, placements[i], true); cursor += o.Mask.Width;
                }
                if (fits)
                {
                    int band = leader.Mask.Height > leader.Mask.Width ? 0 : leader.Mask.Height == leader.Mask.Width ? 1 : 2;
                    plans.Add((placements, band, y, x, placements.Count(q => q.Rotated)));
                }
            }
        }
        return plans.OrderBy(v => v.orientationBand).ThenBy(v => v.y).ThenBy(v => v.x)
            .ThenBy(v => v.rotations).ThenBy(v => PlacementKey(v.placements), StringComparer.Ordinal)
            .Select(v => v.placements).FirstOrDefault();
    }

    private static Placement? BestShelfPlacement(LayoutProblem p, bool[,] occupied,
        IReadOnlyList<Placement> placed, LayoutItem item, bool portraitOnly)
    {
        var candidates = new List<(Placement placement, int band, int row, int horizontal, int contact)>();
        foreach (var o in PreferredOrientations(item, portraitOnly))
        for (int y = 0; y <= p.Height - o.Mask.Height; y++)
        for (int x = 0; x <= p.Width - o.Mask.Width; x++)
        {
            if (!Fits(p, occupied, o.Mask, x, y)) continue;
            var q = new Placement(item.StableId, x, y, o.GameOrientation, o.Mask, item.PrimaryCategory, o.Rotated);
            int band = o.Mask.Height > o.Mask.Width ? 0 : o.Mask.Height == o.Mask.Width ? 1 : 2;
            candidates.Add((q, band, SameTypeRowMates(placed, q), HorizontalTypeContact(placed, q), TypeContact(placed, q)));
        }
        return candidates.OrderBy(v => v.band).ThenByDescending(v => v.row)
            .ThenByDescending(v => v.horizontal).ThenByDescending(v => v.contact)
            .ThenBy(v => v.placement.Y).ThenBy(v => v.placement.X)
            .ThenBy(v => v.placement.Rotated).ThenBy(v => v.placement.GameOrientation)
            .Select(v => v.placement).FirstOrDefault();
    }

    private static IEnumerable<Orientation> PreferredOrientations(LayoutItem item, bool portraitOnly)
    {
        var ordered = item.Orientations.OrderByDescending(o => o.Mask.Height > o.Mask.Width)
            .ThenByDescending(o => o.Mask.Height - o.Mask.Width)
            .ThenBy(o => o.Rotated).ThenBy(o => o.GameOrientation);
        return portraitOnly ? ordered.Where(o => o.Mask.Height >= o.Mask.Width) : ordered;
    }

    private static Placement[]? GreedyMaxRects(LayoutProblem p, bool[,] baseOcc,
        IReadOnlyList<LayoutItem> items, PackingHeuristic heuristic, bool dense)
    {
        var occ = (bool[,])baseOcc.Clone();
        var free = BuildFreeRectangles(p, occ);
        var result = new List<Placement>();

        foreach (var item in items)
        {
            var candidates = new List<(Placement p, int orient, int a, int b, int c, int d)>();
            foreach (var o in item.Orientations.OrderBy(x => x.GameOrientation))
            for (int y = 0; y <= p.Height - o.Mask.Height; y++)
            for (int x = 0; x <= p.Width - o.Mask.Width; x++)
            {
                if (!Fits(p, occ, o.Mask, x, y)) continue;
                var q = new Placement(item.StableId, x, y, o.GameOrientation, o.Mask, item.PrimaryCategory, o.Rotated);
                var containers = free.Where(r => r.Contains(x, y, o.Mask.Width, o.Mask.Height)).ToArray();
                int areaFit = containers.Length == 0 ? int.MaxValue / 4 :
                    containers.Min(r => r.Area - o.Mask.Width * o.Mask.Height);
                int shortFit = containers.Length == 0 ? int.MaxValue / 4 :
                    containers.Min(r => Math.Min(r.Width - o.Mask.Width, r.Height - o.Mask.Height));
                int longFit = containers.Length == 0 ? int.MaxValue / 4 :
                    containers.Min(r => Math.Max(r.Width - o.Mask.Width, r.Height - o.Mask.Height));
                int contact = OccupiedContact(p, occ, q);
                int categoryContact = CategoryContact(result, q);
                int orient = o.Mask.Height > o.Mask.Width ? 0 : o.Mask.Height == o.Mask.Width ? 1 : 2;
                var metrics = heuristic switch
                {
                    PackingHeuristic.BestShortSideFit => (shortFit, longFit, y, x),
                    PackingHeuristic.BestAreaFit => (areaFit, shortFit, y, x),
                    PackingHeuristic.BottomLeft => (y, x, areaFit, shortFit),
                    PackingHeuristic.ContactPoint => (-contact, y, x, areaFit),
                    _ => (-categoryContact, y, x, areaFit)
                };
                candidates.Add((q, orient, metrics.Item1, metrics.Item2, metrics.Item3, metrics.Item4));
            }
            if (candidates.Count == 0) return null;
            var ranked = dense
                ? candidates.OrderBy(v => v.a).ThenBy(v => v.b).ThenBy(v => v.c).ThenBy(v => v.d).ThenBy(v => v.orient)
                : candidates.OrderBy(v => v.orient).ThenBy(v => v.a).ThenBy(v => v.b).ThenBy(v => v.c).ThenBy(v => v.d);
            var chosen = ranked.ThenBy(v => v.p.GameOrientation)
                .ThenBy(v => v.p.StableId, StringComparer.Ordinal).First().p;
            result.Add(chosen);
            Mark(occ, chosen, true);
            foreach (var cell in chosen.Mask.Cells)
                SubtractCell(free, chosen.X + cell.X, chosen.Y + cell.Y);
        }
        return result.OrderBy(x => x.StableId, StringComparer.Ordinal).ToArray();
    }

    private static int OccupiedContact(LayoutProblem p, bool[,] occ, Placement placement)
    {
        int contact = 0;
        foreach (var c in placement.Mask.Cells)
        foreach (var d in new[] { new Cell(1, 0), new Cell(-1, 0), new Cell(0, 1), new Cell(0, -1) })
        {
            int x = placement.X + c.X + d.X, y = placement.Y + c.Y + d.Y;
            if ((uint)x >= p.Width || (uint)y >= p.Height || occ[x, y] || !p.Usable[x, y]) contact++;
        }
        return contact;
    }

    private static List<FreeRect> BuildFreeRectangles(LayoutProblem p, bool[,] occ)
    {
        var free = new List<FreeRect> { new(0, 0, p.Width, p.Height) };
        for (int y = 0; y < p.Height; y++)
        for (int x = 0; x < p.Width; x++)
            if (occ[x, y] || !p.Usable[x, y]) SubtractCell(free, x, y);
        return free;
    }

    private static void SubtractCell(List<FreeRect> free, int x, int y)
    {
        var next = new List<FreeRect>();
        foreach (var r in free)
        {
            if (x < r.X || x >= r.Right || y < r.Y || y >= r.Bottom)
            {
                next.Add(r);
                continue;
            }
            if (x > r.X) next.Add(new FreeRect(r.X, r.Y, x - r.X, r.Height));
            if (x + 1 < r.Right) next.Add(new FreeRect(x + 1, r.Y, r.Right - x - 1, r.Height));
            if (y > r.Y) next.Add(new FreeRect(r.X, r.Y, r.Width, y - r.Y));
            if (y + 1 < r.Bottom) next.Add(new FreeRect(r.X, y + 1, r.Width, r.Bottom - y - 1));
        }
        var valid = next.Where(r => r.Width > 0 && r.Height > 0).Distinct().ToList();
        free.Clear();
        for (int i = 0; i < valid.Count; i++)
        {
            bool contained = false;
            for (int j = 0; j < valid.Count && !contained; j++)
                if (i != j && valid[j].Contains(valid[i])) contained = true;
            if (!contained) free.Add(valid[i]);
        }
        free.Sort((a, b) =>
        {
            int c = a.Y.CompareTo(b.Y); if (c != 0) return c;
            c = a.X.CompareTo(b.X); if (c != 0) return c;
            c = a.Area.CompareTo(b.Area); if (c != 0) return c;
            c = a.Width.CompareTo(b.Width); return c != 0 ? c : a.Height.CompareTo(b.Height);
        });
    }

    private static Placement[]? GreedyFirstFit(LayoutProblem p, bool[,] baseOcc, IReadOnlyList<LayoutItem> items, bool reverseX)
    {
        var occ = (bool[,])baseOcc.Clone();
        var result = new List<Placement>();
        foreach (var item in items)
        {
            var candidates = new List<(Placement p, int band, int row, int horizontal, int contact, int step)>();
            foreach (var o in PreferredOrientations(item, portraitOnly: false))
            for (int y = 0; y <= p.Height - o.Mask.Height; y++)
            for (int step = 0; step <= p.Width - o.Mask.Width; step++)
            {
                int x = reverseX ? p.Width - o.Mask.Width - step : step;
                if (!Fits(p, occ, o.Mask, x, y)) continue;
                var q = new Placement(item.StableId, x, y, o.GameOrientation, o.Mask, item.PrimaryCategory, o.Rotated);
                int band = o.Mask.Height > o.Mask.Width ? 0 : o.Mask.Height == o.Mask.Width ? 1 : 2;
                candidates.Add((q, band, SameTypeRowMates(result, q), HorizontalTypeContact(result, q), TypeContact(result, q), step));
            }
            var chosen = candidates.OrderBy(v => v.band).ThenByDescending(v => v.row)
                .ThenByDescending(v => v.horizontal).ThenByDescending(v => v.contact)
                .ThenBy(v => v.p.Y).ThenBy(v => v.step).ThenBy(v => v.p.GameOrientation)
                .Select(v => v.p).FirstOrDefault();
            if (chosen == null) return null;
            Mark(occ, chosen, true); result.Add(chosen);
        }
        return result.OrderBy(x => x.StableId, StringComparer.Ordinal).ToArray();
    }

    // Dense local search keeps the already legal occupied-cell topology and
    // reassigns equal-shape items to existing slots. This joins split families
    // without requiring empty staging cells or risking a partial layout.
    private static Placement[]? DenseSlotReassignment(LayoutProblem p, IReadOnlyList<Placement> original)
    {
        Placement[] current = original.OrderBy(x => x.StableId, StringComparer.Ordinal).ToArray();
        try { _ = BuildOccupied(p, current); } catch { return null; }
        var itemById = p.Items.ToDictionary(x => x.StableId, StringComparer.Ordinal);
        var result = new List<Placement>(current.Length);
        foreach (var shapeClass in current.GroupBy(x => x.Mask.Signature, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var slots = shapeClass.OrderBy(x => x.Y).ThenBy(x => x.X).ThenBy(x => x.StableId, StringComparer.Ordinal).ToArray();
            var items = VisualOrder(p, shapeClass.Select(x => itemById[x.StableId])).ToArray();
            for (int i = 0; i < slots.Length; i++)
            {
                var item = items[i];
                var source = current.Single(x => x.StableId == item.StableId);
                var orientation = item.Orientations.Where(o => StringComparer.Ordinal.Equals(o.Mask.Signature, slots[i].Mask.Signature))
                    .OrderBy(o => o.GameOrientation == source.GameOrientation ? 0 : 1).ThenBy(o => o.Rotated).ThenBy(o => o.GameOrientation).FirstOrDefault();
                if (orientation == null) return null;
                result.Add(new Placement(item.StableId, slots[i].X, slots[i].Y, orientation.GameOrientation,
                    orientation.Mask, item.PrimaryCategory, orientation.Rotated));
            }
        }
        var candidate = result.OrderBy(x => x.StableId, StringComparer.Ordinal).ToArray();
        try { _ = BuildOccupied(p, candidate); return candidate; } catch { return null; }
    }
    private Placement[]? ImproveCompleteLayout(LayoutProblem p, IReadOnlyList<Placement> original, Stopwatch sw)
    {
        Placement[] current = original.OrderBy(x => x.StableId, StringComparer.Ordinal).ToArray();
        bool[,] validation;
        try { validation = BuildOccupied(p, current); }
        catch (Exception ex) { Diagnostic = ex.Message; return null; }
        var currentScore = Score(p, p.Fixed.Concat(current).ToArray(), validation);
        var itemById = p.Items.ToDictionary(x => x.StableId, StringComparer.Ordinal);
        for (int pass = 0; pass < 4 && sw.Elapsed < TimeBudget; pass++)
        {
            bool changed = false;
            foreach (var id in current.Select(x => x.StableId).OrderBy(x => x, StringComparer.Ordinal).ToArray())
            {
                if (sw.Elapsed >= TimeBudget) break;
                var item = itemById[id];
                int index = Array.FindIndex(current, x => x.StableId == id);
                var old = current[index];
                var without = current.Where((_, i) => i != index).ToArray();
                var occ = BuildOccupied(p, without);
                Placement bestPlacement = old;
                LayoutScore bestScore = currentScore;
                foreach (var o in item.Orientations.OrderBy(x => x.Rotated).ThenBy(x => x.GameOrientation))
                for (int y = 0; y <= p.Height - o.Mask.Height; y++)
                for (int x = 0; x <= p.Width - o.Mask.Width; x++)
                {
                    if (sw.Elapsed >= TimeBudget) break;
                    if (!Fits(p, occ, o.Mask, x, y)) continue;
                    var q = new Placement(id, x, y, o.GameOrientation, o.Mask, item.PrimaryCategory, o.Rotated);
                    var trial = (Placement[])current.Clone(); trial[index] = q;
                    var trialOcc = (bool[,])occ.Clone(); Mark(trialOcc, q, true);
                    var score = Score(p, p.Fixed.Concat(trial).ToArray(), trialOcc);
                    if (CompareForProblem(p, score, bestScore) > 0) { bestScore = score; bestPlacement = q; }
                }
                if (!SamePlacement(old, bestPlacement))
                {
                    current[index] = bestPlacement;
                    currentScore = bestScore;
                    changed = true;
                }
            }
            if (!changed) break;
        }
        return current.OrderBy(x => x.StableId, StringComparer.Ordinal).ToArray();
    }

    private static bool SamePlacement(Placement a, Placement b) => a.StableId == b.StableId && a.X == b.X && a.Y == b.Y && a.GameOrientation == b.GameOrientation;

    private static bool[,] BuildOccupied(LayoutProblem p, IReadOnlyList<Placement> movable)
    {
        var occ = new bool[p.Width, p.Height];
        for (int y = 0; y < p.Height; y++) for (int x = 0; x < p.Width; x++) occ[x, y] = !p.Usable[x, y];
        var owners = new Dictionary<(int x, int y), string>();
        foreach (var q in p.Fixed.Concat(movable))
        {
            foreach (var c in q.Mask.Cells)
            {
                int x = q.X + c.X, y = q.Y + c.Y;
                if ((uint)x >= p.Width || (uint)y >= p.Height)
                    throw new InvalidOperationException($"Out of bounds: {q.StableId} at {q.X},{q.Y} o={q.GameOrientation} mask={q.Mask.Width}x{q.Mask.Height} cell={x},{y}");
                if (!p.Usable[x, y])
                    throw new InvalidOperationException($"Disabled cell: {q.StableId} at {x},{y}");
                if (owners.TryGetValue((x, y), out string? owner))
                    throw new InvalidOperationException($"Overlap cell {x},{y}: {q.StableId} at {q.X},{q.Y} o={q.GameOrientation} mask={q.Mask.Width}x{q.Mask.Height} with {owner}");
            }
            foreach (var c in q.Mask.Cells) owners[(q.X + c.X, q.Y + c.Y)] = q.StableId;
            Mark(occ, q, true);
        }
        return occ;
    }

    public static (int holes, int narrow, int raggedness) EvaluateSpaceMetrics(LayoutProblem p, IReadOnlyList<Placement> movable)
    {
        var occ = BuildOccupied(p, movable);
        var f = FragmentMetrics(p, occ);
        return (f.holes, f.narrow, Raggedness(p, occ));
    }
    public static LayoutScore Evaluate(LayoutProblem p, IReadOnlyList<Placement> movable)
    {
        ValidateProblem(p);
        var occ = new bool[p.Width, p.Height];
        for (int y = 0; y < p.Height; y++) for (int x = 0; x < p.Width; x++) occ[x, y] = !p.Usable[x, y];
        var all = p.Fixed.Concat(movable).ToArray();
        foreach (var q in all) Mark(occ, q, true);
        return Score(p, all, occ);
    }

    private IEnumerable<Placement> EnumerateCandidates(LayoutProblem p, State state, LayoutItem item, Dictionary<string, int> categoryArea)
    {
        var list = new List<(Placement placement, int verticalBand, int rowMates, int horizontalTypeContact, int typeContact, int categoryContact)>();
        foreach (var o in item.Orientations.OrderBy(x => x.GameOrientation))
        for (int y = 0; y <= p.Height - o.Mask.Height; y++)
        for (int x = 0; x <= p.Width - o.Mask.Width; x++)
        {
            if (OutOfTime()) return list.Select(c => c.placement);
            if (!Fits(p, state.Occupied, o.Mask, x, y)) continue;
            var q = new Placement(item.StableId, x, y, o.GameOrientation, o.Mask, item.PrimaryCategory, o.Rotated);
            int verticalBand = o.Mask.Height > o.Mask.Width ? 0 : o.Mask.Height == o.Mask.Width ? 1 : 2;
            list.Add((q, verticalBand, SameTypeRowMates(state.Placements, q), HorizontalTypeContact(state.Placements, q),
                TypeContact(state.Placements, q), CategoryContact(state.Placements, q)));
        }

        // Fragmentation remains in the full state score. Y/X already precede
        // fragment metrics in candidate ordering, so scanning every coordinate
        // here only added allocation and repeated flood fills.
        return list.OrderBy(c => c.verticalBand)
            .ThenByDescending(c => c.rowMates).ThenByDescending(c => c.horizontalTypeContact)
            .ThenByDescending(c => c.typeContact).ThenByDescending(c => c.categoryContact)
            .ThenBy(c => c.placement.Y).ThenBy(c => c.placement.X)
            .ThenBy(c => c.placement.GameOrientation).ThenBy(c => c.placement.StableId, StringComparer.Ordinal)
            .Select(c => c.placement);
    }
    private static int CategoryAnchor(LayoutProblem p, int categoryIndex)
    {
        if (categoryIndex == int.MaxValue || p.CategoryOrder.Count <= 1) return 0;
        return (int)Math.Round((p.Width - 1) * categoryIndex / (double)(p.CategoryOrder.Count - 1));
    }

    private static IEnumerable<LayoutItem> VisualOrder(LayoutProblem p, IEnumerable<LayoutItem> items)
    {
        var list = items.ToArray();
        var groupSize = list.GroupBy(i => VisualGroupKey(i), StringComparer.Ordinal)
            .ToDictionary(g => g.Key,
                g => (area: g.Max(i => i.Area), edge: g.Max(i => i.Orientations.Max(o => Math.Max(o.Mask.Width, o.Mask.Height)))),
                StringComparer.Ordinal);
        return list.OrderBy(i => i.VisualPriority)
            .ThenBy(i => p.CategoryOrder.TryGetValue(i.PrimaryCategory, out int r) ? r : int.MaxValue)
            // Order whole same-type runs by their largest footprint, then keep
            // every member of that run adjacent and large-first internally.
            .ThenByDescending(i => groupSize[VisualGroupKey(i)].area)
            .ThenByDescending(i => groupSize[VisualGroupKey(i)].edge)
            .ThenBy(i => ItemTypeKey(i.StableId), StringComparer.Ordinal)
            .ThenByDescending(i => i.Area)
            .ThenByDescending(i => i.Complexity)
            .ThenBy(i => i.StableId, StringComparer.Ordinal);
    }

    private static string VisualGroupKey(LayoutItem i) => ItemTypeKey(i.StableId);

    private static string ShelfCohortKey(LayoutItem i)
    {
        var o = i.Orientations.OrderByDescending(x => x.Mask.Height > x.Mask.Width)
            .ThenByDescending(x => x.Mask.Height - x.Mask.Width)
            .ThenBy(x => x.Rotated).ThenBy(x => x.GameOrientation).First();
        return i.PrimaryCategory + "\u001e" + o.Mask.Width + "x" + o.Mask.Height + "a" + o.Mask.Area;
    }

    private static string ShelfCohortKey(Placement p)
    {
        int shortSide = Math.Min(p.Mask.Width, p.Mask.Height);
        int longSide = Math.Max(p.Mask.Width, p.Mask.Height);
        return p.Category + "\u001e" + shortSide + "x" + longSide + "a" + p.Mask.Area;
    }
    private static int EstimateLegalPositions(LayoutProblem p, bool[,] occupied, LayoutItem item)
    {
        int count = 0;
        foreach (var o in item.Orientations)
            for (int y = 0; y <= p.Height - o.Mask.Height; y++) for (int x = 0; x <= p.Width - o.Mask.Width; x++)
                if (Fits(p, occupied, o.Mask, x, y)) count++;
        return count;
    }

    private static bool Fits(LayoutProblem p, bool[,] occupied, ShapeMask mask, int x, int y)
    {
        foreach (var c in mask.Cells)
        {
            int gx = x + c.X, gy = y + c.Y;
            if ((uint)gx >= p.Width || (uint)gy >= p.Height || !p.Usable[gx, gy] || occupied[gx, gy]) return false;
        }
        return true;
    }

    private static void Mark(bool[,] occupied, Placement p, bool value)
    {
        foreach (var c in p.Mask.Cells) occupied[p.X + c.X, p.Y + c.Y] = value;
    }

    private static int TypeContact(IReadOnlyList<Placement> placed, Placement q)
    {
        string type = ItemTypeKey(q.StableId);
        int contact = 0;
        foreach (var p in placed.Where(p => ItemTypeKey(p.StableId) == type))
        foreach (var a in p.Mask.Cells)
        foreach (var b in q.Mask.Cells)
            if (Math.Abs((p.X + a.X) - (q.X + b.X)) + Math.Abs((p.Y + a.Y) - (q.Y + b.Y)) == 1) contact++;
        return contact;
    }
    private static int SameTypeRowMates(IReadOnlyList<Placement> placed, Placement q)
    {
        string type = ItemTypeKey(q.StableId);
        return placed.Count(p => ItemTypeKey(p.StableId) == type && p.Y == q.Y && p.Mask.Height == q.Mask.Height);
    }

    private static int HorizontalTypeContact(IReadOnlyList<Placement> placed, Placement q)
    {
        string type = ItemTypeKey(q.StableId);
        int contact = 0;
        foreach (var p in placed.Where(p => ItemTypeKey(p.StableId) == type))
        foreach (var a in p.Mask.Cells)
        foreach (var b in q.Mask.Cells)
            if (p.Y + a.Y == q.Y + b.Y && Math.Abs((p.X + a.X) - (q.X + b.X)) == 1) contact++;
        return contact;
    }

    private static (int extraRuns, int rowPairs, int horizontalContact) ShelfMetrics(
        IReadOnlyList<Placement> movable, Func<Placement, string> key)
    {
        int extraRuns = 0, rowPairs = 0, horizontalContact = 0;
        foreach (var group in movable.GroupBy(key, StringComparer.Ordinal).Where(g => g.Count() > 1))
        {
            var a = group.OrderBy(p => p.StableId, StringComparer.Ordinal).ToArray();
            var parent = Enumerable.Range(0, a.Length).ToArray();
            int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
            void Union(int x, int y) { x = Find(x); y = Find(y); if (x != y) parent[y] = x; }
            for (int i = 0; i < a.Length; i++)
            for (int j = i + 1; j < a.Length; j++)
            {
                if (a[i].Y == a[j].Y) rowPairs++;
                bool touching = false;
                foreach (var ca in a[i].Mask.Cells)
                foreach (var cb in a[j].Mask.Cells)
                {
                    int dx = Math.Abs((a[i].X + ca.X) - (a[j].X + cb.X));
                    int dy = Math.Abs((a[i].Y + ca.Y) - (a[j].Y + cb.Y));
                    if (dx + dy != 1) continue;
                    touching = true;
                    if (dy == 0) horizontalContact++;
                }
                if (touching) Union(i, j);
            }            extraRuns += Enumerable.Range(0, a.Length).Select(Find).Distinct().Count() - 1;
        }
        return (extraRuns, rowPairs, horizontalContact);
    }

    private static int CategoryContact(IReadOnlyList<Placement> placed, Placement q)
    {
        int contact = 0;
        foreach (var p in placed.Where(p => p.Category == q.Category))
        foreach (var a in p.Mask.Cells)
        foreach (var b in q.Mask.Cells)
            if (Math.Abs((p.X + a.X) - (q.X + b.X)) + Math.Abs((p.Y + a.Y) - (q.Y + b.Y)) == 1) contact++;
        return contact;
    }

    private static LayoutScore Score(LayoutProblem p, IReadOnlyList<Placement> placements, bool[,] occ, bool includeStableTie = true)
    {
        int sameType = 0, sameCategory = 0, different = 0, consistent = 0;
        var byCell = new Dictionary<(int, int), Placement>();
        foreach (var q in placements)
            foreach (var c in q.Mask.Cells)
                byCell[(q.X + c.X, q.Y + c.Y)] = q;

        foreach (var kv in byCell)
        foreach (var d in new[] { (1, 0), (0, 1) })
        {
            if (!byCell.TryGetValue((kv.Key.Item1 + d.Item1, kv.Key.Item2 + d.Item2), out var n)) continue;
            if (kv.Value.StableId == n.StableId) continue;
            if (ItemTypeKey(kv.Value.StableId) == ItemTypeKey(n.StableId)) sameType++;
            if (kv.Value.Category == n.Category) sameCategory++; else different++;
        }

        var movable = placements.Where(x => x.Category != "~fixed").ToArray();
        var itemById = p.Items.ToDictionary(x => x.StableId, StringComparer.Ordinal);
        int priorityOrder = 0, categoryOrder = 0, largeFirst = 0;
        for (int i = 0; i < movable.Length; i++)
        for (int j = i + 1; j < movable.Length; j++)
        {
            var a = movable[i]; var b = movable[j];
            int linearA = a.Y * p.Width + a.X, linearB = b.Y * p.Width + b.X;
            var itemA = itemById[a.StableId]; var itemB = itemById[b.StableId];
            if (itemA.VisualPriority != itemB.VisualPriority)
                priorityOrder += (itemA.VisualPriority < itemB.VisualPriority) == (linearA < linearB) ? 1 : -1;
            int rankA = p.CategoryOrder.TryGetValue(itemA.PrimaryCategory, out int ca) ? ca : int.MaxValue;
            int rankB = p.CategoryOrder.TryGetValue(itemB.PrimaryCategory, out int cb) ? cb : int.MaxValue;
            if (rankA != rankB)
                categoryOrder += (rankA < rankB) == (linearA < linearB) ? 1 : -1;
            if (a.Category == b.Category && a.Mask.Area != b.Mask.Area)
                largeFirst += (a.Mask.Area > b.Mask.Area) == (linearA < linearB) ? 1 : -1;
        }

        foreach (var g in movable.GroupBy(x => ItemTypeKey(x.StableId), StringComparer.Ordinal))
            consistent += g.GroupBy(x => x.Mask.Signature, StringComparer.Ordinal).Max(x => x.Count());

        int vertical = movable.Count(x => x.Mask.Height > x.Mask.Width);
        var typeShelves = ShelfMetrics(movable, p => ItemTypeKey(p.StableId));
        var cohorts = ShelfMetrics(movable, ShelfCohortKey);
        int topLeft = -movable.Sum(x => x.Y * p.Width + x.X);
        int rowRegularity = -Raggedness(p, occ);
        int rotations = movable.Count(x => x.Rotated);
        var fm = FragmentMetrics(p, occ);
        return new LayoutScore(placements.Count, placements.Sum(x => x.Mask.Area),
            -typeShelves.extraRuns, typeShelves.rowPairs, typeShelves.horizontalContact,
            -cohorts.extraRuns, cohorts.rowPairs, cohorts.horizontalContact,
            consistent, vertical, priorityOrder,
            categoryOrder, largeFirst, sameType,
            sameCategory, topLeft, rowRegularity,
            -(fm.holes * 8 + fm.narrow), -different, -rotations,
            includeStableTie ? PlacementKey(placements) : string.Empty);
    }

    private static int Raggedness(LayoutProblem p, bool[,] occ)
    {
        int holesBeforeEdge = 0, edgeVariation = 0, previousRight = -1;
        for (int y = 0; y < p.Height; y++)
        {
            int right = -1;
            for (int x = 0; x < p.Width; x++)
                if (p.Usable[x, y] && occ[x, y]) right = x;
            if (right >= 0)
            {
                for (int x = 0; x <= right; x++)
                    if (p.Usable[x, y] && !occ[x, y]) holesBeforeEdge++;
                if (previousRight >= 0) edgeVariation += Math.Abs(previousRight - right);
                previousRight = right;
            }
        }
        return holesBeforeEdge * 4 + edgeVariation;
    }
    private static (int holes, int narrow) FragmentMetrics(LayoutProblem p, bool[,] occ)
    {
        var seen = new bool[p.Width, p.Height]; int holes = 0, narrow = 0;
        for (int y = 0; y < p.Height; y++) for (int x = 0; x < p.Width; x++)
        {
            if (seen[x, y] || occ[x, y] || !p.Usable[x, y]) continue;
            var q = new Queue<Cell>(); q.Enqueue(new(x, y)); seen[x, y] = true; int size = 0; bool edge = false;
            while (q.Count > 0)
            {
                var c = q.Dequeue(); size++; edge |= c.X == 0 || c.Y == 0 || c.X == p.Width - 1 || c.Y == p.Height - 1;
                foreach (var d in new[] { new Cell(1, 0), new Cell(-1, 0), new Cell(0, 1), new Cell(0, -1) })
                { int nx = c.X + d.X, ny = c.Y + d.Y; if ((uint)nx < p.Width && (uint)ny < p.Height && !seen[nx, ny] && !occ[nx, ny] && p.Usable[nx, ny]) { seen[nx, ny] = true; q.Enqueue(new(nx, ny)); } }
            }
            if (!edge) holes += size; if (size <= 2) narrow += size;
        }
        return (holes, narrow);
    }

    private static int EdgeRegularity(LayoutProblem p, bool[,] occ)
    {
        int score = 0;
        for (int y = 0; y < p.Height; y++) for (int x = 0; x < p.Width; x++) if (occ[x, y])
        { if (x == 0 || occ[x - 1, y]) score++; if (y == 0 || occ[x, y - 1]) score++; }
        return score;
    }

    private static string ItemTypeKey(string stableId)
    {
        int separator = stableId.IndexOf('\u001f');
        return separator < 0 ? stableId : stableId.Substring(0, separator);
    }

    private static string PlacementKey(IEnumerable<Placement> placements) => string.Join("|", placements.OrderBy(x => x.StableId, StringComparer.Ordinal)
        .Select(x => $"{x.StableId}:{x.X},{x.Y},{x.GameOrientation}"));

    private static Placement[]? OriginalLayout(LayoutProblem p)
    {
        var occ = new bool[p.Width, p.Height];
        for (int y = 0; y < p.Height; y++) for (int x = 0; x < p.Width; x++) occ[x, y] = !p.Usable[x, y];
        foreach (var f in p.Fixed) { if (!Fits(p, occ, f.Mask, f.X, f.Y)) return null; Mark(occ, f, true); }
        var result = new List<Placement>();
        foreach (var i in p.Items.OrderBy(i => i.StableId, StringComparer.Ordinal))
        {
            var o = i.Orientations.FirstOrDefault(o => o.GameOrientation == i.OriginalOrientation) ?? i.Orientations[0];
            if (!Fits(p, occ, o.Mask, i.OriginalX, i.OriginalY)) return null;
            var q = new Placement(i.StableId, i.OriginalX, i.OriginalY, o.GameOrientation, o.Mask,
                i.PrimaryCategory, o.Mask.Width > o.Mask.Height);
            Mark(occ, q, true); result.Add(q);
        }
        return result.ToArray();
    }

    public static string DescribeSplits(IReadOnlyList<Placement> placements)
    {
        var split = new List<string>();
        foreach (var group in placements.Where(p => p.Category != "~fixed")
                     .GroupBy(p => ItemTypeKey(p.StableId), StringComparer.Ordinal).Where(g => g.Count() > 1))
        {
            int runs = HorizontalRunCount(group.ToArray());
            if (runs > 1) split.Add(group.Key + "x" + runs);
        }
        return split.Count == 0 ? "none" : string.Join(",", split.OrderBy(x => x, StringComparer.Ordinal).Take(16));
    }

    private static int HorizontalRunCount(IReadOnlyList<Placement> a)
    {
        var parent = Enumerable.Range(0, a.Count).ToArray();
        int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
        void Union(int x, int y) { x = Find(x); y = Find(y); if (x != y) parent[y] = x; }
        for (int i = 0; i < a.Count; i++)
        for (int j = i + 1; j < a.Count; j++)
        {
            bool touching = false;
            foreach (var ca in a[i].Mask.Cells)
            foreach (var cb in a[j].Mask.Cells)
                if (Math.Abs((a[i].X + ca.X) - (a[j].X + cb.X)) + Math.Abs((a[i].Y + ca.Y) - (a[j].Y + cb.Y)) == 1)
                    touching = true;
            if (touching) Union(i, j);
        }
        return Enumerable.Range(0, a.Count).Select(Find).Distinct().Count();
    }
    public static void ValidateProblem(LayoutProblem p)
    {
        if (p.Width <= 0 || p.Height <= 0 || p.Usable.GetLength(0) != p.Width || p.Usable.GetLength(1) != p.Height) throw new ArgumentException("Invalid warehouse dimensions.");
        if (p.Items.Select(i => i.StableId).Distinct(StringComparer.Ordinal).Count() != p.Items.Count) throw new ArgumentException("Stable IDs must be unique.");
        if (p.Items.Any(i => i.Orientations.Count == 0)) throw new ArgumentException("Every item needs an orientation.");
    }
}





