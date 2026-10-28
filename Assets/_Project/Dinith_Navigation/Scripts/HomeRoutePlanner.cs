using System;
using System.Collections.Generic;
using System.Threading;

namespace SpatialSense.RoomScan
{
    public sealed partial class HomeMap
    {
        private sealed class RouteHeap
        {
            private readonly List<((int,int) key,float f,float g)> values=new List<((int,int),float,float)>();
            public int Count=>values.Count;
            public void Push((int,int) key,float f,float g)
            {
                values.Add((key,f,g));var i=values.Count-1;
                while(i>0){var parent=(i-1)/2;if(values[parent].f<=values[i].f)break;var swap=values[parent];values[parent]=values[i];values[i]=swap;i=parent;}
            }
            public ((int,int) key,float f,float g) Pop()
            {
                var result=values[0];var last=values[values.Count-1];values.RemoveAt(values.Count-1);if(values.Count==0)return result;values[0]=last;
                var i=0;while(true){var next=i*2+1;if(next>=values.Count)break;if(next+1<values.Count&&values[next+1].f<values[next].f)next++;
                    if(values[i].f<=values[next].f)break;var swap=values[i];values[i]=values[next];values[next]=swap;i=next;}
                return result;
            }
        }
        private bool Traversable(int x,int z,float clearance)
        {
            if(!Walkable(x,z))return false;if(clearance<=0)return true;
            var steps=(int)Math.Ceiling(clearance/CellSize+.5f);
            for(var dz=-steps;dz<=steps;dz++)for(var dx=-steps;dx<=steps;dx++)
            {
                if(!cells.TryGetValue((x+dx,z+dz),out var c)||!Blocked(c))continue;
                var nearX=Math.Max(0,Math.Abs(dx)*CellSize-CellSize*.5f);var nearZ=Math.Max(0,Math.Abs(dz)*CellSize-CellSize*.5f);
                if(nearX*nearX+nearZ*nearZ<clearance*clearance)return false;
            }
            return true;
        }
        public bool CanWalkSegment(MeshPoint a,MeshPoint b,float clearance=.015f)
        {
            if(!ValidPoint(a)||!ValidPoint(b))return false;
            var dx=b.X-a.X;var dz=b.Z-a.Z;var steps=Math.Max(1,(int)Math.Ceiling(Math.Max(Math.Abs(dx),Math.Abs(dz))/(CellSize*.25f)));
            var previous=Key(a.X,a.Z);
            for(var i=0;i<=steps;i++)
            {
                var k=Key(a.X+dx*i/steps,a.Z+dz*i/steps);
                if(!Traversable(k.Item1,k.Item2,clearance))return false;
                if(k.Item1!=previous.Item1 && k.Item2!=previous.Item2
                    && (!Traversable(k.Item1,previous.Item2,clearance)||!Traversable(previous.Item1,k.Item2,clearance)))return false;
                // Measure the requested extra margin at the actual line position,
                // rather than only at the centre of each traversed grid square.
                if(clearance>0)
                {
                    var x=a.X+dx*i/steps;var z=a.Z+dz*i/steps;var radius=(int)Math.Ceiling(clearance/CellSize);
                    for(var oz=-radius;oz<=radius;oz++)for(var ox=-radius;ox<=radius;ox++)
                    {
                        if(!cells.TryGetValue((k.Item1+ox,k.Item2+oz),out var c)||!Blocked(c))continue;
                        var nearX=Math.Max(c.x*CellSize-x,Math.Max(0,x-(c.x+1)*CellSize));
                        var nearZ=Math.Max(c.z*CellSize-z,Math.Max(0,z-(c.z+1)*CellSize));
                        if(nearX*nearX+nearZ*nearZ<clearance*clearance)return false;
                    }
                }
                previous=k;
            }
            return true;
        }
        public bool RouteClear(IReadOnlyList<MeshPoint> route,float clearance=.015f)
        {
            if(route==null||route.Count==0)return false;
            if(!CanWalkSegment(route[0],route[0],clearance))return false;
            for(var i=1;i<route.Count;i++)if(!CanWalkSegment(route[i-1],route[i],clearance))return false;return true;
        }
        public List<MeshPoint> Route(MeshPoint start,MeshPoint item,CancellationToken token,float clearance=.015f)
        {
            token.ThrowIfCancellationRequested();var output=new List<MeshPoint>();
            if(!ValidPoint(start)||!ValidPoint(item)||!Finite(clearance)||clearance<0||clearance>.2f)return output;
            var origin=Key(start.X,start.Z);if(!Traversable(origin.Item1,origin.Item2,clearance))return output;
            var frontier=new RouteHeap();var costs=new Dictionary<(int,int),float>();var parents=new Dictionary<(int,int),(int,int)>();
            float Distance((int,int) k){var p=Centre(k.Item1,k.Item2);return (float)Math.Sqrt((p.X-item.X)*(p.X-item.X)+(p.Z-item.Z)*(p.Z-item.Z));}
            float Heuristic((int,int) k)=>Math.Max(0,Distance(k)-.25f);
            costs[origin]=0;parents[origin]=origin;frontier.Push(origin,Heuristic(origin),0);
            var best=origin;var bestDistance=float.MaxValue;var bestCost=float.MaxValue;var found=false;var iterations=0;
            while(frontier.Count>0)
            {
                if((iterations++&127)==0)token.ThrowIfCancellationRequested();
                var node=frontier.Pop();var k=node.key;if(node.g>costs[k]+.00001f)continue;
                var distance=Distance(k);var p=Centre(k.Item1,k.Item2);var score=Math.Max(.25f,distance);
                if(distance<=1.1f && CanReachItem(p,item)
                    && (score<bestDistance-CellSize*.5f || (Math.Abs(score-bestDistance)<=CellSize*.5f && node.g<bestCost)))
                {best=k;bestDistance=score;bestCost=node.g;found=true;if(distance<=.25f)break;}
                for(var z=-1;z<=1;z++)for(var x=-1;x<=1;x++)
                {
                    if(x==0&&z==0)continue;var next=(k.Item1+x,k.Item2+z);
                    if(!Traversable(next.Item1,next.Item2,clearance))continue;
                    if(x!=0&&z!=0&&(!Traversable(k.Item1+x,k.Item2,clearance)||!Traversable(k.Item1,k.Item2+z,clearance)))continue;
                    var cost=node.g+CellSize*(x!=0&&z!=0?1.41421356f:1f);
                    if(costs.TryGetValue(next,out var prior)&&cost>=prior-.00001f)continue;
                    costs[next]=cost;parents[next]=k;frontier.Push(next,cost+Heuristic(next),cost);
                }
            }
            if(!found)return output;
            while(true){output.Add(Centre(best.Item1,best.Item2));if(best==origin)break;best=parents[best];}
            output.Reverse();output[0]=new MeshPoint(start.X,0,start.Z);
            var smooth=new List<MeshPoint>{output[0]};var from=0;
            while(from<output.Count-1)
            {
                token.ThrowIfCancellationRequested();var far=from+1;
                for(var i=output.Count-1;i>from+1;i--){token.ThrowIfCancellationRequested();if(CanWalkSegment(output[from],output[i],clearance)){far=i;break;}}
                if(!CanWalkSegment(output[from],output[far],clearance))return new List<MeshPoint>();
                smooth.Add(output[far]);from=far;
            }
            return smooth;
        }
    }
}
