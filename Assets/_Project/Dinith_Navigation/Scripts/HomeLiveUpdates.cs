using System;
using System.Collections.Generic;

namespace SpatialSense.RoomScan
{
    public readonly struct HomeObstacleSample
    {
        public readonly float Low,High;
        public readonly bool Wall;
        public HomeObstacleSample(float low,float high,bool wall){Low=low;High=high;Wall=wall;}
    }
    public sealed partial class HomeMap
    {
        private sealed class Evidence
        {
            public int hits;
            public double lastHit=-10,lastEvidence=-10,firstFree=double.NaN;
            public readonly byte[] counts=new byte[8];
            public readonly double[] seen=new double[8];
        }
        private readonly Dictionary<(int,int),Evidence> evidence=new Dictionary<(int,int),Evidence>();
        private long lastBatch=-1;
        private int probeCursor,batchCount;
        public int DynamicCount {get;private set;}
        public int ReopenedCount {get;private set;}
        public bool EvidenceAtCapacity=>evidence.Count>=8192;
        private Evidence GetEvidence((int,int) key)
        {
            if(evidence.TryGetValue(key,out var e))return e;
            if(EvidenceAtCapacity)return null;e=new Evidence();evidence.Add(key,e);return e;
        }
        private void SetObstacle(HomeCell cell,float low,float high,bool wall,bool dynamic)
        {
            var was=Blocked(cell);var oldWall=cell.wall;var oldLow=cell.low;var oldHigh=cell.high;
            if(cell.cleared){ReopenedCount--;cell.low=cell.high=0;}
            var wasDynamic=was&&cell.dynamic;
            cell.low=cell.low>0?Math.Min(cell.low,low):low;cell.high=Math.Max(cell.high,high);
            cell.low=Math.Max(.12f,(float)Math.Floor(cell.low*20)/20);cell.high=Math.Min(1.9f,(float)Math.Ceiling(cell.high*20)/20);
            cell.dynamic|=dynamic&&!cell.blocked;cell.blocked=true;cell.cleared=false;cell.wall|=wall;
            if(!wasDynamic&&cell.dynamic)DynamicCount++;
            if(!was || cell.wall!=oldWall || cell.low!=oldLow || cell.high!=oldHigh){Revision++;BlockingRevision++;}
        }
        private static int Levels(HomeCell c)=>Math.Min(7,Math.Max(1,(int)Math.Ceiling((c.high-c.low)/.25f)+1));
        private static float Height(HomeCell c,int slot)=>Levels(c)==1?c.low:c.low+(c.high-c.low)*slot/(Levels(c)-1);
        public void ResetEvidence(){evidence.Clear();lastBatch=-1;}
        // Each call is a distinct acquisition batch. Positive occupied-space
        // evidence wins over free rays from the same batch; missing depth does
        // not cast a free ray and never opens a confirmed cell.
        public void ObserveLive(IReadOnlyList<DepthObservationRay> rays,long batch,double now,bool detect=true,bool refresh=true,
            Dictionary<(int,int),HomeObstacleSample> surfaceHits=null,bool baseline=false)
        {
            if(batch<=lastBatch || double.IsNaN(now)||double.IsInfinity(now))return;lastBatch=batch;
            var hits=surfaceHits??new Dictionary<(int,int),HomeObstacleSample>();
            var masks=new Dictionary<(int,int),int>();
            void Mark((int,int) key,int mask){masks.TryGetValue(key,out var prior);masks[key]=prior|mask;}
            foreach(var ray in rays)
            {
                if(!ValidPoint(ray.Origin)||!ValidPoint(ray.Hit))continue;
                var dx=ray.Hit.X-ray.Origin.X;var dy=ray.Hit.Y-ray.Origin.Y;var dz=ray.Hit.Z-ray.Origin.Z;
                var length=Math.Sqrt(dx*dx+dy*dy+dz*dz);if(length<.08 || length>4)continue;
                var end=Key(ray.Hit.X,ray.Hit.Z);
                if(ray.Hit.Y>=.15f && ray.Hit.Y<=1.9f)
                {
                    var h=new HomeObstacleSample(ray.Hit.Y,ray.Hit.Y,false);
                    if(hits.TryGetValue(end,out var prior))h=new HomeObstacleSample(Math.Min(prior.Low,h.Low),Math.Max(prior.High,h.High),prior.Wall);
                    hits[end]=h;
                }
                if(!refresh)continue;
                if(ray.HasNormal && ValidNormal(ray.Normal) && Math.Abs(ray.Normal.Y)>.9f && Math.Abs(ray.Hit.Y)<.08f
                    && cells.TryGetValue(end,out var floorCell) && floorCell.blocked && !floorCell.wall)Mark(end,128);
                var steps=(int)Math.Ceiling(Math.Max(Math.Abs(dx),Math.Abs(dz))/CellSize*2);
                if(steps>512)continue;var previous=(int.MinValue,int.MinValue);
                for(var i=0;i<=steps;i++)
                {
                    var t=steps==0?0:i/(float)steps;var key=Key(ray.Origin.X+dx*t,ray.Origin.Z+dz*t);
                    if(key==previous)continue;previous=key;
                    if(!cells.TryGetValue(key,out var c)||!c.blocked||c.wall||c.cleared||c.high>1.6f)continue;
                    for(var slot=0;slot<Levels(c);slot++)if(PassesVolume(ray,c,Height(c,slot),length))Mark(key,1<<slot);
                }
            }
            foreach(var pair in hits)
            {
                var key=pair.Key;var hit=pair.Value;
                if(!detect)continue;
                var c=Get(key.Item1,key.Item2);if(c==null)continue;
                // Confirmed/reoccupied space must reblock even when the
                // temporary-evidence budget is full. Structural cells do not
                // consume verification slots that can never clear them.
                if(c.blocked || hit.Wall)SetObstacle(c,hit.Low,hit.High,hit.Wall,!baseline);
                if(c.wall && !evidence.ContainsKey(key))continue;
                var e=GetEvidence(key);if(e==null)continue;
                e.hits=now-e.lastHit<=1.5?Math.Min(3,e.hits+1):1;e.lastHit=now;e.lastEvidence=now;
                Array.Clear(e.counts,0,8);e.firstFree=double.NaN;
                if(!c.blocked && e.hits>=2)SetObstacle(c,hit.Low,hit.High,hit.Wall,!baseline);
            }
            if(refresh)foreach(var pair in masks)
            {
                if(hits.ContainsKey(pair.Key) || !cells.TryGetValue(pair.Key,out var c) || !Blocked(c)||c.wall||c.high>1.6f)continue;
                var e=GetEvidence(pair.Key);if(e==null)continue;
                if(now-e.lastEvidence>3 || now<e.lastEvidence){Array.Clear(e.counts,0,8);e.firstFree=double.NaN;}
                e.lastEvidence=now;if(double.IsNaN(e.firstFree))e.firstFree=now;
                for(var slot=0;slot<8;slot++)if((pair.Value&(1<<slot))!=0)
                {if(now-e.seen[slot]>3 || now<e.seen[slot])e.counts[slot]=0;if(e.counts[slot]<3)e.counts[slot]++;e.seen[slot]=now;}
                var verified=c.floor==31 && now-e.firstFree>=.6 && e.counts[7]>=3 && now-e.seen[7]<=3;
                for(var slot=0;slot<Levels(c)&&verified;slot++)verified=e.counts[slot]>=3&&now-e.seen[slot]<=3;
                if(verified){c.cleared=true;ReopenedCount++;if(c.dynamic)DynamicCount--;Revision++;BlockingRevision++;}
            }
            if(++batchCount%20==0)
            {var stale=new List<(int,int)>();foreach(var pair in evidence)if(now-pair.Value.lastEvidence>10)stale.Add(pair.Key);foreach(var key in stale)evidence.Remove(key);}
        }
        private static bool ValidNormal(MeshPoint n)=>Finite(n.X)&&Finite(n.Y)&&Finite(n.Z);
        private static bool PassesVolume(DepthObservationRay ray,HomeCell c,float y,double length)
        {
            var p=Centre(c.x,c.z);float enter=0,exit=1;var half=CellSize*.5f;
            bool Clip(float origin,float delta,float min,float max)
            {
                if(Math.Abs(delta)<.000001)return origin>=min&&origin<=max;
                var a=(min-origin)/delta;var b=(max-origin)/delta;if(a>b){var swap=a;a=b;b=swap;}
                enter=Math.Max(enter,a);exit=Math.Min(exit,b);return enter<=exit;
            }
            return Clip(ray.Origin.X,ray.Hit.X-ray.Origin.X,p.X-half,p.X+half)
                &&Clip(ray.Origin.Y,ray.Hit.Y-ray.Origin.Y,y-.1f,y+.1f)
                &&Clip(ray.Origin.Z,ray.Hit.Z-ray.Origin.Z,p.Z-half,p.Z+half)
                &&enter>0 && exit<1-.08/length;
        }
        public List<MeshPoint> ProbeTargets(MeshPoint head,MeshPoint forward,int maximum=8)
        {
            var candidates=new List<(HomeCell,float)>();var centre=Key(head.X,head.Z);
            for(var z=centre.Item2-40;z<=centre.Item2+40;z++)for(var x=centre.Item1-40;x<=centre.Item1+40;x++)
            {
                if(!cells.TryGetValue((x,z),out var c)||!Blocked(c)||c.wall||c.high>1.6f)continue;
                var p=Centre(x,z);var dx=p.X-head.X;var dz=p.Z-head.Z;var dy=(c.low+c.high)/2-head.Y;
                var distance=(float)Math.Sqrt(dx*dx+dy*dy+dz*dz);if(distance<.3f||distance>3.3f)continue;
                var dot=(dx*forward.X+dy*forward.Y+dz*forward.Z)/distance;if(dot<.7f)continue;
                candidates.Add((c,(1-dot)*5+distance*.1f));
            }
            candidates.Sort((a,b)=>a.Item2.CompareTo(b.Item2));var count=Math.Min(16,candidates.Count);var output=new List<MeshPoint>();
            if(count==0)return output;
            for(var i=0;i<maximum;i++)
            {
                var cursor=probeCursor++&0x7fffffff;var c=candidates[cursor%count].Item1;var slot=(cursor/count)%8;var p=Centre(c.x,c.z);
                output.Add(new MeshPoint(p.X,slot>=Levels(c)?0:Height(c,slot),p.Z));
            }
            return output;
        }
        public void ReturnUnusedProbes(int count){probeCursor-=Math.Max(0,count);}
    }
}
