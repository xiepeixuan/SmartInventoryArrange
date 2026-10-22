namespace Il2Cpp;
// Deliberately narrow fake API. The SAME production reader source is compiled
// against this API and the real game separately. No game-native test is claimed.
public sealed class TagState {public string identifier="";public float valueFloat;public long valueLong;public bool valueBool;public bool valueEnabled=true;public int valueInt;public double valueDouble;public string internalValueString="";}
public sealed class TagSystem {public Dictionary<string,TagState> dict=new(StringComparer.Ordinal);public TagState GetTag(string key)=>throw new Exception("MUTATION GetTag");}
public sealed partial class GameItem {
 public string identifier="";public string Name="";public TagSystem state=new();public List<ItemFeature> itemFeatures=new();public List<string> itemTypes=new();
 public string GetDisplayName(bool preferCustomName){if(Environment.CurrentManagedThreadId!=NativeThread)throw new Exception("Native display/state read from worker");return Name;}
 public TagState GetTagReadonly(string key)=>throw new Exception("FORBIDDEN initializer-through-readonly");
}
public sealed class ItemCondition {public string identifier="";public string category="";public int customValue;public int compareValue;public string display="";}
public sealed class ItemFeature {
 public string category="",identifier="";public bool isDisabled;public int featureType;public bool useCondition=true,isExposable;public ItemCondition? realCondition,fakeCondition;
 public bool Discovered,Exposed,InitiallyShown,PublicHidden;
 public bool IsInitiallyShown()=>InitiallyShown;public bool GetPublicHidden()=>PublicHidden;
 public bool IsFeatureDiscovered()=>Discovered;public bool IsExposed()=>Exposed;
 public void SetExposed(bool b)=>throw new Exception("MUTATION appraisal");
}
public sealed class Liquid {public static List<Liquid> Liquids=new();public string currentPart="";}
public static class WaterFeatureHelper {
 // Native method's read-only loop and factory grade boundaries reproduced for
 // headless testing. Production calls actual original GetPurityArrayIndex(int).
 public static int GetPurityArrayIndex(int n){int[] thresholds={9990,9900,9800,9600,9000,3500};for(int k=0;k<thresholds.Length;k++)if(n>=thresholds[k])return k;return 5;}
}
