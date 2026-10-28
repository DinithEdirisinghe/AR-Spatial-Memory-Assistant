using System;
using System.Collections.Generic;
using System.Threading;

namespace SpatialSense.RoomScan
{
    [Serializable] public sealed class HomeCell
    {
        public int x, z, floor;
        public bool blocked, wall;
        public bool cleared, dynamic;
        public float low, high;
    }
    [Serializable] public sealed class HomeItem
    {
        public bool saved;
        public float x, y, z;
    }
    [Serializable] public sealed class HomeSurface
    {
        public int x, z;
        public float[] vertices;
        public int[] indices;
    }
    [Serializable] public sealed class HomeRecord
    {
        public int version = 2;
        public float cellSize = HomeMap.CellSize;
        public string anchorId, savedUtc;
        public bool floorVerified;
        public HomeCell[] cells;
        public HomeSurface[] surfaces;
        public HomeItem[] items;
        public bool Valid()
        {
            if ((version != 1 && version != 2) || !Guid.TryParse(anchorId, out var id) || id == Guid.Empty
                || (version == 2 && (!HomeMap.Finite(cellSize)||Math.Abs(cellSize-HomeMap.CellSize)>.0001f))
                || !DateTimeOffset.TryParse(savedUtc, out _) || cells == null || cells.Length > (version==1?80000:HomeMap.MaxCells)
                || surfaces == null || surfaces.Length > 48 || items == null || items.Length != 3) return false;
            var keys = new HashSet<(int,int)>();
            foreach (var c in cells)
                if (c == null || Math.Abs((long)c.x) > (version==1?400:800) || Math.Abs((long)c.z) > (version==1?400:800)
                    || c.floor < 0 || c.floor > 31 || (c.wall && (!c.blocked || c.cleared))
                    || (c.cleared && !c.blocked) || !HomeMap.Finite(c.low) || !HomeMap.Finite(c.high)
                    || (version==2 && c.blocked && (c.low<.1f || c.high>2.1f || c.high<c.low)) || !keys.Add((c.x,c.z))) return false;
            var vertices = 0; var indices = 0; keys.Clear();
            foreach (var s in surfaces)
            {
                if (s == null || Math.Abs((long)s.x) > 11 || Math.Abs((long)s.z) > 11 || !keys.Add((s.x,s.z))
                    || s.vertices == null || s.vertices.Length % 3 != 0 || s.indices == null || s.indices.Length % 3 != 0
                    || s.vertices.Length > DepthMeshRecord.MaxVertices * 3 || s.indices.Length > DepthMeshRecord.MaxIndices) return false;
                vertices += s.vertices.Length / 3; indices += s.indices.Length;
                if (vertices > 120000 || indices > 600000) return false;
                foreach (var f in s.vertices) if (!HomeMap.Finite(f) || Math.Abs(f) > 60) return false;
                foreach (var i in s.indices) if (i < 0 || i >= s.vertices.Length / 3) return false;
            }
            foreach (var i in items) if (i == null || !HomeMap.Finite(i.x) || !HomeMap.Finite(i.y) || !HomeMap.Finite(i.z)
                || Math.Abs(i.x) > 60 || Math.Abs(i.z) > 60 || Math.Abs(i.y) > 5) return false;
            return true;
        }
    }

    // Sparse, single-level observed-floor graph. No room IDs, artificial doorway
    // links, head-pose footprints, or free-space inference from missing depth.
    public sealed partial class HomeMap
    {
        public const float CellSize = 0.075f;
        public const int MaxCells = 320000;
        private readonly Dictionary<(int,int), HomeCell> cells = new Dictionary<(int,int), HomeCell>();
        public IEnumerable<HomeCell> Cells => cells.Values;
        public int Count => cells.Count;
        public int Revision { get; private set; }
        public int BlockingRevision { get; private set; }
        public bool Full { get; private set; }
        public static bool Finite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);
        public static (int,int) Key(float x, float z) => ((int)Math.Floor(x / CellSize), (int)Math.Floor(z / CellSize));
        public static MeshPoint Centre(int x,int z) => new MeshPoint((x+.5f)*CellSize,0,(z+.5f)*CellSize);
        public static bool Blocked(HomeCell c) => c.blocked && !c.cleared;
        public bool Walkable(int x,int z) => cells.TryGetValue((x,z),out var c) && c.floor == 31 && !Blocked(c);
        private static HomeCell Clone(HomeCell c)=>new HomeCell {x=c.x,z=c.z,floor=c.floor,blocked=c.blocked,wall=c.wall,
            low=c.low,high=c.high,cleared=c.cleared,dynamic=c.dynamic};
        public HomeCell[] Snapshot()
        {
            var output = new HomeCell[cells.Count]; var i=0;
            foreach(var c in cells.Values) output[i++]=Clone(c);
            return output;
        }
        public static HomeMap Restore(HomeCell[] saved)
        {
            var map = new HomeMap();
            foreach(var c in saved) {map.cells.Add((c.x,c.z),Clone(c));if(c.cleared)map.ReopenedCount++;else if(c.dynamic && c.blocked)map.DynamicCount++;}
            return map;
        }
        public static HomeMap FromRecord(HomeRecord record,CancellationToken token)
        {
            if(record.version==2)return Restore(record.cells);
            // Re-rasterize genuine saved surfaces. Splitting a coarse cell into
            // four green cells would falsely claim newly measured detail.
            var map=new HomeMap();var count=0;
            foreach(var surface in record.surfaces)
            {
                MeshPoint Point(int i)=>new MeshPoint(surface.vertices[3*i],surface.vertices[3*i+1],surface.vertices[3*i+2]);
                for(var i=0;i<surface.indices.Length;i+=3)
                {if((count++&255)==0)token.ThrowIfCancellationRequested();map.Observe(new DepthSurfaceTriangle(Point(surface.indices[i]),Point(surface.indices[i+1]),Point(surface.indices[i+2])));}
            }
            return map;
        }
        private HomeCell Get(int x,int z)
        {
            if(cells.TryGetValue((x,z),out var c))return c;
            if(cells.Count>=MaxCells || Math.Abs(x)>800 || Math.Abs(z)>800) {Full=true;return null;}
            c=new HomeCell {x=x,z=z};cells.Add((x,z),c);return c;
        }
        public void Observe(DepthSurfaceTriangle t,bool captureObstacles=true,Dictionary<(int,int),HomeObstacleSample> hits=null)
        {
            var a=t.A;var b=t.B;var c=t.C;
            var nx=(b.Y-a.Y)*(c.Z-a.Z)-(b.Z-a.Z)*(c.Y-a.Y);
            var ny=(b.Z-a.Z)*(c.X-a.X)-(b.X-a.X)*(c.Z-a.Z);
            var nz=(b.X-a.X)*(c.Y-a.Y)-(b.Y-a.Y)*(c.X-a.X);
            var length=Math.Sqrt(nx*nx+ny*ny+nz*nz);
            if(length<.00001 || !ValidPoint(a) || !ValidPoint(b) || !ValidPoint(c))return;
            var floor=Math.Abs(a.Y)<.09f && Math.Abs(b.Y)<.09f && Math.Abs(c.Y)<.09f && Math.Abs(ny)/length>.94;
            var low=Math.Min(a.Y,Math.Min(b.Y,c.Y)); var high=Math.Max(a.Y,Math.Max(b.Y,c.Y));
            if(!floor && (high<.12f || low>1.9f))return;
            var min=Key(Math.Min(a.X,Math.Min(b.X,c.X)),Math.Min(a.Z,Math.Min(b.Z,c.Z)));
            var max=Key(Math.Max(a.X,Math.Max(b.X,c.X)),Math.Max(a.Z,Math.Max(b.Z,c.Z)));
            if(max.Item1-min.Item1>10 || max.Item2-min.Item2>10)return;
            for(var x=min.Item1;x<=max.Item1;x++)for(var z=min.Item2;z<=max.Item2;z++)
            {
                var p=Centre(x,z);var mask=0;
                if(floor)
                {
                    if(Inside(p.X,p.Z,a,b,c))mask|=1;
                    var d=CellSize*.42f;
                    if(Inside(p.X-d,p.Z-d,a,b,c))mask|=2;
                    if(Inside(p.X+d,p.Z-d,a,b,c))mask|=4;
                    if(Inside(p.X-d,p.Z+d,a,b,c))mask|=8;
                    if(Inside(p.X+d,p.Z+d,a,b,c))mask|=16;
                    if(mask==0)continue;
                }
                else if(!Intersects(a,b,c,x*CellSize,z*CellSize,CellSize))continue;
                var cell=Get(x,z);if(cell==null)continue;
                if(floor){var before=cell.floor;cell.floor|=mask;if(before!=cell.floor)Revision++;}
                else
                {
                    var structural=Math.Abs(ny)/length<.3 && high>1.6f;
                    if(hits!=null)
                    {var key=(x,z);var sample=new HomeObstacleSample(Math.Max(.12f,low),Math.Min(1.9f,high),structural);
                        if(hits.TryGetValue(key,out var prior))sample=new HomeObstacleSample(Math.Min(prior.Low,sample.Low),Math.Max(prior.High,sample.High),prior.Wall||sample.Wall);
                        hits[key]=sample;}
                    if(captureObstacles)SetObstacle(cell,Math.Max(.12f,low),Math.Min(1.9f,high),structural,false);
                }
            }
        }
        private static float Cross(float x,float z,MeshPoint a,MeshPoint b)=>(b.X-a.X)*(z-a.Z)-(b.Z-a.Z)*(x-a.X);
        private static bool Inside(float x,float z,MeshPoint a,MeshPoint b,MeshPoint c)
        {
            if(Math.Abs(Cross(c.X,c.Z,a,b))<.0000001)return false;
            var ab=Cross(x,z,a,b);var bc=Cross(x,z,b,c);var ca=Cross(x,z,c,a);
            return (ab>=-.000001 && bc>=-.000001 && ca>=-.000001)||(ab<=.000001 && bc<=.000001 && ca<=.000001);
        }
        private static bool Intersects( MeshPoint a,MeshPoint b,MeshPoint c,float x,float z,float size)
        {
            bool In(MeshPoint p)=>p.X>=x && p.X<=x+size && p.Z>=z && p.Z<=z+size;
            if(In(a)||In(b)||In(c)||Inside(x+size/2,z+size/2,a,b,c))return true;
            return Segment(a,b,x,z,size)||Segment(b,c,x,z,size)||Segment(c,a,x,z,size);
        }
        private static bool Segment(MeshPoint a,MeshPoint b,float x,float z,float size)
        {
            float lo=0,hi=1;
            bool Clip(float origin,float delta,float min,float max)
            {
                if(Math.Abs(delta)<.000001)return origin>=min && origin<=max;
                var t0=(min-origin)/delta;var t1=(max-origin)/delta;
                if(t0>t1){var swap=t0;t0=t1;t1=swap;}lo=Math.Max(lo,t0);hi=Math.Min(hi,t1);return lo<=hi;
            }
            return Clip(a.X,b.X-a.X,x,x+size)&&Clip(a.Z,b.Z-a.Z,z,z+size);
        }
        private static bool ValidPoint(MeshPoint p)=>Finite(p.X)&&Finite(p.Y)&&Finite(p.Z)&&Math.Abs(p.X)<=60&&Math.Abs(p.Z)<=60&&Math.Abs(p.Y)<=5;
        public bool CanApproachItem(MeshPoint from,MeshPoint item)=>CanReachItem(from,item);
        private bool CanReachItem(MeshPoint from,MeshPoint item)
        {
            var distance=Math.Sqrt((from.X-item.X)*(from.X-item.X)+(from.Z-item.Z)*(from.Z-item.Z));
            var steps=Math.Max(1,(int)Math.Ceiling(distance/(CellSize*.25f)));
            var target=Key(item.X,item.Z);
            var allowTargetFootprint=cells.TryGetValue(target,out var targetCell) && Blocked(targetCell) && !targetCell.wall
                && item.Y>=targetCell.low-.15f && item.Y<=targetCell.high+.35f;
            var leftTargetFootprint=false;
            for(var i=0;i<=steps;i++)
            {
                // An approach may reach over the item's own supporting table,
                // but cannot jump a second obstacle or a partly observed wall.
                var key=Key(item.X+(from.X-item.X)*i/steps,item.Z+(from.Z-item.Z)*i/steps);
                if(cells.TryGetValue(key,out var cell) && Blocked(cell))
                {
                    if(cell.wall || !allowTargetFootprint || leftTargetFootprint || cell.high<item.Y-.35f || cell.low>item.Y+.15f)return false;
                }
                else leftTargetFootprint=true;
            }
            return true;
        }
    }
}
