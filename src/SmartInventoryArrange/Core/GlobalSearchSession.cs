using System.Diagnostics;
using System.Numerics;
namespace SmartInventoryArrange.Core;

public sealed partial class Native34Solver
{
    private SearchSession? _activeSession;
    private readonly HashSet<string> _interlockAnchors = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Ranked> _rankCache = new(StringComparer.Ordinal);
    private sealed class Ranked
    {
        public string Key = ""; public Placement[] Q = Array.Empty<Placement>();
        public Metrics M = null!; public double Value; public double? Preference;
    }
    private Ranked Rank(string key)
    {
        if (_rankCache.TryGetValue(key, out var r)) return r;
        var q = _pool[key].Q; var m = Measure(q);
        r = new() { Key = key, Q = q, M = m, Value = Value(m) }; _rankCache[key] = r; return r;
    }
    private int CompareRank(Ranked a, Ranked b)
    {
        int c = a.Value.CompareTo(b.Value); if (c != 0) return c;
        a.Preference ??= _model.Value(FeatureVector(a.Q)); b.Preference ??= _model.Value(FeatureVector(b.Q));
        c = b.Preference.Value.CompareTo(a.Preference.Value); if (c != 0) return c;
        c = a.M.Priority.CompareTo(b.M.Priority); return c != 0 ? c : StringComparer.Ordinal.Compare(a.Key, b.Key);
    }
    private Family[] OrderedFamilies(string order, bool unitTail = false)
    {
        var q = _families.OrderBy(g => unitTail && g.Items[0].Area == 1).ThenBy(g => !g.Items.Any(Anchor));
        int Height(Family g) => Options(g.Items[0], "portrait")[0].Mask.Height;
        int Width(Family g) => Options(g.Items[0], "portrait")[0].Mask.Width;
        q = order switch
        {
            "height" => q.ThenByDescending(Height).ThenByDescending(g => g.Items.Sum(i => i.Area)),
            "width" => q.ThenByDescending(Width).ThenByDescending(Height),
            "cohort" => q.ThenBy(g => g.Items[0].PrimaryCategory, StringComparer.Ordinal).ThenByDescending(Height).ThenByDescending(Width),
            "category" => q.ThenBy(g => g.Items[0].PrimaryCategory, StringComparer.Ordinal).ThenByDescending(g => g.Items.Sum(i => i.Area)),
            "constrained" => q.ThenBy(g => g.Items[0].Orientations.Count).ThenByDescending(g => g.Items.Sum(i => i.Area)),
            "small" => q.ThenBy(g => g.Items.Max(i => i.Area)).ThenByDescending(Height),
            "family-area" => q.ThenByDescending(g => g.Items.Sum(i => i.Area)),
            _ => q.ThenByDescending(g => g.Items.Max(i => i.Area)).ThenByDescending(g => g.Items.Sum(i => i.Area))
        };
        return q.ThenBy(g => g.Key, StringComparer.Ordinal).ToArray();
    }
    public SearchSession CreateSession(LayoutProblem p, IReadOnlyList<Placement>? original = null)
    {
        DeterministicSolver.ValidateProblem(p);
        if (NodeLimit < 1 || MaxPasses < 1) throw new ArgumentException("Positive deterministic budgets required.");
        _activeSession?.Dispose();
        // Own a deep managed snapshot. No Unity/IL2CPP reference enters the session.
        ShapeMask Mask(ShapeMask m) => new(m.Cells);
        var items = p.Items.OrderBy(i => i.StableId, StringComparer.Ordinal).Select(i => new LayoutItem {
            StableId=i.StableId,ItemId=i.ItemId,PrimaryCategory=i.PrimaryCategory,Categories=i.Categories.ToArray(),VisualPriority=i.VisualPriority,
            OriginalX=i.OriginalX,OriginalY=i.OriginalY,OriginalOrientation=i.OriginalOrientation,OriginalMask=i.OriginalMask == null ? null : Mask(i.OriginalMask),
            Orientations=i.Orientations.Select(o => new Orientation(o.GameOrientation,Mask(o.Mask),o.Rotated)).ToArray() }).ToArray();
        _p = new() { Width=p.Width,Height=p.Height,Usable=(bool[,])p.Usable.Clone(),Items=items,
            Fixed=p.Fixed.Select(q => q with { Mask=Mask(q.Mask) }).ToArray(),CategoryOrder=new Dictionary<string,int>(p.CategoryOrder,StringComparer.Ordinal) };
        _model=FrozenPreferenceModel.LoadEmbedded(); _docs=items.ToDictionary(i=>i.StableId,StringComparer.Ordinal);
        _templates.Clear();_runs.Clear();_metricCache.Clear();_interlockCache.Clear();_optionCache.Clear();_packed.Clear();_performance.Clear();_legalOrientations.Clear();_rankCache.Clear();_pool.Clear();_pass=0;
        _currentOrientations.Clear();
        foreach(var i in items) _legalOrientations[i.StableId]=i.Orientations.GroupBy(o=>o.Mask.Signature,StringComparer.Ordinal).ToDictionary(g=>g.Key,g=>g.OrderBy(o=>o.GameOrientation).First(),StringComparer.Ordinal);
        _families=items.GroupBy(i=>Type(i)+"\u001e"+ShapeKey(i),StringComparer.Ordinal).OrderBy(g=>g.Key,StringComparer.Ordinal).Select(g=>new Family(g.Key,g.ToArray())).ToList();
        _interlockAnchors.Clear();foreach(var family in _families)if(family.Items.Length>1&&family.Items.Any(Anchor)&&Interlocking(family.Items[0]))foreach(var i in family.Items)_interlockAnchors.Add(i.StableId);
        var incoming=(original ?? items.Select(i=>At(i,i.OriginalX,i.OriginalY,i.OriginalMask!=null?new(i.OriginalOrientation,i.OriginalMask,false):i.Orientations.First(o=>o.GameOrientation==i.OriginalOrientation))).ToArray())
            .Select(q=>q with { Mask=Mask(q.Mask) }).OrderBy(q=>q.StableId,StringComparer.Ordinal).ToArray();
        if(!ValidateLayout(_p,incoming))throw new ArgumentException("Complete valid original snapshot required for rollback only.");
        _activeSession=new(this,incoming);return _activeSession;
    }
    public sealed record SearchProgress(LayoutResult Result,int Batch,long NewNodes,long TotalNodes,int NewCandidates,int TotalCandidates,
        int RawSolutions,bool Improved,bool Exhausted,bool CanContinue,string Reason);
    private sealed record SearchTick(Placement[]? Solution);
    private sealed class Route
    {
        public string Id="";public IEnumerator<SearchTick> Iterator=null!;public bool Done;public long Nodes;
    }
    public sealed class SearchSession : IDisposable
    {
        private readonly object _gate=new();
        private readonly Native34Solver _owner;
        private readonly Placement[] _rollback;
        private readonly HashSet<string> _seen=new(StringComparer.Ordinal);
        private readonly List<Route> _routes=new();
        private bool _seeded,_disposed,_candidateCap;private int _route,_batch,_raw,_quantumUsed;
        private string? _bestKey;
        public long TotalNodes { get; private set; }
        public int UniqueCandidates => _seen.Count;
        public string Diagnostic => _owner.Diagnostic;
        public bool Exhausted => _seeded && _routes.All(r=>r.Done);
        public bool CanContinue => !_disposed && !_candidateCap && !Exhausted;
        internal SearchSession(Native34Solver owner,Placement[] rollback){_owner=owner;_rollback=rollback;}
        private void Seeds()
        {
            if(_seeded)return;
            foreach(var layout in new[]{"shelf","compact","unit-tail"})
            foreach(var order in new[]{"item-area","family-area","height","width","cohort","category","constrained","small"})
                _owner.RunStage($"fresh:{layout}:{order}",()=>_owner.Shelf(order,"portrait",layout));
            if(_owner._p.Items.Count==0)_owner.Add(Array.Empty<Placement>(),"fresh:empty");
            foreach(var key in _owner._pool.Keys)_seen.Add(key);
            foreach(var order in new[]{"item-area","height","constrained","small"})foreach(bool whole in new[]{true,false})
            {
                string id=$"resume:{order}:{(whole?"whole":"flexible")}";
                _routes.Add(new(){Id=id,Iterator=_owner.EnumerateFresh(order,whole).GetEnumerator()});
            }
            _seeded=true;
        }
        public SearchProgress Advance(int nodeBudget=25000,int solutionBatch=64)
        {
            lock(_gate)return AdvanceCore(nodeBudget,solutionBatch);
        }
        private SearchProgress AdvanceCore(int nodeBudget,int solutionBatch)
        {
            if(nodeBudget<1||solutionBatch<1)throw new ArgumentException("Positive work and solution batches required.");
            if(_disposed)throw new ObjectDisposedException(nameof(SearchSession));
            var watch=Stopwatch.StartNew();long allocated=GC.GetAllocatedBytesForCurrentThread();long nodesBefore=TotalNodes;int uniqueBefore=_seen.Count,rawBefore=_raw;
            Seeds();
            while(TotalNodes-nodesBefore<nodeBudget && _raw-rawBefore<solutionBatch && !_candidateCap && _routes.Any(r=>!r.Done))
            {
                var route=_routes[_route];
                if(route.Done){_route=(_route+1)%_routes.Count;_quantumUsed=0;continue;}
                if(!route.Iterator.MoveNext())
                {route.Done=true;route.Iterator.Dispose();_route=(_route+1)%_routes.Count;_quantumUsed=0;continue;}
                _quantumUsed++;var tick=route.Iterator.Current;
                if(tick.Solution==null){TotalNodes++;route.Nodes++;}
                else
                {
                    _raw++;string key=Key(tick.Solution);
                    if(_seen.Add(key))_owner.Add(tick.Solution,route.Id);
                    else if(_owner._pool.ContainsKey(key))_owner.Add(tick.Solution,route.Id);
                    if(_seen.Count>=4096)_candidateCap=true;
                }
                if(_quantumUsed==128){_route=(_route+1)%_routes.Count;_quantumUsed=0;}
            }
            var ranks=_owner._pool.Keys.Select(_owner.Rank).ToList();ranks.Sort(_owner.CompareRank);
            foreach(var excess in ranks.Skip(256))_owner._pool.Remove(excess.Key);
            Ranked? best=ranks.FirstOrDefault();bool improved=best!=null&&best.Key!=_bestKey;_bestKey=best?.Key;
            var output=(best?.Q??_rollback).ToArray();
            if(!ValidateLayout(_owner._p,output))throw new InvalidOperationException("Final immutable geometry validation failed.");
            _owner.CandidateRecords=ranks.Take(256).Select(r=>new CandidateTrace(Hash(r.Key),_owner._pool[r.Key].Generators.ToArray(),true,r==best?"":"ranked-lower",Summary(r.M),r==best)).ToArray();
            var m=_owner.Measure(output);var score=DeterministicSolver.Evaluate(_owner._p,output);var space=DeterministicSolver.EvaluateSpaceMetrics(_owner._p,output);
            string reason=best==null?"no-fresh-complete-layout":_candidateCap?"candidate-memory-cap":Exhausted?"generated-search-space-exhausted":improved?"fresh-best-selected":"no-better-candidate-this-batch";
            watch.Stop();_owner._performance.Add(new(_batch,"session-advance",watch.Elapsed.TotalMilliseconds,GC.GetAllocatedBytesForCurrentThread()-allocated,(int)(TotalNodes-nodesBefore)));
            _owner.Diagnostic=$"engine=global313-session, batch={_batch}, newNodes={TotalNodes-nodesBefore}, totalNodes={TotalNodes}, unique={_seen.Count}, retained={_owner._pool.Count}, rawSolutions={_raw}, source=fresh-only, reason={reason}, after=[{Summary(m)}], routes=[{string.Join(';',_routes.Select(r=>$"{r.Id}:nodes={r.Nodes}:done={r.Done}"))}]";
            var result=new LayoutResult(true,output,score,best==null,false,watch.Elapsed,space.holes,space.narrow);
            return new(result,_batch++,TotalNodes-nodesBefore,TotalNodes,_seen.Count-uniqueBefore,_seen.Count,_raw-rawBefore,improved,Exhausted,CanContinue,reason);
        }
        public void Dispose(){lock(_gate){if(_disposed)return;foreach(var r in _routes)r.Iterator.Dispose();_routes.Clear();_disposed=true;}}
    }
    private IEnumerable<SearchTick> EnumerateFresh(string order,bool whole)
    {
        int W=_p.Width,H=_p.Height;BigInteger full=(BigInteger.One<<checked(W*H))-1,initial=0;
        for(int y=0;y<H;y++)for(int x=0;x<W;x++)if(!_p.Usable[x,y])initial|=BigInteger.One<<(y*W+x);
        foreach(var c in Cells(_p.Fixed))initial|=BigInteger.One<<(c.Y*W+c.X);
        var groups=OrderedFamilies(order);var counts=groups.Select(g=>g.Items.Length).ToArray();var patterns=new List<(int Count,PackedTemplate P)>[groups.Length];
        var radix=new BigInteger[groups.Length];BigInteger multiplier=1,code=0;
        for(int gi=0;gi<groups.Length;gi++)
        {
            radix[gi]=multiplier;code+=multiplier*counts[gi];multiplier*=counts[gi]+1;patterns[gi]=new();
            foreach(int count in whole?new[]{counts[gi]}:Enumerable.Range(1,counts[gi]).Reverse())
                foreach(var t in Templates(groups[gi].Items.Take(count).ToArray(),"portrait"))patterns[gi].Add((count,Pack(t)));
            patterns[gi]=patterns[gi].OrderByDescending(t=>t.Count).ThenBy(t=>t.P.T.Rows).ThenBy(t=>t.P.T.Landscape).ThenBy(t=>t.P.T.Height).ThenBy(t=>t.P.T.Width).ToList();
        }
        var anchors=groups.Select(g=>g.Items.Any(Anchor)?0:1).ToArray();var path=new List<SearchChoice>();
        var dead=new HashSet<SearchState>();long solutions=0;
        Placement[] Materialize(SearchChoice c)
        {
            var qs=c.Pattern.T.Items;var q=new Placement[qs.Length];for(int i=0;i<qs.Length;i++){var item=groups![c.Gi].Items[c.Offset+i];q[i]=At(item,qs[i].X+c.X,qs[i].Y+c.Y,_legalOrientations[item.StableId][qs[i].Mask.Signature]);}return q;
        }
        int Compare(SearchChoice a,SearchChoice b)
        {
            int c=a.Continuation.CompareTo(b.Continuation);if(c!=0)return c;c=a.Anchor.CompareTo(b.Anchor);if(c!=0)return c;
            c=b.Count.CompareTo(a.Count);if(c!=0)return c;c=a.Pattern.T.Rows.CompareTo(b.Pattern.T.Rows);if(c!=0)return c;
            c=a.Pattern.T.Landscape.CompareTo(b.Pattern.T.Landscape);if(c!=0)return c;c=a.Gi.CompareTo(b.Gi);if(c!=0)return c;
            c=StringComparer.Ordinal.Compare(a.TieKey??=Key(Materialize(a)),b.TieKey??=Key(Materialize(b)));return c!=0?c:a.Sequence.CompareTo(b.Sequence);
        }
        IEnumerable<SearchTick> Dfs(BigInteger occupied,BigInteger remainingCode,int area,int holes,int last)
        {
            yield return new(null); // A real suspension point, counted once across all calls.
            if(area==0){solutions++;yield return new(path.SelectMany(Materialize).ToArray());yield break;}
            var state=new SearchState(occupied,remainingCode);if(dead.Contains(state))yield break;
            var free=full^occupied;if(free.IsZero)yield break;int first=LowestBit(free),fx=first%W,fy=first/W;
            long priorSolutions=solutions;var choices=new List<SearchChoice>();
            for(int gi=0;gi<groups.Length;gi++)if(counts[gi]>0)foreach(var entry in patterns[gi])
            {
                if(entry.Count>counts[gi])continue;var packed=entry.P;var t=packed.T;int x=fx-packed.First.X,y=fy-packed.First.Y;
                if(x<0||y<0||x+t.Width>W||y+t.Height>H)continue;var bits=packed.Bits<<(y*W+x);if((bits&occupied)!=0)continue;
                choices.Add(new(){Gi=gi,Count=entry.Count,X=x,Y=y,Offset=groups[gi].Items.Length-counts[gi],Pattern=packed,Bits=bits,Anchor=anchors[gi],Continuation=gi==last?0:1,Sequence=choices.Count});
            }
            choices.Sort(Compare);
            foreach(var c in choices)
            {
                counts[c.Gi]-=c.Count;path.Add(c);
                foreach(var tick in Dfs(occupied|c.Bits,remainingCode-radix[c.Gi]*c.Count,area-groups[c.Gi].Items[0].Area*c.Count,holes,c.Gi))yield return tick;
                path.RemoveAt(path.Count-1);counts[c.Gi]+=c.Count;
            }
            if(holes>0)foreach(var tick in Dfs(occupied|(BigInteger.One<<first),remainingCode,area,holes-1,last))yield return tick;
            // Only fully explored IMPOSSIBLE subtrees are memoized. Memoizing a
            // viable occupancy state would hide alternative typed arrangements.
            if(priorSolutions==solutions && dead.Count<60000)dead.Add(state);
        }
        int leftArea=groups.Sum(g=>g.Items.Sum(i=>i.Area)),holes=W*H-CountBits(initial)-leftArea;
        if(holes>=0)foreach(var tick in Dfs(initial,code,leftArea,holes,-1))yield return tick;
    }
}
