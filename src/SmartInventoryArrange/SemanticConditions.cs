namespace SmartInventoryArrange;
// Exact 0.46D IDs verified from native ItemConditionList factories. Lower rank is better.
public static class SemanticConditions
{
    public static int? Rank(string category,string id)=>category switch
    {
        "CATEGORY_INGOT_PURITY"=>Index(id,"INGOT_PURITY_PERFECT","INGOT_PURITY_VERY_HIGH","INGOT_PURITY_HIGH","INGOT_PURITY_FAIR","INGOT_PURITY_LOW","INGOT_PURITY_VERY_LOW"),
        "CATEGORY_FRESHNESS"=>Index(id,"very_fresh","fresh","stale","spoiled"),
        "CATEGORY_EQUIPMENT_CONDITION"=>Index(id,"equipment_condition_new","equipment_condition_excellent","equipment_condition_good","equipment_condition_fair","equipment_condition_poor"),
        "CATEGORY_CHEMICAL_PURITY"=>Index(id,"lab_pure","pharma_grade","near_pharma","street_standard","cut_product","junk_grade","starch_grade"),
        "CATEGORY_EXPIRED"=>Index(id,"non_expired","expired"),
        "CATEGORY_OPEN_BOX"=>Index(id,"factory_sealed","open_box"),
        "CATEGORY_RUSTED"=>Index(id,"rust_free","rusted"),
        "CATEGORY_DENTED"=>Index(id,"non_dented","dented"),
        "CATEGORY_REPLICA"=>Index(id,"non_replica","replica"),
        "CATEGORY_GENUINE_CIGARETTE"=>Index(id,"genuineCigarette","counterfeitCigarette"),
        "CATEGORY_GENUINE_STAMP"=>Index(id,"genuineStamp","counterfeitStamp"),
        "CATEGORY_GENUINE_INJECTOR"=>id=="genuineInjector"?0:id is "counterfeitInjector" or "expiredInjector" or "unusableInjector"?1:null,
        _=>null
    };
    private static int? Index(string id,params string[] values){int k=Array.IndexOf(values,id);return k<0?null:k;}
    public static bool Relevant(string category)=>category is "CATEGORY_INGOT_PURITY" or "CATEGORY_WATER_PURITY" or "CATEGORY_FRESHNESS" or "CATEGORY_EQUIPMENT_CONDITION" or "CATEGORY_CHEMICAL_PURITY" or "CATEGORY_EXPIRED" or "CATEGORY_OPEN_BOX" or "CATEGORY_RUSTED" or "CATEGORY_DENTED" or "CATEGORY_REPLICA" or "CATEGORY_GENUINE_CIGARETTE" or "CATEGORY_GENUINE_STAMP" or "CATEGORY_GENUINE_INJECTOR" or "CATEGORY_WINE_QUALITY";
}
