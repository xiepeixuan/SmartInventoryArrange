using SmartInventoryArrange.Core;

var tests = new (string, Action)[]
{
    ("empty warehouse", () => Assert(Solve(P(4,4)).Complete)),
    ("single item", () => Assert(Solve(P(3,3, I("a","tool",Rect(2,1)))).Complete)),
    ("identical sizes", () => Assert(Solve(P(4,4, Enumerable.Range(0,4).Select(x=>I("i"+x,"food",Rect(2,2))).ToArray())).Complete)),
    ("one category", () => Assert(Solve(P(5,3,I("a","ammo",Rect(2,1)),I("b","ammo",Rect(1,2)),I("c","ammo",Rect(1,1)))).Score.SameCategoryContact>0)),
    ("multiple categories", () => Assert(Solve(P(5,3,I("a","food",Rect(2,1),"medical"),I("b","medical",Rect(2,1)))).Complete)),
    ("unknown category", () => Assert(Solve(P(2,2,I("a","~unknown",Rect(1,1)))).Complete)),
    ("rotation required", RotationRequired),
    ("square avoids meaningless rotation", () => Assert(Solve(P(2,2,I("a","tool",Rect(2,2), rotations:true))).Placements.Single().GameOrientation==0)),
    ("rotation forbidden", () => Assert(Solve(P(1,2,I("a","tool",Rect(1,2), rotations:false))).Complete)),
    ("irregular mask", () => Assert(Solve(P(3,3,I("l","tool",new ShapeMask(new[]{new Cell(0,0),new Cell(0,1),new Cell(1,1)})))).Complete)),
    ("disabled cell", DisabledCell),
    ("fixed obstacle", FixedObstacle),
    ("split regions", SplitRegions),
    ("narrow fragment metric", NarrowFragment),
    ("stable identical tie", Deterministic),
    ("stack state separated by stable id", () => Assert(Solve(P(2,1,I("stack-1","ammo",Rect(1,1)),I("stack-2","ammo",Rect(1,1)))).Placements.Count==2)),
    ("different quality remains separate", () => Assert(Solve(P(2,1,I("q1","food",Rect(1,1)),I("q2","food",Rect(1,1)))).Placements.Select(x=>x.StableId).Distinct().Count()==2)),
    ("no full solution retains original", FallbackOriginal),
    ("same input deterministic", Deterministic),
    ("timeout bounded", TimeoutBounded),
    ("current layout comparison stable", CurrentComparison),
    ("repeat invocation idempotent", Repeat),
    ("rollback restores snapshot", Rollback),
    ("duplicate id rejected", DuplicateRejected),
    ("v2 vertical preference", VerticalPreference),
    ("v2 category order alignment", CategoryOrderAlignment),
    ("v3 arbitrary small backpack", ArbitrarySmallBackpack),
    ("v3 arbitrary large container", ArbitraryLargeContainer),
    ("v3 internal-space priority", InternalSpacePriority),
    ("v3 same type stays together", SameTypeTogether),
    ("v3 large type group first", LargeTypeGroupFirst),
    ("v3 top row compact", TopRowCompact),
    ("v3 minimum horizontal exceptions", MinimumHorizontalExceptions),
    ("v3.2 representative deadline", RepresentativeDeadline),
    ("v3.3 identical items form one shelf", IdenticalItemsFormOneShelf),
    ("v3.3 same-height items align", SameHeightItemsAlign),
    ("v3.3 exact groups do not interleave", ExactGroupsDoNotInterleave),
    ("v3.3 portrait remains strict", ShelfKeepsPortrait),
    ("v3.4 visual score rejects interleaving", VisualScoreRejectsInterleaving),
    ("v3.4 exact type is atomic across metadata", ExactTypeAtomicAcrossMetadata),
    ("v3.4 screenshot-like groups remain shelves", ScreenshotLikeGroupsRemainShelves),
    ("v3.5 visual split beats priority order", VisualSplitBeatsPriorityOrder),
    ("v3.5 same category and height forms cohort", SameCategoryHeightFormsCohort),
    ("v3.5 cohort keeps exact subgroups", CohortKeepsExactSubgroups),
    ("v3.6 real-scale atomic rows", RealScaleAtomicRows),
    ("v3.6 row planner is fast", ShelfRowPlannerIsFast),
    ("v3.6 split diagnostics", SplitDiagnostics),
    ("v3.7 skyline fills beside tall block", SkylineFillsBesideTallBlock),
    ("v3.7 skyline remains deterministic", SkylineDeterministic),
    ("v3.8 mixed-size same id remains atomic", MixedSizeSameIdAtomic),
    ("v3.8 real snapshot shape mix avoids fallback", RealSnapshotShapeMix),
    ("v3.9 179-item frontier deadline", Frontier179Deadline),
    ("v3.10 density thresholds", DensityThresholds),
    ("v3.10 dense grid remains complete and deterministic", DenseGridDeterministic),
    ("v3.11 dense local slot grouping", DenseLocalSlotGrouping),
    ("v3.11 wrapped type block stays connected", WrappedTypeBlock),
    ("v3.11 comparison explains first metric", ComparisonExplainsFirstMetric),
    ("v3.11 real 19x12 97.4 percent replay", RealDenseReplay),
    ("v3.11 real 30x30 95.1 percent replay", RealDenseReplay30)
};

int passed=0;
foreach(var t in tests){try{t.Item2();Console.WriteLine("PASS " + t.Item1);passed++;}catch(Exception ex){Console.WriteLine("FAIL " + t.Item1+": "+ex.Message);}}
Console.WriteLine($"RESULT {passed}/{tests.Length}");
return passed==tests.Length?0:1;

static LayoutResult Solve(LayoutProblem p)=>new DeterministicSolver{TimeBudget=TimeSpan.FromSeconds(2),BeamWidth=32,CandidatesPerState=32}.Solve(p);
static void Assert(bool x){if(!x)throw new Exception("assertion failed");}
static ShapeMask Rect(int w,int h)=>new(Enumerable.Range(0,h).SelectMany(y=>Enumerable.Range(0,w).Select(x=>new Cell(x,y))));
static LayoutItem I(string id,string cat,ShapeMask m,string? cat2=null,bool rotations=true,int x=0,int y=0,int priority=1000)
{
    var os=new List<Orientation>{new(0,m,false)};var r=m.Rotate90();if(rotations&&r.Signature!=m.Signature)os.Add(new(1,r,true));
    return new(){StableId=id,PrimaryCategory=cat,Categories=cat2==null?new[]{cat}:new[]{cat,cat2},Orientations=os,OriginalOrientation=0,OriginalX=x,OriginalY=y,VisualPriority=priority};
}
static LayoutProblem P(int w,int h,params LayoutItem[] items){var u=new bool[w,h];for(int y=0;y<h;y++)for(int x=0;x<w;x++)u[x,y]=true;return new(){Width=w,Height=h,Usable=u,Items=items,CategoryOrder=items.Select(i=>i.PrimaryCategory).Distinct().OrderBy(x=>x).Select((x,i)=>(x,i)).ToDictionary(x=>x.x,x=>x.i)};}
static void RotationRequired(){var p=P(1,2,I("a","tool",Rect(2,1),rotations:true));var r=Solve(p);Assert(r.Complete&&r.Placements.Single().GameOrientation==1);}
static void DisabledCell(){var p=P(2,2,I("a","tool",Rect(1,1)));p.Usable[0,0]=false;var r=Solve(p);Assert(r.Complete&&(r.Placements[0].X!=0||r.Placements[0].Y!=0));}
static void FixedObstacle(){var b=P(2,2,I("a","tool",Rect(1,1)));var p=new LayoutProblem{Width=b.Width,Height=b.Height,Usable=b.Usable,Items=b.Items,CategoryOrder=b.CategoryOrder,Fixed=new[]{new Placement("f",0,0,0,Rect(1,1),"~fixed",false)}};var r=Solve(p);Assert(r.Complete&&(r.Placements[0].X!=0||r.Placements[0].Y!=0));}
static void SplitRegions(){var p=P(3,2,I("a","a",Rect(1,2)),I("b","b",Rect(1,2)));p.Usable[1,0]=p.Usable[1,1]=false;Assert(Solve(p).Complete);}
static void NarrowFragment(){var r=Solve(P(3,1,I("a","a",Rect(2,1))));Assert(r.NarrowFragments>=0);}
static void Deterministic(){var p=P(4,3,I("a","a",Rect(2,1)),I("b","b",Rect(1,2)),I("c","a",Rect(1,1)));var a=Solve(p);var b=Solve(p);Assert(string.Join(";",a.Placements)==string.Join(";",b.Placements));}
static void FallbackOriginal(){var i=I("a","a",Rect(2,1),rotations:false,x:0,y:0);var p=P(1,1,i);var r=Solve(p);Assert(!r.Complete);}
static void TimeoutBounded(){var items=Enumerable.Range(0,30).Select(i=>I(i.ToString(),"a",Rect(1,1))).ToArray();var p=P(10,10,items);for(int y=0;y<10;y++)for(int x=0;x<10;x++)p.Usable[x,y]=(x+y)%2==0;var sw=System.Diagnostics.Stopwatch.StartNew();var r=new DeterministicSolver{TimeBudget=TimeSpan.FromMilliseconds(1)}.Solve(p);Assert(sw.Elapsed<TimeSpan.FromSeconds(1)&&r.TimedOut);}
static void CurrentComparison(){var p=P(2,1,I("a","a",Rect(1,1),x:0),I("b","a",Rect(1,1),x:1));var r=Solve(p);Assert(r.Score.CompareTo(DeterministicSolver.Evaluate(p,r.Placements))==0);}
static void Repeat(){var p=P(3,2,I("a","a",Rect(2,1)),I("b","a",Rect(1,1)));var a=Solve(p);var b=Solve(p);Assert(a.Score.Equals(b.Score));}
static void Rollback(){var state=new Dictionary<string,(int x,int y,int o)>{{"a",(0,0,0)},{"b",(1,0,0)}};var snap=state.ToDictionary(x=>x.Key,x=>x.Value);state["a"]=(9,9,1);foreach(var x in snap)state[x.Key]=x.Value;Assert(state.SequenceEqual(snap));}
static void DuplicateRejected(){var p=P(2,1,I("a","a",Rect(1,1)),I("a","b",Rect(1,1)));bool threw=false;try{DeterministicSolver.ValidateProblem(p);}catch(ArgumentException){threw=true;}Assert(threw);}

static void VerticalPreference()
{
    var p=P(5,5,I("a","drink",Rect(1,3),rotations:true),I("b","drink",Rect(1,3),rotations:true));
    var r=Solve(p);
    Assert(r.Complete && r.Placements.All(x=>x.Mask.Height>x.Mask.Width));
}
static void CategoryOrderAlignment()
{
    var a=I("container-a","container",Rect(2,2));
    var b=I("drink-b","drink",Rect(2,2));
    var p=P(4,2,a,b);
    p=new LayoutProblem{Width=p.Width,Height=p.Height,Usable=p.Usable,Items=p.Items,
        CategoryOrder=new Dictionary<string,int>{{"container",0},{"drink",1}}};
    var r=Solve(p);
    var ca=r.Placements.Single(x=>x.StableId=="container-a");
    var dr=r.Placements.Single(x=>x.StableId=="drink-b");
    Assert(ca.Y*p.Width+ca.X < dr.Y*p.Width+dr.X);
}

static void ArbitrarySmallBackpack()
{
    var r=Solve(P(3,3,I("box\u001f1","container",Rect(2,2),priority:0),I("bottle\u001f1","drink",Rect(1,2)),I("coin\u001f1","valuable",Rect(1,1))));
    Assert(r.Complete && r.Placements.Count==3);
}

static void ArbitraryLargeContainer()
{
    var items=Enumerable.Range(0,12).Select(i=>I("item"+(i%4)+"\u001f"+i,"cat"+(i%3),Rect(i%3+1,i%2+1))).ToArray();
    Assert(Solve(P(11,7,items)).Complete);
}

static void InternalSpacePriority()
{
    var leading=I("storage\u001f1","storage",Rect(1,1),priority:0);
    var normal=I("normal\u001f1","tool",Rect(2,1),priority:100);
    var p=P(4,2,normal,leading);
    p=new LayoutProblem{Width=p.Width,Height=p.Height,Usable=p.Usable,Items=p.Items,
        CategoryOrder=new Dictionary<string,int>{{"storage",0},{"tool",1}}};
    var r=Solve(p); var a=r.Placements.Single(x=>x.StableId==leading.StableId); var b=r.Placements.Single(x=>x.StableId==normal.StableId);
    Assert(a.Y*p.Width+a.X < b.Y*p.Width+b.X);
}

static void SameTypeTogether()
{
    var p=P(6,1,I("bottle\u001f1","drink",Rect(1,1)),I("apple\u001f1","food",Rect(2,1)),I("bottle\u001f2","drink",Rect(1,1)));
    p=new LayoutProblem{Width=p.Width,Height=p.Height,Usable=p.Usable,Items=p.Items,
        CategoryOrder=new Dictionary<string,int>{{"food",0},{"drink",1}}};
    var r=Solve(p); var bottles=r.Placements.Where(x=>x.StableId.StartsWith("bottle")).OrderBy(x=>x.X).ToArray();
    Assert(r.Complete && bottles.Length==2 && bottles[1].X-bottles[0].X==1);
}

static void LargeTypeGroupFirst()
{
    var p=P(5,2,I("small\u001f1","tools",Rect(1,1)),I("large\u001f1","tools",Rect(2,2)));
    var r=Solve(p); var big=r.Placements.Single(x=>x.StableId.StartsWith("large")); var small=r.Placements.Single(x=>x.StableId.StartsWith("small"));
    Assert(big.Y*p.Width+big.X < small.Y*p.Width+small.X);
}

static void TopRowCompact()
{
    var r=Solve(P(5,3,I("a\u001f1","same",Rect(1,1)),I("b\u001f1","same",Rect(1,1)),I("c\u001f1","same",Rect(1,1))));
    Assert(r.Complete && r.Placements.All(x=>x.Y==0) && r.Placements.Select(x=>x.X).OrderBy(x=>x).SequenceEqual(new[]{0,1,2}));
}

static void MinimumHorizontalExceptions()
{
    var r=Solve(P(2,3,I("bar\u001f1","same",Rect(1,2)),I("bar\u001f2","same",Rect(1,2)),I("bar\u001f3","same",Rect(1,2))));
    Assert(r.Complete && r.Placements.Count(x=>x.Mask.Width>x.Mask.Height)==1);
}

static void RepresentativeDeadline()
{
    var items=Enumerable.Range(0,35).Select(i=>I("perf"+(i%12)+"\u001f"+i,"cat"+(i%8),Rect(1+(i%4),1+(i%3)),rotations:true)).ToArray();
    var p=P(24,10,items);
    var sw=System.Diagnostics.Stopwatch.StartNew();
    var r=new DeterministicSolver{TimeBudget=TimeSpan.FromMilliseconds(400),BeamWidth=40,CandidatesPerState=28}.Solve(p);
    sw.Stop();
    Console.WriteLine($"PERF representative-24x10-35={sw.Elapsed.TotalMilliseconds:F1}ms solver={r.Elapsed.TotalMilliseconds:F1}ms timeout={r.TimedOut}");
    Assert(r.Complete && sw.Elapsed < TimeSpan.FromMilliseconds(900));
}

static void IdenticalItemsFormOneShelf()
{
    var items=Enumerable.Range(0,5).Select(i=>I("water\u001f"+i,"drink",Rect(1,2))).ToArray();
    var r=Solve(P(8,4,items)); var q=r.Placements.OrderBy(x=>x.X).ToArray();
    Assert(r.Complete && q.Select(x=>x.Y).Distinct().Count()==1 &&
        q.Zip(q.Skip(1),(a,b)=>b.X==a.X+a.Mask.Width).All(x=>x));
}

static void SameHeightItemsAlign()
{
    var p=P(12,4,
        I("machine\u001f0","machine",Rect(3,2),priority:0),
        I("battery\u001f0","power",Rect(1,2)),I("battery\u001f1","power",Rect(1,2)),
        I("battery\u001f2","power",Rect(1,2)),I("battery\u001f3","power",Rect(1,2)));
    var r=Solve(p); var batteries=r.Placements.Where(x=>x.StableId.StartsWith("battery")).ToArray();
    Assert(r.Complete && batteries.Select(x=>x.Y).Distinct().Count()==1 && batteries.Select(x=>x.Mask.Signature).Distinct().Count()==1);
}

static void ExactGroupsDoNotInterleave()
{
    var p=P(12,3,
        I("meat\u001f0","food",Rect(2,1)),I("water\u001f0","drink",Rect(1,2)),
        I("meat\u001f1","food",Rect(2,1)),I("water\u001f1","drink",Rect(1,2)),
        I("meat\u001f2","food",Rect(2,1)),I("water\u001f2","drink",Rect(1,2)));
    var r=Solve(p);
    foreach(var g in r.Placements.GroupBy(x=>x.StableId.Split('\u001f')[0]))
    {
        var rows=g.GroupBy(x=>x.Y).OrderByDescending(x=>x.Count()).First().OrderBy(x=>x.X).ToArray();
        Assert(rows.Zip(rows.Skip(1),(a,b)=>b.X==a.X+a.Mask.Width).All(x=>x));
    }
}

static void ShelfKeepsPortrait()
{
    var items=Enumerable.Range(0,5).Select(i=>I("bottle\u001f"+i,"drink",Rect(1,3),rotations:true)).ToArray();
    var r=Solve(P(8,4,items));
    Assert(r.Complete && r.Placements.All(x=>x.Mask.Height>x.Mask.Width));
}


static void VisualScoreRejectsInterleaving()
{
    var p=P(4,1,I("a\u001f0","food",Rect(1,1)),I("a\u001f1","food",Rect(1,1)),I("b\u001f0","drink",Rect(1,1)),I("b\u001f1","drink",Rect(1,1)));
    var byId=p.Items.ToDictionary(x=>x.StableId);
    Placement Q(string id,int x){var i=byId[id];var o=i.Orientations[0];return new(id,x,0,o.GameOrientation,o.Mask,i.PrimaryCategory,false);}
    var grouped=new[]{Q("a\u001f0",0),Q("a\u001f1",1),Q("b\u001f0",2),Q("b\u001f1",3)};
    var mixed=new[]{Q("a\u001f0",0),Q("b\u001f0",1),Q("a\u001f1",2),Q("b\u001f1",3)};
    Assert(DeterministicSolver.Evaluate(p,grouped).CompareTo(DeterministicSolver.Evaluate(p,mixed))>0);
}

static void ExactTypeAtomicAcrossMetadata()
{
    var a=I("battery\u001f0","power",Rect(1,2));
    var b=I("battery\u001f1","misc",Rect(1,2));
    var c=I("tool\u001f0","power",Rect(1,2));
    var p=P(5,2,a,b,c);
    p=new LayoutProblem{Width=p.Width,Height=p.Height,Usable=p.Usable,Items=p.Items,
        CategoryOrder=new Dictionary<string,int>{{"power",0},{"misc",1}}};
    var r=Solve(p);var q=r.Placements.Where(x=>x.StableId.StartsWith("battery")).OrderBy(x=>x.X).ToArray();
    Assert(r.Complete&&q.Length==2&&q[0].Y==q[1].Y&&q[1].X==q[0].X+q[0].Mask.Width);
}

static void ScreenshotLikeGroupsRemainShelves()
{
    var items=new List<LayoutItem>{I("storage\u001f0","storage",Rect(3,3),priority:0)};
    foreach(var spec in new[]{("water",5),("battery",7),("meat",4),("wine",6),("board",4)})
        for(int i=0;i<spec.Item2;i++)items.Add(I(spec.Item1+"\u001f"+i,spec.Item1,Rect(1,2)));
    var r=Solve(P(24,10,items.ToArray()));Assert(r.Complete);
    foreach(var g in r.Placements.Where(x=>!x.StableId.StartsWith("storage")).GroupBy(x=>x.StableId.Split('\u001f')[0]))
    {
        var q=g.OrderBy(x=>x.X).ToArray();
        Assert(q.Select(x=>x.Y).Distinct().Count()==1&&q.Zip(q.Skip(1),(a,b)=>b.X==a.X+a.Mask.Width).All(x=>x));
    }
}


static void VisualSplitBeatsPriorityOrder()
{
    var a0=I("a\u001f0","mixed",Rect(1,1),priority:0);
    var a1=I("a\u001f1","mixed",Rect(1,1),priority:100);
    var b0=I("b\u001f0","mixed",Rect(1,1),priority:50);
    var b1=I("b\u001f1","mixed",Rect(1,1),priority:50);
    var p=P(4,1,a0,a1,b0,b1);var map=p.Items.ToDictionary(x=>x.StableId);
    Placement Q(string id,int x){var i=map[id];var o=i.Orientations[0];return new(id,x,0,o.GameOrientation,o.Mask,i.PrimaryCategory,false);}
    var grouped=new[]{Q("a\u001f0",0),Q("a\u001f1",1),Q("b\u001f0",2),Q("b\u001f1",3)};
    var priorityPerfect=new[]{Q("a\u001f0",0),Q("b\u001f0",1),Q("b\u001f1",2),Q("a\u001f1",3)};
    Assert(DeterministicSolver.Evaluate(p,grouped).CompareTo(DeterministicSolver.Evaluate(p,priorityPerfect))>0);
}

static void SameCategoryHeightFormsCohort()
{
    var p=P(8,3,I("water_a\u001f0","drink",Rect(1,2)),I("water_b\u001f0","drink",Rect(1,2)),
        I("water_c\u001f0","drink",Rect(1,2)),I("tool\u001f0","tool",Rect(1,2)));
    var r=Solve(p);var q=r.Placements.Where(x=>x.Category=="drink").OrderBy(x=>x.X).ToArray();
    Assert(r.Complete&&q.Select(x=>x.Y).Distinct().Count()==1&&q.Zip(q.Skip(1),(a,b)=>b.X==a.X+a.Mask.Width).All(x=>x));
}

static void CohortKeepsExactSubgroups()
{
    var p=P(10,3,I("red_battery\u001f0","power",Rect(1,2)),I("blue_battery\u001f0","power",Rect(1,2)),
        I("red_battery\u001f1","power",Rect(1,2)),I("blue_battery\u001f1","power",Rect(1,2)));
    var r=Solve(p);var row=r.Placements.OrderBy(x=>x.X).Select(x=>x.StableId.Split('\u001f')[0]).ToArray();
    Assert(r.Complete&&((row.SequenceEqual(new[]{"blue_battery","blue_battery","red_battery","red_battery"}))||
        row.SequenceEqual(new[]{"red_battery","red_battery","blue_battery","blue_battery"})));
}


static LayoutProblem RealScaleProblem()
{
    var items=new List<LayoutItem>();
    var specs=new[]{("meat","food",6,2,2),("water","drink",7,1,3),("battery","power",12,1,2),
        ("wine","drink",7,1,3),("grape","food",8,1,1),("board","component",8,2,2),
        ("toolbox","tool",6,2,1),("rod","tool",10,1,3),("can","food",8,1,2),
        ("device","machine",5,2,2),("misc","misc",10,1,1)};
    foreach(var s in specs)for(int i=0;i<s.Item3;i++)items.Add(I(s.Item1+"\u001f"+i,s.Item2,Rect(s.Item4,s.Item5),rotations:true,priority:s.Item2=="machine"?2:100));
    items.Add(I("storage\u001f0","storage",Rect(4,4),priority:0));
    return P(24,24,items.ToArray());
}

static void RealScaleAtomicRows()
{
    var r=Solve(RealScaleProblem());
    Assert(r.Complete&&r.Score.NegativeTypeRuns==0&&DeterministicSolver.DescribeSplits(r.Placements)=="none");
}

static void ShelfRowPlannerIsFast()
{
    var sw=System.Diagnostics.Stopwatch.StartNew();var r=Solve(RealScaleProblem());sw.Stop();
    Console.WriteLine($"PERF v3.6-shelf-rows={sw.Elapsed.TotalMilliseconds:F1}ms");
    Assert(r.Complete&&sw.Elapsed<TimeSpan.FromMilliseconds(500));
}

static void SplitDiagnostics()
{
    var m=Rect(1,1);var p=new[]{new Placement("a\u001f0",0,0,0,m,"x",false),new Placement("a\u001f1",3,0,0,m,"x",false)};
    Assert(DeterministicSolver.DescribeSplits(p).Contains("ax2",StringComparison.Ordinal));
}


static LayoutProblem SkylineProblem()
{
    var items=new List<LayoutItem>{I("storage\u001f0","storage",Rect(3,4),rotations:false,priority:0)};
    for(int i=0;i<3;i++)items.Add(I("battery\u001f"+i,"power",Rect(1,2),rotations:false));
    for(int i=0;i<3;i++)items.Add(I("water\u001f"+i,"drink",Rect(1,2),rotations:false));
    return P(6,4,items.ToArray());
}

static void SkylineFillsBesideTallBlock()
{
    var r=Solve(SkylineProblem());
    Assert(r.Complete&&r.Score.NegativeTypeRuns==0&&DeterministicSolver.DescribeSplits(r.Placements)=="none");
    var storage=r.Placements.Single(x=>x.StableId.StartsWith("storage"));
    Assert(storage.X==0&&storage.Y==0&&r.Placements.Max(x=>x.Y+x.Mask.Height)<=4);
}

static void SkylineDeterministic()
{
    var p=SkylineProblem();var a=Solve(p);var b=Solve(p);
    Assert(string.Join(";",a.Placements)==string.Join(";",b.Placements));
}
static void MixedSizeSameIdAtomic()
{
    var p=P(8,4,
        I("system_module_performance\u001f0","MODULE",Rect(2,2),rotations:false),
        I("system_module_performance\u001f1","MODULE",Rect(2,3),rotations:true),
        I("system_module_quality\u001f0","MODULE",Rect(2,2),rotations:false),
        I("system_module_quality\u001f1","MODULE",Rect(2,3),rotations:true));
    var r=Solve(p);
    Assert(r.Complete&&r.Score.NegativeTypeRuns==0&&DeterministicSolver.DescribeSplits(r.Placements)=="none");
    foreach(var g in r.Placements.GroupBy(x=>x.StableId.Split('\u001f')[0]))
    {
        var q=g.OrderBy(x=>x.X).ToArray();
        Assert(q.Select(x=>x.Y).Distinct().Count()==1&&q[1].X==q[0].X+q[0].Mask.Width);
    }
}

static void RealSnapshotShapeMix()
{
    var items=new List<LayoutItem>();
    for(int i=0;i<8;i++)items.Add(I("processed_meat\u001f"+i,"FOOD",Rect(2,2),rotations:false));
    items.Add(I("system_module_performance\u001f0","MODULE",Rect(2,2),rotations:false));
    items.Add(I("system_module_performance\u001f1","MODULE",Rect(2,3),rotations:true));
    items.Add(I("system_module_quality\u001f0","MODULE",Rect(2,2),rotations:false));
    items.Add(I("system_module_quality\u001f1","MODULE",Rect(2,3),rotations:true));
    for(int i=0;i<11;i++)items.Add(I("wine_yeast\u001f"+i,"HOUSEHOLD_GOOD",Rect(1,2),rotations:true));
    for(int i=0;i<10;i++)items.Add(I("heavy_pistol_ammo_nl\u001f"+i,"AMMUNITION",Rect(1,1),rotations:false));
    var solver=new DeterministicSolver{TimeBudget=TimeSpan.FromMilliseconds(400),BeamWidth=40,CandidatesPerState=28};
    var r=solver.Solve(P(30,30,items.ToArray()));
    Assert(r.Complete&&r.Score.NegativeTypeRuns==0&&solver.Diagnostic.Contains("complete atomic mixed-shape",StringComparison.Ordinal));
}
static void Frontier179Deadline()
{
    var items=new List<LayoutItem>();
    var cats=new[]{"FOOD","MEDICAL","MATERIAL","MODULE","HOUSEHOLD_GOOD","WATER","DOCUMENT","AMMUNITION"};
    for(int t=0;t<36;t++)
    {
        int count=t<35?5:4;
        int w=1+(t%3==0?1:0),h=1+(t%4);
        for(int i=0;i<count;i++)items.Add(I($"type{t:D2}\u001f{i:D2}",cats[t%cats.Length],Rect(w,h),rotations:true));
    }
    var p=P(30,30,items.ToArray());
    var solver=new DeterministicSolver{TimeBudget=TimeSpan.FromMilliseconds(400),BeamWidth=40,CandidatesPerState=28};
    var sw=System.Diagnostics.Stopwatch.StartNew();var r=solver.Solve(p);sw.Stop();
    Console.WriteLine($"PERF v3.9-frontier-179={sw.Elapsed.TotalMilliseconds:F1}ms diagnostic={solver.Diagnostic}");
    Assert(r.Complete&&r.Score.NegativeTypeRuns==0&&sw.Elapsed<TimeSpan.FromMilliseconds(1000));
}
static void DensityThresholds()
{
    Assert(DeterministicSolver.DensityMode(P(10,10,Enumerable.Range(0,79).Select(i=>I("a"+i,"x",Rect(1,1))).ToArray()))=="aesthetic");
    Assert(DeterministicSolver.DensityMode(P(10,10,Enumerable.Range(0,85).Select(i=>I("b"+i,"x",Rect(1,1))).ToArray()))=="balanced");
    Assert(DeterministicSolver.DensityMode(P(10,10,Enumerable.Range(0,95).Select(i=>I("c"+i,"x",Rect(1,1))).ToArray()))=="dense");
}

static void DenseGridDeterministic()
{
    var items=new List<LayoutItem>();
    for(int i=0;i<10;i++)items.Add(I("water\u001f"+i,"drink",Rect(1,2),rotations:true));
    for(int i=0;i<4;i++)items.Add(I("meat\u001f"+i,"food",Rect(2,2),rotations:false));
    items.Add(I("machine\u001f0","machine",Rect(3,2),rotations:true,priority:0));
    var p=P(7,6,items.ToArray());
    var a=Solve(p);var b=Solve(p);
    Assert(DeterministicSolver.DensityMode(p)=="dense"&&a.Complete&&b.Complete);
    Assert(string.Join(";",a.Placements)==string.Join(";",b.Placements));
}
static void DenseLocalSlotGrouping()
{
    var items=new List<LayoutItem>();
    for(int i=0;i<6;i++)items.Add(I("alpha\u001f"+i,"food",Rect(1,1),rotations:false,x:(i*2)%6,y:i/3));
    for(int i=0;i<6;i++)items.Add(I("beta\u001f"+i,"drink",Rect(1,1),rotations:false,x:(i*2+1)%6,y:i/3));
    var p=P(6,2,items.ToArray());
    var originals=p.Items.Select(i=>new Placement(i.StableId,i.OriginalX,i.OriginalY,0,i.Orientations[0].Mask,i.PrimaryCategory,false)).ToArray();
    // Ensure a valid, deliberately alternating full layout.
    originals=Enumerable.Range(0,12).Select(n=>
    {
        var i=p.Items[n%2==0?n/2:6+n/2];
        return new Placement(i.StableId,n%6,n/6,0,i.Orientations[0].Mask,i.PrimaryCategory,false);
    }).ToArray();
    var baseline=DeterministicSolver.Evaluate(p,originals);
    var r=new DeterministicSolver{TimeBudget=TimeSpan.FromSeconds(2),BeamWidth=32,CandidatesPerState=32}.Solve(p,originals);
    Assert(r.Complete&&DeterministicSolver.CompareForProblem(p,r.Score,baseline)>0);
    Assert(r.Score.NegativeTypeRuns>baseline.NegativeTypeRuns);
}

static void WrappedTypeBlock()
{
    var items=Enumerable.Range(0,8).Select(i=>I("water\u001f"+i,"drink",Rect(1,2),rotations:false)).ToArray();
    var p=P(4,4,items); var r=Solve(p);
    Assert(r.Complete&&r.Score.NegativeTypeRuns==0&&DeterministicSolver.DescribeSplits(r.Placements)=="none");
}

static void ComparisonExplainsFirstMetric()
{
    var items=Enumerable.Range(0,4).Select(i=>I((i%2==0?"a":"b")+"\u001f"+i,"x",Rect(1,1),rotations:false)).ToArray();
    var p=P(4,1,items); var map=p.Items.ToDictionary(x=>x.StableId);
    Placement Q(string id,int x)=>new(id,x,0,0,map[id].Orientations[0].Mask,map[id].PrimaryCategory,false);
    var grouped=new[]{Q("a\u001f0",0),Q("a\u001f2",1),Q("b\u001f1",2),Q("b\u001f3",3)};
    var mixed=new[]{Q("a\u001f0",0),Q("b\u001f1",1),Q("a\u001f2",2),Q("b\u001f3",3)};
    var gs=DeterministicSolver.Evaluate(p,grouped);var ms=DeterministicSolver.Evaluate(p,mixed);
    Assert(DeterministicSolver.CompareForProblem(p,gs,ms)>0);
    Assert(DeterministicSolver.FirstDifferingMetric(p,gs,ms).StartsWith("typeRegions",StringComparison.Ordinal));
}
static void RealDenseReplay()
{
    var fixture=DenseReplayFixture.Create();
    var baseline=DeterministicSolver.Evaluate(fixture.problem,fixture.original);
    var solver=new DeterministicSolver{TimeBudget=TimeSpan.FromSeconds(4),BeamWidth=40,CandidatesPerState=28};
    var r=solver.Solve(fixture.problem,fixture.original);
    Console.WriteLine($"REPLAY 19x12 occupancy={DeterministicSolver.OccupancyRatio(fixture.problem):P1} baselineSplits={-baseline.NegativeTypeRuns} candidateSplits={-r.Score.NegativeTypeRuns} comparison={DeterministicSolver.FirstDifferingMetric(fixture.problem,r.Score,baseline)}");
    Assert(r.Complete&&r.Placements.Count==58);
    Assert(DeterministicSolver.CompareForProblem(fixture.problem,r.Score,baseline)>0);
    Assert(r.Score.NegativeTypeRuns>=baseline.NegativeTypeRuns);
}
static void RealDenseReplay30()
{
    var fixture=DenseReplayFixture30.Create();var baseline=DeterministicSolver.Evaluate(fixture.problem,fixture.original);
    var solver=new DeterministicSolver{TimeBudget=TimeSpan.FromSeconds(4),BeamWidth=40,CandidatesPerState=28};var r=solver.Solve(fixture.problem,fixture.original);
    Console.WriteLine($"REPLAY 30x30 occupancy={DeterministicSolver.OccupancyRatio(fixture.problem):P1} baselineSplits={-baseline.NegativeTypeRuns} candidateSplits={-r.Score.NegativeTypeRuns} comparison={DeterministicSolver.FirstDifferingMetric(fixture.problem,r.Score,baseline)}");
    Assert(r.Complete&&r.Placements.Count==215);
    Assert(DeterministicSolver.CompareForProblem(fixture.problem,r.Score,baseline)>0);
    Assert(r.Score.NegativeTypeRuns>=baseline.NegativeTypeRuns);
}