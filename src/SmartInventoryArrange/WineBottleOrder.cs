using SmartInventoryArrange.Core;

namespace SmartInventoryArrange;

/// <summary>Read-only instance metadata, not an item ID or a synthetic stable ID.</summary>
public sealed record WineBottleInfo(string StableId,string CurrentName,int? ContainerState,int? AgeDays);

/// <summary>Reassign wine identities to existing wine slots. Never searches for,
/// moves, resizes or scores geometry, and never alters an item's state.</summary>
public static class WineBottleOrder
{
    public const string TargetItemId="wine_bottle";
    private static int StateRank(int? value)=>value is 0 or 1 or 2?value.Value:value.HasValue?3:4;
    public static int Compare(WineBottleInfo a,WineBottleInfo b)
    {
        int c=StringComparer.Ordinal.Compare(a.CurrentName,b.CurrentName);if(c!=0)return c;
        c=StateRank(a.ContainerState).CompareTo(StateRank(b.ContainerState));if(c!=0)return c;
        c=Nullable.Compare(a.ContainerState,b.ContainerState);if(c!=0)return c;
        c=(a.AgeDays.HasValue?0:1).CompareTo(b.AgeDays.HasValue?0:1);if(c!=0)return c;
        if(a.AgeDays.HasValue){c=b.AgeDays!.Value.CompareTo(a.AgeDays.Value);if(c!=0)return c;}
        return StringComparer.Ordinal.Compare(a.StableId,b.StableId);
    }
    public static IReadOnlyList<Placement> Apply(LayoutProblem problem,IReadOnlyList<Placement> placements,IReadOnlyList<WineBottleInfo> metadata)
    {
        var wine=problem.Items.Where(i=>StringComparer.Ordinal.Equals(i.ItemId,TargetItemId)).ToDictionary(i=>i.StableId,StringComparer.Ordinal);
        if(wine.Count<2)return placements;
        if(!Native34Solver.ValidateLayout(problem,placements))throw new InvalidOperationException("Wine ordering requires a complete legal layout.");
        var byId=metadata.Where(i=>wine.ContainsKey(i.StableId)).ToDictionary(i=>i.StableId,StringComparer.Ordinal);
        if(wine.Keys.Any(id=>!byId.ContainsKey(id)))throw new InvalidOperationException("Wine ordering metadata missing for a movable wine instance.");
        var ordered=wine.Keys.Select(id=>byId[id]).OrderBy(i=>i,Comparer<WineBottleInfo>.Create(Compare)).ToArray();
        var slots=placements.Where(p=>wine.ContainsKey(p.StableId)).OrderBy(p=>p.Y).ThenBy(p=>p.X).ThenBy(p=>p.StableId,StringComparer.Ordinal).ToArray();
        if(slots.Length!=ordered.Length)throw new InvalidOperationException("Wine slot conservation check failed.");
        var assigned=new Dictionary<string,Placement>(StringComparer.Ordinal);bool changed=false;
        for(int k=0;k<slots.Length;k++)
        {
            var slot=slots[k];var instance=wine[ordered[k].StableId];
            var direction=instance.Orientations.Where(o=>o.Mask.Signature==slot.Mask.Signature)
                .OrderBy(o=>o.GameOrientation==slot.GameOrientation?0:1).ThenBy(o=>o.GameOrientation).FirstOrDefault();
            if(direction==null)throw new InvalidOperationException($"Wine ordering incompatible native shape: instance={instance.StableId}, slot={slot.X},{slot.Y}; no layout applied.");
            assigned.Add(slot.StableId,new(instance.StableId,slot.X,slot.Y,direction.GameOrientation,slot.Mask,instance.PrimaryCategory,slot.Mask.Width>slot.Mask.Height));
            changed|=slot.StableId!=instance.StableId||slot.GameOrientation!=direction.GameOrientation;
        }
        if(!changed)return placements;
        var result=placements.Select(p=>assigned.TryGetValue(p.StableId,out var q)?q:p).ToArray();
        if(!Native34Solver.ValidateLayout(problem,result))throw new InvalidOperationException("Wine ordering final legality check failed.");
        return result;
    }
}
