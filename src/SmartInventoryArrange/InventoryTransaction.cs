using Il2Cpp;
using SmartInventoryArrange.Core;

namespace SmartInventoryArrange;

internal sealed class InventoryTransaction
{
    private sealed record Snap(GameItem Item, string StableId, GridShape OriginalShape, string Fingerprint, LayoutItem Model);
    private sealed record DiagnosticItem(string StableId, string Name, string ItemId, int Width, int Height, string Type, string[] Categories, int Orientation, bool CanRotate, bool Movable, int OriginalX, int OriginalY);
    private readonly GameGridInventory _inventory;
    private readonly List<Snap> _movable;
    private readonly List<(GameItem item, GridShape shape)> _allOriginal;
    private readonly int _originX;
    private readonly int _originY;
    private readonly IReadOnlyList<DiagnosticItem> _diagnostics;
    public LayoutProblem Problem { get; }
    public IReadOnlyList<Placement> OriginalPlacements { get; }
    public int Width => Problem.Width;
    public int Height => Problem.Height;
    public int MovableCount => _movable.Count;
    public int FixedCount => Problem.Fixed.Count;
    public int CategoryCount => Problem.Items.Select(i => i.PrimaryCategory).Distinct(StringComparer.Ordinal).Count();
    public int MultiCategoryCount => Problem.Items.Count(i => i.Categories.Count > 1);
    public int UnknownCategoryCount => Problem.Items.Count(i => i.PrimaryCategory == "~unknown");

    private InventoryTransaction(GameGridInventory inventory, List<Snap> movable, List<(GameItem, GridShape)> allOriginal,
        LayoutProblem problem, IReadOnlyList<Placement> originals, int originX, int originY, IReadOnlyList<DiagnosticItem> diagnostics)
    { _inventory = inventory; _movable = movable; _allOriginal = allOriginal; Problem = problem; OriginalPlacements = originals; _originX = originX; _originY = originY; _diagnostics = diagnostics; }

    public static InventoryTransaction Capture(GameGridInventory inventory)
    {
        if (!InventorySortHelper.MaySort(inventory) || !InventorySortHelper.CanSort(inventory)) throw new InvalidOperationException("Game reports this inventory cannot be sorted.");
        var container = inventory.inventoryShape ?? throw new InvalidOperationException("Inventory has no grid shape.");
        int width = container.width, height = container.height;
        var usable = new bool[width, height];
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++) usable[x, y] = container.Get(x + container.minX, y + container.minY) == 0;

        var movable = new List<Snap>(); var fixedItems = new List<Placement>(); var allOriginal = new List<(GameItem, GridShape)>();
        var raw = new List<(GameItem item, string id, GridShape shape, List<string> categories, bool movable)>();
        var children = inventory.childItems;
        for (int i = 0; i < children.Count; i++)
        {
            var item = children[i]; if (item == null || item.modifiedShape == null) continue;
            var shape = item.modifiedShape.Clone(); allOriginal.Add((item, shape));
            string id = StableId(item, i); var cats = Categories(item);
            bool canMove = item.MayRemove() && item.parentInventory == inventory;
            raw.Add((item, id, shape, cats, canMove));
        }
        var order = raw.SelectMany(r => r.categories).Distinct(StringComparer.Ordinal).OrderBy(CategoryRank).ThenBy(x => x, StringComparer.Ordinal)
            .Select((c, i) => (c, i)).ToDictionary(x => x.c, x => x.i, StringComparer.Ordinal);
        if (!order.ContainsKey("~unknown")) order["~unknown"] = order.Count;

        foreach (var r in raw.OrderBy(x => x.id, StringComparer.Ordinal))
        {
            string primary = r.categories.OrderBy(CategoryRank).ThenBy(x => x, StringComparer.Ordinal).FirstOrDefault() ?? "~unknown";
            var orientations = BuildOrientations(r.shape);
            var model = new LayoutItem { StableId = r.id, ItemId = r.item.identifier, PrimaryCategory = primary, Categories = r.categories,
                Orientations = orientations, OriginalMask = Mask(r.shape), OriginalOrientation = r.shape.orientation, OriginalX = r.shape.minX - container.minX, OriginalY = r.shape.minY - container.minY,
                VisualPriority = VisualPriority(primary) };
            if (r.movable) movable.Add(new Snap(r.item, r.id, r.shape, Fingerprint(r.item), model));
            else fixedItems.Add(new Placement(r.id, r.shape.minX - container.minX, r.shape.minY - container.minY, r.shape.orientation, Mask(r.shape), "~fixed", false));
        }
        var originals = movable.Select(s =>
        {
            // Equivalent collision masks are deduplicated, so a saved numeric
            // orientation may not exist in Model.Orientations. Preserve the
            // actual captured shape and its game orientation directly.
            var originalMask = Mask(s.OriginalShape);
            return new Placement(s.StableId, s.OriginalShape.minX - container.minX, s.OriginalShape.minY - container.minY,
                s.OriginalShape.orientation, originalMask, s.Model.PrimaryCategory, originalMask.Width > originalMask.Height);
        }).ToArray();
        var problem = new LayoutProblem { Width = width, Height = height, Usable = usable, Fixed = fixedItems,
            Items = movable.Select(s => s.Model).ToArray(), CategoryOrder = order };
        var diagnostics = raw.OrderBy(x => x.id, StringComparer.Ordinal).Select(x =>
        {
            string primary = x.categories.OrderBy(CategoryRank).ThenBy(v => v, StringComparer.Ordinal).FirstOrDefault() ?? "~unknown";
            var mask = Mask(x.shape);
            return new DiagnosticItem(x.id, x.item.identifier, x.item.identifier, mask.Width, mask.Height, primary,
                x.categories.ToArray(), x.shape.orientation, BuildOrientations(x.shape).Count > 1, x.movable,
                x.shape.minX - container.minX, x.shape.minY - container.minY);
        }).ToArray();
        DeterministicSolver.ValidateProblem(problem);
        return new(inventory, movable, allOriginal, problem, originals, container.minX, container.minY, diagnostics);
    }

    public string WriteDiagnosticSnapshot()
    {
        string logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MelonLoader", "Logs");
        Directory.CreateDirectory(logDirectory);
        string path = Path.Combine(logDirectory, "SmartInventoryArrange-Snapshot.log");
        var b = new System.Text.StringBuilder(512 + _diagnostics.Count * 160);
        b.AppendLine("========== Smart Arrange Snapshot ==========");
        b.Append("Time: ").AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
        b.Append("ContainerSize: ").Append(Width).Append('x').AppendLine(Height.ToString());
        b.Append("TotalItems: ").AppendLine(_diagnostics.Count.ToString());
        b.Append("MovableItems: ").AppendLine(MovableCount.ToString());
        b.Append("FixedItems: ").AppendLine(FixedCount.ToString());
        b.AppendLine("---------- Item Details ----------");
        int index = 0;
        foreach (var item in _diagnostics)
        {
            b.Append('[').Append((++index).ToString("D3")).Append("] Name: ").Append(item.Name)
             .Append(" | ItemId: ").Append(item.ItemId)
             .Append(" | Size: ").Append(item.Width).Append('x').Append(item.Height)
             .Append(" | Type: ").Append(item.Type)
             .Append(" | Categories: [").Append(string.Join(",", item.Categories)).Append(']')
             .Append(" | Orientation: ").Append(item.Orientation)
             .Append(" | CanRotate: ").Append(item.CanRotate)
             .Append(" | Movable: ").AppendLine(item.Movable.ToString());
        }
        b.AppendLine("========== Snapshot End ==========");
        b.AppendLine();
        File.AppendAllText(path, b.ToString(), new System.Text.UTF8Encoding(false));
        return path;
    }
    public void WriteResultSnapshot(IReadOnlyList<Placement> movablePlacements, string outcome)
    {
        string logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MelonLoader", "Logs");
        Directory.CreateDirectory(logDirectory);
        string path = Path.Combine(logDirectory, "SmartInventoryArrange-Snapshot.log");
        var positions = Problem.Fixed.Concat(movablePlacements)
            .ToDictionary(x => x.StableId, x => x, StringComparer.Ordinal);
        var b = new System.Text.StringBuilder(512 + _diagnostics.Count * 150);
        b.AppendLine("========== Smart Arrange Result ==========");
        b.Append("Time: ").AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
        b.Append("Outcome: ").AppendLine(outcome);
        b.Append("ContainerSize: ").Append(Width).Append('x').AppendLine(Height.ToString());
        int index = 0;
        foreach (var item in _diagnostics)
        {
            if (positions.TryGetValue(item.StableId, out var q))
            {
                b.Append('[').Append((++index).ToString("D3")).Append("] Name: ").Append(item.Name)
                 .Append(" | ItemId: ").Append(item.ItemId)
                 .Append(" | Coordinate: (").Append(q.X).Append(',').Append(q.Y).Append(')')
                 .Append(" | Size: ").Append(q.Mask.Width).Append('x').Append(q.Mask.Height)
                 .Append(" | Type: ").Append(item.Type)
                 .Append(" | Orientation: ").AppendLine(q.GameOrientation.ToString());
            }
            else
            {
                b.Append('[').Append((++index).ToString("D3")).Append("] Name: ").Append(item.Name)
                 .Append(" | ItemId: ").Append(item.ItemId)
                 .Append(" | Coordinate: (").Append(item.OriginalX).Append(',').Append(item.OriginalY).Append(')')
                 .Append(" | Size: ").Append(item.Width).Append('x').Append(item.Height)
                 .Append(" | Type: ").Append(item.Type)
                 .Append(" | Orientation: ").Append(item.Orientation)
                 .AppendLine(" | Status: original/unresolved");
            }
        }
        b.AppendLine("========== Result End ==========");
        b.AppendLine();
        File.AppendAllText(path, b.ToString(), new System.Text.UTF8Encoding(false));
    }
    public void Apply(IReadOnlyList<Placement> placements)
    {
        if (placements.Count != _movable.Count || placements.Select(p => p.StableId).Distinct(StringComparer.Ordinal).Count() != _movable.Count)
            throw new InvalidOperationException("Candidate does not contain every movable item exactly once.");
        VerifyUnchanged();
        var targets = new List<(Snap snap, GridShape target, Placement placement)>();
        var ignored = new Il2CppSystem.Collections.Generic.List<GameItem>();
        foreach (var s in _movable) ignored.Add(s.Item);
        foreach (var s in _movable.OrderBy(x => x.StableId, StringComparer.Ordinal))
        {
            var p = placements.Single(x => x.StableId == s.StableId);
            var target = new GridShapeBuilder(s.OriginalShape).SetOrientation(p.GameOrientation).SetPosition(p.X + _originX, p.Y + _originY).Build();
            if (!_inventory.CheckShapePlacement(s.Item, target, ignored))
                throw new InvalidOperationException($"Game collision precheck rejected {s.StableId} at {p.X},{p.Y} orientation {p.GameOrientation}.");
            targets.Add((s, target, p));
        }
        try
        {
            // Slot validity sees the inventory's current occupancy. Validate it
            // only after every target shape is staged, otherwise old positions
            // of other movable items cause false rejections.
            foreach (var entry in targets) entry.snap.Item.modifiedShape = entry.target;
            foreach (var entry in targets)
                if (!_inventory.CheckInventorySlotStillValid(entry.snap.Item, entry.target, entry.snap.Item.state))
                    throw new InvalidOperationException($"Game slot/state validation rejected {entry.snap.StableId} at {entry.placement.X},{entry.placement.Y} orientation {entry.placement.GameOrientation}.");
            _inventory.Validate(); _inventory.ForceRecomputeDynamicBackground();
            VerifyUnchanged(ignoreShape: true);
            foreach (var entry in targets) if (entry.snap.Item.modifiedShape.minX != entry.target.minX || entry.snap.Item.modifiedShape.minY != entry.target.minY || entry.snap.Item.modifiedShape.orientation != entry.target.orientation)
                throw new InvalidOperationException("Post-apply shape verification failed for " + entry.snap.Item.identifier);
        }
        catch
        {
            Rollback(); throw;
        }
    }

    private void Rollback()
    {
        foreach (var x in _allOriginal) x.item.modifiedShape = x.shape.Clone();
        _inventory.Validate(); _inventory.ForceRecomputeDynamicBackground();
        foreach (var x in _allOriginal) if (x.item.modifiedShape.minX != x.shape.minX || x.item.modifiedShape.minY != x.shape.minY || x.item.modifiedShape.orientation != x.shape.orientation)
            MelonLoader.MelonLogger.Error("Rollback verification failed for " + x.item.identifier);
        MelonLoader.MelonLogger.Warning("Smart arrange transaction rolled back to the captured layout.");
    }

    private void VerifyUnchanged(bool ignoreShape = false)
    {
        if (_inventory.childItems.Count != _allOriginal.Count) throw new InvalidOperationException("Inventory item count changed during arrangement.");
        foreach (var s in _movable) if (Fingerprint(s.Item) != s.Fingerprint) throw new InvalidOperationException("Item state changed during arrangement: " + s.StableId);
    }

    private static string StableId(GameItem item, int index) => $"{item.identifier}\u001f{item.uniqueId:D10}\u001f{index:D6}";
    private static string Fingerprint(GameItem i) => $"{i.identifier}|{i.uniqueId}|{i.unitCount}|{i.state}";

    private static List<string> Categories(GameItem item)
    {
        var result = new List<string>();
        try
        {
            // Structured catalogue/type data. Instance state is not category data.
            var types = item.itemTypes;
            if (types != null)
            {
                for (int i = 0; i < types.Count; i++)
                {
                    string value = types[i];
                    if (!string.IsNullOrWhiteSpace(value)) result.Add(value.Trim());
                }
            }
        }
        catch (Exception ex) { MelonLoader.MelonLogger.Warning("Item type read failed for " + item.identifier + ": " + ex.Message); }
        if (result.Count == 0) result.Add("~unknown");
        return result.Distinct(StringComparer.Ordinal).ToList();
    }

    private static int VisualPriority(string primaryCategory)
    {
        // Keep startup and context-menu code free from new IL2CPP member access.
        // Structured itemTypes still put containers/bags/machines first.
        int rank = CategoryRank(primaryCategory);
        return rank <= 3 ? 10 + rank : 100 + rank;
    }
    private static int CategoryRank(string category)
    {
        if (category == "~unknown") return 3;
        string k = category.ToLowerInvariant();
        string[] stable = {
            "container", "storage", "bag", "backpack",
            "machine", "device", "equipment", "appliance",
            "weapon", "ammo", "armor", "medical", "med",
            "food", "drink", "tool", "material", "component",
            "valuable", "misc"
        };
        for (int i = 0; i < stable.Length; i++)
        {
            if (!k.Contains(stable[i], StringComparison.Ordinal)) continue;
            if (i <= 1) return 0;       // containers/storage
            if (i <= 3) return 1;       // bags/backpacks
            if (i <= 7) return 2;       // machines/devices/equipment
            return 10 + i;              // remaining catalogue groups
        }
        return 1000;
    }

    private static IReadOnlyList<Orientation> BuildOrientations(GridShape shape)
    {
        // Ask GridShapeBuilder for every game orientation, then read the exact
        // resulting global collision mask. This keeps the solver and the target
        // shapes submitted to the game on identical rotation semantics.
        var list = new List<Orientation>();
        var signatures = new HashSet<string>(StringComparer.Ordinal);
        foreach (int o in new[] { 0, 1, 2, 3, shape.orientation }.Distinct())
        {
            var built = new GridShapeBuilder(shape).SetOrientation(o).SetPosition(0, 0).Build();
            var mask = ReadBuiltMask(built);
            if (signatures.Add(mask.Signature))
                list.Add(new Orientation(o, mask, mask.Width > mask.Height));
        }
        return list.OrderByDescending(x => x.Mask.Height > x.Mask.Width)
            .ThenByDescending(x => x.Mask.Height - x.Mask.Width)
            .ThenBy(x => x.GameOrientation).ToArray();
    }

    private static ShapeMask ReadBuiltMask(GridShape shape)
    {
        var cells = new List<Cell>();
        for (int y = 0; y < shape.globalHeight; y++)
        for (int x = 0; x < shape.globalWidth; x++)
            if (shape.Get(shape.minX + x, shape.minY + y) != 0)
                cells.Add(new Cell(x, y));
        if (cells.Count == 0) throw new InvalidOperationException("Item has an empty collision mask.");
        return new ShapeMask(cells);
    }

    private static ShapeMask Mask(GridShape shape) => ReadBuiltMask(shape);

}

