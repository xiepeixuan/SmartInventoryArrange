using System.Security.Cryptography;
using System.Text.Json;
namespace SmartInventoryArrange.Core;

/// <summary>One bounded pure-managed search session. A continuation owns the same
/// suspended iterators, not a larger budget for another fresh Solve invocation.</summary>
public sealed class GlobalArrangeController : IDisposable
{
    public int InitialNodeBudget { get; init; } = 250000;
    public int ContinueNodeBudget { get; init; } = 25000;
    public int SolutionBatch { get; init; } = 64;
    private Native34Solver.SearchSession? _session;
    private string? _problem,_expectedPose,_context;
    private long _generation;
    private bool _pending;
    public sealed record Request(Native34Solver.SearchSession Session,bool Restarted,long Generation,string InputSignature,int NodeBudget,int SolutionBatch);
    public Request Begin(LayoutProblem p,IReadOnlyList<Placement> original,string context)
    {
        if(_pending)throw new InvalidOperationException("Previous request still pending.");
        string problem=ProblemSignature(p),pose=Native34Solver.Key(original);
        bool restart=_session==null||problem!=_problem||pose!=_expectedPose||context!=_context;
        if(restart)
        {
            _session?.Dispose();_session=new Native34Solver().CreateSession(p,original);
            _problem=problem;_context=context;_expectedPose=pose;_generation++;
        }
        _pending=true;
        return new(_session!,restart,_generation,problem,restart?InitialNodeBudget:ContinueNodeBudget,SolutionBatch);
    }
    public void Complete(Request request,LayoutProblem p,IReadOnlyList<Placement> output)
    {
        if(!_pending||request.Generation!=_generation||!ReferenceEquals(request.Session,_session)||ProblemSignature(p)!=_problem)
            throw new InvalidOperationException("Stale search-session completion.");
        if(!Native34Solver.ValidateLayout(p,output))throw new InvalidOperationException("Illegal completed output.");
        _expectedPose=Native34Solver.Key(output);_pending=false;
    }
    public void Reset(){_session?.Dispose();_session=null;_problem=_expectedPose=_context=null;_pending=false;_generation++;}
    public void Dispose()=>Reset();
    public static string ProblemSignature(LayoutProblem p)
    {
        var usable=new List<bool>();for(int y=0;y<p.Height;y++)for(int x=0;x<p.Width;x++)usable.Add(p.Usable[x,y]);
        var bytes=JsonSerializer.SerializeToUtf8Bytes(new{p.Width,p.Height,usable,
            fixedItems=p.Fixed.OrderBy(q=>q.StableId,StringComparer.Ordinal).Select(q=>new{q.StableId,q.X,q.Y,q.GameOrientation,q.Category,mask=q.Mask.Signature}),
            items=p.Items.OrderBy(i=>i.StableId,StringComparer.Ordinal).Select(i=>new{i.StableId,i.ItemId,i.PrimaryCategory,i.VisualPriority,
                categories=i.Categories.OrderBy(c=>c,StringComparer.Ordinal),orientations=i.Orientations.OrderBy(o=>o.GameOrientation).ThenBy(o=>o.Mask.Signature,StringComparer.Ordinal).Select(o=>new{o.GameOrientation,mask=o.Mask.Signature})}),
            categoryOrder=p.CategoryOrder.OrderBy(k=>k.Key,StringComparer.Ordinal).Select(k=>new{k.Key,k.Value})});
        return Convert.ToHexString(SHA256.HashData(bytes));
    }
}
