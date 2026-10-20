using System.Text.Json;
using Il2Cpp;
using MelonLoader;
using SmartInventoryArrange;
using SmartInventoryArrange.Core;
#if BASELINE
bool baseline=true;
#else
bool baseline=false;
#endif
string output=args.Length>0?Path.GetFullPath(args[0]):Path.Combine(Path.GetTempPath(),"SmartInventoryArrange-PowerblockSafety");Directory.CreateDirectory(Path.Combine(output,"results"));
int checks=0;void Check(bool b,string why){checks++;if(!b)throw new Exception($"{checks}: {why}");}
void Reject(Action a,string why){bool failed=false;try{a();}catch(InvalidOperationException){failed=true;}Check(failed,why);}
GameItem Item(string id,int x=4,int y=3,bool window=false,int orientation=0,string name="能量阵列",string category="MACHINE")=>new(){identifier=id,Name=name,uniqueId=0,modifiedShape=GridShape.Rect(2,2,x,y,orientation),itemTypes=new(){category},contentWindow=window?new PixelWindow():null};
string Pose(GameItem i)=>$"{i.modifiedShape.minX},{i.modifiedShape.minY},{i.modifiedShape.orientation}:{string.Join(';',i.modifiedShape.Cells)}";
string State(GameGridInventory inv)=>string.Join('|',inv.childItems.Select(i=>$"{i.Pointer}:{i.identifier}:{i.uniqueId}:{i.unitCount}:{Pose(i)}:{InstanceOrderNative.FingerprintSuffix(i)}"));
LayoutResult Solve(InventoryTransaction tx){using var c=new GlobalArrangeController();var req=c.Begin(tx.Problem,tx.OriginalPlacements,tx.SessionContext);return tx.OrderInstances(req.Session.Advance(req.NodeBudget,req.SolutionBatch).Result);}
new SmartArrangeMod().OnInitializeMelon();int cases=0;
foreach(string id in new[]{"powerblock","Powerblock","powerblock_plus","powerblock ","other_machine","battery"})
foreach(string name in new[]{"能量阵列","Custom array name"})foreach(string category in new[]{"MACHINE","MISC"})
foreach(bool hasWindow in new[]{false,true})foreach(bool config in new[]{false,true})foreach(bool keepMode in new[]{false,true}){
 var inv=new GameGridInventory();var i=Item(id,window:hasWindow,name:name,category:category);inv.Add(i);bool effective=config&&keepMode;int reads=GameItem.WindowReads;
 var tx=InventoryTransaction.Capture(inv,effective);bool expected=effective&&(hasWindow||(!baseline&&id=="powerblock"));
 Check(tx.FixedCount==(expected?1:0),"exact ID and mode/config predicate");Check(tx.MovableCount==(expected?0:1),"movable complement");Check(tx.PreservedContainerCount==(expected?1:0),"additional-preserved count");
 if(!effective)Check(GameItem.WindowReads==reads,"ordinary/config-off path never inspects windows");
 Check(i.identifier==id&&i.Name==name&&i.unitCount==1&&i.state.dict.Count==0,"capture preserves instance data");cases++;
}
var normalResults=new List<object>();
foreach(var size in new[]{(8,8),(24,10),(30,30)}){
 var inv=new GameGridInventory(size.Item1,size.Item2);inv.inventoryShape.minX=10;inv.inventoryShape.minY=20;
 var a=Item("powerblock",12,22,orientation:1);var b=Item("powerblock",15,24,orientation:3);b.Name="第二个阵列";
 a.state.dict["TEST_CHARGE"]=new(){identifier="TEST_CHARGE",valueInt=123};b.state.dict["TEST_CHARGE"]=new(){identifier="TEST_CHARGE",valueInt=456};inv.Add(a);inv.Add(b);
 for(int k=0;k<6;k++){var q=Item("material",10+k,20,name:"test");q.modifiedShape=GridShape.Rect(1,1,10+k,20);inv.Add(q);}
 string before=State(inv),pa=Pose(a),pb=Pose(b);var normal=InventoryTransaction.Capture(inv);var nr=Solve(normal);Check(nr.Complete&&Native34Solver.ValidateLayout(normal.Problem,nr.Placements),"normal layout complete+legal");normalResults.Add(new{width=size.Item1,height=size.Item2,key=Native34Solver.Key(nr.Placements)});
 var tx=InventoryTransaction.Capture(inv,true);var r=Solve(tx);Check(r.Complete&&!r.UsedFallback&&Native34Solver.ValidateLayout(tx.Problem,r.Placements),"mixed fixed global solve complete");Check(r.Placements.Count==tx.MovableCount,"all movable once");Check(r.Placements.Select(q=>q.StableId).Distinct().Count()==r.Placements.Count,"no duplicate placement");Check(tx.FixedCount==(baseline?0:2),"both zero-ID arrays classified correctly");
 var repeat=Solve(InventoryTransaction.Capture(inv,true));Check(Native34Solver.SameLayout(r.Placements,repeat.Placements),"repeat deterministic");tx.AssertSnapshotCurrent();tx.Apply(r.Placements);
 if(!baseline){Check(Pose(a)==pa&&Pose(b)==pb,"both arrays retain exact position and direction");Check(!inv.LastIgnored.Contains(a)&&!inv.LastIgnored.Contains(b),"arrays remain native collision obstacles");}
 Check(inv.childItems.Count==8&&a.state.dict["TEST_CHARGE"].valueInt==123&&b.state.dict["TEST_CHARGE"].valueInt==456,"count and energy states conserved");tx.RestoreCapturedSnapshot();Check(State(inv)==before,"all original instances states poses restored");
}
// Other locked rules still win even with the configuration disabled.
{var inv=new GameGridInventory();var a=Item("powerblock");a.Removable=false;inv.Add(a);var tx=InventoryTransaction.Capture(inv,false);Check(tx.FixedCount==1&&tx.PreservedContainerCount==0,"native immovable still fixed when config off");}
// The production UI runner captures preference once and leaves normal sorting intact.
foreach(bool config in new[]{false,true})foreach(bool keepMode in new[]{false,true}){
 var inv=new GameGridInventory();var a=Item("powerblock",7,5);inv.Add(a);string before=State(inv);SmartArrangeMod.KeepContainers.Value=config;SmartArrangeMod.Run(inv,keepMode);bool fixedExpected=!baseline&&config&&keepMode;
 if(fixedExpected)Check(MelonCoroutines.Jobs.Count==0&&State(inv)==before,"all-array preserve mode makes no application");
 else{Check(MelonCoroutines.Jobs.Count==1,"normal/config-off or baseline runs full solver");MelonCoroutines.Drain();Check(a.modifiedShape.minX==0&&a.modifiedShape.minY==0,"normal mode may move the array");}
 Check(!SmartArrangeMod.Busy,"busy reset");
}
// Existing transaction guard automatically covers the new fixed exception.
foreach(bool corruptFixed in new[]{false,true}){
 var inv=new GameGridInventory();var a=Item("powerblock",5,3);inv.Add(a);var m=Item("material",1,1);m.modifiedShape=GridShape.Rect(1,1,1,1);inv.Add(m);var tx=InventoryTransaction.Capture(inv,true);var r=Solve(tx);Check(r.Complete&&!r.UsedFallback,"failure fixture complete");string before=State(inv);
 if(corruptFixed)inv.OnValidate=()=>a.modifiedShape=GridShape.Rect(2,2,8,5);else inv.RejectSlot=true;
 Reject(()=>tx.Apply(r.Placements),"application rejection/fixed corruption detected");Check(State(inv)==before,"failure restores full snapshot including array");
}
{var inv=new GameGridInventory();var a=Item("powerblock");inv.Add(a);var tx=InventoryTransaction.Capture(inv,true);a.modifiedShape.minX++;Reject(()=>tx.AssertSnapshotCurrent(),"external array pose change cancels before apply");Check(inv.Validations==0,"no commit after stale snapshot");}
SmartArrangeMod.KeepContainers.Value=true;
string phase=baseline?"baseline":"modified";File.WriteAllText(Path.Combine(output,$"results/powerblock-{phase}.json"),JsonSerializer.Serialize(new{checks,passed=checks,predicateCases=cases,normalResults,baseline,productionSource=true,gameNativeExecuted=false},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine($"POWERBLOCK_{phase.ToUpperInvariant()} checks={checks}/{checks} predicateCases={cases} preserveWithoutWindow={!baseline} normalMovable=True exactIdOnly=True transactionRollback=True gameNativeExecuted=False exit=0");
