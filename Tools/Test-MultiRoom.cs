using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using SpatialSense.RoomScan;
using SpatialSense.Persistence;

public static class MultiRoomTests
{
    private static void Assert(bool value,string reason) { if(!value)throw new Exception(reason); }
    private static MeshPoint P(float x,float y,float z)=>new MeshPoint(x,y,z);
    private static string Id()=>Guid.NewGuid().ToString("D");
    private static RoomNavigationArchive Archive(string room,string floor,bool wall=false)
    {
        var vertices=new List<float> {0,0,0,6,0,0,6,0,6,0,0,6};
        var indices=new List<int> {0,1,2,0,2,3};
        if(wall) { vertices.AddRange(new float[] {3,0,0,3,1.8f,0,3,1.8f,6,3,0,6});indices.AddRange(new[] {4,5,6,4,6,7}); }
        return new RoomNavigationArchive { roomId=room,floorId=floor,layoutId="layout-"+room,capturedUtc=DateTime.UtcNow.ToString("O"),vertices=vertices.ToArray(),
            triangles=indices.ToArray(),floorIndices=6,furniture=new bool[indices.Count/3],fromFloor=new float[] {1,0,0,0,0,1,0,0,0,0,1,0} };
    }
    private static RoomDoorEndpoint End(RoomGraphMap room,float x,float z)=>new RoomDoorEndpoint {roomId=room.RoomId,floorId=room.FloorId,layoutId=room.LayoutId,x=x,y=0,z=z};
    private static RoomDoorConnection Door(RoomGraphMap a,float ax,RoomGraphMap b,float bx)=>new RoomDoorConnection {
        id=Id(),taughtUtc=DateTime.UtcNow.ToString("O"),a=End(a,ax,3),b=End(b,bx,3),crossingMetres=1 };
    private static bool Plan(RoomGraphMap start,RoomGraphMap target,Dictionary<string,RoomGraphMap> rooms,List<RoomDoorConnection> links,out List<RoomRouteLeg> legs)
        =>RoomConnectionGraph.TryPlan(start.RoomId,P(1,0,3),target.RoomId,rooms,links,out legs,out _);
    public static int Main(string[] args)
    {
        try {
            var ar=Archive(Id(),Id());var br=Archive(Id(),Id());var cr=Archive(Id(),Id());
            var a=ar.Build(0.03f);var b=br.Build(0.03f);var c=cr.Build(0.03f);
            var rooms=new Dictionary<string,RoomGraphMap> {{a.RoomId,a},{b.RoomId,b},{c.RoomId,c}};
            var ab=Door(a,5,b,1);var bc=Door(b,5,c,1);var doors=new List<RoomDoorConnection> {ab,bc};
            Assert(Plan(a,b,rooms,new List<RoomDoorConnection>(),out _)==false,"Overlapping local meshes invented a connection.");
            Assert(Plan(a,b,rooms,doors,out var one) && one.Count==1 && one[0].Exit.roomId==a.RoomId && one[0].NextRoomId==b.RoomId,"Two-room doorway route failed.");
            Assert(Plan(a,c,rooms,doors,out var chain) && chain.Count==2 && chain[1].RoomId==b.RoomId && chain[1].NextRoomId==c.RoomId,"Multi-hop route did not check the intermediate room.");
            Assert(Plan(b,a,rooms,doors,out var reverse) && reverse.Count==1 && reverse[0].Exit==ab.b,"Doorway did not work in reverse.");
            var missing=new Dictionary<string,RoomGraphMap> {{a.RoomId,a}};
            Assert(!Plan(a,b,missing,doors,out _),"Missing destination map produced a path.");
            var wrong=Door(a,5,b,1);wrong.b.floorId=Id();
            Assert(!Plan(a,b,rooms,new List<RoomDoorConnection>{wrong},out _),"Wrong floor anchor reused an old doorway.");
            wrong=Door(a,5,b,1);wrong.b.layoutId="changed-layout";
            Assert(!Plan(a,b,rooms,new List<RoomDoorConnection>{wrong},out _),"Rescanned structural layout reused a stale doorway.");
            var split=Archive(b.RoomId,b.FloorId,true).Build(0.03f);rooms[b.RoomId]=split;
            Assert(!Plan(a,c,rooms,doors,out _),"Room graph crossed an impassable intermediate wall.");rooms[b.RoomId]=b;
            var far=Door(a,5,b,5);var near=Door(a,1.5f,b,1);
            Assert(Plan(a,b,rooms,new List<RoomDoorConnection>{far,near},out var shortest) && shortest[0].ConnectionId==near.id,"Door selection ignored walking distance.");
            a.Map.Cells[a.Map.Index(1.5f,3)]=1;
            Assert(Plan(a,b,rooms,new List<RoomDoorConnection>{far,near},out var detour) && detour[0].ConnectionId==far.id,"Blocked door approach did not choose an alternative.");
            a=ar.Build(0.03f);rooms[a.RoomId]=a;
            var rotated=ar.Build(0.03f);rotated.FromFloor=new float[] {1,0,0,0,0,0,1,0,0,1,0,0};
            var rotatedDoor=Door(rotated,5,b,1);rotatedDoor.a.y=3;rotatedDoor.a.z=0;rooms[a.RoomId]=rotated;
            Assert(Plan(rotated,b,rooms,new List<RoomDoorConnection>{rotatedDoor},out _),"Floor-plane coordinates were treated as world XZ.");rooms[a.RoomId]=a;
            var cancelled=new CancellationTokenSource();cancelled.Cancel();var cancellationObserved=false;
            try { RoomConnectionGraph.TryPlan(a.RoomId,P(1,0,3),b.RoomId,rooms,doors,out _,out _,cancelled.Token); }
            catch(OperationCanceledException) { cancellationObserved=true; }
            Assert(cancellationObserved,"Cancelled graph planning continued.");

            var walk=new DoorwayTraversal(a.RoomId,P(0,1.6f,0),0);
            for(var i=1;i<=20;i++) Assert(walk.Observe(i<=10?a.RoomId:b.RoomId,P(i*0.05f,1.6f,0),i*0.1,true),"Valid tracked doorway traversal failed.");
            Assert(walk.CanFinish(b.RoomId,P(1,1.6f,0),0,out var crossing) && Math.Abs(crossing-1)<0.001,"Doorway crossing proof failed.");
            Assert(!walk.CanFinish(b.RoomId,P(1,1.6f,0),0.3f,out _),"Stairs or different levels were accepted.");
            var jump=new DoorwayTraversal(a.RoomId,P(0,1.6f,0),0);
            Assert(!jump.Observe(b.RoomId,P(5,1.6f,0),0.1,true),"Tracking teleport created a doorway.");
            var lost=new DoorwayTraversal(a.RoomId,P(0,1.6f,0),0);
            Assert(!lost.Observe(b.RoomId,P(0.1f,1.6f,0),0.1,false),"Lost tracking created a doorway.");
            var third=new DoorwayTraversal(a.RoomId,P(0,1.6f,0),0);
            Assert(third.Observe(b.RoomId,P(0.1f,1.6f,0),0.1,true) && !third.Observe(c.RoomId,P(0.2f,1.6f,0),0.2,true),"One doorway traversal silently crossed a third room.");
            var loop=new DoorwayTraversal(a.RoomId,P(0,1.6f,0),0);
            Assert(loop.Observe(b.RoomId,P(0.1f,1.6f,0),0.1,true) && !loop.Observe(a.RoomId,P(0.2f,1.6f,0),0.2,true),"Returning through another doorway was accepted as one direct crossing.");
            var gap=new DoorwayTraversal(a.RoomId,P(0,1.6f,0),0);
            Assert(gap.Observe(null,P(0.1f,1.6f,0),0.1,true) && !gap.CanFinish(null,P(0.1f,1.6f,0),0,out _),"Unknown room was accepted as side 2.");
            var timeout=new DoorwayTraversal(a.RoomId,P(0,1.6f,0),0);
            Assert(!timeout.Observe(b.RoomId,P(0,1.6f,0),46,true),"Expired traversal remained valid.");

            var options=new JsonSerializerOptions {IncludeFields=true};
            var cache=JsonSerializer.Deserialize<RoomNavigationArchive>(JsonSerializer.Serialize(ar,options),options);
            var restored=cache.Build(0.03f);Assert(restored.Map.WalkableCount==a.Map.WalkableCount,"Room archive did not round-trip.");
            var connection=JsonSerializer.Deserialize<RoomDoorConnection>(JsonSerializer.Serialize(ab,options),options);
            Assert(connection.IsValid(),"Saved doorway did not round-trip.");
            var root=Path.Combine(Path.GetFullPath(args[0]),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            AtomicAnchorRecordStore.Write(Path.Combine(root,"connections.json"),JsonSerializer.Serialize(new RoomConnectionRecord {doors=doors},options));
            var saved=JsonSerializer.Deserialize<RoomConnectionRecord>(File.ReadAllText(Path.Combine(root,"connections.json")),options);
            Assert(Plan(a,c,rooms,saved.doors,out var savedChain) && savedChain.Count==2,"Persisted connections could not be used after reopening.");
            cache.vertices[0]=float.NaN;var rejected=false;
            try {cache.Build(0.03f);}catch(ArgumentException){rejected=true;}
            Assert(rejected,"Invalid archived geometry was accepted.");
            Assert(!new RoomDoorConnection {id=Id(),taughtUtc=DateTime.UtcNow.ToString("O"),a=ab.a,b=ab.a,crossingMetres=1}.IsValid(),"Self room link was accepted.");
            Console.WriteLine("PASS: independent room coordinates, taught-only connections, multi-hop/reverse routes, floor/layout invalidation, blocked doors/intermediate walls, distance-aware alternatives, floor-plane transforms, cancellation, tracked doorway proof and persistent archive/link recovery.");
            return 0;
        } catch(Exception error) {Console.Error.WriteLine(error);return 1;}
    }
}
