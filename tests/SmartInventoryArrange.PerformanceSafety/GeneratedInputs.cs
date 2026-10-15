using SmartInventoryArrange.Core;
internal static class GeneratedInputs
{
    public static (LayoutProblem P, Placement[] Q) Create(int number)
    {
        int k=number-201;int[] widths={3,5,8,13,24,33};int W=widths[k%6],H=k%6==5?34:6+k%4;
        var usable=new bool[W,H];for(int y=0;y<H;y++)for(int x=0;x<W;x++)usable[x,y]=true;
        usable[W-1,H-1]=false;var fixeds=new[]{new Placement("fixed",W-1,0,0,new ShapeMask(new[]{new Cell(0,0)}),"MODULE",false)};
        var occupied=new HashSet<Cell>{new(W-1,0)};var items=new List<LayoutItem>();var qs=new List<Placement>();
        for(int j=0;j<Math.Min(9,W+2);j++)
        {
            int type=j%3;var mask=type switch{0=>new ShapeMask(new[]{new Cell(0,0),new Cell(0,1)}),1=>new ShapeMask(new[]{new Cell(1,0),new Cell(0,1),new Cell(1,1)}),_=>new ShapeMask(new[]{new Cell(0,0)})};
            var os=Enumerable.Range(0,4).Select(rot=>{var m=mask;for(int z=0;z<rot;z++)m=m.Rotate90();return new Orientation(rot*2+j%2,m,rot%2!=0);}).GroupBy(o=>o.Mask.Signature).Select(g=>g.First()).ToArray();
            var options=j%2==0?os:os.Reverse().ToArray();Placement? chosen=null;string id="type"+type,sid=id+"\u001f"+j.ToString("D3"),cat=type==0&&k%2==0?"machine":"MODULE";
            foreach(var o in options){for(int y=0;y+o.Mask.Height<=H&&chosen==null;y++)for(int x=0;x+o.Mask.Width<=W;x++){if(o.Mask.Cells.Any(c=>!usable[x+c.X,y+c.Y]||occupied.Contains(new(x+c.X,y+c.Y))))continue;chosen=new(sid,x,y,o.GameOrientation,o.Mask,cat,o.Mask.Width>o.Mask.Height);break;}if(chosen!=null)break;}
            if(chosen==null)break;var q=chosen;foreach(var c in q.Mask.Cells)occupied.Add(new(q.X+c.X,q.Y+c.Y));qs.Add(q);
            items.Add(new(){StableId=sid,ItemId=id,PrimaryCategory=cat,Categories=new[]{cat},VisualPriority=type==0?0:100,Orientations=os,OriginalMask=q.Mask,OriginalX=q.X,OriginalY=q.Y,OriginalOrientation=q.GameOrientation});
        }
        var p=new LayoutProblem{Width=W,Height=H,Usable=usable,Fixed=fixeds,Items=k%2==0?items:items.AsEnumerable().Reverse().ToArray()};return(p,qs.AsEnumerable().Reverse().ToArray());
    }
}
