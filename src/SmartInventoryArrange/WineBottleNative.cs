using System.Text.Json;
using Il2Cpp;

namespace SmartInventoryArrange;

// Called only while capturing or validating on the main thread. Reading dict
// directly preserves a missing tag as null and never invokes GetTag/init/set.
internal static class WineBottleNative
{
    internal static bool IsWine(GameItem item)=>StringComparer.Ordinal.Equals(item.identifier,WineBottleOrder.TargetItemId);
    private static int? ReadInt(GameItem item,string key)
    {
        var dict=item.state?.dict;
        if(dict==null||!dict.ContainsKey(key))return null;
        var tag=dict[key];return tag==null?null:tag.valueInt;
    }
    internal static WineBottleInfo Read(GameItem item,string stableId)=>new(stableId,item.GetDisplayName(true)??"",ReadInt(item,"WINE_CONTAINER_STATE"),ReadInt(item,"AGE_INT"));
    internal static string FingerprintSuffix(GameItem item)=>JsonSerializer.Serialize(Read(item,""));
}
