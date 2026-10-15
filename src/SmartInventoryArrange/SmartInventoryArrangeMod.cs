using System.Collections;
using System.Text;
using System.Threading.Tasks;
using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using SmartInventoryArrange.Core;
using UnityEngine;

[assembly: MelonInfo(typeof(SmartInventoryArrange.SmartArrangeMod), "Smart Inventory Arrange", "3.13.0", "OpenAI")]
[assembly: MelonGame(null, "Probably Stolen")]

namespace SmartInventoryArrange;

public sealed class SmartArrangeMod : MelonMod
{
    internal static bool Busy;
    private static string? _lastOptimizedSignature;

    public override void OnInitializeMelon()
    {
        LoggerInstance.Msg("SmartInventoryArrange v3.13.0 loaded: native34 global family repack + row bands + semantic anchors + interlocks + immutable transaction + coordinate snapshots; game 0.46D / MelonLoader 0.7.3 / IL2CPP");
        LoggerInstance.Msg("Patching ItemContextHandler.OnEventInit, TriggerButton and OnEventReset");
    }

    internal static void Run(GameGridInventory inventory)
    {
        if (Busy) { MelonLogger.Warning("Smart arrange ignored: an arrangement is already running."); return; }
        Busy = true;
        try
        {
            var tx = InventoryTransaction.Capture(inventory);
            string snapshotPath = tx.WriteDiagnosticSnapshot();
            double occupancy = DeterministicSolver.OccupancyRatio(tx.Problem);
            string mode = DeterministicSolver.DensityMode(tx.Problem);
            MelonLogger.Msg($"Smart arrange snapshot: {tx.Width}x{tx.Height}, occupancy={occupancy:P1}, mode={mode}, movable={tx.MovableCount}, fixed={tx.FixedCount}, categories={tx.CategoryCount}, multi-category={tx.MultiCategoryCount}, unknown={tx.UnknownCategoryCount}, details={snapshotPath}");
            if (tx.MovableCount == 0)
            {
                tx.WriteResultSnapshot(tx.OriginalPlacements, "empty");
                MelonLogger.Msg("Smart arrange: warehouse is empty or contains only fixed items.");
                Busy = false;
                return;
            }

            string inputSignature = LayoutSignature(tx.Problem, tx.OriginalPlacements);
            if (StringComparer.Ordinal.Equals(inputSignature, _lastOptimizedSignature))
            {
                MelonLogger.Msg("Smart arrange skipped: current layout already matches the last deterministic result.");
                Busy = false;
                return;
            }

            int cells = tx.Width * tx.Height;
            int budgetMs = cells <= 36 ? 100 : cells <= 100 ? 220 : 400;
            int beam = cells <= 36 ? 24 : cells <= 100 ? 32 : 40;
            int candidates = cells <= 36 ? 20 : 28;
            MelonCoroutines.Start(SolveAndApply(tx, budgetMs, beam, candidates));
        }
        catch (Exception ex)
        {
            Busy = false;
            MelonLogger.Error("Smart arrange snapshot cancelled: " + ex);
        }
    }

    private static IEnumerator SolveAndApply(InventoryTransaction tx, int budgetMs, int beam, int candidates)
    {
        var task = Task.Run(() =>
        {
            var solver = new Native34Solver();
            var result = solver.Solve(tx.Problem, tx.OriginalPlacements);
            var baseline = DeterministicSolver.Evaluate(tx.Problem, tx.OriginalPlacements);
            return (result, baseline, solver.Diagnostic);
        });

        while (!task.IsCompleted) yield return null;

        try
        {
            if (task.IsFaulted)
                throw task.Exception?.GetBaseException() ?? new InvalidOperationException("Background solver failed.");
            if (task.IsCanceled)
                throw new OperationCanceledException("Background solver was cancelled.");

            var solved = task.Result;
            var result = solved.result;
            int comparison = Native34Solver.SameLayout(result.Placements, tx.OriginalPlacements) ? 0 : 1;
            if (!Native34Solver.ValidateLayout(tx.Problem, result.Placements))
                throw new InvalidOperationException("Native34 final immutable geometry validation failed.");
            if (!result.Complete || comparison <= 0)
            {
                _lastOptimizedSignature = LayoutSignature(tx.Problem, tx.OriginalPlacements);
                tx.WriteResultSnapshot(tx.OriginalPlacements, "kept-original");
                string reason = !result.Complete ? "candidate-incomplete" : comparison == 0 ? "visual-score-equal" : "baseline-visual-score-better";
                var baselineSpace = DeterministicSolver.EvaluateSpaceMetrics(tx.Problem, tx.OriginalPlacements);
                string firstDifference = DeterministicSolver.FirstDifferingMetric(tx.Problem, result.Score, solved.baseline);
                MelonLogger.Msg($"Smart arrange kept original layout: reason={reason}, firstDifference={firstDifference}, mode={DeterministicSolver.DensityMode(tx.Problem)}, occupancy={DeterministicSolver.OccupancyRatio(tx.Problem):P1}, candidate complete={result.Complete}, timeout={result.TimedOut}, fallback={result.UsedFallback}, elapsed={result.Elapsed.TotalMilliseconds:F1}ms, candidateSpace=holes:{result.Holes},narrow:{result.NarrowFragments}, baselineSpace=holes:{baselineSpace.holes},narrow:{baselineSpace.narrow},ragged:{baselineSpace.raggedness}, candidateVisual={VisualSummary(result.Score)}, baselineVisual={VisualSummary(solved.baseline)}, diagnostic={solved.Diagnostic}");
                yield break;
            }

            tx.Apply(result.Placements);
            tx.WriteResultSnapshot(result.Placements, "applied");
            _lastOptimizedSignature = LayoutSignature(tx.Problem, result.Placements);
            int rotations = result.Placements.Count(p => p.Rotated);
            MelonLogger.Msg($"Smart arrange success: mode={DeterministicSolver.DensityMode(tx.Problem)}, occupancy={DeterministicSolver.OccupancyRatio(tx.Problem):P1}, placed={result.Placements.Count}, horizontalExceptions={rotations}, holes={result.Holes}, narrowFragments={result.NarrowFragments}, elapsed={result.Elapsed.TotalMilliseconds:F1}ms, fallback={result.UsedFallback}, visual={VisualSummary(result.Score)}, splitGroups={DeterministicSolver.DescribeSplits(result.Placements)}, native34={solved.Diagnostic}");
        }
        catch (Exception ex)
        {
            try { tx.WriteResultSnapshot(tx.OriginalPlacements, "cancelled-restored"); } catch (Exception logEx) { MelonLogger.Warning("Final coordinate snapshot failed: " + logEx.Message); }
            MelonLogger.Error("Smart arrange cancelled/restored: " + ex);
        }
        finally
        {
            Busy = false;
        }
    }

    private static string VisualSummary(LayoutScore score) =>
        $"typeSplits={-score.NegativeTypeRuns},typeRows={score.SameTypeRowAlignment},typeEdges={score.SameTypeHorizontalContact},cohortSplits={-score.NegativeCohortRuns},cohortRows={score.CohortRowAlignment},cohortEdges={score.CohortHorizontalContact}";

    private static string LayoutSignature(LayoutProblem p, IEnumerable<Placement> placements)
    {
        var b = new StringBuilder(128 + p.Items.Count * 48);
        b.Append(p.Width).Append('x').Append(p.Height).Append('|');
        for (int y = 0; y < p.Height; y++)
            for (int x = 0; x < p.Width; x++) b.Append(p.Usable[x, y] ? '1' : '0');
        foreach (var item in p.Items.OrderBy(x => x.StableId, StringComparer.Ordinal))
            b.Append('|').Append(item.StableId).Append(':').Append(item.PrimaryCategory).Append(':')
             .Append(string.Join(',', item.Orientations.Select(x => x.Mask.Signature)));
        foreach (var q in placements.OrderBy(x => x.StableId, StringComparer.Ordinal))
            b.Append('|').Append(q.StableId).Append('@').Append(q.X).Append(',').Append(q.Y).Append(',').Append(q.GameOrientation);
        return b.ToString();
    }
}
[HarmonyPatch(typeof(ItemContextHandler), nameof(ItemContextHandler.OnEventInit))]
internal static class ContextInitPatch
{
    private static void Postfix(ItemContextHandler __instance, bool __result)
    {
        if (!__result || __instance.sortWindow == null || __instance.buttonSortSmallEnd == null) return;
        try
        {
            NativeSmartMenu.Close();
            NativeSmartMenu.Open(__instance);
        }
        catch (Exception ex)
        {
            NativeSmartMenu.Close();
            MelonLogger.Error("Smart arrange menu injection failed: " + ex);
        }
    }

    internal static bool IsChinese()
    {
        var language = Application.systemLanguage;
        return language == SystemLanguage.Chinese || language == SystemLanguage.ChineseSimplified || language == SystemLanguage.ChineseTraditional;
    }
}

internal static class NativeSmartMenu
{
    internal static RichTextElement? Button;
    internal static GridPixelElement? Grid;

    internal static void Open(ItemContextHandler owner)
    {
        var oldElement = owner.sortWindow.childElement ?? throw new InvalidOperationException("Sort window has no child element.");
        var oldGrid = oldElement.Cast<GridPixelElement>();
        var nativeButtons = new[]
        {
            owner.buttonSortBigStart,
            owner.buttonSortSmallStart,
            owner.buttonSortBigEnd,
            owner.buttonSortSmallEnd
        };
        if (nativeButtons.Any(x => x == null)) throw new InvalidOperationException("One or more native sort buttons are missing.");

        var source = owner.buttonSortSmallEnd;
        int rowHeight = Math.Max(1, source.heightPixels);
        int width = Math.Max(owner.sortWindow.widthPixels, nativeButtons.Max(x => x.widthPixels));
        string label = ContextInitPatch.IsChinese() ? "智能排列" : "Smart Arrange";

        Button = new RichTextElement(false, width, rowHeight, true);
        Button.SetPadding(source.leftPadding, source.rightPadding, source.topPadding, source.bottomPadding);
        Button.SetText(label, Math.Max(source.maxCharWidth, label.Length), owner.textColor);
        Button.Validate();

        if (!owner.sortWindow.Detach(oldElement))
            throw new InvalidOperationException("Failed to detach the native 1x4 sort grid.");
        oldGrid.DetachAll();

        Grid = new GridPixelElement(1, 5, false).SetSpacing(oldGrid._xSpacing, oldGrid._ySpacing);
        Grid.justifyWidth = oldGrid.justifyWidth;
        Grid.searchUsingPriority = oldGrid.searchUsingPriority;
        for (int row = 0; row < nativeButtons.Length; row++)
            if (!Grid.AttachPos(nativeButtons[row].Cast<PixelElement>(), 0, row))
                throw new InvalidOperationException($"Failed to attach native sort row {row}.");
        if (!Grid.AttachPos(Button.Cast<PixelElement>(), 0, 4))
            throw new InvalidOperationException("Failed to attach the smart sort row.");
        if (!owner.sortWindow.Attach(Grid.Cast<PixelElement>()))
            throw new InvalidOperationException("Failed to attach the expanded 1x5 sort grid.");

        owner.sortWindow.ResizePixels(width, owner.sortWindow.heightPixels + rowHeight + oldGrid._ySpacing);
        owner.sortWindow.Validate();
        MelonLogger.Msg($"Smart arrange menu injected: label={label}, nativeGrid={oldGrid.gridWidth}x{oldGrid.gridHeight}, expandedGrid=1x5, row={rowHeight}, window={owner.sortWindow.widthPixels}x{owner.sortWindow.heightPixels}");
    }

    internal static void Close()
    {
        Button = null;
        Grid = null;
    }
}

[HarmonyPatch(typeof(ItemContextHandler), nameof(ItemContextHandler.TriggerButton))]
internal static class ContextTriggerPatch
{
    private static bool Prefix(ItemContextHandler __instance, RichTextElement button)
    {
        if (NativeSmartMenu.Button == null || button.Pointer != NativeSmartMenu.Button.Pointer) return true;
        var inventory = __instance.currentInventory;
        __instance.sortWindow?.Hide();
        if (inventory != null) SmartArrangeMod.Run(inventory);
        return false;
    }
}

[HarmonyPatch(typeof(ItemContextHandler), nameof(ItemContextHandler.OnEventReset))]
internal static class ContextResetPatch
{
    private static void Postfix() { }
}
