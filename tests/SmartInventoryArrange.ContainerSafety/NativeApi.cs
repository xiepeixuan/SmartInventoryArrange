
// Headless managed API doubles. Production menu, transaction and state reader
// sources are linked unchanged; this does NOT run Unity or the game-native DLL.
using System.Collections;
namespace HarmonyLib {
 [AttributeUsage(AttributeTargets.Class,AllowMultiple=true)] public class HarmonyPatch:Attribute {public HarmonyPatch(Type type,string name){}}
}
namespace MelonLoader {
 [AttributeUsage(AttributeTargets.Assembly)] public class MelonInfoAttribute:Attribute {public MelonInfoAttribute(Type t,string name,string version,string author){}}
 [AttributeUsage(AttributeTargets.Assembly)] public class MelonGameAttribute:Attribute {public MelonGameAttribute(string? dev,string game){}}
 public class MelonMod {public Logger LoggerInstance=new();public virtual void OnInitializeMelon(){}}
 public class Logger {public void Msg(string s)=>MelonLogger.Msg(s);}
 public static class MelonLogger {public static List<string> Lines=new();public static void Msg(string s)=>Lines.Add(s);public static void Warning(string s)=>Lines.Add(s);public static void Error(string s)=>Lines.Add(s);}
 public sealed class MelonPreferences_Entry<T> {public T Value;public MelonPreferences_Entry(T value){Value=value;}}
 public sealed class Category {public string Name;public Category(string name){Name=name;}public MelonPreferences_Entry<T> CreateEntry<T>(string id,T value,string label){MelonPreferences.Keys.Add(Name+"/"+id);return new(value);}}
 public static class MelonPreferences {public static List<string> Keys=new();public static Category CreateCategory(string name)=>new(name);}
 public static class MelonCoroutines {
  public static List<IEnumerator> Jobs=new();public static object Start(IEnumerator job){Jobs.Add(job);return job;}
  public static void Drain(){foreach(var job in Jobs.ToArray()){int max=30000;while(job.MoveNext()){Thread.Sleep(1);if(--max==0)throw new Exception("Coroutine test timeout");}}Jobs.Clear();}
 }
}
namespace UnityEngine {public enum SystemLanguage {English,Chinese,ChineseSimplified,ChineseTraditional};public static class Application {public static SystemLanguage systemLanguage=SystemLanguage.English;}}
namespace Il2CppSystem.Collections.Generic {public class List<T>:System.Collections.Generic.List<T>{}}
namespace Il2Cpp {
 public class PixelElement {
  static long next=100;public IntPtr Pointer=new(Interlocked.Increment(ref next));public int widthPixels,heightPixels;
  public T Cast<T>() where T:class=>(this as T)??throw new InvalidCastException();
 }
 public sealed class RichTextElement:PixelElement {
  public int leftPadding,rightPadding,topPadding,bottomPadding,maxCharWidth;public string Text="";
  public RichTextElement(bool a,int width,int height,bool b){widthPixels=width;heightPixels=height;}
  public void SetPadding(int a,int b,int c,int d){leftPadding=a;rightPadding=b;topPadding=c;bottomPadding=d;}
  public void SetText(string text,int max,object color){Text=text;maxCharWidth=max;}
  public void Validate(){}
 }
 public sealed class GridPixelElement:PixelElement {
  public int gridWidth,gridHeight,_xSpacing,_ySpacing;public bool justifyWidth,searchUsingPriority;
  public Dictionary<(int,int),PixelElement> Children=new();public static int FailRow=-1;
  public GridPixelElement(int w,int h,bool a){gridWidth=w;gridHeight=h;}
  public GridPixelElement SetSpacing(int x,int y){_xSpacing=x;_ySpacing=y;return this;}
  public bool AttachPos(PixelElement child,int x,int y){if(FailRow==y){FailRow=-1;return false;}return Children.TryAdd((x,y),child);}
  public void DetachAll()=>Children.Clear();
 }
 public sealed class PixelWindow:PixelElement {
  public PixelElement? childElement;public bool Visible;public int ContentCount;
  public bool Detach(PixelElement p){if(childElement?.Pointer!=p.Pointer)return false;childElement=null;return true;}
  public bool Attach(PixelElement p){if(childElement!=null)return false;childElement=p;return true;}
  public void ResizePixels(int w,int h){widthPixels=w;heightPixels=h;}public void Validate(){}public void Hide()=>Visible=false;
 }
 public sealed class ItemContextHandler {
  public PixelWindow sortWindow=new();public RichTextElement buttonSortBigStart=new(false,120,17,true),buttonSortSmallStart=new(false,120,17,true),buttonSortBigEnd=new(false,120,17,true),buttonSortSmallEnd=new(false,120,17,true);
  public object textColor=new();public GameGridInventory? currentInventory;
  public bool OnEventInit()=>true;public void TriggerButton(RichTextElement button){}public void OnEventReset(){}
  public ItemContextHandler(){var grid=new GridPixelElement(1,4,false).SetSpacing(0,1);var a=new[]{buttonSortBigStart,buttonSortSmallStart,buttonSortBigEnd,buttonSortSmallEnd};for(int k=0;k<a.Length;k++)grid.AttachPos(a[k],0,k);sortWindow.Attach(grid);sortWindow.ResizePixels(120,71);}
 }
 public sealed class GridShape {
  public int minX,minY,orientation;public bool Container;public (int X,int Y)[] DefaultCells=Array.Empty<(int,int)>();
  public int BaseWidth,BaseHeight;public int globalWidth=>orientation%2==0?BaseWidth:BaseHeight;public int globalHeight=>orientation%2==0?BaseHeight:BaseWidth;
  public int width=>globalWidth;public int height=>globalHeight;public HashSet<(int,int)> Disabled=new();
  public IEnumerable<(int X,int Y)> Cells=>DefaultCells.Select(c=>orientation switch{0=>c,1=>(BaseHeight-1-c.Y,c.X),2=>(BaseWidth-1-c.X,BaseHeight-1-c.Y),3=>(c.Y,BaseWidth-1-c.X),_=>throw new Exception("Orientation")});
  public int Get(int x,int y){int xx=x-minX,yy=y-minY;return Container?(xx<0||yy<0||xx>=width||yy>=height||Disabled.Contains((xx,yy))?1:0):(Cells.Contains((xx,yy))?1:0);}
  public GridShape Clone()=>new(){minX=minX,minY=minY,orientation=orientation,Container=Container,DefaultCells=DefaultCells.ToArray(),BaseWidth=BaseWidth,BaseHeight=BaseHeight,Disabled=new(Disabled)};
  public static GridShape Rect(int w,int h,int x=0,int y=0,int o=0,bool container=false)=>new(){minX=x,minY=y,orientation=o,BaseWidth=w,BaseHeight=h,Container=container,DefaultCells=Enumerable.Range(0,h).SelectMany(yy=>Enumerable.Range(0,w).Select(xx=>(xx,yy))).ToArray()};
 }
 public sealed class GridShapeBuilder {
  GridShape shape;public GridShapeBuilder(GridShape shape){this.shape=shape.Clone();}public GridShapeBuilder SetOrientation(int o){shape.orientation=o;return this;}public GridShapeBuilder SetPosition(int x,int y){shape.minX=x;shape.minY=y;return this;}public GridShape Build()=>shape.Clone();
 }
 public sealed partial class GameItem {
  static long next=10000;public IntPtr Pointer=new(Interlocked.Increment(ref next));public int uniqueId;public int unitCount=1;public GameGridInventory? parentInventory;public GridShape modifiedShape=GridShape.Rect(1,1);public bool Removable=true;
  PixelWindow? window;public static int NativeThread=Environment.CurrentManagedThreadId;public static int WindowReads;
  public PixelWindow? contentWindow {get{if(Environment.CurrentManagedThreadId!=NativeThread)throw new Exception("Native window read from worker");WindowReads++;return window;}set{window=value;}}
  public bool MayRemove()=>Removable;
 }
 public sealed class GameGridInventory {
  static long next=20000;public IntPtr Pointer=new(Interlocked.Increment(ref next));public GridShape inventoryShape;public List<GameItem> childItems=new();public bool CanSort=true,RejectSlot;public int Validations;public Action? OnValidate;public List<GameItem> LastIgnored=new();
  public GameGridInventory(int w=12,int h=8){inventoryShape=GridShape.Rect(w,h,container:true);}
  public void Add(GameItem i){i.parentInventory=this;childItems.Add(i);}
  bool Overlap(GridShape a,GridShape b)=>a.Cells.Any(c=>b.Get(a.minX+c.X,a.minY+c.Y)!=0);
  public bool CheckShapePlacement(GameItem i,GridShape s,Il2CppSystem.Collections.Generic.List<GameItem> ignored){LastIgnored=ignored.ToList();return s.Cells.All(c=>inventoryShape.Get(s.minX+c.X,s.minY+c.Y)==0)&&!childItems.Any(j=>j!=i&&!ignored.Contains(j)&&Overlap(s,j.modifiedShape));}
  public bool CheckInventorySlotStillValid(GameItem i,GridShape s,TagSystem state)=>!RejectSlot&&!childItems.Any(j=>j!=i&&Overlap(s,j.modifiedShape));
  public void Validate(){Validations++;var callback=OnValidate;OnValidate=null;callback?.Invoke();}public void ForceRecomputeDynamicBackground(){}
 }
 public static class InventorySortHelper {public static bool MaySort(GameGridInventory i)=>i.CanSort;public static bool CanSort(GameGridInventory i)=>i.CanSort;}
}
