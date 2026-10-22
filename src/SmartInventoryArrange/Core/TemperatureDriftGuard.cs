using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace SmartInventoryArrange.Core;

// Immutable main-thread readings. Only this exact tag's DOUBLE value may be
// rebased, never a missing tag, other payload field, NaN, or semantic change.
public sealed record StateTag(string Key,bool Missing,string? Identifier,bool Enabled,int Int,long Long,int FloatBits,long DoubleBits,bool Bool,string? Text)
{
    public string Row()=>Missing?JsonSerializer.Serialize(new{key=Key,missing=true}):JsonSerializer.Serialize(new{key=Key,identifier=Identifier,valueEnabled=Enabled,valueInt=Int,valueLong=Long,floatBits=FloatBits,doubleBits=DoubleBits,valueBool=Bool,internalValueString=Text});
}
public sealed record ItemStateSnapshot(string Prefix,string OrderJson,IReadOnlyList<StateTag> Tags)
{
    public string Integrity=>Hash(Tags);
    public string Fingerprint=>Prefix+"|instance-order={\"order\":"+OrderJson+",\"stateIntegrity\":\""+Integrity+"\"}";
    public string SessionToken=>Prefix+"|order="+OrderJson+"|state="+Hash(Tags.Select(t=>TemperatureDriftGuard.Thermal(t)?t with{DoubleBits=0}:t));
    private static string Hash(IEnumerable<StateTag> tags)=>Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(tags.Select(t=>t.Row()).OrderBy(x=>x,StringComparer.Ordinal))));
}
public static class TemperatureDriftGuard
{
    public const string TemperatureTag="CONTAINER_TEMP_TAG";
    public static bool Thermal(StateTag t)=>t.Key==TemperatureTag&&!t.Missing&&t.Identifier==TemperatureTag&&double.IsFinite(BitConverter.Int64BitsToDouble(t.DoubleBits));
    public static IReadOnlyList<string> Differences(ItemStateSnapshot before,ItemStateSnapshot after)
    {
        var output=new List<string>();
        void D(string key,string field,object? a,object? b){if(!Equals(a,b))output.Add($"tag={key} field={field} old={a??"null"} new={b??"null"}");}
        D("<item>","identity/stack/state-string",before.Prefix,after.Prefix);D("<item>","ordering/feature-integrity",before.OrderJson,after.OrderJson);
        var old=before.Tags.ToDictionary(t=>t.Key,StringComparer.Ordinal);var now=after.Tags.ToDictionary(t=>t.Key,StringComparer.Ordinal);
        foreach(string key in old.Keys.Union(now.Keys,StringComparer.Ordinal).OrderBy(x=>x,StringComparer.Ordinal))
        {
            if(!old.TryGetValue(key,out var a)||!now.TryGetValue(key,out var b)){output.Add($"tag={key} field=membership old={old.ContainsKey(key)} new={now.ContainsKey(key)}");continue;}
            D(key,"missing",a.Missing,b.Missing);D(key,"identifier",a.Identifier,b.Identifier);D(key,"valueEnabled",a.Enabled,b.Enabled);D(key,"valueInt",a.Int,b.Int);D(key,"valueLong",a.Long,b.Long);D(key,"valueFloatBits",a.FloatBits,b.FloatBits);
            D(key,"valueDoubleBits",a.DoubleBits,b.DoubleBits);D(key,"valueBool",a.Bool,b.Bool);D(key,"internalValueString",a.Text,b.Text);
        }
        return output.AsReadOnly();
    }
    public static bool IsTemperatureOnly(ItemStateSnapshot before,ItemStateSnapshot after)
    {
        if(before.Prefix!=after.Prefix||before.OrderJson!=after.OrderJson||before.Tags.Count!=after.Tags.Count)return false;
        var current=after.Tags.ToDictionary(t=>t.Key,StringComparer.Ordinal);bool changed=false;
        foreach(var a in before.Tags)
        {
            if(!current.TryGetValue(a.Key,out var b))return false;
            if(a==b)continue;
            if(!Thermal(a)||!Thermal(b)||a with{DoubleBits=b.DoubleBits}!=b)return false;
            changed=true;
        }
        return changed;
    }
}
