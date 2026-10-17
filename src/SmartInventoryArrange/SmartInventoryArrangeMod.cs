using System.Collections;
using System.Text;
using System.Threading.Tasks;
using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using SmartInventoryArrange.Core;
using UnityEngine;

[assembly: MelonInfo(typeof(SmartInventoryArrange.SmartArrangeMod), "Smart Inventory Arrange", "3.17.2", "OpenAI")]
[assembly: MelonGame(null, "Probably Stolen")]

namespace SmartInventoryArrange;

public sealed class SmartArrangeMod : MelonMod
{
    internal static bool Busy;
    private static readonly GlobalArrangeController Search = new();

    public override void OnInitializeMelon()
    {
        LoggerInstance.Msg("SmartInventoryArrange v3.17.2 loaded: v3.13-derived empty-grid global repack + preserved search frontier + removal reset + native-pointer snapshot guard + field diagnostics; game 0.46D / MelonLoader 0.7.3 / IL2CPP");
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
            MelonLogger.Msg($"Smart arrange exact input: {tx.WriteExactInput()}");
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

            var request=Search.Begin(tx.Problem,tx.OriginalPlacements,tx.SessionContext);
            MelonLogger.Msg($"Smart arrange request: action={(request.Restarted ? "fresh-global" : "resume-frontier")}, input={request.InputSignature}, nodeBudget={request.NodeBudget}, previousNodes={request.Session.TotalNodes}");
            MelonCoroutines.Start(SolveAndApply(tx,request));
        }
        catch (Exception ex)
        {
            Busy = false;
            Search.Reset();
            MelonLogger.Error("Smart arrange snapshot cancelled: " + ex);
        }
    }

    private static IEnumerator SolveAndApply(InventoryTransaction tx, GlobalArrangeController.Request request)
    {
        bool applied=false;
        bool applicationStarted=false;
        var task=Task.Run(()=>
        {
            var progress=request.Session.Advance(request.NodeBudget,request.SolutionBatch);
            var baseline=DeterministicSolver.Evaluate(tx.Problem,tx.OriginalPlacements); // telemetry only
            return (result:progress.Result,baseline,Diagnostic:request.Session.Diagnostic,progress);
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
            tx.AssertSnapshotCurrent();
            MelonLogger.Msg($"Smart arrange search complete: batch={solved.progress.Batch}, newNodes={solved.progress.NewNodes}, totalNodes={solved.progress.TotalNodes}, newCandidates={solved.progress.NewCandidates}, uniqueCandidates={solved.progress.TotalCandidates}, canContinue={solved.progress.CanContinue}, reason={solved.progress.Reason}");
            int comparison = Native34Solver.SameLayout(result.Placements, tx.OriginalPlacements) ? 0 : 1;
            if (!Native34Solver.ValidateLayout(tx.Problem, result.Placements))
                throw new InvalidOperationException("Native34 final immutable geometry validation failed.");
            if (!result.Complete || comparison <= 0)
            {
                Search.Complete(request,tx.Problem,tx.OriginalPlacements);
                tx.WriteResultSnapshot(tx.OriginalPlacements, "kept-original");
                string reason = result.UsedFallback ? "no-fresh-complete-candidate-original-preserved" : solved.progress.Reason;
                var baselineSpace = DeterministicSolver.EvaluateSpaceMetrics(tx.Problem, tx.OriginalPlacements);
                string firstDifference = DeterministicSolver.FirstDifferingMetric(tx.Problem, result.Score, solved.baseline);
                MelonLogger.Msg($"Smart arrange kept original layout: reason={reason}, firstDifference={firstDifference}, mode={DeterministicSolver.DensityMode(tx.Problem)}, occupancy={DeterministicSolver.OccupancyRatio(tx.Problem):P1}, candidate complete={result.Complete}, timeout={result.TimedOut}, fallback={result.UsedFallback}, elapsed={result.Elapsed.TotalMilliseconds:F1}ms, candidateSpace=holes:{result.Holes},narrow:{result.NarrowFragments}, baselineSpace=holes:{baselineSpace.holes},narrow:{baselineSpace.narrow},ragged:{baselineSpace.raggedness}, candidateVisual={VisualSummary(result.Score)}, baselineVisual={VisualSummary(solved.baseline)}, diagnostic={solved.Diagnostic}");
                yield break;
            }

            applicationStarted=true;
            tx.Apply(result.Placements);
            applied=true;
            tx.WriteResultSnapshot(result.Placements, "applied");
            Search.Complete(request,tx.Problem,result.Placements);
            int rotations = result.Placements.Count(p => p.Rotated);
            MelonLogger.Msg($"Smart arrange success: mode={DeterministicSolver.DensityMode(tx.Problem)}, occupancy={DeterministicSolver.OccupancyRatio(tx.Problem):P1}, placed={result.Placements.Count}, horizontalExceptions={rotations}, holes={result.Holes}, narrowFragments={result.NarrowFragments}, elapsed={result.Elapsed.TotalMilliseconds:F1}ms, fallback={result.UsedFallback}, visual={VisualSummary(result.Score)}, splitGroups={DeterministicSolver.DescribeSplits(result.Placements)}, native34={solved.Diagnostic}");
        }
        catch (Exception ex)
        {
            Search.Reset();
            try { if(applied)tx.RestoreCapturedSnapshot(); tx.WriteResultSnapshot(tx.OriginalPlacements, applied?"cancelled-restored":applicationStarted?"application-failed":"cancelled-before-apply"); } catch (Exception logEx) { MelonLogger.Warning("Final coordinate snapshot failed: " + logEx.Message); }
            MelonLogger.Error($"Smart arrange {(applied?"cancelled-restored":applicationStarted?"application-failed":"cancelled-before-apply")}: " + ex);
        }
        finally
        {
            Busy = false;
        }
    }

    private static string VisualSummary(LayoutScore score) =>
        $"typeSplits={-score.NegativeTypeRuns},typeRows={score.SameTypeRowAlignment},typeEdges={score.SameTypeHorizontalContact},cohortSplits={-score.NegativeCohortRuns},cohortRows={score.CohortRowAlignment},cohortEdges={score.CohortHorizontalContact}";


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
        string label = ContextInitPatch.IsChinese() ? "\u667A\u80FD\u6392\u5217" : "Smart Arrange";

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
