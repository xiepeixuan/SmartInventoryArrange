using SmartInventoryArrange.Core;
using System.Numerics;
using System.Reflection;
int checks=0;void Check(string name,bool value){if(!value)throw new Exception(name);checks++;}
// Force all memo states into the same hash bucket: exact record equality must still distinguish them.
var state=typeof(Native34Solver).GetNestedType("SearchState",BindingFlags.NonPublic)!;
object Make(BigInteger occupied,BigInteger counts)=>Activator.CreateInstance(state,occupied,counts)!;
var keys=new HashSet<object>(new ConstantHashComparer());for(int i=0;i<96;i++)Check("memo-add",keys.Add(Make(BigInteger.One<<i,(BigInteger.One<<130)+i)));for(int i=0;i<96;i++)Check("memo-equal",!keys.Add(Make(BigInteger.One<<i,(BigInteger.One<<130)+i)));Check("memo-no-loss",keys.Count==96);
var values=new HashSet<BigInteger>();for(int a=0;a<3;a++)for(int b=0;b<4;b++)for(int c=0;c<5;c++){BigInteger code=a+3*b+12*c;Check("radix-bijection",values.Add(code));if(c>0)Check("radix-update",code-12==a+3*b+12*(c-1));}
// Packed origin masks translated by a shift must equal the original per-cell construction,
// including L-shapes whose top-left occupied cell does not have x=0.
foreach(int W in new[]{3,13,33})foreach(var mask in new[]{new ShapeMask(new[]{new Cell(1,0),new Cell(0,1),new Cell(1,1)}),new ShapeMask(new[]{new Cell(0,0),new Cell(0,1)})})for(int y=0;y<4;y++)for(int x=0;x+mask.Width<=W;x++){BigInteger origin=0,moved=0;foreach(var c in mask.Cells){origin|=BigInteger.One<<(c.Y*W+c.X);moved|=BigInteger.One<<((c.Y+y)*W+c.X+x);}Check("packed-translation",(origin<<(y*W+x))==moved);}
var reused=new Native34Solver{NodeLimit=256};foreach(int n in new[]{201,202,206,207,212,201}){var(p,q)=GeneratedInputs.Create(n);var input=Native34Solver.Key(q);var r=reused.Solve(p,q);var fresh=new Native34Solver{NodeLimit=256};var f=fresh.Solve(p,q);Check("cache-reset-output",Native34Solver.SameLayout(r.Placements,f.Placements));Check("cache-reset-trace",reused.Diagnostic==fresh.Diagnostic);Check("source-immutable",input==Native34Solver.Key(q));Check("safe",r.Complete&&Native34Solver.ValidateLayout(p,r.Placements));Check("profiles-present",reused.Performance.Count>0&&reused.Performance.All(s=>s.Milliseconds>=0&&s.AllocatedBytes>=0));}
Console.WriteLine($"PERF_SAFETY checks={checks}/{checks} collisionEquality=True mixedRadix=True nativeMaskTranslation=True crossInputReset=True exit=0");
sealed class ConstantHashComparer:IEqualityComparer<object>{public new bool Equals(object? a,object? b)=>object.Equals(a,b);public int GetHashCode(object o)=>0;}
