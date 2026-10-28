using System;
using System.Collections.Generic;

namespace SpatialSense.RoomScan
{
    public readonly struct DepthPatchSample
    {
        public readonly bool Hit;
        public readonly MeshPoint Point;
        public DepthPatchSample(bool hit,MeshPoint point) { Hit=hit;Point=point; }
    }
    public readonly struct DepthSurfaceTriangle
    {
        public readonly MeshPoint A,B,C;
        public DepthSurfaceTriangle(MeshPoint a,MeshPoint b,MeshPoint c) { A=a;B=b;C=c; }
    }
    public sealed class DepthPatchBatch
    {
        public readonly List<DepthSurfaceTriangle> Triangles=new List<DepthSurfaceTriangle>();
        public int VisitedCells,NextCell;
        public double Milliseconds;
        public bool BudgetReached;
    }
    public static class DepthPatchSampler
    {
        // Each quad is completed in one call/frame. A time limit stops BETWEEN
        // quads, retaining all valid completed triangles, including the first
        // quad on a slow device. No unfinished samples carry into another frame.
        public static DepthPatchBatch Capture(int width,int height,int firstCell,int maxCells,float maxEdge,double budgetMs,
            Func<int,int,DepthPatchSample> sample,Func<double> clockMs)
        {
            if(width<2 || height<2 || width>64 || height>64 || maxCells<1 || maxEdge<=0 || float.IsNaN(maxEdge) || float.IsInfinity(maxEdge)
                || double.IsNaN(budgetMs) || double.IsInfinity(budgetMs) || budgetMs<=0)
                throw new ArgumentException("Invalid depth patch schedule.");
            var cells=(width-1)*(height-1);var result=new DepthPatchBatch();var began=clockMs();
            var cursor=((firstCell%cells)+cells)%cells;
            for(var i=0;i<Math.Min(maxCells,cells);i++)
            {
                var x=cursor%(width-1);var y=cursor/(width-1);
                var a=sample(x,y);var b=sample(x,y+1);var c=sample(x+1,y);var d=sample(x+1,y+1);
                Add(result,a,b,c,maxEdge);Add(result,c,b,d,maxEdge);
                result.VisitedCells++;cursor=(cursor+1)%cells;result.NextCell=cursor;
                result.Milliseconds=Math.Max(0,clockMs()-began);
                if(result.Milliseconds>=budgetMs) { result.BudgetReached=true;break; }
            }
            return result;
        }
        private static void Add(DepthPatchBatch batch,DepthPatchSample a,DepthPatchSample b,DepthPatchSample c,float maxEdge)
        {
            var abX=b.Point.X-a.Point.X;var abY=b.Point.Y-a.Point.Y;var abZ=b.Point.Z-a.Point.Z;
            var acX=c.Point.X-a.Point.X;var acY=c.Point.Y-a.Point.Y;var acZ=c.Point.Z-a.Point.Z;
            var bcX=c.Point.X-b.Point.X;var bcY=c.Point.Y-b.Point.Y;var bcZ=c.Point.Z-b.Point.Z;
            var crossX=abY*acZ-abZ*acY;var crossY=abZ*acX-abX*acZ;var crossZ=abX*acY-abY*acX;
            if(DepthTrianglePolicy.Accept(a.Hit,b.Hit,c.Hit,abX*abX+abY*abY+abZ*abZ,acX*acX+acY*acY+acZ*acZ,
                bcX*bcX+bcY*bcY+bcZ*bcZ,crossX*crossX+crossY*crossY+crossZ*crossZ,maxEdge))
                batch.Triangles.Add(new DepthSurfaceTriangle(a.Point,b.Point,c.Point));
        }
    }
}
