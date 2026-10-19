using System.Collections;
using System.Text;
using System.Threading.Tasks;
using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using SmartInventoryArrange.Core;
using UnityEngine;

[assembly: MelonInfo(typeof(SmartInventoryArrange.SmartArrangeMod), "Smart Inventory Arrange", "3.17.8", "OpenAI")]
[assembly: MelonGame(null, "Probably Stolen")]

namespace SmartInventoryArrange;

public sealed class SmartArrangeMod : MelonMod
{
    internal static bool Busy;
    internal static MelonPreferences_Entry<bool> KeepContainers = null!;
    private static readonly GlobalArrangeController Search = new();

    public override void OnInitializeMelon()
    {
        KeepContainers = MelonPreferences.CreateCategory("SmartInventoryArrange")
            .CreateEntry<bool>("KeepContainers", true, "Keep containers in Preserve Containers mode");
        LoggerInstance.Msg("SmartInventoryArrange v3.17.8 loaded: v3.13-derived empty-grid global repack + preserved search frontier + removal reset + native-pointer snapshot guard + slot-only real-state instance order (appraisal ignored, no flags changed) + SUPPLY_CRATE default-horizontal only + optional preserve-container obstacles; game 0.46D / MelonLoader 0.7.3 / IL2CPP");
        LoggerInstance.Msg("Patching ItemContextHandler.OnEventInit, TriggerButton and OnEventReset");
    }

    internal static void Run(GameGridInventory inventory, bool preserveContainers = false)
    {
        if (Busy) { MelonLogger.Warning("Smart arrange ignored: an arrangement is already running."); return; }
        Busy = true;
        try
        {
            bool keepContainers = preserveContainers && KeepContainers.Value;
            var tx = InventoryTransaction.Capture(inventory, keepContainers);
            MelonLogger.Msg($"Smart arrange mode: requested={(preserveContainers ? "preserve-containers" : "smart")}, keepContainers={keepContainers}, preservedContainers={tx.PreservedContainerCount}");
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
            return (result:tx.OrderInstances(progress.Result),baseline,Diagnostic:request.Session.Diagnostic,progress);
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
    internal static RichTextElement? PreserveButton;
    internal static GridPixelElement? Grid;
    private static IntPtr _window;

    internal static void Open(ItemContextHandler owner)
    {
        var window = owner.sortWindow;
        var oldElement = window.childElement ?? throw new InvalidOperationException("Sort window has no child element.");
        // Native UI nodes may be reused. Do not append rows or increase height twice.
        if (Grid != null && Button != null && PreserveButton != null &&
            _window == window.Pointer && oldElement.Pointer == Grid.Pointer) return;
        Close();
        var oldGrid = oldElement.Cast<GridPixelElement>();
        var nativeButtons = new[] { owner.buttonSortBigStart, owner.buttonSortSmallStart,
            owner.buttonSortBigEnd, owner.buttonSortSmallEnd };
        if (nativeButtons.Any(x => x == null)) throw new InvalidOperationException("One or more native sort buttons are missing.");
        var source = owner.buttonSortSmallEnd;
        int rowHeight = Math.Max(1, source.heightPixels);
        int width = Math.Max(window.widthPixels, nativeButtons.Max(x => x.widthPixels));
        string label = ContextInitPatch.IsChinese() ? "\u667A\u80FD\u6392\u5217" : "Smart Arrange";
        string preserveLabel = ContextInitPatch.IsChinese() ? "\u4FDD\u7559\u5BB9\u5668\u6392\u5217" : "Preserve Containers";
        RichTextElement MakeButton(string text)
        {
            var button = new RichTextElement(false, width, rowHeight, true);
            button.SetPadding(source.leftPadding, source.rightPadding, source.topPadding, source.bottomPadding);
            button.SetText(text, Math.Max(source.maxCharWidth, text.Length), owner.textColor);
            button.Validate();
            return button;
        }
        var smart = MakeButton(label);
        var preserve = MakeButton(preserveLabel);
        width = Math.Max(width, Math.Max(smart.widthPixels, preserve.widthPixels));
        var grid = new GridPixelElement(1, 6, false).SetSpacing(oldGrid._xSpacing, oldGrid._ySpacing);
        grid.justifyWidth = oldGrid.justifyWidth;
        grid.searchUsingPriority = oldGrid.searchUsingPriority;
        int oldWidth = window.widthPixels, oldHeight = window.heightPixels;
        if (!window.Detach(oldElement)) throw new InvalidOperationException("Failed to detach the native sort grid.");
        oldGrid.DetachAll();
        try
        {
            for (int row = 0; row < nativeButtons.Length; row++)
                if (!grid.AttachPos(nativeButtons[row].Cast<PixelElement>(), 0, row))
                    throw new InvalidOperationException($"Failed to attach native sort row {row}.");
            if (!grid.AttachPos(smart.Cast<PixelElement>(), 0, 4) ||
                !grid.AttachPos(preserve.Cast<PixelElement>(), 0, 5))
                throw new InvalidOperationException("Failed to attach smart sort rows.");
            if (!window.Attach(grid.Cast<PixelElement>())) throw new InvalidOperationException("Failed to attach the expanded 1x6 sort grid.");
            window.ResizePixels(width, oldHeight + 2 * (rowHeight + oldGrid._ySpacing));
            window.Validate();
            Button = smart; PreserveButton = preserve; Grid = grid; _window = window.Pointer;
        }
        catch
        {
            // Restore the four native controls if menu construction fails.
            window.Detach(grid.Cast<PixelElement>()); grid.DetachAll();
            for (int row = 0; row < nativeButtons.Length; row++)
                oldGrid.AttachPos(nativeButtons[row].Cast<PixelElement>(), 0, row);
            window.Attach(oldElement); window.ResizePixels(oldWidth, oldHeight); window.Validate();
            Close(); throw;
        }
        MelonLogger.Msg($"Smart arrange menu injected: label={label}, preserveLabel={preserveLabel}, nativeGrid={oldGrid.gridWidth}x{oldGrid.gridHeight}, expandedGrid=1x6, row={rowHeight}, window={window.widthPixels}x{window.heightPixels}");
    }

    internal static bool IsCurrent(ItemContextHandler owner) => Grid != null &&
        owner.sortWindow != null && owner.sortWindow.Pointer == _window &&
        owner.sortWindow.childElement?.Pointer == Grid.Pointer;

    internal static void Close()
    {
        Button = null; PreserveButton = null; Grid = null; _window = IntPtr.Zero;
    }
}

[HarmonyPatch(typeof(ItemContextHandler), nameof(ItemContextHandler.TriggerButton))]
internal static class ContextTriggerPatch
{
    private static bool Prefix(ItemContextHandler __instance, RichTextElement button)
    {
        if (!NativeSmartMenu.IsCurrent(__instance)) return true;
        bool smart = NativeSmartMenu.Button != null && button.Pointer == NativeSmartMenu.Button.Pointer;
        bool preserve = NativeSmartMenu.PreserveButton != null && button.Pointer == NativeSmartMenu.PreserveButton.Pointer;
        if (!smart && !preserve) return true;
        var inventory = __instance.currentInventory;
        __instance.sortWindow?.Hide();
        if (inventory != null) SmartArrangeMod.Run(inventory, preserve);
        return false;
    }
}

[HarmonyPatch(typeof(ItemContextHandler), nameof(ItemContextHandler.OnEventReset))]
internal static class ContextResetPatch
{
    private static void Postfix() { }
}
