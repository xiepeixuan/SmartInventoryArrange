using SmartInventoryArrange.Core;
using System.Text.Json;
namespace SmartInventoryArrange;

/// <summary>Immutable read-only values; no native object enters the worker.</summary>
public sealed record InstanceOrderInfo(string StableId,string ItemId,string CurrentName,
    int? WineState=null,int? AgeDays=null,long? VolumeUl=null,int? RemainingUses=null,
    int? RemainingEnergy=null,int? Durability=null,double? Purity=null,
    double? ChemicalContamination=null,double? PhysicalContamination=null)
{
    public string Profile {get;init;}="name";
    public string Purpose {get;init;}="";
    // Compatibility field only. Sorting ignores appraisal; the game flags are never changed.
    public bool IsUnidentified {get;init;}
    public bool? IsEmpty {get;init;}
    public int? QualityRank {get;init;}
    public int? FreshnessRank {get;init;}
    public int? EquipmentRank {get;init;}
    public int? Expired {get;init;}
    public int? Unavailable {get;init;}
    public int? Opened {get;init;}
    public int? Rusted {get;init;}
    public int? Dented {get;init;}
    public int? Counterfeit {get;init;}
    public int? FermentationRemaining {get;init;}
    public string Diagnostics {get;init;}="";
    // Integrity-only digest: never participates in ranking, no hidden grade in logs.
    public string FeatureIntegrity {get;init;}="";
}

/// <summary>Pure semantic ranking and slot-only permutation. No packing search.</summary>
public static class InstanceOrder
{
    public const string Policy="semantic-slots-v3:real-state-appraisal-ignored;name;empty-last;usable-sealed-rustfree;wine-2-1-0-age-desc-ferment-asc;water-grade-then-volume;food-fresh-first;absolute-remaining-desc;quality-before-quantity;stable-id";
    public static long? SumVolume(IReadOnlyList<int?> components)
    {
        long sum=0;bool any=false;
        foreach(var value in components)if(value.HasValue){sum+=value.Value;any=true;}
        return any?sum:null;
    }
    private static int Number<T>(T? a,T? b,bool descending=false) where T:struct,IComparable<T>
    {
        int c=(a.HasValue?0:1).CompareTo(b.HasValue?0:1);if(c!=0)return c;
        return !a.HasValue?0:descending?b!.Value.CompareTo(a.Value):a.Value.CompareTo(b!.Value);
    }
    private static double? Finite(double? n)=>n.HasValue&&double.IsFinite(n.Value)?n:null;
    private static int? WineRank(int? value)=>value switch{2=>0,1=>1,0=>2,_=>null};
    public static int Compare(InstanceOrderInfo a,InstanceOrderInfo b)
    {
        int c=StringComparer.Ordinal.Compare(a.ItemId,b.ItemId);if(c!=0)return c;
        c=StringComparer.Ordinal.Compare(a.CurrentName,b.CurrentName);if(c!=0)return c;
        // Empty is an explicit observed zero/state, never an invented null->0.
        if(a.IsEmpty.HasValue||b.IsEmpty.HasValue){c=Number(a.IsEmpty,b.IsEmpty);if(c!=0)return c;}
        // User-selected policy: appraisal flags never veto real-state keys or form a group.
        // Purpose groups have deterministic labels, not a 'better purpose' score.
        c=StringComparer.Ordinal.Compare(a.Purpose,b.Purpose);if(c!=0)return c;
        c=StringComparer.Ordinal.Compare(a.Profile,b.Profile);if(c!=0)return c;
        c=Number(a.Expired,b.Expired);if(c!=0)return c;
        c=Number(a.Unavailable,b.Unavailable);if(c!=0)return c;
        c=Number(a.Opened,b.Opened);if(c!=0)return c;
        c=Number(a.Rusted,b.Rusted);if(c!=0)return c;
        c=Number(a.Dented,b.Dented);if(c!=0)return c;
        c=Number(a.Counterfeit,b.Counterfeit);if(c!=0)return c;
        switch(a.Profile)
        {
            case "wine":
                c=Number(WineRank(a.WineState),WineRank(b.WineState));if(c!=0)return c;
                if(a.WineState==2){c=Number(a.AgeDays,b.AgeDays,true);if(c!=0)return c;}
                if(a.WineState==1){c=Number(a.FermentationRemaining,b.FermentationRemaining);if(c!=0)return c;}
                break;
            case "water":
                c=Number(a.QualityRank,b.QualityRank);if(c!=0)return c;
                c=Number(a.VolumeUl,b.VolumeUl,true);if(c!=0)return c;break;
            case "chemical":
                c=Number(a.QualityRank,b.QualityRank);if(c!=0)return c;
                c=Number(Finite(a.Purity),Finite(b.Purity),true);if(c!=0)return c;
                c=Number(a.VolumeUl,b.VolumeUl,true);if(c!=0)return c;break;
            case "ingot":c=Number(a.QualityRank,b.QualityRank);if(c!=0)return c;break;
            case "food":c=Number(a.FreshnessRank,b.FreshnessRank);if(c!=0)return c;break;
            case "energy":c=Number(a.RemainingEnergy,b.RemainingEnergy,true);if(c!=0)return c;break;
            case "uses":c=Number(a.RemainingUses,b.RemainingUses,true);if(c!=0)return c;break;
            case "durability":c=Number(a.Durability,b.Durability,true);if(c!=0)return c;break;
            case "condition":c=Number(a.EquipmentRank,b.EquipmentRank);if(c!=0)return c;break;
        }
        return StringComparer.Ordinal.Compare(a.StableId,b.StableId);
    }
    // Identical native mask sets guarantee that every identity can occupy every
    // slot in its class. Each identity retains its own original category.
    public static string ShapeClass(LayoutItem i)=>JsonSerializer.Serialize(i.Orientations.Select(o=>o.Mask.Signature)
        .Concat(i.OriginalMask==null?Array.Empty<string>():new[]{i.OriginalMask.Signature}).Distinct(StringComparer.Ordinal).OrderBy(s=>s,StringComparer.Ordinal));
    public static IReadOnlyList<Placement> Apply(LayoutProblem problem,IReadOnlyList<Placement> placements,IReadOnlyList<InstanceOrderInfo> metadata)
    {
        if(!Native34Solver.ValidateLayout(problem,placements))throw new InvalidOperationException("Instance ordering requires a complete legal layout.");
        var docs=problem.Items.ToDictionary(i=>i.StableId,StringComparer.Ordinal);
        var info=metadata.ToDictionary(i=>i.StableId,StringComparer.Ordinal);
        if(info.Count!=docs.Count||docs.Values.Any(i=>!info.TryGetValue(i.StableId,out var m)||m.ItemId!=i.ItemId))
            throw new InvalidOperationException("Instance ordering metadata identity/membership mismatch.");
        var bySlot=placements.ToDictionary(q=>q.StableId,StringComparer.Ordinal);
        var assigned=new Dictionary<string,Placement>(StringComparer.Ordinal);bool changed=false;
        foreach(var family in problem.Items.GroupBy(i=>(i.ItemId,Shape:ShapeClass(i)))
            .OrderBy(g=>g.Key.ItemId,StringComparer.Ordinal).ThenBy(g=>g.Key.Shape,StringComparer.Ordinal))
        {
            if(family.Count()<2)continue;
            var members=family.Select(i=>info[i.StableId]).ToArray();
            // Unexpected profile disagreement must not invent a semantic ranking.
            // Retain deterministic name/ID ordering for that whole compatibility class.
            var profiles=members.Select(x=>x.Profile).Where(x=>x!="name").Distinct(StringComparer.Ordinal).ToArray();
            bool profileConflict=profiles.Length>1;
            var ordered=members.Select(m=>profileConflict
                ?new InstanceOrderInfo(m.StableId,m.ItemId,m.CurrentName){IsUnidentified=m.IsUnidentified}
                :m.Profile=="name"&&profiles.Length==1?m with{Profile=profiles[0]}:m)
                .OrderBy(i=>i,Comparer<InstanceOrderInfo>.Create(Compare)).ToArray();
            var slots=family.Select(i=>bySlot[i.StableId]).OrderBy(q=>q.Y).ThenBy(q=>q.X).ThenBy(q=>q.StableId,StringComparer.Ordinal).ToArray();
            for(int k=0;k<slots.Length;k++)
            {
                var slot=slots[k];var item=docs[ordered[k].StableId];
                var options=item.Orientations.AsEnumerable();
                if(item.OriginalMask!=null)options=options.Append(new(item.OriginalOrientation,item.OriginalMask,false));
                var direction=options.Where(o=>o.Mask.Signature==slot.Mask.Signature)
                    .OrderBy(o=>o.GameOrientation==slot.GameOrientation?0:1).ThenBy(o=>o.GameOrientation).FirstOrDefault();
                if(direction==null)throw new InvalidOperationException("Instance ordering incompatible native shape; no layout applied.");
                bool same=slot.StableId==item.StableId&&slot.GameOrientation==direction.GameOrientation;
                assigned.Add(slot.StableId,same?slot:new(item.StableId,slot.X,slot.Y,direction.GameOrientation,slot.Mask,item.PrimaryCategory,slot.Mask.Width>slot.Mask.Height));
                changed|=!same;
            }
        }
        if(!changed)return placements;
        var output=placements.Select(q=>assigned.TryGetValue(q.StableId,out var mapped)?mapped:q).ToArray();
        if(!Native34Solver.ValidateLayout(problem,output))throw new InvalidOperationException("Instance ordering final conservation/legality check failed.");
        return output;
    }
}
