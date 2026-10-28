using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SpatialSense.Persistence;
using UnityEngine;

namespace SpatialSense.RoomScan
{
    public sealed class MultiRoomNavigation : MonoBehaviour
    {
        private LiveRoomScan app;
        private OVRCameraRig rig;
        private RoomConnectionRecord record=new RoomConnectionRecord();
        private readonly Dictionary<string,RoomNavigationArchive> archives=new Dictionary<string,RoomNavigationArchive>();
        private Dictionary<string,RoomGraphMap> maps=new Dictionary<string,RoomGraphMap>();
        private string folder,linksPath,journeyOwner,targetItem,targetRoom,legRoom;
        private int catalogRevision=-1,journeyRevision,teachingRevision,selectedLink;
        private float catalogMargin=-1,journeyMargin,nextTick,nextPlan,forgetUntil;
        private double gapSince=-1;
        private string locatedRoom;
        private double locatedAt;
        private DoorwayTraversal traversal;
        private RoomDoorEndpoint firstSide;
        private float firstFloorRawY;
        private CancellationTokenSource planning;
        private bool finding,planningRoute;
        private readonly HashSet<string> avoidedDoors=new HashSet<string>();
        private string activeConnection;
        public bool IsBuilding { get; private set; }
        public bool IsFinding=>finding;
        public bool IsTeaching=>traversal!=null;
        public bool CanAvoidDoorway=>finding && activeConnection!=null && app.Room.Ready && app.Room.RoomUuid.ToString("D")==legRoom;
        public bool IsActive=>IsFinding || IsTeaching;
        public string Status { get; private set; }="Load Meta rooms, then link the doorway between them once.";
        public string Summary=>$"Current: {Label(app.Room.RoomUuid.ToString("D"))} | remembered maps {maps.Count} | doorways {record.doors.Count}\n"+Status;
        public string SelectedConnection=>record.doors.Count==0?"No linked doorways":Label(record.doors[selectedLink%record.doors.Count].a.roomId)+" <-> "+Label(record.doors[selectedLink%record.doors.Count].b.roomId);
        public bool ForgetArmed=>Time.unscaledTime<forgetUntil && record.doors.Count>0;

        public async void Configure(LiveRoomScan source,OVRCameraRig cameraRig)
        {
            app=source;rig=cameraRig;folder=Path.Combine(Application.persistentDataPath,"room-navigation"); linksPath=Path.Combine(folder,"connections-v1.json");
            try
            {
                Directory.CreateDirectory(folder); LoadLinks(); IsBuilding=true;
                var margin=app.Navigation.Clearance;
                var loaded=await Task.Run(()=>
                {
                    var result=new List<(RoomNavigationArchive archive,RoomGraphMap map)>();
                    foreach(var file in Directory.GetFiles(folder,"room-*.json"))
                    {
                        try { if(new FileInfo(file).Length>4*1024*1024) continue; var a=JsonUtility.FromJson<RoomNavigationArchive>(File.ReadAllText(file)); result.Add((a,a.Build(margin))); }
                        catch(Exception) { /* Preserve invalid cache files; loaded SDK data can replace them. */ }
                    }
                    return result;
                });
                if(this==null)return;
                foreach(var a in loaded) { archives[a.archive.roomId]=a.archive; maps[a.archive.roomId]=a.map; }
                catalogMargin=margin;
            }
            catch(Exception error) { Status="Room navigation storage could not load: "+error.Message;Debug.LogWarning(error); }
            finally { if(this!=null) IsBuilding=false; }
        }
        private void LoadLinks()
        {
            if(!File.Exists(linksPath))return;
            try
            {
                var loaded=JsonUtility.FromJson<RoomConnectionRecord>(File.ReadAllText(linksPath));
                if(loaded==null || loaded.version!=1 || loaded.doors==null || loaded.rooms==null || loaded.doors.Count>64 || loaded.rooms.Count>128)
                    throw new InvalidDataException("Unsupported room connections.");
                var ids=new HashSet<string>();
                foreach(var door in loaded.doors) if(door==null || !door.IsValid() || !ids.Add(door.id)) throw new InvalidDataException("Invalid doorway.");
                ids.Clear();
                foreach(var alias in loaded.rooms) if(alias==null || !Guid.TryParse(alias.roomId,out _) || string.IsNullOrEmpty(alias.label)
                    || alias.label.Length>32 || !ids.Add(alias.roomId))throw new InvalidDataException("Invalid room labels.");
                record=loaded;
            }
            catch(Exception error)
            {
                File.Copy(linksPath,linksPath+"."+Guid.NewGuid().ToString("N")+".unreadable");
                Status="Old doorway file was unreadable; a copy was kept. Teach the doorways again.";Debug.LogWarning(error);
            }
        }
        private void SaveLinks()=>AtomicAnchorRecordStore.Write(linksPath,JsonUtility.ToJson(record,true));
        public string Label(string id)
        {
            if(string.IsNullOrEmpty(id) || id==Guid.Empty.ToString("D"))return "room not located";
            foreach(var alias in record.rooms) if(alias!=null && alias.roomId==id)return alias.label;
            return "room "+id.Substring(0,Math.Min(6,id.Length));
        }
        private void EnsureLabels(IEnumerable<string> ids)
        {
            var changed=false;
            foreach(var id in ids)
            {
                if(record.rooms.Exists(alias=>alias!=null && alias.roomId==id))continue;
                if(record.rooms.Count>=128)continue;
                record.rooms.Add(new RoomAlias { roomId=id,label="Room "+(record.rooms.Count+1) });changed=true;
            }
            if(changed)SaveLinks();
        }
        private async void RefreshCatalog()
        {
            if(IsBuilding || !app.Room.CatalogReady)return;
            IsBuilding=true;var revision=app.Room.DataRevision;var margin=app.Navigation.Clearance;
            try
            {
                var fresh=app.Room.CaptureRoomMaps();
                foreach(var source in fresh)
                {
                    var vertices=new float[source.Points.Length*3];
                    for(var i=0;i<source.Points.Length;i++) { vertices[i*3]=source.Points[i].X;vertices[i*3+1]=source.Points[i].Y;vertices[i*3+2]=source.Points[i].Z; }
                    archives[source.RoomId]=new RoomNavigationArchive { roomId=source.RoomId,floorId=source.FloorId,layoutId=source.LayoutId,capturedUtc=DateTime.UtcNow.ToString("O"),
                        vertices=vertices,triangles=source.Triangles,furniture=source.Furniture,floorIndices=source.FloorIndices,fromFloor=source.FromFloor };
                }
                EnsureLabels(archives.Keys);
                var inputs=new List<RoomNavigationArchive>(archives.Values);var cacheFolder=folder;
                var result=await Task.Run(()=>
                {
                    var built=new Dictionary<string,RoomGraphMap>();
                    foreach(var a in inputs)
                    {
                        try { built[a.roomId]=a.Build(margin); AtomicAnchorRecordStore.Write(Path.Combine(cacheFolder,"room-"+a.roomId+".json"),JsonUtility.ToJson(a)); }
                        catch(Exception error) { Debug.LogWarning("[SpatialSense rooms] "+error.Message); }
                    }
                    return built;
                });
                if(this==null || revision!=app.Room.DataRevision)return;
                maps=result;catalogRevision=revision;catalogMargin=margin;
                if(!IsActive)Status=$"Remembering {maps.Count} room maps. Link a doorway once to travel between rooms.";
            }
            catch(Exception error) { if(this!=null) { catalogRevision=revision;Status="Could not prepare the room maps: "+error.Message;Debug.LogWarning(error); } }
            finally { if(this!=null)IsBuilding=false; }
        }
        private MeshPoint RawHead()
        { var p=rig.trackingSpace.InverseTransformPoint(rig.centerEyeAnchor.position);return new MeshPoint(p.x,p.y,p.z); }
        private bool Tracked=>OVRManager.hasVrFocus && OVRPlugin.GetNodePositionTracked(OVRPlugin.Node.EyeCenter);
        private void Update()
        {
            if(app==null)return;
            if(app.Home!=null && app.Home.Active)return;
            if(IsActive && OVRInput.GetDown(OVRInput.Button.PrimaryThumbstick,OVRInput.Controller.RTouch)) { Cancel("Room action cancelled.");return; }
            if(IsActive && (!Tracked || app.UseAppScan || (app.Evaluation!=null && app.Evaluation.IsLocked)))
            { Cancel("Room guidance paused because tracking, scan mode or evaluation context changed. Choose Find again when located.");return; }
            if(Time.unscaledTime<nextTick)return;nextTick=Time.unscaledTime+0.1f;
            var now=Time.realtimeSinceStartupAsDouble;
            if(IsTeaching)
            {
                if(teachingRevision!=app.Room.TrackingRevision || !traversal.Observe(app.Room.Ready?app.Room.RoomUuid.ToString("D"):null,RawHead(),now,Tracked))
                    Cancel("Doorway walk invalidated. Keep tracking, walk directly through one same-level doorway, and try again.");
            }
            if(finding && (journeyRevision!=app.Room.TrackingRevision || Mathf.Abs(journeyMargin-app.Navigation.Clearance)>0.0001f))
            { Cancel("Tracking or margin changed. Saved items and links are kept; choose Find again."); }
            if(!IsBuilding && app.Room.CatalogReady && (catalogRevision!=app.Room.DataRevision || Mathf.Abs(catalogMargin-app.Navigation.Clearance)>0.0001f))RefreshCatalog();
            if(!finding)return;
            if(app.Items.SelectedId!=targetItem) { Cancel("Selected item changed.");return; }
            if(!app.Room.Ready)
            {
                locatedRoom=null;
                if(gapSince<0)gapSince=now;
                if(app.Navigation.RouteOwner==journeyOwner)app.Navigation.ClearRoute();
                Status="Waiting for the next room to locate. Cross only the linked doorway and look around; no path is drawn through an unknown area.";
                if(now-gapSince>25)Cancel("The next room did not locate. Load its saved Meta scan, then choose Find again.");
                return;
            }
            gapSince=-1;
            var current=app.Room.RoomUuid.ToString("D");
            if(locatedRoom!=current) { locatedRoom=current;locatedAt=now; }
            if(now-locatedAt<0.3) { Status="Waiting for "+Label(current)+" tracking to settle...";return; }
            if(current==targetRoom)
            {
                if(!app.Navigation.EvaluationReady || app.IsWorking)return;
                var item=targetItem;finding=false;planning?.Cancel();
                if(app.Navigation.RouteOwner==journeyOwner)app.Navigation.ClearRoute();
                Status="Arrived in "+Label(targetRoom)+". Locating the saved item's anchor...";
                if(app.Items.SelectedId==item)app.Items.Find();return;
            }
            if(IsBuilding || !app.Navigation.EvaluationReady) { Status="Preparing the current room's walking map...";return; }
            if(current!=legRoom || app.Navigation.RouteOwner!=journeyOwner || !app.Navigation.RouteAvailable)
            { if(!planningRoute && Time.unscaledTime>=nextPlan)PlanNextRoom(); }
            else
            {
                var distance=Vector2.Distance(new Vector2(rig.centerEyeAnchor.position.x,rig.centerEyeAnchor.position.z),new Vector2(app.Navigation.StandingPosition.x,app.Navigation.StandingPosition.z));
                Status=distance<0.45f?"At the linked doorway. Check that it is open, then cross to "+Label(nextRoom)+" and look around. Guidance resumes after it locates."
                    :"Follow the blue path to the doorway for "+Label(nextRoom)+". Item saved in "+Label(targetRoom)+".";
            }
        }
        private string nextRoom;
        public bool BeginFind(string itemId,string destinationRoom)
        {
            Cancel();
            if(app.UseAppScan) { Status="Cross-room guidance currently uses Meta room maps. Select Meta room scan first; app scanning remains available locally.";return false; }
            if(app.Evaluation!=null && app.Evaluation.IsLocked) { Status="Finish the single-room evaluation trial before cross-room guidance.";return false; }
            if(!Guid.TryParse(destinationRoom,out var id) || id==Guid.Empty) { Status="Saved item has no valid room identity.";return false; }
            targetItem=itemId;targetRoom=id.ToString("D");journeyOwner="doorway:"+Guid.NewGuid().ToString("N");journeyRevision=app.Room.TrackingRevision;
            journeyMargin=app.Navigation.Clearance;finding=true;legRoom=null;locatedRoom=null;nextPlan=0;gapSince=-1;
            avoidedDoors.Clear();activeConnection=null;
            Status="Finding linked doorways to "+Label(targetRoom)+"...";return true;
        }
        private async void PlanNextRoom()
        {
            nextPlan=Time.unscaledTime+1f;
            if(!app.Navigation.TryCaptureRoomRouteMap(out var current,out var head))return;
            maps[current.RoomId]=current;
            var rooms=new Dictionary<string,RoomGraphMap>(maps);var links=record.doors.FindAll(door=>!avoidedDoors.Contains(door.id));var owner=journeyOwner;var revision=app.Room.DataRevision;var destination=targetRoom;
            planning?.Cancel();var cancellation=new CancellationTokenSource();planning=cancellation;planningRoute=true;
            try
            {
                var result=await Task.Run(()=> { var success=RoomConnectionGraph.TryPlan(current.RoomId,head,destination,rooms,links,out var legs,out var reason,cancellation.Token);return(success,legs,reason); },cancellation.Token);
                if(this==null || !finding || owner!=journeyOwner || revision!=app.Room.DataRevision || !app.Room.Ready || app.Room.RoomUuid.ToString("D")!=current.RoomId)return;
                if(!result.success || result.legs.Count==0) { Status=result.reason;return; }
                var leg=result.legs[0];
                if(avoidedDoors.Contains(leg.ConnectionId))return;
                if(!app.Navigation.SetDoorwayDestination(owner,leg.Exit,out var blocked)) { Status="Doorway approach unavailable: "+blocked;return; }
                legRoom=leg.RoomId;nextRoom=leg.NextRoomId;activeConnection=leg.ConnectionId;Status="Follow the doorway route to "+Label(nextRoom)+".";
            }
            catch(OperationCanceledException) { }
            catch(Exception error) { if(this!=null) { Status="Could not plan the room journey: "+error.Message;Debug.LogWarning(error); } }
            finally { if(planning==cancellation) { planning=null;planningRoute=false; } cancellation.Dispose(); }
        }
        public void Cancel(string message=null)
        {
            planning?.Cancel();finding=false;traversal=null;firstSide=null;forgetUntil=0;
            if(app!=null && app.Navigation!=null && !string.IsNullOrEmpty(journeyOwner) && app.Navigation.RouteOwner==journeyOwner)app.Navigation.ClearRoute();
            journeyOwner=null;legRoom=null;gapSince=-1;
            if(message!=null)Status=message;
        }
        public void AvoidCurrentDoorway()
        {
            if(!CanAvoidDoorway)return;
            avoidedDoors.Add(activeConnection);planning?.Cancel();
            if(app.Navigation.RouteOwner==journeyOwner)app.Navigation.ClearRoute();
            legRoom=null;nextPlan=0;Status="Doorway excluded for this search. Looking for another linked route...";
        }
        public void MarkDoorway()
        {
            if(app.UseAppScan || !Tracked || (app.Evaluation!=null && app.Evaluation.IsLocked)) { Status="Use Meta room scan outside evaluation, with headset tracking active.";return; }
            if(!app.Navigation.TryCaptureDoorPoint(out var world) || !app.Room.TryCaptureFloorPoint(world,out var endpoint))
            { Status="Stand on an ordinary green floor cell just inside the doorway, wait for the room map, then mark this side.";return; }
            var rawFloor=rig.trackingSpace.InverseTransformPoint(world);
            if(!IsTeaching)
            {
                app.Items.Cancel();Cancel();firstSide=endpoint;teachingRevision=app.Room.TrackingRevision;firstFloorRawY=rawFloor.y;
                traversal=new DoorwayTraversal(endpoint.roomId,RawHead(),Time.realtimeSinceStartupAsDouble);
                Status="Side 1 marked in "+Label(endpoint.roomId)+". Walk directly through the doorway, stand on green floor in the next room, then finish side 2.";return;
            }
            traversal.Observe(endpoint.roomId,RawHead(),Time.realtimeSinceStartupAsDouble,Tracked);
            if(!traversal.CanFinish(endpoint.roomId,RawHead(),rawFloor.y-firstFloorRawY,out var crossing))
            { Status="Finish in a different located room across this same-level doorway, within 3 m of side 1. Otherwise cancel and retry.";return; }
            if(maps.TryGetValue(firstSide.roomId,out var original) && (original.FloorId!=firstSide.floorId || original.LayoutId!=firstSide.layoutId))
            { Cancel("The first room's layout changed during teaching. Link the doorway again after maps finish loading.");return; }
            if(record.doors.Count>=64) { Status="Doorway limit reached. Forget an unused connection before teaching another.";return; }
            var link=new RoomDoorConnection { id=Guid.NewGuid().ToString("D"),taughtUtc=DateTime.UtcNow.ToString("O"),a=firstSide,b=endpoint,crossingMetres=crossing };
            if(!link.IsValid()) { Cancel("Invalid doorway points; try again.");return; }
            var previous=record.doors.FindIndex(d=>(Near(d.a,link.a)&&Near(d.b,link.b)) || (Near(d.a,link.b)&&Near(d.b,link.a)));
            var old=previous>=0?record.doors[previous]:null;
            if(previous>=0) { link.id=old.id;record.doors[previous]=link; }else record.doors.Add(link);
            try { SaveLinks();selectedLink=previous>=0?previous:record.doors.Count-1;Cancel("Doorway saved: "+Label(link.a.roomId)+" <-> "+Label(link.b.roomId)+". Find can now use this connection."); }
            catch(Exception error) { if(previous>=0)record.doors[previous]=old;else record.doors.Remove(link);Cancel("Doorway save failed; previous links kept. "+error.Message); }
        }
        private static bool Near(RoomDoorEndpoint a,RoomDoorEndpoint b)
        { var dx=a.x-b.x;var dy=a.y-b.y;var dz=a.z-b.z;return a.roomId==b.roomId && a.floorId==b.floorId && dx*dx+dy*dy+dz*dz<0.16f; }
        public void CycleConnection() { if(record.doors.Count>0)selectedLink=(selectedLink+1)%record.doors.Count;forgetUntil=0; }
        public void ForgetConnection()
        {
            if(record.doors.Count==0)return;
            if(!ForgetArmed) { forgetUntil=Time.unscaledTime+5;Status="Press Confirm forget within five seconds to remove only this doorway.";return; }
            var index=selectedLink%record.doors.Count;var old=record.doors[index];record.doors.RemoveAt(index);
            try { SaveLinks();Cancel("Doorway removed. Saved items and room scans are kept.");selectedLink=0; }
            catch(Exception error) { record.doors.Insert(index,old);Status="Doorway could not be removed: "+error.Message; }
        }
        private void OnApplicationPause(bool paused) { if(paused && IsActive)Cancel("Room journey interrupted. Locate the room and choose Find again."); }
        private void OnDestroy() { planning?.Cancel(); }
    }
}
