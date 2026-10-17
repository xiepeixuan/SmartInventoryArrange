namespace SmartInventoryArrange.Core;

// Pure managed, immutable observations read on the game's main thread. Native
// pointers are identity checks only; they never decide layout order.
public sealed record SnapshotPose(int X,int Y,int Orientation,int Width,int Height,string Cells);
public sealed record SnapshotItem(nint Pointer,nint ParentPointer,string StableId,bool Movable,string State,SnapshotPose? Pose);
public sealed record InventoryObservation(nint Pointer,string Geometry,IReadOnlyList<SnapshotItem> Items);
public static class SnapshotConsistency
{
    public static bool SameNativeObject(nint left,nint right)=>left!=0&&left==right;
    public static void AssertSame(InventoryObservation expected,InventoryObservation actual,string stage)
    {
        var differences=new List<string>();
        void Diff(string field,object? before,object? after){if(!Equals(before,after))differences.Add($"{field}: old={before??"null"}, new={after??"null"}");}
        Diff("inventoryPointer",expected.Pointer,actual.Pointer);Diff("containerGeometry",expected.Geometry,actual.Geometry);
        Diff("itemCount",expected.Items.Count,actual.Items.Count);
        if(expected.Pointer==0||actual.Pointer==0)differences.Add("inventoryPointer: null");
        var oldPointers=expected.Items.Select(x=>x.Pointer).ToHashSet();
        var current=new Dictionary<nint,SnapshotItem>();
        foreach(var item in actual.Items)
        {
            if(item.Pointer==0){differences.Add("currentMember: null-pointer");continue;}
            if(!current.TryAdd(item.Pointer,item))differences.Add($"duplicateMember: pointer={item.Pointer}");
            if(!oldPointers.Contains(item.Pointer))differences.Add($"unexpectedMember: id={item.StableId}, pointer={item.Pointer}");
        }
        if(oldPointers.Count!=expected.Items.Count||oldPointers.Contains(0))differences.Add("capturedMembers: invalid-or-duplicate-pointer");
        foreach(var old in expected.Items.OrderBy(i=>i.StableId,StringComparer.Ordinal))
        {
            string prefix=$"item={old.StableId}, fixed={!old.Movable}, pointer={old.Pointer}";
            if(!current.TryGetValue(old.Pointer,out var now)){differences.Add(prefix+", member: missing");continue;}
            Diff(prefix+", parentPointer",old.ParentPointer,now.ParentPointer);
            if(old.Movable&&!SameNativeObject(old.ParentPointer,expected.Pointer))differences.Add(prefix+", capturedParent: not-target");
            Diff(prefix+", state",old.State,now.State);
            if(old.Pose==null||now.Pose==null){differences.Add(prefix+$", shape: oldPresent={old.Pose!=null}, newPresent={now.Pose!=null}");continue;}
            Diff(prefix+", x",old.Pose.X,now.Pose.X);Diff(prefix+", y",old.Pose.Y,now.Pose.Y);
            Diff(prefix+", orientation",old.Pose.Orientation,now.Pose.Orientation);
            Diff(prefix+", width",old.Pose.Width,now.Pose.Width);Diff(prefix+", height",old.Pose.Height,now.Pose.Height);
            Diff(prefix+", cells",old.Pose.Cells,now.Pose.Cells);
        }
        if(differences.Count>0)throw new InvalidOperationException($"Snapshot guard rejected stage={stage}; differences={differences.Count}; "+string.Join(" | ",differences.Take(12)));
    }
}
