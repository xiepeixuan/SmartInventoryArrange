using Il2Cpp;
using MelonLoader;
using SmartInventoryArrange;
using SmartInventoryArrange.Core;
using System.Text.Json;
using System.Security.Cryptography;
#if BASELINE
bool baseline=true;
#else
bool baseline=false;
#endif
int checks=0,cases=0;var caseNames=new List<string>();
void Check(bool ok,string why){checks++;if(!ok)throw new Exception($"{checks}: {why}");}
void Reject(Action action,string why,string? field=null){try{action();throw new Exception("accepted: "+why);}catch(InvalidOperationException e){Check(field==null||e.Message.Contains(field),why+" diagnostic: "+e.Message);}}
TagState Tag(string key,int value=0,double temp=0)=>new(){identifier=key,valueInt=value,valueDouble=temp};
Liquid.Liquids=new(){new(){currentPart="CURRENT_PART_WATER"},new(){currentPart="CURRENT_PART_CHEMICAL_CONTAMINANT"}};
(GameGridInventory Inv,GameItem Water,GameItem Second) Fixture(string id="bottled_water",bool fixedWater=false,bool keep=false){
 var inv=new GameGridInventory(12,8);var a=new GameItem{identifier=id,Name="基础水",uniqueId=0,modifiedShape=GridShape.Rect(1,3,8,3),itemTypes=new(){"WATER"},Removable=!fixedWater};
 a.state.dict["LIQUID_CONTAINER_TAG"]=Tag("LIQUID_CONTAINER_TAG");a.state.dict["CHEMICAL_INTENT_TAG"]=Tag("CHEMICAL_INTENT_TAG",1);
 a.state.dict["CURRENT_PART_WATER"]=Tag("CURRENT_PART_WATER",740000);a.state.dict["CURRENT_PART_CHEMICAL_CONTAMINANT"]=Tag("CURRENT_PART_CHEMICAL_CONTAMINANT",10000);a.state.dict["CONTAINER_TEMP_TAG"]=Tag("CONTAINER_TEMP_TAG",temp:25.97199979111705);
 var b=new GameItem{identifier="battery",Name="电池",uniqueId=0,modifiedShape=GridShape.Rect(1,2,10,5),itemTypes=new(){"ENERGY_CELL"}};b.state.dict["power_source_item_energy"]=Tag("power_source_item_energy",7);inv.Add(a);inv.Add(b);return(inv,a,b);
}
double Temp(GameItem i)=>i.state.dict["CONTAINER_TEMP_TAG"].valueDouble;
void Warm(GameItem i,double to=25.972198621415576)=>i.state.dict["CONTAINER_TEMP_TAG"].valueDouble=to;
string Pose(GameItem i)=>$"{i.modifiedShape.minX},{i.modifiedShape.minY},{i.modifiedShape.orientation}:{string.Join(';',i.modifiedShape.Cells)}";
LayoutResult Solve(InventoryTransaction tx){using var ctrl=new GlobalArrangeController();var req=ctrl.Begin(tx.Problem,tx.OriginalPlacements,tx.SessionContext);return tx.OrderInstances(req.Session.Advance(req.NodeBudget,req.SolutionBatch).Result);}
void Case(string name){cases++;caseNames.Add(name);}
new SmartArrangeMod().OnInitializeMelon();
// Baseline proof: all three affected kinds are falsely rejected solely by a
// finite temperature tick; content, grade, identity, and geometry are unchanged.
foreach(var id in new[]{"bottled_water","large_bottled_water","water_jug","water_pitcher"}){
 Case("warming-"+id);var f=Fixture(id);var tx=InventoryTransaction.Capture(f.Inv);var r=Solve(tx);Check(r.Complete&&!r.UsedFallback,"fixture produces complete fresh candidate");string oldPose=Pose(f.Water);Warm(f.Water);
 if(baseline){Reject(()=>tx.AssertSnapshotCurrent(),"old guard rejects harmless tick");Check(f.Inv.Validations==0&&Pose(f.Water)==oldPose,"baseline cancels before application");}
 else{tx.AssertSnapshotCurrent();tx.Apply(r.Placements);Check(Temp(f.Water)==25.972198621415576&&f.Inv.childItems.Count==2,"latest temperature preserved on commit");Check(Pose(f.Water)!=oldPose,"candidate actually applies after rebase");Check(MelonLogger.Lines.Any(l=>l.Contains("temperature-only-rebase")&&l.Contains("valueDoubleBits")),"raw field diff emitted");}
}
if(baseline){Console.WriteLine($"TEMPERATURE_BASELINE checks={checks}/{checks} cases={cases} harmlessWarmingRejected=True exit=0");return;}
#if !BASELINE
// Cooling, substantial thermal evolution, repeated observations and fixed water.
foreach(bool fixedWater in new[]{false,true})foreach(bool preserve in new[]{false,true})foreach(double t in new[]{24.0,27.0,-5.0,100.0}){
 Case($"temperature-{fixedWater}-{preserve}-{t}");var f=Fixture(fixedWater:fixedWater);var tx=InventoryTransaction.Capture(f.Inv,preserve);var r=Solve(tx);string pose=Pose(f.Water);Warm(f.Water,t);tx.AssertSnapshotCurrent();Warm(f.Water,t+0.01);tx.AssertSnapshotCurrent();tx.Apply(r.Placements);Check(Temp(f.Water)==t+0.01,"no stale temperature restored");if(fixedWater)Check(Pose(f.Water)==pose,"fixed thermal object remains fixed");Check(f.Inv.childItems.Select(x=>x.Pointer).Distinct().Count()==2,"all instances conserved");
}
// Changed semantics and every raw tag field must fail closed, even alongside
// an otherwise acceptable temperature drift.
var mutations=new Dictionary<string,Action<GameGridInventory,GameItem,GameItem>>{
 ["liquid-volume"]=(v,a,b)=>a.state.dict["CURRENT_PART_WATER"].valueInt--,
 ["liquid-composition-same-total"]=(v,a,b)=>{a.state.dict["CURRENT_PART_WATER"].valueInt--;a.state.dict["CURRENT_PART_CHEMICAL_CONTAMINANT"].valueInt++;},
 ["water-grade"]=(v,a,b)=>{a.state.dict["CURRENT_PART_WATER"].valueInt=400000;a.state.dict["CURRENT_PART_CHEMICAL_CONTAMINANT"].valueInt=350000;},
 ["empty"]=(v,a,b)=>{a.state.dict["CURRENT_PART_WATER"].valueInt=0;a.state.dict["CURRENT_PART_CHEMICAL_CONTAMINANT"].valueInt=0;},
 ["quality-feature"]=(v,a,b)=>a.itemFeatures.Add(new(){category="CATEGORY_WATER_PURITY",identifier="quality",realCondition=new(){category="CATEGORY_WATER_PURITY",identifier="basic",customValue=9600}}),
 ["chemical-reaction"]=(v,a,b)=>a.state.dict["CURRENT_PART_NEW_PRODUCT"]=Tag("CURRENT_PART_NEW_PRODUCT",1),
 ["stack"]=(v,a,b)=>a.unitCount++, ["id"]=(v,a,b)=>a.identifier="other_water",["uid"]=(v,a,b)=>a.uniqueId++,
 ["name"]=(v,a,b)=>a.Name="新的水名", ["category"]=(v,a,b)=>a.itemTypes.Add("MACHINE"),
 ["lock"]=(v,a,b)=>a.Removable=false, ["unlock-fixed"]=(v,a,b)=>a.Removable=true,
 ["move"]=(v,a,b)=>a.modifiedShape.minX--,["rotate"]=(v,a,b)=>a.modifiedShape.orientation=1,
 ["shape"]=(v,a,b)=>a.modifiedShape.DefaultCells=new[]{(0,0),(0,2)},
 ["parent"]=(v,a,b)=>a.parentInventory=new GameGridInventory(),["remove"]=(v,a,b)=>v.childItems.Remove(a),
 ["replace-same-id"]=(v,a,b)=>{v.childItems.Remove(a);v.Add(new(){identifier=a.identifier,Name=a.Name,uniqueId=a.uniqueId,modifiedShape=a.modifiedShape.Clone(),state=a.state,itemTypes=a.itemTypes});},
 ["duplicate"]=(v,a,b)=>v.childItems.Add(a),["disabled-cell"]=(v,a,b)=>v.inventoryShape.Disabled.Add((0,0)),
 ["sorting-disabled"]=(v,a,b)=>v.CanSort=false, ["temp-missing"]=(v,a,b)=>a.state.dict.Remove("CONTAINER_TEMP_TAG"),
 ["temp-null"]=(v,a,b)=>a.state.dict["CONTAINER_TEMP_TAG"]=null!, ["temp-identifier"]=(v,a,b)=>a.state.dict["CONTAINER_TEMP_TAG"].identifier="other",
 ["temp-enabled"]=(v,a,b)=>a.state.dict["CONTAINER_TEMP_TAG"].valueEnabled=false,["temp-int"]=(v,a,b)=>a.state.dict["CONTAINER_TEMP_TAG"].valueInt++,
 ["temp-long"]=(v,a,b)=>a.state.dict["CONTAINER_TEMP_TAG"].valueLong++, ["temp-float"]=(v,a,b)=>a.state.dict["CONTAINER_TEMP_TAG"].valueFloat=1,
 ["temp-bool"]=(v,a,b)=>a.state.dict["CONTAINER_TEMP_TAG"].valueBool=true,["temp-string"]=(v,a,b)=>a.state.dict["CONTAINER_TEMP_TAG"].internalValueString="changed",
 ["temp-NaN"]=(v,a,b)=>Warm(a,double.NaN),["temp-infinity"]=(v,a,b)=>Warm(a,double.PositiveInfinity),
 ["other-state"]=(v,a,b)=>b.state.dict["power_source_item_energy"].valueInt++,
 ["other-double"]=(v,a,b)=>b.state.dict["UNRELATED_TEMPERATURE"]=Tag("UNRELATED_TEMPERATURE",temp:26),
};
foreach(var mutation in mutations){
 Case(mutation.Key);var f=Fixture(fixedWater:mutation.Key=="unlock-fixed");var tx=InventoryTransaction.Capture(f.Inv);Warm(f.Water);mutation.Value(f.Inv,f.Water,f.Second);string pose=Pose(f.Water);
 Reject(()=>tx.AssertSnapshotCurrent(),mutation.Key);Check(f.Inv.Validations==0&&Pose(f.Water)==pose,"rejection never applies or reverts external changes");
}
// Native eligibility must still pass. A callback race during rebase must not
// publish ANY part of the new baseline.
{
 Case("native-slot-reject-and-retry");var f=Fixture();var tx=InventoryTransaction.Capture(f.Inv);var r=Solve(tx);Warm(f.Water);f.Inv.RejectSlot=true;Reject(()=>tx.Apply(r.Placements),"latest native slot invalid");Check(f.Inv.Validations==0,"no stage");f.Inv.RejectSlot=false;tx.Apply(r.Placements);Check(Temp(f.Water)==25.972198621415576,"retry retains live temperature");
}
{
 Case("rebase-confirmation-is-atomic");var f=Fixture();var tx=InventoryTransaction.Capture(f.Inv);Warm(f.Water);f.Inv.OnSlotCheck=()=>{Warm(f.Water,25.98);f.Inv.OnSlotCheck=null;};Reject(()=>tx.AssertSnapshotCurrent(),"native read/check callback changes state");Check(f.Inv.Validations==0,"no commit before confirmation");tx.AssertSnapshotCurrent();Check(Temp(f.Water)==25.98,"failed rebase can retry from original baseline");
}
// Rollback does not write old thermodynamics. Every staged shape is restored.
foreach(string fault in new[]{"slot","fixed-pose","movable-state","fixed-state","commit-temperature","membership","movable-pose"}){
 Case("rollback-"+fault);var f=Fixture(fixedWater:fault is "fixed-pose" or "fixed-state");var tx=InventoryTransaction.Capture(f.Inv);var r=Solve(tx);string p1=Pose(f.Water),p2=Pose(f.Second);Warm(f.Water);tx.AssertSnapshotCurrent();
 if(fault=="slot")f.Inv.RejectSlot=true;
 else f.Inv.OnValidate=()=>{if(fault=="fixed-pose")f.Water.modifiedShape.minX--;else if(fault=="movable-pose")f.Second.modifiedShape.minX--;else if(fault=="commit-temperature")Warm(f.Water,25.99);else if(fault=="membership")f.Inv.childItems.Remove(f.Second);else if(fault=="fixed-state")f.Water.state.dict["CURRENT_PART_WATER"].valueInt--;else f.Second.state.dict["power_source_item_energy"].valueInt++;};
 Reject(()=>tx.Apply(r.Placements),fault);Check(Pose(f.Water)==p1&&Pose(f.Second)==p2,"all original shapes restored on staged failure");Check(Temp(f.Water)==(fault=="commit-temperature"?25.99:25.972198621415576),"rollback never restores stale temperature");Check(MelonLogger.Lines.Any(x=>x.Contains("transaction rolled back")),"rollback path executed");
}
{
 Case("precheck-callback-change-before-stage");var f=Fixture();var tx=InventoryTransaction.Capture(f.Inv);var r=Solve(tx);string pose=Pose(f.Water);f.Inv.OnShapeCheck=()=>{Warm(f.Water);f.Inv.OnShapeCheck=null;};Reject(()=>tx.Apply(r.Placements),"temperature after target validation is NOT rebased");Check(f.Inv.Validations==0&&Pose(f.Water)==pose,"no stage after precheck race");
}
// Full coroutine: native readings and rebase on caller thread, geometry search
// on worker, no stale state exception from the later _movable check.
foreach(bool keep in new[]{false,true}){
 Case("coroutine-"+keep);var f=Fixture();SmartArrangeMod.Run(f.Inv,keep);Warm(f.Water);MelonCoroutines.Drain();Check(!SmartArrangeMod.Busy,"busy cleared");Check(f.Inv.Validations==1,"real production coroutine commits exactly once");Check(Temp(f.Water)==25.972198621415576,"coroutine preserves current thermal state");Check(MelonLogger.Lines.Any(x=>x.Contains("Smart arrange success")),"coroutine success observed under native API doubles");
}
// Session normalization is not just a standalone hash: the real controller
// resumes its preserved frontier after a successful sort and thermal tick.
{
 Case("session-resumes-after-temperature-tick");var f=Fixture();var tx=InventoryTransaction.Capture(f.Inv);using var ctrl=new GlobalArrangeController();var first=ctrl.Begin(tx.Problem,tx.OriginalPlacements,tx.SessionContext);var r=tx.OrderInstances(first.Session.Advance(first.NodeBudget,first.SolutionBatch).Result);tx.Apply(r.Placements);ctrl.Complete(first,tx.Problem,r.Placements);Warm(f.Water);var next=InventoryTransaction.Capture(f.Inv);var resumed=ctrl.Begin(next.Problem,next.OriginalPlacements,next.SessionContext);Check(!resumed.Restarted,"temperature tick retains search frontier");ctrl.Complete(resumed,next.Problem,next.OriginalPlacements);f.Second.state.dict["power_source_item_energy"].valueInt++;var changed=InventoryTransaction.Capture(f.Inv);Check(ctrl.Begin(changed.Problem,changed.OriginalPlacements,changed.SessionContext).Restarted,"nonthermal state still resets context");
}
// Fixed containers and non-window powerblock exceptions retain old geometry
// and participate in the SAME strict post-apply all-state guard.
foreach(bool window in new[]{false,true}){
 Case("fixed-mode-temperature-"+window);var f=Fixture(window?"water_jug":"powerblock");if(window)f.Water.contentWindow=new PixelWindow();var tx=InventoryTransaction.Capture(f.Inv,true);var r=Solve(tx);string pose=Pose(f.Water);Warm(f.Water);tx.Apply(r.Placements);Check(Pose(f.Water)==pose&&Temp(f.Water)==25.972198621415576,"preserved instance fixed with current state");
}
// Immutable snapshots and deterministic field diagnostics across dictionary order.
{
 Case("dictionary-order-immutability");var f=Fixture();var a=InstanceOrderNative.CaptureState(f.Water);f.Water.state.dict=f.Water.state.dict.Reverse().ToDictionary(x=>x.Key,x=>x.Value);Check(a.Fingerprint==InstanceOrderNative.CaptureState(f.Water).Fingerprint,"canonical hash independent of dictionary order");Warm(f.Water);var b=InstanceOrderNative.CaptureState(f.Water);Check(a.Fingerprint!=b.Fingerprint&&a.SessionToken==b.SessionToken,"full strict integrity retained, normalized session only");Check(TemperatureDriftGuard.Differences(a,b).Count==1,"only exact raw double field differs");
}
Console.WriteLine($"TEMPERATURE_SAFETY checks={checks}/{checks} cases={cases} syntheticOnly=True thermalRebase=True semanticChangesRejected=True rollback=True gameNativeExecuted=False exit=0");
#endif
