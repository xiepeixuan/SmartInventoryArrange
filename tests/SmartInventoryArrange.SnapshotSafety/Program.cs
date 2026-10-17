using SmartInventoryArrange.Core;
int checks=0;
void Check(bool ok,string name){if(!ok)throw new Exception(name);checks++;}
var wrapper1=new Wrapper((nint)101);var wrapper2=new Wrapper((nint)101);
Check(!ReferenceEquals(wrapper1,wrapper2),"different CLR wrappers");
if(args.Length>0&&args[0]=="baseline")
{
 bool oldRejected=wrapper1!=wrapper2;Check(oldRejected,"old CLR predicate reproduces false rejection");Check(wrapper1.Pointer==wrapper2.Pointer,"same native identity");
 Console.WriteLine($"BASELINE_ALIAS_REPRO checks={checks}/{checks} samePointer=True oldRejected=True exit=0");return;
}
var pose=new SnapshotPose(3,4,1,1,2,"0,0;0,1");var item=new SnapshotItem(12,101,"battery#1",true,"stack=2|quality=5|durability=8",pose);
InventoryObservation View(params SnapshotItem[] items)=>new(101,"30,30|usable-mask",Array.AsReadOnly(items));
var original=View(item);SnapshotConsistency.AssertSame(original,View(item with{}),"wrapper-recreated");checks++;
Check(SnapshotConsistency.SameNativeObject(wrapper1.Pointer,wrapper2.Pointer),"native identity accepts aliases");Check(!SnapshotConsistency.SameNativeObject(0,0),"null identity rejected");Check(!SnapshotConsistency.SameNativeObject(101,102),"different native rejected");
void Reject(InventoryObservation live,string field)
{
 try{SnapshotConsistency.AssertSame(original,live,"before-apply");throw new Exception("accepted "+field);}catch(InvalidOperationException e){Check(e.Message.Contains(field)&&e.Message.Contains("stage=before-apply"),field+" diagnostic");}
}
Reject(View(),"itemCount");Reject(View(item with{Pointer=99}),"member: missing");Reject(View(item with{Pointer=99}),"unexpectedMember");Reject(View(item with{ParentPointer=102}),"parentPointer");Reject(View(item with{State="stack=1"}),"state");Reject(View(item with{Pose=null}),"shape");
Reject(View(item with{Pose=pose with{X=9}}),"x");Reject(View(item with{Pose=pose with{Y=9}}),"y");Reject(View(item with{Pose=pose with{Orientation=3}}),"orientation");Reject(View(item with{Pose=pose with{Width=2}}),"width");Reject(View(item with{Pose=pose with{Height=4}}),"height");Reject(View(item with{Pose=pose with{Cells="0,0;1,0"}}),"cells");Reject(View(item,item),"duplicateMember");Reject(View(item,new SnapshotItem(0,0,"null",false,"",null)),"null-pointer");Reject(original with{Pointer=500},"inventoryPointer");Reject(original with{Geometry="disabled-cell-changed"},"containerGeometry");
var fixedItem=item with{Movable=false,ParentPointer=202};SnapshotConsistency.AssertSame(View(fixedItem),View(fixedItem with{}),"fixed-unchanged");checks++;
try{SnapshotConsistency.AssertSame(View(fixedItem),View(fixedItem with{ParentPointer=203}),"fixed-parent-change");throw new Exception("fixed change accepted");}catch(InvalidOperationException e){Check(e.Message.Contains("parentPointer")&&e.Message.Contains("fixed=True"),"fixed parent diagnosed");}
var wrongParent=item with{ParentPointer=202};try{SnapshotConsistency.AssertSame(View(wrongParent),View(wrongParent),"capture-complete");throw new Exception("capture parent accepted");}catch(InvalidOperationException e){Check(e.Message.Contains("capturedParent"),"capture inconsistency caught");}
var second=item with{Pointer=13,StableId="battery#2"};SnapshotConsistency.AssertSame(View(item,second),View(second,item),"child-order-change");checks++;
SnapshotConsistency.AssertSame(View(),View(),"empty");checks++;
Check(original.Items[0]==item&&original.Items[0].Pose==pose,"snapshot remains unchanged");
Console.WriteLine($"SNAPSHOT checks={checks}/{checks} aliasAccepted=True realChangesRejected=True fixedParentPreserved=True diagnostics=True exit=0");
sealed class Wrapper { public nint Pointer { get; } public Wrapper(nint pointer) { Pointer=pointer; } }
