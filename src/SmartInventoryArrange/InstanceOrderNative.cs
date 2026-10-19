using Il2Cpp;
using System.Text.Json;
using System.Security.Cryptography;
namespace SmartInventoryArrange;
// Main-thread capture/guard only; native objects never enter the search worker.
internal static class InstanceOrderNative
{
    private static TagState? Tag(GameItem item,string key)
    {var d=item.state?.dict;return d==null||!d.ContainsKey(key)?null:d[key];}
    private static int? Int(GameItem item,string key)=>Tag(item,key)?.valueInt;
    private static string Text(GameItem item,string key)=>Tag(item,key)?.internalValueString??"";
    private static bool Has(GameItem item,string key)=>Tag(item,key)?.valueEnabled==true;
    private static double? Double(GameItem item,string key){var t=Tag(item,key);if(t==null)return null;return double.IsFinite(t.valueDouble)?t.valueDouble:null;}
    private static long? Volume(GameItem item)
    {
        // Both liquid containers AND chemical solids have compositions. Marker
        // absence is not empty. Never call GetTag/GetTagReadonly to fill gaps.
        if(!Has(item,"LIQUID_CONTAINER_TAG")&&!Has(item,"CHEM_ITEM_TAG"))return null;
        var liquids=Liquid.Liquids;if(liquids==null)return null;
        var parts=new int?[liquids.Count];
        for(int k=0;k<parts.Length;k++){var liquid=liquids[k];if(liquid!=null&&!string.IsNullOrEmpty(liquid.currentPart))parts[k]=Int(item,liquid.currentPart);}
        return InstanceOrder.SumVolume(parts);
    }
    private sealed record Condition(string Category,string Id,int Custom);
    private static (Condition[] Conditions,string Integrity) Features(GameItem item)
    {
        var observed=new List<Condition>();var guard=new List<string>();var features=item.itemFeatures;
        if(features!=null)for(int k=0;k<features.Count;k++)
        {
            var f=features[k];if(f==null)continue;
            string category=f.category??"";
            if((int)f.featureType!=0&&(int)f.featureType!=2)continue;
            bool semantic=SemanticConditions.Relevant(category);
            bool appraisable=f.isExposable&&category!="CATEGORY_FREE";
            if(!semantic&&!appraisable)continue;
            bool discovered=f.IsFeatureDiscovered(),exposed=f.IsExposed();
            // Appraisal flags and real/fake state remain read-only integrity inputs.
            // Ranking uses real state regardless of discovery/exposure, per user policy.
            var real=f.realCondition;var fake=f.fakeCondition;
            guard.Add(JsonSerializer.Serialize(new{category,id=f.identifier,disabled=f.isDisabled,kind=(int)f.featureType,f.useCondition,f.isExposable,discovered,exposed,
                real=real==null?null:new{real.identifier,real.category,real.customValue,real.compareValue,real.display},
                fake=fake==null?null:new{fake.identifier,fake.category,fake.customValue,fake.compareValue,fake.display}}));
            if(f.isDisabled)continue;
            if(semantic&&f.useCondition&&real!=null)
                observed.Add(new(category,real.identifier??"",real.customValue));
            else if(appraisable)observed.Add(new(category,"",0));
        }
        string integrity=Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(guard.OrderBy(x=>x,StringComparer.Ordinal))));
        return (observed.OrderBy(x=>x.Category,StringComparer.Ordinal).ThenBy(x=>x.Id,StringComparer.Ordinal).ThenBy(x=>x.Custom).ToArray(),integrity);
    }
    internal static InstanceOrderInfo Read(GameItem item,string stableId)
    {
        var captured=Features(item);var conditions=captured.Conditions;
        long? volume=Volume(item);bool liquid=Has(item,"LIQUID_CONTAINER_TAG");
        bool wine=StringComparer.Ordinal.Equals(item.identifier,WineBottleOrder.TargetItemId);
        int? wineState=wine?Int(item,"WINE_CONTAINER_STATE"):null;
        bool? empty=wine?wineState switch{0=>true,1 or 2=>false,_=>null}:liquid&&volume.HasValue?volume.Value==0:null;
        string[] types=item.itemTypes==null?Array.Empty<string>():Enumerable.Range(0,item.itemTypes.Count).Select(k=>item.itemTypes[k]).ToArray();
        bool conditionOnly=types.Any(t=>t is "WEAPON" or "FIREARM" or "MELEE_WEAPON" or "MODULE" or "MACHINE" or "MACHINERY" or "STORAGE" or "BACKPACK")||Has(item,"MODULE_TAG")||Has(item,"STANDARD_MACHINE_TAG")||Has(item,"machinery")||Has(item,"BACKPACK_TAG")||Has(item,"CONTAINER_TAG");
        bool waterFeature=conditions.Any(c=>c.Category=="CATEGORY_WATER_PURITY");
        bool chem=Has(item,"CHEMICAL_PRODUCT_TAG")||conditions.Any(c=>c.Category=="CATEGORY_CHEMICAL_PURITY");
        string profile=wine?"wine":conditionOnly?"condition":chem?"chemical":liquid&&(waterFeature||Int(item,"CHEMICAL_INTENT_TAG")==1)?"water":liquid?"chemical":
            (Has(item,"power_source_item")||types.Contains("ENERGY_CELL")||Int(item,"power_source_item_energy").HasValue)?"energy":(Has(item,"USE_COUNT_ITEM_TAG")||Int(item,"CURRENT_USE_COUNT_INT").HasValue)?"uses":(Tag(item,"MAX_DURABILITY_TAG")!=null||Int(item,"DURABILITY_TAG").HasValue)?"durability":
            (conditions.Any(c=>c.Category=="CATEGORY_FRESHNESS"&&c.Id is not "module_normale" and not "module_stuck")||types.Contains("FOOD"))?"food":(item.identifier=="metal_ingot"||Tag(item,"INGOT_PURITY_TAG")!=null)?"ingot":"name";
        var value=new InstanceOrderInfo(stableId,item.identifier,item.GetDisplayName(true)??"") {Profile=profile,IsUnidentified=false,IsEmpty=empty,FeatureIntegrity=captured.Integrity};
        // Sorting ignores appraisal; no game discovery/exposure state is written.
        int? Rank(string category)
        {
            var cs=conditions.Where(c=>c.Category==category).ToArray();
            if(cs.Length==0)return null;
            var ranks=cs.Select(c=>SemanticConditions.Rank(c.Category,c.Id)).ToArray();
            return ranks.All(r=>r.HasValue)?ranks.Max():null;
        }
        var injector=conditions.FirstOrDefault(c=>c.Category=="CATEGORY_GENUINE_INJECTOR");
        int? expired=Rank("CATEGORY_EXPIRED");if(injector?.Id=="expiredInjector")expired=1;
        int? unavailable=injector?.Id=="unusableInjector"?1:null;
        if(profile=="condition"&&Int(item,"DURABILITY_TAG") is int durability)unavailable=durability<=0?1:0;
        if(conditions.Any(c=>c.Id=="module_stuck"))unavailable=1;
        else if(conditions.Any(c=>c.Id=="module_normale"))unavailable=0;
        int? counterfeit=Rank("CATEGORY_GENUINE_CIGARETTE")??Rank("CATEGORY_GENUINE_STAMP")??Rank("CATEGORY_GENUINE_INJECTOR")??Rank("CATEGORY_REPLICA");
        int? quality=null;string diagnostics="appraisal-ignored:real-state";
        if(profile=="water")
        {
            // Reuse original pure numeric grade selection, NOT absolute contaminant
            // amount. GetPurityArrayIndex has no GameItem/tag/state argument.
            long? water=Int(item,"CURRENT_PART_WATER");
            if(water.HasValue&&volume>0){long purity=Math.Clamp(water.Value*10000L/volume.Value,0,10000);quality=WaterFeatureHelper.GetPurityArrayIndex((int)purity);}
            else if(volume!=0)diagnostics+=";missing-water-composition";
        }
        else if(profile=="chemical")quality=Rank("CATEGORY_CHEMICAL_PURITY");
        else if(profile=="ingot")quality=Rank("CATEGORY_INGOT_PURITY")??SemanticConditions.Rank("CATEGORY_INGOT_PURITY",Text(item,"INGOT_PURITY_TAG"));
        string ingredient=Text(item,"ACTIVATE_INGREDIENT_TAG");int? intent=Int(item,"CHEMICAL_INTENT_TAG");
        if(profile=="food"&&Rank("CATEGORY_FRESHNESS")==null)diagnostics+=";unmapped-freshness: native condition absent; no tag direction guessed";
        string purpose=profile is "chemical" or "water"?(ingredient.Length>0?ingredient:intent.HasValue?intent.Value.ToString("D10",System.Globalization.CultureInfo.InvariantCulture):""):"";
        return value with{Purpose=purpose,WineState=wineState,AgeDays=wine?Int(item,"AGE_INT"):null,
            FermentationRemaining=wine?Int(item,"FERMENTATION_TIME_REMAINING_INT"):null,VolumeUl=volume,
            RemainingUses=profile=="uses"?Int(item,"CURRENT_USE_COUNT_INT"):null,
            RemainingEnergy=profile=="energy"?Int(item,"power_source_item_energy"):null,
            Durability=profile=="durability"?Int(item,"DURABILITY_TAG"):null,
            Purity=profile=="chemical"?Double(item,"CHEMICAL_PRODUCT_PURITY_TAG"):null,
            QualityRank=quality,FreshnessRank=Rank("CATEGORY_FRESHNESS"),EquipmentRank=Rank("CATEGORY_EQUIPMENT_CONDITION"),
            Expired=expired,Unavailable=unavailable,Opened=Rank("CATEGORY_OPEN_BOX"),Rusted=Rank("CATEGORY_RUSTED"),Dented=Rank("CATEGORY_DENTED"),Counterfeit=counterfeit,Diagnostics=diagnostics};
    }
    private static string StateIntegrity(GameItem item)
    {
        var rows=new List<string>();var dict=item.state?.dict;
        if(dict!=null)foreach(var key in dict.Keys)
        {
            var t=dict[key];
            rows.Add(t==null?JsonSerializer.Serialize(new{key,missing=true}):JsonSerializer.Serialize(new{key,t.identifier,t.valueEnabled,t.valueInt,t.valueLong,
                floatBits=BitConverter.SingleToInt32Bits(t.valueFloat),doubleBits=BitConverter.DoubleToInt64Bits(t.valueDouble),t.valueBool,t.internalValueString}));
        }
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(rows.OrderBy(x=>x,StringComparer.Ordinal))));
    }
    internal static string FingerprintSuffix(GameItem item)=>JsonSerializer.Serialize(new{order=Read(item,""),stateIntegrity=StateIntegrity(item)});
}
