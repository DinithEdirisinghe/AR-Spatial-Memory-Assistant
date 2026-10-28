using System;
using System.Collections.Generic;
using System.Linq;
using SpatialSense.RoomScan;

public static class DepthPatchSamplerTests
{
    private static void Assert(bool ok,string reason) { if(!ok)throw new Exception(reason); }
    private static DepthPatchSample Plane(int x,int y,float z=0)=>new DepthPatchSample(true,new MeshPoint(x*0.1f,y*0.1f,z));
    public static int Main(string[] args)
    {
        try
        {
            double clock=0;var rays=0;
            DepthPatchSample Slow(int x,int y) { clock+=3;rays++;return Plane(x,y); }
            var first=DepthPatchSampler.Capture(8,6,0,35,0.45f,8,Slow,()=>clock);
            Assert(first.VisitedCells==1 && first.Triangles.Count==2 && rays==4,"Slow sampling discarded the first complete quad or exceeded one budgeted unit.");
            Assert(first.BudgetReached && first.NextCell==1,"Slow scan did not advance to the next region.");
            var seen=new HashSet<int> {0};var cursor=first.NextCell;
            for(var i=1;i<35;i++)
            {
                seen.Add(cursor);var batch=DepthPatchSampler.Capture(8,6,cursor,35-i,0.45f,8,Slow,()=>clock);
                Assert(batch.Triangles.Count==2,"A later slow patch was lost.");cursor=batch.NextCell;
            }
            Assert(seen.Count==35 && cursor==0,"Budget overruns permanently omitted part of the view.");
            clock=0;rays=0;
            DepthPatchSample Fast(int x,int y) {clock++;rays++;return Plane(x,y);}
            var two=DepthPatchSampler.Capture(8,6,0,35,0.45f,8,Fast,()=>clock);
            Assert(two.VisitedCells==2 && two.Triangles.Count==4 && rays==8,"Valid completed patches were discarded at the budget boundary.");
            var one=DepthPatchSampler.Capture(8,6,34,1,0.45f,100,Fast,()=>clock);
            Assert(one.VisitedCells==1 && one.NextCell==0,"The final cell overran the remaining sweep or failed to wrap.");
            var missing=DepthPatchSampler.Capture(2,2,0,1,0.45f,8,(x,y)=>x==1&&y==1?new DepthPatchSample(false,default):Plane(x,y),()=>0);
            Assert(missing.Triangles.Count==1,"A missing corner either invented geometry or discarded the complete neighbour triangle.");
            var gap=DepthPatchSampler.Capture(2,2,0,1,0.45f,8,(x,y)=>new DepthPatchSample(true,new MeshPoint(x*2,y*2,0)),()=>0);
            Assert(gap.Triangles.Count==0,"Patch sampling bridged a large depth discontinuity.");
            var empty=DepthPatchSampler.Capture(2,2,0,1,0.45f,8,(x,y)=>new DepthPatchSample(false,default),()=>0);
            Assert(empty.Triangles.Count==0 && empty.VisitedCells==1,"Missing depth was converted to a surface.");
            var frameA=DepthPatchSampler.Capture(3,2,0,1,0.45f,8,(x,y)=>Plane(x,y,0),()=>0);
            var frameB=DepthPatchSampler.Capture(3,2,frameA.NextCell,1,0.45f,8,(x,y)=>Plane(x,y,10),()=>0);
            Assert(frameB.Triangles.All(t=>t.A.Z==10 && t.B.Z==10 && t.C.Z==10),"A triangle combined samples from different capture calls.");
            foreach(var profile in new[]{(8,6),(10,8),(14,10)})
            {
                var calls=0;
                var all=DepthPatchSampler.Capture(profile.Item1,profile.Item2,0,1000,0.45f,8,(x,y)=>
                {Assert(x>=0&&y>=0&&x<profile.Item1&&y<profile.Item2,"Sampler queried outside its detail profile.");calls++;return Plane(x,y);},()=>0);
                var cells=(profile.Item1-1)*(profile.Item2-1);
                Assert(all.VisitedCells==cells && calls==cells*4 && all.Triangles.Count==cells*2,"A complete detail profile lost cells.");
            }
            Console.WriteLine("PASS: slow-device progress, retained complete patches, full-view coverage, profile bounds, no cross-frame triangles, missing-depth preservation and discontinuity rejection.");return 0;
        }
        catch(Exception error) {Console.Error.WriteLine(error);return 1;}
    }
}
