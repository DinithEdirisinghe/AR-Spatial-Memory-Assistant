using System;
using System.Collections.Generic;
using System.Threading;

namespace SpatialSense.RoomScan
{
    [Serializable] public sealed class RoomDoorEndpoint
    {
        public string roomId, floorId, layoutId;
        public float x, y, z;
        public bool IsValid() => Guid.TryParse(roomId, out var r) && r != Guid.Empty && roomId==r.ToString("D")
            && Guid.TryParse(floorId, out var f) && f != Guid.Empty && floorId==f.ToString("D")
            && !string.IsNullOrEmpty(layoutId) && layoutId.Length<=64
            && Finite(x) && Finite(y) && Finite(z) && Math.Abs(x) < 100 && Math.Abs(y) < 100 && Math.Abs(z) < 100;
        private static bool Finite(float x) => !float.IsNaN(x) && !float.IsInfinity(x);
    }
    [Serializable] public sealed class RoomDoorConnection
    {
        public string id, taughtUtc;
        public RoomDoorEndpoint a, b;
        public float crossingMetres;
        public bool IsValid() => Guid.TryParse(id, out var idValue) && idValue != Guid.Empty && DateTimeOffset.TryParse(taughtUtc, out _)
            && a != null && b != null && a.IsValid() && b.IsValid() && a.roomId != b.roomId
            && !float.IsNaN(crossingMetres) && crossingMetres >= 0.1f && crossingMetres <= 3f;
    }
    [Serializable] public sealed class RoomAlias { public string roomId, label; }
    [Serializable] public sealed class RoomConnectionRecord
    {
        public int version = 1;
        public List<RoomAlias> rooms = new List<RoomAlias>();
        public List<RoomDoorConnection> doors = new List<RoomDoorConnection>();
    }
    [Serializable] public sealed class RoomNavigationArchive
    {
        public int version=1;
        public string roomId, floorId, layoutId, capturedUtc;
        public float[] vertices, fromFloor;
        public int[] triangles;
        public bool[] furniture;
        public int floorIndices;
        public RoomGraphMap Build(float margin,CancellationToken cancellation=default)
        {
            if(version!=1 || !Guid.TryParse(roomId,out var room) || room==Guid.Empty || !Guid.TryParse(floorId,out var floor) || floor==Guid.Empty
                || string.IsNullOrEmpty(layoutId) || layoutId.Length>64
                || vertices==null || vertices.Length<9 || vertices.Length%3!=0 || vertices.Length>120000
                || triangles==null || triangles.Length<3 || triangles.Length>240000 || triangles.Length%3!=0
                || fromFloor==null || fromFloor.Length!=12 || furniture==null || furniture.Length!=triangles.Length/3)
                throw new ArgumentException("Invalid saved room navigation model.");
            var points=new MeshPoint[vertices.Length/3];
            for(var i=0;i<vertices.Length;i++) if(float.IsNaN(vertices[i]) || float.IsInfinity(vertices[i]) || Math.Abs(vertices[i])>200)
                throw new ArgumentException("Invalid saved room geometry.");
            foreach(var value in fromFloor) if(float.IsNaN(value) || float.IsInfinity(value) || Math.Abs(value)>200)
                throw new ArgumentException("Invalid saved room coordinates.");
            for(var i=0;i<points.Length;i++) points[i]=new MeshPoint(vertices[i*3],vertices[i*3+1],vertices[i*3+2]);
            foreach(var index in triangles) if(index<0 || index>=points.Length) throw new ArgumentException("Invalid saved room indices.");
            return new RoomGraphMap { RoomId=roomId,FloorId=floorId,LayoutId=layoutId,FromFloor=fromFloor,
                Map=ScanNavigationGrid.Build(points,triangles,0,margin,cancellation,true,floorIndices,furniture) };
        }
    }

    public sealed class RoomGraphMap
    {
        public string RoomId, FloorId, LayoutId;
        public ScanNavigationGrid Map;
        // Floor-anchor local -> that room's Y-up navigation coordinates.
        public float[] FromFloor;
        public MeshPoint Point(RoomDoorEndpoint end) => new MeshPoint(
            FromFloor[0]*end.x + FromFloor[1]*end.y + FromFloor[2]*end.z + FromFloor[3],
            FromFloor[4]*end.x + FromFloor[5]*end.y + FromFloor[6]*end.z + FromFloor[7],
            FromFloor[8]*end.x + FromFloor[9]*end.y + FromFloor[10]*end.z + FromFloor[11]);
    }
    public sealed class RoomRouteLeg
    {
        public string RoomId, NextRoomId, ConnectionId;
        public RoomDoorEndpoint Exit;
    }

    public static class RoomConnectionGraph
    {
        private sealed class State
        {
            public string room, key;
            public MeshPoint point;
            public double cost;
            public State parent;
            public RoomRouteLeg leg;
        }
        public static bool TryPlan(string startRoom, MeshPoint start, string targetRoom,
            IReadOnlyDictionary<string, RoomGraphMap> rooms, IReadOnlyList<RoomDoorConnection> doors,
            out List<RoomRouteLeg> legs, out string reason, CancellationToken cancellation = default)
        {
            legs = new List<RoomRouteLeg>();
            if (!rooms.ContainsKey(startRoom) || !rooms.ContainsKey(targetRoom))
            { reason = "Both saved room maps must be loaded before planning between them."; return false; }
            var open = new List<State> { new State { room=startRoom, key=startRoom+"/start", point=start } };
            var best = new Dictionary<string,double>();
            while (open.Count > 0)
            {
                cancellation.ThrowIfCancellationRequested();
                var index=0;
                for (var i=1;i<open.Count;i++) if(open[i].cost<open[index].cost) index=i;
                var current=open[index]; open.RemoveAt(index);
                if (best.TryGetValue(current.key,out var known) && current.cost>known) continue;
                if (current.room == targetRoom)
                {
                    for(var state=current;state.parent!=null;state=state.parent) legs.Add(state.leg);
                    legs.Reverse(); reason="Follow the linked doorways; each room's floor route is checked separately."; return true;
                }
                var map=rooms[current.room];
                foreach(var door in doors)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if(door==null || !door.IsValid()) continue;
                    var exit=door.a.roomId==current.room?door.a:door.b.roomId==current.room?door.b:null;
                    if(exit==null) continue;
                    var entry=exit==door.a?door.b:door.a;
                    if(exit.floorId!=map.FloorId || exit.layoutId!=map.LayoutId || !rooms.TryGetValue(entry.roomId,out var next)
                        || entry.floorId!=next.FloorId || entry.layoutId!=next.LayoutId) continue;
                    var end=map.Point(exit); var nextStart=next.Point(entry);
                    var entryCell=next.Map.Index(nextStart.X,nextStart.Z);
                    if(entryCell<0 || next.Map.Cells[entryCell]!=2) continue;
                    if(!map.Map.TryRoute(current.point.X,current.point.Z,end.X,end.Z,out var path,out _)) continue;
                    double cost=current.cost+door.crossingMetres;
                    for(var i=1;i<path.Count;i++) { var dx=path[i].X-path[i-1].X; var dz=path[i].Z-path[i-1].Z; cost+=Math.Sqrt(dx*dx+dz*dz); }
                    var key=entry.roomId+"/"+door.id;
                    if(best.TryGetValue(key,out var old) && cost>=old) continue;
                    best[key]=cost;
                    open.Add(new State { room=entry.roomId,key=key,point=nextStart,cost=cost,parent=current,
                        leg=new RoomRouteLeg { RoomId=current.room,NextRoomId=entry.roomId,ConnectionId=door.id,Exit=exit } });
                }
            }
            reason="No connected walkable doorway route. Teach/re-teach links after a rescan, or clear a blocked approach.";
            return false;
        }
    }

    // Raw tracking-space positions make this independent of MRUK's world-lock
    // changes when entering another room. Recenter/tracking changes cancel it.
    public sealed class DoorwayTraversal
    {
        private readonly string source;
        private readonly MeshPoint start;
        private readonly double began;
        private MeshPoint previous;
        private double previousTime;
        private string secondRoom;
        private double walked;
        public bool Valid { get; private set; } = true;
        public DoorwayTraversal(string room, MeshPoint rawHead, double time)
        { source=room; start=previous=rawHead; began=previousTime=time; Valid=!string.IsNullOrEmpty(room) && Finite(rawHead) && !double.IsNaN(time) && !double.IsInfinity(time); }
        public bool Observe(string room, MeshPoint rawHead, double time, bool tracked)
        {
            if(!Valid) return false;
            var dt=time-previousTime; var dx=rawHead.X-previous.X; var dz=rawHead.Z-previous.Z;
            var distance=Math.Sqrt(dx*dx+dz*dz);
            if(!tracked || !Finite(rawHead) || double.IsNaN(time) || double.IsInfinity(time) || dt<0 || dt>0.75 || time-began>45 || distance>3*dt+0.15)
                return Valid=false;
            walked+=distance; if(walked>4) return Valid=false;
            if(room==source && secondRoom!=null)return Valid=false;
            if(!string.IsNullOrEmpty(room) && room!=source)
            { if(secondRoom!=null && secondRoom!=room) return Valid=false; secondRoom=room; }
            previous=rawHead; previousTime=time; return true;
        }
        public bool CanFinish(string room, MeshPoint rawHead, float floorHeightDifference, out float crossing)
        {
            var dx=rawHead.X-start.X;var dz=rawHead.Z-start.Z;crossing=(float)Math.Sqrt(dx*dx+dz*dz);
            return Valid && !string.IsNullOrEmpty(room) && secondRoom!=null && room!=source && room==secondRoom && crossing>=0.1f && crossing<=3 && Math.Abs(floorHeightDifference)<=0.15f;
        }
        private static bool Finite(MeshPoint p)=>!float.IsNaN(p.X)&&!float.IsNaN(p.Y)&&!float.IsNaN(p.Z)
            &&!float.IsInfinity(p.X)&&!float.IsInfinity(p.Y)&&!float.IsInfinity(p.Z);
    }
}
