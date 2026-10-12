using SmartInventoryArrange.Core;

internal static class DenseReplayFixture
{
    internal sealed record Spec(string Name,int W,int H,string Category,int Orientation,bool CanRotate,int X,int Y,int Copy);
    internal static (LayoutProblem problem, Placement[] original) Create()
    {
        var specs = new[]
        {
        new Spec("cat_bar",2,2,"FOOD",0,false,6,0,0),
        new Spec("cat_bar",2,2,"FOOD",0,false,8,0,1),
        new Spec("cat_bar",2,2,"FOOD",0,false,0,4,2),
        new Spec("common_chemical",3,2,"CHEMICAL_SUPPLIES",0,true,14,8,0),
        new Spec("crypto_module_cmd",2,1,"CONTRABAND",0,true,3,7,0),
        new Spec("crypto_module_med",2,1,"CONTRABAND",0,true,2,8,0),
        new Spec("energy_credit",1,2,"ENERGY_CELL",0,true,16,2,0),
        new Spec("energy_credit",1,2,"ENERGY_CELL",0,true,16,4,1),
        new Spec("energy_credit",1,2,"ENERGY_CELL",0,true,16,0,2),
        new Spec("flux_agent",1,1,"MATERIAL",0,false,16,7,0),
        new Spec("furnace_module_blast",1,4,"MODULE",0,true,8,6,0),
        new Spec("furnace_module_blast",1,4,"MODULE",0,true,9,6,1),
        new Spec("galaxy_blend",2,2,"FOOD",0,false,2,4,0),
        new Spec("galaxy_blend",2,2,"FOOD",0,false,4,4,1),
        new Spec("large_purple_injector",4,1,"MEDICAL",0,true,14,10,0),
        new Spec("nudka",4,2,"ALCOHOL",3,true,4,8,0),
        new Spec("nudka",4,2,"ALCOHOL",3,true,10,8,1),
        new Spec("pheromone_perfume",2,2,"CONTRABAND",0,false,6,4,0),
        new Spec("pheromone_perfume",2,2,"CONTRABAND",0,false,10,0,1),
        new Spec("pink_injector",4,1,"MEDICAL",0,true,12,0,0),
        new Spec("pink_injector",4,1,"MEDICAL",0,true,12,1,1),
        new Spec("pink_injector",4,1,"MEDICAL",0,true,10,6,2),
        new Spec("pink_injector",4,1,"MEDICAL",0,true,10,7,3),
        new Spec("processed_cheese",2,2,"FOOD",0,false,17,0,0),
        new Spec("raw_meat",3,2,"FOOD",0,true,0,0,0),
        new Spec("raw_meat",3,2,"FOOD",0,true,3,0,1),
        new Spec("red_beer",4,1,"ALCOHOL",3,true,0,9,0),
        new Spec("sci_keycard",2,2,"ACCESS_CARD",0,false,17,2,0),
        new Spec("sci_keycard",2,2,"ACCESS_CARD",0,false,17,4,1),
        new Spec("sec_keycard",2,2,"ACCESS_CARD",0,false,0,2,0),
        new Spec("sec_keycard",2,2,"ACCESS_CARD",0,false,8,4,1),
        new Spec("sec_keycard",2,2,"ACCESS_CARD",0,false,10,4,2),
        new Spec("sec_keycard",2,2,"ACCESS_CARD",0,false,2,2,3),
        new Spec("small_raw_meat",2,2,"FOOD",0,false,17,6,0),
        new Spec("small_raw_meat",2,2,"FOOD",0,false,17,8,1),
        new Spec("system_capped_neural_core",2,2,"CONTRABAND",0,false,4,2,0),
        new Spec("system_capped_neural_core",2,2,"CONTRABAND",0,false,6,2,1),
        new Spec("system_module_efficiancy",2,2,"MODULE",0,false,12,4,0),
        new Spec("system_module_efficiancy",2,2,"MODULE",0,false,14,4,1),
        new Spec("system_module_performance",3,2,"MODULE",0,true,0,6,0),
        new Spec("system_module_quality",3,2,"MODULE",0,true,3,6,0),
        new Spec("toilet_paper",2,2,"HOUSEHOLD_GOOD",0,false,8,2,0),
        new Spec("toilet_paper",2,2,"HOUSEHOLD_GOOD",0,false,10,2,1),
        new Spec("toilet_paper",2,2,"HOUSEHOLD_GOOD",0,false,14,6,2),
        new Spec("water_ration",2,2,"WATER_RATION",0,false,0,10,0),
        new Spec("water_ration",2,2,"WATER_RATION",0,false,2,10,1),
        new Spec("wine_berry",2,2,"FOOD",0,false,4,10,0),
        new Spec("wine_berry",2,2,"FOOD",0,false,6,10,1),
        new Spec("wine_berry",2,2,"FOOD",0,false,8,10,2),
        new Spec("wine_berry",2,2,"FOOD",0,false,10,10,3),
        new Spec("wine_berry",2,2,"FOOD",0,false,6,6,4),
        new Spec("wine_berry",2,2,"FOOD",0,false,0,7,5),
        new Spec("wine_berry",2,2,"FOOD",0,false,12,10,6),
        new Spec("wine_yeast",1,2,"HOUSEHOLD_GOOD",0,true,12,2,0),
        new Spec("wine_yeast",1,2,"HOUSEHOLD_GOOD",0,true,13,2,1),
        new Spec("wine_yeast",1,2,"HOUSEHOLD_GOOD",0,true,14,2,2),
        new Spec("wine_yeast",1,2,"HOUSEHOLD_GOOD",0,true,15,2,3),
        new Spec("zyanide_pill",1,1,"CONTRABAND",0,false,16,6,0),
        };
        ShapeMask Rect(int w,int h)=>new(Enumerable.Range(0,h).SelectMany(y=>Enumerable.Range(0,w).Select(x=>new Cell(x,y))));
        var items=new List<LayoutItem>(); int notches=0;
        foreach(var s in specs)
        {
            ShapeMask mask;
            if(notches<4&&s.W>=2&&s.H>=2){mask=new ShapeMask(Enumerable.Range(0,s.H).SelectMany(y=>Enumerable.Range(0,s.W).Where(x=>x!=s.W-1||y!=s.H-1).Select(x=>new Cell(x,y))));notches++;}
            else mask=Rect(s.W,s.H);
            var orientations=new List<Orientation>{new(s.Orientation,mask,mask.Width>mask.Height)};
            var rotated=mask.Rotate90(); if(s.CanRotate&&rotated.Signature!=mask.Signature)orientations.Add(new(s.Orientation==0?1:0,rotated,rotated.Width>rotated.Height));
            string id=s.Name+'\u001f'+s.Copy.ToString("D3");
            items.Add(new LayoutItem{StableId=id,PrimaryCategory=s.Category,Categories=new[]{s.Category},Orientations=orientations,OriginalOrientation=s.Orientation,OriginalX=0,OriginalY=0,VisualPriority=100});
        }
        var usable=new bool[19,12]; for(int y=0;y<12;y++)for(int x=0;x<19;x++)usable[x,y]=true;
        var order=items.Select(x=>x.PrimaryCategory).Distinct(StringComparer.Ordinal).OrderBy(x=>x,StringComparer.Ordinal).Select((x,i)=>(x,i)).ToDictionary(x=>x.x,x=>x.i,StringComparer.Ordinal);
        var problem=new LayoutProblem{Width=19,Height=12,Usable=usable,Items=items,CategoryOrder=order};
        var occupied=new bool[19,12]; var packed=new List<Placement>();
        foreach(var item in items.OrderByDescending(x=>x.Area).ThenByDescending(x=>x.Orientations[0].Mask.Height).ThenBy(x=>x.StableId,StringComparer.Ordinal))
        {
            Placement? chosen=null;
            foreach(var o in item.Orientations.OrderByDescending(x=>x.Mask.Height>=x.Mask.Width).ThenBy(x=>x.GameOrientation))
            for(int y=0;y<=12-o.Mask.Height&&chosen==null;y++)for(int x=0;x<=19-o.Mask.Width&&chosen==null;x++)
            {
                if(o.Mask.Cells.Any(c=>occupied[x+c.X,y+c.Y]))continue;
                chosen=new Placement(item.StableId,x,y,o.GameOrientation,o.Mask,item.PrimaryCategory,o.Rotated);
            }
            if(chosen==null)throw new InvalidOperationException("Could not build dense replay baseline for "+item.StableId);
            foreach(var c in chosen.Mask.Cells)occupied[chosen.X+c.X,chosen.Y+c.Y]=true; packed.Add(chosen);
        }
        var itemById=items.ToDictionary(x=>x.StableId,StringComparer.Ordinal); var original=new List<Placement>();
        foreach(var g in packed.GroupBy(x=>x.Mask.Signature,StringComparer.Ordinal))
        {
            var slots=g.OrderBy(x=>x.Y).ThenBy(x=>x.X).ToArray();
            var queues=g.Select(x=>itemById[x.StableId]).GroupBy(x=>x.StableId.Split('\u001f')[0],StringComparer.Ordinal)
                .OrderBy(x=>x.Key,StringComparer.Ordinal).Select(x=>new Queue<LayoutItem>(x.OrderBy(v=>v.StableId,StringComparer.Ordinal))).ToList();
            var mixed=new List<LayoutItem>(); while(queues.Any(q=>q.Count>0))foreach(var q in queues)if(q.Count>0)mixed.Add(q.Dequeue());
            for(int i=0;i<slots.Length;i++)
            {
                var item=mixed[i]; var o=item.Orientations.First(x=>x.Mask.Signature==slots[i].Mask.Signature);
                original.Add(new Placement(item.StableId,slots[i].X,slots[i].Y,o.GameOrientation,o.Mask,item.PrimaryCategory,o.Rotated));
            }
        }
        return (problem,original.OrderBy(x=>x.StableId,StringComparer.Ordinal).ToArray());    }
}