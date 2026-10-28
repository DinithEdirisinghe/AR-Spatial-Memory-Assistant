using System;
using System.Collections.Generic;
using System.Threading;
using SpatialSense.RoomScan;

public static class HomeMapTests
{
    private static void Check(bool value,string message){if(!value)throw new Exception(message);}
    private static void Floor(HomeMap map,float x,float z,float width,float length)
    {
        // Sensor-sized triangles, not one synthetic house-sized face.
        for(var a=x;a<x+width-.01f;a+=.3f)for(var b=z;b<z+length-.01f;b+=.3f)
        {
            var p=new MeshPoint(a,0,b);var q=new MeshPoint(a+.3f,0,b);var r=new MeshPoint(a,0,b+.3f);var s=new MeshPoint(a+.3f,0,b+.3f);
            map.Observe(new DepthSurfaceTriangle(p,r,q));map.Observe(new DepthSurfaceTriangle(q,r,s));
        }
    }
    private static List<MeshPoint> Route(HomeMap map,float x,float z,float tx,float tz)=>map.Route(new MeshPoint(x,0,z),new MeshPoint(tx,1,tz),CancellationToken.None);
    public static int Main()
    {
        try
        {
            var map=new HomeMap();Floor(map,0,0,3,3);Floor(map,4.5f,0,3,3);
            Check(Route(map,.375f,1.275f,6,1.275f).Count==0,"Disconnected rooms were linked without floor evidence.");
            Floor(map,3,1.05f,1.5f,.6f);
            var route=Route(map,.375f,1.275f,6,1.275f);
            Check(route.Count>1 && map.RouteClear(route),"Scanning a doorway did not automatically connect rooms.");
            foreach(var p in route){var key=HomeMap.Key(p.X,p.Z);Check(map.Walkable(key.Item1,key.Item2),"Route crossed unobserved or blocked floor.");}
            Check(Route(HomeMap.Restore(map.Snapshot()),.375f,1.275f,6,1.275f).Count==route.Count,"Restored home lost its cross-room path.");
            for(var z=1.05f;z<1.65f;z+=.15f)
            {
                map.Observe(new DepthSurfaceTriangle(new MeshPoint(3.75f,.15f,z),new MeshPoint(3.75f,.4f,z),new MeshPoint(3.75f,.15f,z+.15f)));
            }
            Check(Route(map,.375f,1.275f,6,1.275f).Count==0,"A thin vertical obstacle did not close the passage.");
            Floor(map,3,1.05f,1.5f,.6f);
            Check(Route(map,.375f,1.275f,6,1.275f).Count==0,"Seeing floor alone incorrectly cleared a solid obstacle.");
            var longHome=new HomeMap();Floor(longHome,-24,-.3f,48,.9f);
            var longRoute=Route(longHome,-23.625f,.075f,23,.075f);
            Check(longRoute.Count>1 && longRoute.Count<=4 && longHome.RouteClear(longRoute),"Long home route failed or retained a staircase of grid points.");
            Check(Route(longHome,-23.625f,4,23,.075f).Count==0,"Start was snapped across unknown space to a floor cell.");
            var missing=new HomeMap();
            missing.Observe(new DepthSurfaceTriangle(new MeshPoint(0,.7f,0),new MeshPoint(.3f,.7f,0),new MeshPoint(0,.7f,.3f)));
            Check(!missing.Walkable(0,0),"A table was mistaken for floor.");
            var diagonalCells=new List<HomeCell>();for(var i=0;i<20;i++)diagonalCells.Add(new HomeCell{x=i,z=i,floor=31});
            var diagonal=HomeMap.Restore(diagonalCells.ToArray());
            Check(Route(diagonal,.0375f,.0375f,1.4625f,1.4625f).Count==0,"Route cut diagonally through unobserved corners.");
            Check(!diagonal.CanWalkSegment(new MeshPoint(.0375f,0,.0375f),new MeshPoint(1.4625f,0,1.4625f)),"Smoothing cut an unknown diagonal corner.");
            var wallMap=new HomeMap();Floor(wallMap,0,0,3,3);
            for(var z=0f;z<3;z+=.15f)
                wallMap.Observe(new DepthSurfaceTriangle(new MeshPoint(1.5f,1,z),new MeshPoint(1.5f,1.3f,z),new MeshPoint(1.5f,1,z+.15f)));
            Check(Route(wallMap,.975f,1.275f,1.8f,1.275f).Count==0,"Item approach incorrectly stopped on the other side of a wall.");
            var record=new HomeRecord{anchorId=Guid.NewGuid().ToString(),savedUtc=DateTime.UtcNow.ToString("O"),floorVerified=true,
                cells=map.Snapshot(),surfaces=Array.Empty<HomeSurface>(),items=new[]{new HomeItem(),new HomeItem(),new HomeItem()}};
            Check(record.Valid(),"Valid checkpoint rejected.");
            record.cells=new[]{new HomeCell{x=0,z=0,floor=31},new HomeCell{x=0,z=0,floor=31}};
            Check(!record.Valid(),"Duplicate saved cells accepted.");
            record.cells=Array.Empty<HomeCell>();record.items[0].x=float.NaN;Check(!record.Valid(),"Non-finite saved item accepted.");
            record.items[0].x=0;record.anchorId="not-an-anchor";Check(!record.Valid(),"Invalid reference accepted.");
            var token=new CancellationTokenSource();token.Cancel();var cancelled=false;
            try{longHome.Route(new MeshPoint(-23.625f,0,.075f),new MeshPoint(23,0,.075f),token.Token);}catch(OperationCanceledException){cancelled=true;}
            Check(cancelled,"Long route ignored cancellation.");
            LiveUpdates();Migration();RouteAccuracy();
            Console.WriteLine("PASS: automatic observed-doorway connectivity, disconnected rooms, thin walls, persistent obstacles, 48 m extent, strict start coverage, table rejection, corner safety, checkpoint validation and route cancellation.");return 0;
        }
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
    private static void LiveUpdates()
    {
        var map=new HomeMap();Floor(map,0,0,3,3);var key=HomeMap.Key(1.2375f,1.2375f);var point=HomeMap.Centre(key.Item1,key.Item2);
        // The free-volume rays end on a real, already-scanned far wall.
        var far=point.X+1;
        map.Observe(new DepthSurfaceTriangle(new MeshPoint(far,.6f,point.Z-.03f),new MeshPoint(far,1.8f,point.Z-.03f),new MeshPoint(far,.6f,point.Z+.03f)));
        var wall=new Dictionary<(int,int),HomeObstacleSample>{{key,new HomeObstacleSample(.6f,.8f,false)}};
        var empty=Array.Empty<DepthObservationRay>();
        map.ObserveLive(empty,1,0,true,true,wall);Check(map.Walkable(key.Item1,key.Item2),"One noisy frame became a confirmed obstacle.");
        map.ObserveLive(empty,1,.1,true,true,wall);Check(map.Walkable(key.Item1,key.Item2),"The same acquisition was counted twice.");
        map.ObserveLive(empty,2,.25,true,true,wall);Check(!map.Walkable(key.Item1,key.Item2)&&map.DynamicCount==1,"A repeated new obstacle was not blocked.");
        map.ObserveLive(empty,3,20);Check(!map.Walkable(key.Item1,key.Item2),"Missing depth erased a confirmed obstacle.");
        var rays=new List<DepthObservationRay>();
        foreach(var h in new[]{.6f,.8f})rays.Add(new DepthObservationRay(new MeshPoint(point.X-1,h,point.Z),new MeshPoint(point.X+1,h,point.Z)));
        // Seeing floor by itself is insufficient; all old height bands must be positively empty.
        var floorRay=new DepthObservationRay(new MeshPoint(point.X,1.5f,point.Z-.3f),new MeshPoint(point.X,0,point.Z),new MeshPoint(0,1,0));
        for(var i=0;i<4;i++)map.ObserveLive(new[]{floorRay},4+i,21+i*.3);
        Check(!map.Walkable(key.Item1,key.Item2),"A floor glimpse cleared unverified old height bands.");
        rays.Add(floorRay);
        for(var i=0;i<5;i++)map.ObserveLive(rays,8+i,23+i*.3);
        Check(map.Walkable(key.Item1,key.Item2)&&map.ReopenedCount==1&&map.DynamicCount==0,"Positive multi-height and floor observations did not reopen moved furniture.");
        var restored=HomeMap.Restore(map.Snapshot());Check(restored.Walkable(key.Item1,key.Item2)&&restored.ReopenedCount==1,"Verified reopened space was lost after restoring.");
        map.ObserveLive(empty,13,25,true,true,wall);Check(!map.Walkable(key.Item1,key.Item2)&&map.ReopenedCount==0&&map.DynamicCount==1,"Positive reoccupation did not immediately reblock the old position.");
        var protectedMap=new HomeMap();Floor(protectedMap,0,0,3,3);
        protectedMap.ObserveLive(empty,1,0,true,true,new Dictionary<(int,int),HomeObstacleSample>{{key,new HomeObstacleSample(.6f,1.8f,true)}});
        for(var i=0;i<10;i++)protectedMap.ObserveLive(rays,2+i,1+i*.3);
        Check(!protectedMap.Walkable(key.Item1,key.Item2),"Free rays deleted a protected structural surface.");
        var occluded=new HomeMap();Floor(occluded,0,0,3,3);
        occluded.ObserveLive(empty,1,0,true,true,wall);occluded.ObserveLive(empty,2,.2,true,true,wall);
        var shortRay=new DepthObservationRay(new MeshPoint(point.X-1,.7f,point.Z),new MeshPoint(point.X-.2f,.7f,point.Z));
        for(var i=0;i<5;i++)occluded.ObserveLive(new[]{shortRay,floorRay},3+i,1+i*.3);
        Check(!occluded.Walkable(key.Item1,key.Item2),"An occluded ray stopping before old furniture cleared it.");
        // Unknown floor must remain unavailable even if the old volume appears empty.
        var unknown=HomeMap.Restore(new[]{new HomeCell{x=key.Item1,z=key.Item2,blocked=true,low=.6f,high=.8f}});
        for(var i=0;i<5;i++)unknown.ObserveLive(rays,i+1,i*.3);
        Check(!unknown.Walkable(key.Item1,key.Item2),"A missing floor was invented during obstacle clearing.");
        // Conflicting positive evidence must win within the exact same acquisition.
        for(var i=0;i<5;i++)occluded.ObserveLive(rays,8+i,3+i*.3,true,true,wall);
        Check(!occluded.Walkable(key.Item1,key.Item2),"A free-ray vote overruled occupied geometry from the same frame.");
        Console.WriteLine("PASS: temporal new obstacles, no duplicate votes, positive free-volume clearing, floor requirement, persistence, immediate reoccupation, permanent surfaces, occlusion and hit precedence.");
    }
    private static void Migration()
    {
        var legacy=new HomeRecord{version=1,anchorId=Guid.NewGuid().ToString(),savedUtc=DateTime.UtcNow.ToString("O"),floorVerified=true,
            cells=new[]{new HomeCell{x=0,z=0,floor=31}},items=new[]{new HomeItem{saved=true,x=.5f,y=.7f,z=.5f},new HomeItem(),new HomeItem()},
            surfaces=new[]{new HomeSurface{x=0,z=0,vertices=new[]{0f,0f,0f, .3f,0f,0f, 0f,0f,.3f, .3f,0f,.3f},indices=new[]{0,2,1,1,2,3}}}};
        Check(legacy.Valid(),"A real v1 checkpoint was rejected.");var map=HomeMap.FromRecord(legacy,CancellationToken.None);
        Check(map.Walkable(1,1)&&map.Walkable(2,2),"Legacy surfaces were not rebuilt at the finer resolution.");
        Check(legacy.items[0].saved&&legacy.items[0].x==.5f,"Migration changed the saved item.");
        legacy.surfaces=Array.Empty<HomeSurface>();var noSurface=HomeMap.FromRecord(legacy,CancellationToken.None);
        Check(noSurface.Count==0,"Migration fabricated fine floor from a coarse cell without surface geometry.");
        var fine=new HomeRecord{anchorId=legacy.anchorId,savedUtc=legacy.savedUtc,cells=map.Snapshot(),surfaces=Array.Empty<HomeSurface>(),items=legacy.items};
        Check(fine.Valid(),"New fine checkpoint rejected.");fine.cellSize=.15f;Check(!fine.Valid(),"A v2 checkpoint with the wrong cell size was accepted.");
        Console.WriteLine("PASS: v1 surface-based migration, retained items, no fabricated fine coverage, explicit checkpoint grid scale.");
    }
    private static void RouteAccuracy()
    {
        float Length(List<MeshPoint> route)
        {float length=0;for(var i=1;i<route.Count;i++){var dx=route[i].X-route[i-1].X;var dz=route[i].Z-route[i-1].Z;length+=(float)Math.Sqrt(dx*dx+dz*dz);}return length;}
        var map=new HomeMap();Floor(map,0,0,3,3);
        var direct=Route(map,.3375f,.3375f,2.7375f,2.7375f);
        Check(direct.Count==2 && map.RouteClear(direct),"Open diagonal route was not smoothed to a direct observed-floor segment.");
        var end=direct[direct.Count-1];Check(Math.Sqrt((end.X-2.7375f)*(end.X-2.7375f)+(end.Z-2.7375f)*(end.Z-2.7375f))<=.3,"Route stopped too far from an accessible item.");
        var straight=Route(map,.3375f,.9375f,2.7375f,.9375f);
        var barrier=new Dictionary<(int,int),HomeObstacleSample>();
        for(var z=0;z<22;z++)barrier[(20,z)]=new HomeObstacleSample(.2f,.8f,false);
        map.ObserveLive(Array.Empty<DepthObservationRay>(),1,0,true,true,barrier);
        map.ObserveLive(Array.Empty<DepthObservationRay>(),2,.2,true,true,barrier);
        Check(!map.RouteClear(straight),"A new obstacle did not revoke a route through its cells.");
        var detour=Route(map,.3375f,.9375f,2.7375f,.9375f);
        Check(detour.Count>=3 && map.RouteClear(detour) && Length(detour)>Length(straight)+.2,"Replanning did not find a valid route around a new obstacle.");
        // A finer grid resolves a narrow geometric corridor; increasing the
        // explicit margin must still close it rather than silently ignoring it.
        var narrow=new HomeMap();Floor(narrow,0,0,3,.3f);var cells=new List<HomeCell>(narrow.Snapshot());
        foreach(var c in cells)if(c.z==2){c.blocked=true;c.wall=true;c.low=.12f;c.high=1.8f;}
        for(var x=0;x<40;x++)cells.Add(new HomeCell{x=x,z=-1,blocked=true,wall=true,low=.12f,high=1.8f});
        narrow=HomeMap.Restore(cells.ToArray());
        var thin=narrow.Route(new MeshPoint(.3375f,0,.075f),new MeshPoint(2.7375f,1,.075f),CancellationToken.None,.015f);
        var wide=narrow.Route(new MeshPoint(.3375f,0,.075f),new MeshPoint(2.7375f,1,.075f),CancellationToken.None,.05f);
        Check(thin.Count>0 && wide.Count==0,"Fine corridor geometry or configurable obstacle margin was ignored.");
        var lowWall=new HomeMap();Floor(lowWall,0,0,3,3);
        for(var z=0f;z<3;z+=.15f)lowWall.Observe(new DepthSurfaceTriangle(new MeshPoint(1.5f,.2f,z),new MeshPoint(1.5f,.5f,z),new MeshPoint(1.5f,.2f,z+.15f)));
        Check(Route(lowWall,.975f,1.275f,1.8f,1.275f).Count==0,"A partially observed low wall allowed stopping in the wrong room.");
        var table=new HomeMap();Floor(table,0,0,3,3);
        for(var x=1.2f;x<1.8f;x+=.15f)for(var z=1.2f;z<1.8f;z+=.15f)
            table.Observe(new DepthSurfaceTriangle(new MeshPoint(x,.7f,z),new MeshPoint(x+.15f,.7f,z),new MeshPoint(x,.7f,z+.15f)));
        var tableRoute=table.Route(new MeshPoint(.3375f,0,.3375f),new MeshPoint(1.5f,.8f,1.5f),CancellationToken.None);
        Check(tableRoute.Count>0&&table.RouteClear(tableRoute),"Item-support approach rules prevented reaching the edge of a table.");
        Console.WriteLine("PASS: direct diagonal smoothing, close reachable item endpoint, blocked-route revocation, obstacle detour and fine-grid clearance.");
    }
}
