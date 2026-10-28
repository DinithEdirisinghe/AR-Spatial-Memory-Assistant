using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SpatialSense.Persistence;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace SpatialSense.RoomScan
{
    // Owns a continuous, single-floor workspace. Native-room and experimental
    // scan files are untouched. All geometry and items share a persisted anchor.
    public sealed class HomeScanner : MonoBehaviour
    {
        private LiveRoomScan app;
        private OVRCameraRig rig;
        private Material surfaceMaterial, floorMaterial, blockedMaterial, lineMaterial,pathMaterial,liveMaterial,freedMaterial;
        private OVRSpatialAnchor anchor;
        private OVRDisplay display;
        private HomeMap map = new HomeMap();
        private HomeDepthCapture dense = new HomeDepthCapture();
        private readonly Queue<DepthSurfaceTriangle> denseTriangles = new Queue<DepthSurfaceTriangle>();
        private HomeDepthFrame activeDepthFrame;
        private Dictionary<(int,int),HomeObstacleSample> surfaceHits=new Dictionary<(int,int),HomeObstacleSample>();
        private List<MeshPoint> activeRoute=new List<MeshPoint>();
        private long liveBatch;
        private float nextDense,nextTargeted,nextDepthEnsure,lastDense=-10;
        private int rayOffset,lastPlannedRevision=-1;
        private Vector3 lastPlannedHead;
        private Vector3 bodyHead,bodyRight,bodyLeft;
        private bool rightTracked,leftTracked;
        private int checkedRevision=-1;
        public bool DynamicEnabled {get;private set;}=true;
        public bool FurnitureEnabled {get;private set;}=true;
        public float Clearance {get;private set;}=.015f;
        private int denseFailures;
        private HomeItem[] items = {new HomeItem(),new HomeItem(),new HomeItem()};
        private readonly Dictionary<(int,int),Chunk> chunks = new Dictionary<(int,int),Chunk>();
        private readonly HashSet<(int,int)> changedChunks = new HashSet<(int,int)>();
        private sealed class Chunk
        {
            public SpatialMeshAccumulator data=new SpatialMeshAccumulator();public Mesh mesh;public GameObject go;
            public HashSet<(int,int,int,int)> samples=new HashSet<(int,int,int,int)>();
        }
        private Mesh floorMesh,blockedMesh,liveMesh,freedMesh;
        private GameObject root,marker;
        private LineRenderer line;
        private string path,saveError,notice="Start scanning your home. No room setup or doorway linking needed.";
        private bool invalid,dirty,floorVerified,registering,gripReleased,finding,planning,saveBusy,saveAgain,capacityReached;
#if UNITY_ANDROID && !UNITY_EDITOR
        private bool permissionPending;
#endif
        private int generation,cursor,revision,selected,totalVertices,totalIndices,floorVotes,lastFloorView=-1;
        private float nextSample,nextVisual,nextSave,nextPlan,lastHit,lastPoseAt,firstFloorAt;
        private Vector3 lastPose;
        private bool havePose;
        private CancellationTokenSource routeCancel;
        public bool Active { get; private set; } = true;
        public bool Scanning { get; private set; }
        public bool Busy { get; private set; }
        public bool Ready => anchor!=null && anchor.Created && anchor.Localized && anchor.IsTracked && !invalid
            && OVRPlugin.GetNodePositionTracked(OVRPlugin.Node.EyeCenter);
        public bool HasSaved => File.Exists(path);
        public bool HasWorkspace => anchor!=null;
        public bool HasItem => items[selected].saved;
        public bool CanFind => Ready && HasItem && floorVerified;
        public bool CanConfirm => Ready && finding && activeRoute.Count>0 && HasItem
            && Vector2.Distance(new Vector2(lastPose.x,lastPose.z),new Vector2(items[selected].x,items[selected].z))<=1.35f
            && Vector2.Distance(new Vector2(lastPose.x,lastPose.z),new Vector2(activeRoute[activeRoute.Count-1].X,activeRoute[activeRoute.Count-1].Z))<=.65f;
        public string UpdateSummary=>$"Live obstacles {(DynamicEnabled?"ON":"OFF")} {map.DynamicCount} | reopened {map.ReopenedCount} | floor 7.5 cm"
            +(map.EvidenceAtCapacity?" | update checks full":"");
        public bool ShowSurfaces { get; set; } = true;
        public bool ShowFloor { get; set; } = true;
        public string Name => selected==0?"Keys":selected==1?"Wallet":"Remote";
        public string Status => notice;
        public string Summary => notice+"\n"+(saveError??(saveBusy?"Saving...":dirty?"Progress waiting to save":"Progress saved / unchanged"))
            +$" | {dense.Mode}\n"+UpdateSummary;
        public string ItemLabel(int slot) => (slot==0?"Keys":slot==1?"Wallet":"Remote")+(selected==slot?" [selected]":"")+"\n"+(items[slot].saved?"Saved in home":"Not registered");
        public void Configure(LiveRoomScan application,OVRCameraRig cameraRig,Material material)
        {
            app=application;rig=cameraRig;surfaceMaterial=material;
            floorMaterial=Tint(material,new Color(.1f,.85f,.3f,.7f));blockedMaterial=Tint(material,new Color(1f,.15f,.1f,.7f));
            lineMaterial=Tint(material,new Color(.1f,.5f,1f,1));
            var routeAsset=Resources.Load<Material>("HomeRouteOcclusion");
            pathMaterial=routeAsset!=null && routeAsset.shader!=null && routeAsset.shader.isSupported?new Material(routeAsset):Tint(material,new Color(.1f,.5f,1f,1));
            if(routeAsset==null)Debug.LogWarning("[Home route] Occlusion material missing; normal route rendering is used.");
            liveMaterial=Tint(material,new Color(1f,.1f,.75f,.8f));freedMaterial=Tint(material,new Color(.05f,.65f,1f,.85f));
            path=Path.Combine(Application.persistentDataPath,"home-scan","home-v1.json");
            display=OVRManager.display;if(display!=null)display.RecenteredPose+=Invalidate;
            OVRManager.TrackingLost+=Invalidate;
            if(HasSaved)notice="Welcome back. Start scanning your home will restore and continue your saved map.";
        }
        private static Material Tint(Material source,Color color){var m=new Material(source);m.SetColor("_BaseColor",color);return m;}
        public void Enter()
        {
            if(app.IsWorking || Busy || saveBusy)return;
            if(app.Evaluation.IsLocked){notice="Finish or cancel the current trial before switching to Home.";return;}
            app.PauseCapture();app.Items.Cancel();app.RoomNavigation.Cancel("Home scan selected.");app.Navigation.SourceChanged();
            Active=true;app.Room.SetHomeMode(true);app.Room.MeshVisible=false;
            notice=HasWorkspace?"Home mode ready. Continue scanning, or find a saved home item.":"Start scanning your home to capture connected spaces.";
        }
        public void Leave()
        {
            if(Busy || saveBusy)return;
            Pause();CancelItem();Active=false;app.Room.SetHomeMode(false);invalid=anchor!=null;havePose=false;
            if(root!=null)root.SetActive(false);
        }
        public void StartOrContinue()
        {
            if(Busy || !Active)return;
            if(capacityReached || map.Count>=HomeMap.MaxCells)
            {notice="This scan is full. Find saved items, or start a different home scan.";return;}
#if UNITY_ANDROID && !UNITY_EDITOR
            if(!Permission.HasUserAuthorizedPermission(OVRPermissionsRequester.ScenePermission))
            {
                permissionPending=true;Permission.RequestUserPermission(OVRPermissionsRequester.ScenePermission);
                notice="Allow spatial-data access to begin. Scanning will start after permission is granted.";return;
            }
#endif
            if(!app.EnableDepthForProbe()){notice="Live depth unavailable. Check the Quest spatial-data permission and OpenXR setup.";return;}
            dense.Enable(rig.trackingSpace);
            if(!Ready){if(HasSaved)Load(true);else if(!HasWorkspace)NewHome();else notice="Tracking needs recovery. Pause here and look around the starting area, then retry.";return;}
            CancelItem();Scanning=true;nextSave=Time.unscaledTime+10;lastHit=Time.unscaledTime;havePose=false;
            notice=floorVerified?"Scanning your home. Walk slowly; look at floor, doorways and furniture. B finishes.":"Look down at the floor briefly so I can verify its height.";
            app.CloseMenu();
        }
        public void Pause()
        {
            Scanning=false;
#if UNITY_ANDROID && !UNITY_EDITOR
            permissionPending=false;
#endif
            ClearDepthBatch();
            dense.Cancel();
            if(!Busy){notice="Scan paused. Progress saves automatically. Continue scanning or use Items.";Save();}
        }
        public async void NewHome()
        {
            if(Busy || saveBusy || !Active)return;
            if(!app.EnableDepthForProbe()) {notice="Allow spatial-data access, then start scanning.";return;}
            dense.Enable(rig.trackingSpace);
            if(OVRManager.instance==null || (OVRManager.instance.trackingOriginType!=OVRManager.TrackingOrigin.FloorLevel
                && OVRManager.instance.trackingOriginType!=OVRManager.TrackingOrigin.Stage))
            {notice="Home scanning needs Floor Level tracking. Set OVR Manager > Tracking Origin Type to Floor Level, then retry.";return;}
            Busy=true;Scanning=false;CancelItem();var operation=++generation;OVRSpatialAnchor candidate=null;
            try
            {
                notice="Preparing a saved home reference. Keep the headset on and look around.";
                var head=rig.centerEyeAnchor.position;var floor=rig.trackingSpace.TransformPoint(Vector3.zero).y;
                var go=new GameObject("Home spatial reference");go.transform.SetPositionAndRotation(new Vector3(head.x,floor,head.z),Quaternion.identity);
                candidate=go.AddComponent<OVRSpatialAnchor>();
                if(!await WaitTracked(candidate,operation))throw new IOException("Could not establish home tracking. Look around and retry.");
                var saved=await candidate.SaveAnchorAsync();
                if(!saved.Success)throw new IOException("Could not save the home reference: "+saved.Status);
                if(this==null || operation!=generation) return;
                Release();anchor=candidate;candidate=null;invalid=false;
                map=new HomeMap();items=new[]{new HomeItem(),new HomeItem(),new HomeItem()};
                floorVerified=false;floorVotes=0;lastFloorView=-1;cursor=0;totalVertices=totalIndices=0;capacityReached=false;saveError=null;
                CreateVisuals();dirty=true;revision++;havePose=false;Scanning=true;
                nextSave=Time.unscaledTime+2;lastHit=Time.unscaledTime;
                notice="Look down at nearby floor briefly. Then walk through your home, looking around each doorway.";app.CloseMenu();
            }
            catch(Exception e){notice=e.Message;Debug.LogWarning("[Home scan] "+e);}
            finally {if(candidate!=null)Destroy(candidate.gameObject);Busy=false;}
        }
        private async Task<bool> WaitTracked(OVRSpatialAnchor value,int operation)
        {
            var until=Time.unscaledTime+20;var since=-1f;
            while(this!=null && operation==generation && value!=null && Time.unscaledTime<until)
            {
                if(value.Created && value.Localized && value.IsTracked && OVRManager.hasVrFocus && OVRPlugin.GetNodePositionTracked(OVRPlugin.Node.EyeCenter))
                {if(since<0)since=Time.unscaledTime;if(Time.unscaledTime-since>=1.5f)return true;}
                else since=-1;
                await Task.Yield();
            }
            return false;
        }
        private void Update()
        {
            if(!Active)return;
#if UNITY_ANDROID && !UNITY_EDITOR
            if(permissionPending && OVRManager.hasVrFocus && Permission.HasUserAuthorizedPermission(OVRPermissionsRequester.ScenePermission))
            {permissionPending=false;StartOrContinue();}
#endif
            if(root!=null)root.SetActive(Ready && OVRManager.hasVrFocus);
            if(!Ready || !OVRManager.hasVrFocus)
            {
                if(Scanning || finding || registering)Invalidate();
                return;
            }
            var local=anchor.transform.InverseTransformPoint(rig.centerEyeAnchor.position);
            var now=Time.unscaledTime;
            if(havePose && (now-lastPoseAt>1 || Vector3.Distance(local,lastPose)>Mathf.Max(.35f,(now-lastPoseAt)*3f)))
            {Invalidate();return;}
            havePose=true;lastPose=local;lastPoseAt=now;
            bodyHead=local;rightTracked=OVRInput.GetControllerPositionTracked(OVRInput.Controller.RTouch);
            leftTracked=OVRInput.GetControllerPositionTracked(OVRInput.Controller.LTouch);
            bodyRight=anchor.transform.InverseTransformPoint(rig.rightControllerAnchor.position);
            bodyLeft=anchor.transform.InverseTransformPoint(rig.leftControllerAnchor.position);
            if(!Busy && (Scanning || DynamicEnabled || FurnitureEnabled))
            {
                if(now>=nextDepthEnsure){nextDepthEnsure=now+2;app.EnableDepthForProbe();dense.Enable(rig.trackingSpace);}
                var frame=dense.Poll(generation);
                if(frame!=null && ValidateDense(frame.Samples))
                {
                    lastDense=now;activeDepthFrame=frame;surfaceHits.Clear();var size=HomeDepthCapture.Size;
                    var batch=DepthPatchSampler.Capture(size,size,0,(size-1)*(size-1),.3f,100,(x,y)=>frame.Samples[x+y*size],()=>0);
                    foreach(var triangle in batch.Triangles)denseTriangles.Enqueue(triangle);
                }
                if(denseTriangles.Count>0)
                {
                    var triangles=new List<DepthSurfaceTriangle>();
                    while(denseTriangles.Count>0 && triangles.Count<96)triangles.Add(denseTriangles.Dequeue());
                    AcceptTriangles(triangles,surfaceHits);
                }
                if(denseTriangles.Count==0)
                {
                    if(activeDepthFrame!=null){CommitLive(DenseRays(activeDepthFrame),surfaceHits);activeDepthFrame=null;surfaceHits.Clear();}
                    if(now>=nextDense){nextDense=now+.2f;dense.Request(anchor.transform,rig.centerEyeAnchor.position,generation);}
                }
                if(now-lastDense>3 && now>=nextSample){nextSample=now+.08f;Capture();}
                if(floorVerified && FurnitureEnabled && now>=nextTargeted){nextTargeted=now+.12f;CheckOldSpace();}
            }
            if(finding && checkedRevision!=map.BlockingRevision)
            {
                checkedRevision=map.BlockingRevision;
                var item=items[selected];
                if(activeRoute.Count>0 && (!map.RouteClear(activeRoute,Clearance)
                    || !map.CanApproachItem(activeRoute[activeRoute.Count-1],new MeshPoint(item.x,item.y,item.z))))
                {activeRoute.Clear();line.positionCount=0;nextPlan=0;notice="Walking space changed. Checking a new route...";}
            }
            if(now>=nextVisual){nextVisual=now+.75f;RefreshVisuals(local);}
            if(dirty && !saveBusy && now>=nextSave){nextSave=now+15;Save();}
            if(registering)
            {
                if(!OVRInput.Get(OVRInput.Button.PrimaryHandTrigger,OVRInput.Controller.RTouch))gripReleased=true;
                if(gripReleased && OVRInput.GetDown(OVRInput.Button.PrimaryHandTrigger,OVRInput.Controller.RTouch)
                    && OVRInput.GetControllerPositionTracked(OVRInput.Controller.RTouch))
                {
                    var p=anchor.transform.InverseTransformPoint(rig.rightControllerAnchor.position);
                    if(Mathf.Abs(p.x)>60 || Mathf.Abs(p.z)>60 || Mathf.Abs(p.y)>5){notice="Item outside the supported home area.";return;}
                    items[selected]=new HomeItem {saved=true,x=p.x,y=p.y,z=p.z};registering=false;dirty=true;revision++;
                    notice=Name+" registered. Saving its location in this home.";Save();
                }
            }
            if(finding && !planning && now>=nextPlan
                && (activeRoute.Count==0 || map.Revision!=lastPlannedRevision || Vector3.Distance(local,lastPlannedHead)>.12f))
            {nextPlan=now+.5f;Plan();}
            if(marker!=null){marker.SetActive(HasItem && !Scanning);var item=items[selected];marker.transform.localPosition=new Vector3(item.x,item.y,item.z);}
            if(OVRInput.GetDown(OVRInput.Button.PrimaryThumbstick,OVRInput.Controller.RTouch))CancelItem();
        }
        private void Capture()
        {
            var head=rig.centerEyeAnchor;var position=head.position;var rotation=head.rotation;
            var origin=Point(anchor.transform.InverseTransformPoint(position));var rays=new List<DepthObservationRay>();
            var clock=System.Diagnostics.Stopwatch.StartNew();
            var batch=DepthPatchSampler.Capture(20,16,cursor,285,.3f,3,(x,y)=>
            {
                var direction=rotation*new Vector3(Mathf.Lerp(-.466f,.466f,x/19f),Mathf.Lerp(-.364f,.364f,y/15f),1).normalized;
                var hit=app.TryProbe(new Ray(position,direction),out var point,out var normal)
                    && Vector3.Distance(position,point)>=.3f && Vector3.Distance(position,point)<=3.5f;
                var local=hit?anchor.transform.InverseTransformPoint(point):default;
                if(hit && !BodySample(local))rays.Add(new DepthObservationRay(origin,Point(local),Point(anchor.transform.InverseTransformDirection(normal))));
                if(hit && Mathf.Abs(local.y)<.09f && Mathf.Abs(Vector3.Dot(normal,Vector3.up))>.94f && Time.frameCount!=lastFloorView)
                {lastFloorView=Time.frameCount;if(floorVotes++==0)firstFloorAt=Time.unscaledTime;
                    if(floorVotes>=8 && Time.unscaledTime-firstFloorAt>=1.5f)floorVerified=true;}
                return new DepthPatchSample(hit,new MeshPoint(local.x,local.y,local.z));
            },()=>clock.Elapsed.TotalMilliseconds);
            cursor=batch.NextCell;
            var hits=new Dictionary<(int,int),HomeObstacleSample>();AcceptTriangles(batch.Triangles,hits);CommitLive(rays,hits);
        }
        private bool ValidateDense(DepthPatchSample[] samples)
        {
            // Independent public raycast cross-check guards against graphics API
            // or SDK texture conventions differing on a particular device.
            var matches=0;
            for(int index=samples.Length/2,attempt=0;attempt<16;attempt++,index=(index+137)%samples.Length)
            {
                if(!samples[index].Hit)continue;
                var p=samples[index].Point;var expected=anchor.transform.TransformPoint(new Vector3(p.X,p.Y,p.Z));
                var origin=rig.centerEyeAnchor.position;
                if(!app.TryProbe(new Ray(origin,(expected-origin).normalized),out var hit,out _))continue;
                if(Vector3.Distance(hit,expected)<.15f){if(++matches>=2){denseFailures=0;return true;}continue;}
                if(++denseFailures>=3)dense.Reject();
                return false;
            }
            return false;
        }
        private void AcceptTriangles(List<DepthSurfaceTriangle> triangles,Dictionary<(int,int),HomeObstacleSample> hits)
        {
            if(triangles.Count==0)
            {if(Time.unscaledTime-lastHit>5)notice="Not seeing enough surface detail. Slow down and look at nearby floor and furniture.";return;}
            lastHit=Time.unscaledTime;
            var head=anchor.transform.InverseTransformPoint(rig.centerEyeAnchor.position);
            var rightTracked=OVRInput.GetControllerPositionTracked(OVRInput.Controller.RTouch);
            var leftTracked=OVRInput.GetControllerPositionTracked(OVRInput.Controller.LTouch);
            var right=anchor.transform.InverseTransformPoint(rig.rightControllerAnchor.position);
            var left=anchor.transform.InverseTransformPoint(rig.leftControllerAnchor.position);
            bool NearController(Vector3 p)=>(rightTracked && (p-right).sqrMagnitude<.0324f)||(leftTracked && (p-left).sqrMagnitude<.0324f);
            foreach(var t in triangles)
            {
                var a=new Vector3(t.A.X,t.A.Y,t.A.Z);var b=new Vector3(t.B.X,t.B.Y,t.B.Z);var c=new Vector3(t.C.X,t.C.Y,t.C.Z);
                // Do not bake the scanning user's hands/controllers or torso
                // into permanent furniture. Rejected samples remain unknown.
                var centre=(a+b+c)/3;
                if(centre.y>.12f && (new Vector2(centre.x-head.x,centre.z-head.z).sqrMagnitude<.0625f
                    || NearController(a) || NearController(b) || NearController(c)))continue;
                if(!floorVerified && Mathf.Abs(a.y)<.09f && Mathf.Abs(b.y)<.09f && Mathf.Abs(c.y)<.09f
                    && Mathf.Abs(Vector3.Cross(b-a,c-a).normalized.y)>.94f && Time.frameCount!=lastFloorView)
                {lastFloorView=Time.frameCount;if(floorVotes++==0)firstFloorAt=Time.unscaledTime;
                    if(floorVotes>=8 && Time.unscaledTime-firstFloorAt>=1.5f){floorVerified=true;dirty=true;revision++;}}
                if(Math.Abs(t.A.X)>59 || Math.Abs(t.A.Z)>59 || Math.Abs(t.A.Y)>5
                    || Math.Abs(t.B.X)>59 || Math.Abs(t.B.Z)>59 || Math.Abs(t.B.Y)>5
                    || Math.Abs(t.C.X)>59 || Math.Abs(t.C.Z)>59 || Math.Abs(t.C.Y)>5)continue;
                if(floorVerified){var previous=map.Revision;map.Observe(t,false,hits);if(map.Revision!=previous)MapChanged();}
                if(map.Full){Capacity();break;}
                if(!Scanning)continue;
                var key=((int)Math.Floor(t.A.X/6),(int)Math.Floor(t.A.Z/6));
                if(!chunks.TryGetValue(key,out var chunk))
                {
                    if(chunks.Count>=48)continue;
                    chunk=new Chunk();chunks.Add(key,chunk);
                }
                if(chunk.data.AtCapacity || totalVertices>=119990 || totalIndices>=599990)continue;
                var before=chunk.data.Points.Count;var indices=chunk.data.Indices.Count;
                if(chunk.samples.Add(SurfaceKey(t)) && chunk.data.AddTriangle(t.A,t.B,t.C))
                {totalVertices+=chunk.data.Points.Count-before;totalIndices+=chunk.data.Indices.Count-indices;changedChunks.Add(key);dirty=true;revision++;}
                // Floor is independently corroborated against the headset's floor
                // origin; before that, only surface preview is collected.
                if(map.Full){Capacity();break;}
            }
            if(Scanning)notice=floorVerified?"Scanning your home. Green = observed walking floor. Look through doorways and down at gaps. B finishes."
                :"Verifying floor height. Look down at open floor for a few seconds.";
        }
        private static (int,int,int,int) SurfaceKey(DepthSurfaceTriangle t)
        {
            var a=new Vector3(t.A.X,t.A.Y,t.A.Z);var b=new Vector3(t.B.X,t.B.Y,t.B.Z);var c=new Vector3(t.C.X,t.C.Y,t.C.Z);
            var centre=(a+b+c)/3;var normal=Vector3.Cross(b-a,c-a).normalized;
            var axis=Mathf.Abs(normal.y)>.7f?1:Mathf.Abs(normal.x)>Mathf.Abs(normal.z)?0:2;
            return (Mathf.FloorToInt(centre.x/.05f),Mathf.FloorToInt(centre.y/.05f),Mathf.FloorToInt(centre.z/.05f),axis);
        }
        private static MeshPoint Point(Vector3 p)=>new MeshPoint(p.x,p.y,p.z);
        private void ClearDepthBatch(){denseTriangles.Clear();activeDepthFrame=null;surfaceHits.Clear();}
        private bool BodySample(Vector3 p)=>p.y>.12f && (new Vector2(p.x-bodyHead.x,p.z-bodyHead.z).sqrMagnitude<.0625f
            || (rightTracked&&(p-bodyRight).sqrMagnitude<.0324f)||(leftTracked&&(p-bodyLeft).sqrMagnitude<.0324f));
        private void MapChanged(){dirty=true;revision++;}
        private List<DepthObservationRay> DenseRays(HomeDepthFrame frame)
        {
            var rays=new List<DepthObservationRay>();var size=HomeDepthCapture.Size;var samples=frame.Samples;
            for(var step=0;step<256;step++)
            {
                var index=(rayOffset+step*7)%samples.Length;var sample=samples[index];if(!sample.Hit)continue;
                var p=sample.Point;var point=new Vector3(p.X,p.Y,p.Z);if(BodySample(point))continue;
                var x=index%size;var y=index/size;
                if(x<size-1 && y<size-1 && samples[index+1].Hit && samples[index+size].Hit)
                {
                    var a=samples[index+1].Point;var b=samples[index+size].Point;
                    var ab=new Vector3(a.X-p.X,a.Y-p.Y,a.Z-p.Z);var ac=new Vector3(b.X-p.X,b.Y-p.Y,b.Z-p.Z);
                    if(ab.magnitude<=.3f && ac.magnitude<=.3f)
                    {var normal=Vector3.Cross(ab,ac).normalized;rays.Add(new DepthObservationRay(frame.Origin,p,Point(normal)));continue;}
                }
                rays.Add(new DepthObservationRay(frame.Origin,p));
            }
            rayOffset=(rayOffset+101)%samples.Length;return rays;
        }
        private void CommitLive(List<DepthObservationRay> rays,Dictionary<(int,int),HomeObstacleSample> hits=null)
        {
            if(!floorVerified)return;var previous=map.Revision;
            map.ObserveLive(rays,++liveBatch,Time.unscaledTime,DynamicEnabled||Scanning,FurnitureEnabled,hits,Scanning);
            if(map.Revision!=previous)MapChanged();
        }
        private void CheckOldSpace()
        {
            var head=rig.centerEyeAnchor;var origin=Point(anchor.transform.InverseTransformPoint(head.position));
            var forward=Point(anchor.transform.InverseTransformDirection(head.forward));var targets=map.ProbeTargets(origin,forward);
            var rays=new List<DepthObservationRay>();var clock=System.Diagnostics.Stopwatch.StartNew();var queries=0;
            foreach(var target in targets)
            {
                var world=anchor.transform.TransformPoint(new Vector3(target.X,target.Y,target.Z));queries++;
                if(app.TryProbe(new Ray(head.position,(world-head.position).normalized),out var hit,out var normal))
                {
                    var local=anchor.transform.InverseTransformPoint(hit);
                    if(!BodySample(local))rays.Add(new DepthObservationRay(origin,Point(local),Point(anchor.transform.InverseTransformDirection(normal))));
                }
                if(clock.Elapsed.TotalMilliseconds>=4)break;
            }
            map.ReturnUnusedProbes(targets.Count-queries);CommitLive(rays);
        }
        public void ToggleDynamic(){DynamicEnabled=!DynamicEnabled;map.ResetEvidence();notice=DynamicEnabled?"Continuous obstacle checks enabled.":"New-obstacle checks paused; existing blocks are retained.";}
        public void ToggleFurniture(){FurnitureEnabled=!FurnitureEnabled;map.ResetEvidence();notice=FurnitureEnabled?"Furniture space checks enabled. Look at the old position and its floor.":"Furniture clearing paused; current map is retained.";}
        public void CycleClearance()
        {
            var options=new[]{0f,.015f,.03f,.05f,.1f};var index=Array.FindIndex(options,value=>Math.Abs(value-Clearance)<.001f);
            Clearance=options[(index+1)%options.Length];routeCancel?.Cancel();activeRoute.Clear();if(line!=null)line.positionCount=0;nextPlan=0;
            notice=$"Extra obstacle margin: {Clearance*100:0.#} cm. Checking routes with the new setting.";
        }
        private void Capacity(){Scanning=false;capacityReached=true;notice="Walking-map storage is full. Guidance and live checks remain active; finish and save when ready.";Save();}
        public void Select(int slot){if(slot<0||slot>2)return;CancelItem();selected=slot;notice=Name+": "+(HasItem?"saved in this home":"not registered yet");}
        public void Register()
        {
            if(!Ready || Busy)return;Pause();CancelItem();registering=true;gripReleased=false;
            notice="Hold the right controller beside "+Name+", then press its side grip.";app.CloseMenu();
        }
        public void Find()
        {
            if(!Ready || !HasItem || !floorVerified)return;Pause();CancelItem();finding=true;nextPlan=0;
            app.EnableDepthForProbe();
            dense.Enable(rig.trackingSpace);
            notice="Finding a connected route through your scanned floor…";app.CloseMenu();
        }
        public void Found()
        {
            if(!CanConfirm)return;
            var p=anchor.transform.InverseTransformPoint(rig.centerEyeAnchor.position);var item=items[selected];
            if(new Vector2(p.x-item.x,p.z-item.z).magnitude>1.35f){notice="Move closer to the saved item before confirming.";return;}
            CancelItem();notice=Name+" found.";
        }
        public void CancelItem()
        {
            registering=finding=false;routeCancel?.Cancel();dense.Cancel();ClearDepthBatch();activeRoute.Clear();if(line!=null)line.positionCount=0;
        }
        private async void Plan()
        {
            planning=true;routeCancel?.Dispose();routeCancel=new CancellationTokenSource();var token=routeCancel.Token;
            var op=generation;var slot=selected;var clearance=Clearance;
            var snapshot=HomeMap.Restore(map.Snapshot());var p=anchor.transform.InverseTransformPoint(rig.centerEyeAnchor.position);
            var item=items[selected];
            try
            {
                var points=await Task.Run(()=>snapshot.Route(new MeshPoint(p.x,p.y,p.z),new MeshPoint(item.x,item.y,item.z),token,clearance),token);
                if(this==null || token.IsCancellationRequested || !Ready || !finding || generation!=op || selected!=slot || Clearance!=clearance)return;
                if(points.Count>0 && !map.RouteClear(points,clearance)){nextPlan=0;return;}
                if(points.Count>0 && !map.CanApproachItem(points[points.Count-1],new MeshPoint(item.x,item.y,item.z))){nextPlan=0;return;}
                var current=Point(anchor.transform.InverseTransformPoint(rig.centerEyeAnchor.position));current=new MeshPoint(current.X,0,current.Z);
                if(points.Count>0)
                {
                    var join=-1;for(var i=0;i<points.Count;i++)if(map.CanWalkSegment(current,points[i],clearance))join=i;
                    if(join<0){nextPlan=0;return;}
                    var joined=new List<MeshPoint>{current};for(var i=join;i<points.Count;i++)joined.Add(points[i]);points=joined;
                }
                activeRoute=points;lastPlannedRevision=map.Revision;lastPlannedHead=new Vector3(current.X,p.y,current.Z);
                line.positionCount=points.Count;
                for(var i=0;i<points.Count;i++)line.SetPosition(i,new Vector3(points[i].X,.035f,points[i].Z));
                notice=points.Count==0?"No continuous observed floor route yet. Continue scanning the floor between here and the item, especially doorways."
                    :"Follow the blue line toward "+Name+". Its marker shows the saved location. Check the real floor ahead.";
            }
            catch(OperationCanceledException){}catch(Exception e){notice="Route unavailable: "+e.Message;}
            finally{planning=false;}
        }
        public async void Save()
        {
            if(saveBusy){saveAgain=true;return;}
            if(Busy || anchor==null || !dirty)return;
            saveBusy=true;var op=generation;var savedRevision=revision;
            try
            {
                var surfaces=new List<HomeSurface>();
                foreach(var pair in chunks)
                {
                    var data=pair.Value.data;var v=new float[data.Points.Count*3];var indices=new int[data.Indices.Count];
                    for(var i=0;i<data.Points.Count;i++){v[3*i]=data.Points[i].X;v[3*i+1]=data.Points[i].Y;v[3*i+2]=data.Points[i].Z;}
                    for(var i=0;i<indices.Length;i++)indices[i]=data.Indices[i];
                    surfaces.Add(new HomeSurface {x=pair.Key.Item1,z=pair.Key.Item2,vertices=v,indices=indices});
                }
                var savedItems=new HomeItem[3];for(var i=0;i<3;i++){var item=items[i];savedItems[i]=new HomeItem {saved=item.saved,x=item.x,y=item.y,z=item.z};}
                var record=new HomeRecord {anchorId=anchor.Uuid.ToString("D"),savedUtc=DateTime.UtcNow.ToString("O"),floorVerified=floorVerified,
                    cells=map.Snapshot(),surfaces=surfaces.ToArray(),items=savedItems};
                var destination=path;
                await Task.Run(()=>{if(!record.Valid())throw new InvalidDataException("Home scan failed validation; previous save retained.");
                    Directory.CreateDirectory(Path.GetDirectoryName(destination));AtomicAnchorRecordStore.Write(destination,JsonUtility.ToJson(record));});
                if(this!=null){saveError=null;if(generation==op && revision==savedRevision)dirty=false;}
            }
            catch(Exception e){if(this!=null)saveError="Save failed: "+e.Message;Debug.LogWarning("[Home scan] "+e);}
            finally{saveBusy=false;if(saveAgain && this!=null){saveAgain=false;Save();}}
        }
        public async void Load(bool continueScanning=false)
        {
            if(Busy || saveBusy || !Active)return;
            Busy=true;Scanning=false;CancelItem();var operation=++generation;OVRSpatialAnchor candidate=null;
            try
            {
                notice="Locating your home map. Look around where you started scanning.";
                var file=new FileInfo(path);if(!file.Exists || file.Length>80*1024*1024)throw new IOException("Saved home is missing or exceeds its size limit.");
                var record=await Task.Run(()=>JsonUtility.FromJson<HomeRecord>(File.ReadAllText(path)));
                if(record==null || !record.Valid())throw new IOException("Saved home is invalid. The file has been retained.");
                if(this==null || operation!=generation)return;
                if(anchor!=null && anchor.Uuid.ToString("D")==record.anchorId)
                {
                    if(!await WaitTracked(anchor,operation))throw new IOException("Cannot locate this home yet. Return to the original area and retry.");
                    invalid=false;havePose=false;notice="Home reference recovered. Continue scanning when ready.";
                    // Keep newer in-memory progress rather than replacing it with an older checkpoint.
                }
                else
                {
                    var anchors=new List<OVRSpatialAnchor.UnboundAnchor>();
                    var result=await OVRSpatialAnchor.LoadUnboundAnchorsAsync(new[]{Guid.Parse(record.anchorId)},anchors);
                    if(!result.Success || anchors.Count!=1)throw new IOException("Home reference unavailable. Return to the starting area and retry.");
                    var unbound=anchors[0];if(!unbound.Localized && !await unbound.LocalizeAsync(15))throw new IOException("Home reference is not located yet.");
                    if(this==null || operation!=generation)return;
                    if(!unbound.TryGetPose(out var pose))throw new IOException("Home pose is not available yet. Look around and retry.");
                    var go=new GameObject("Restored home reference");go.transform.SetPositionAndRotation(pose.position,pose.rotation);
                    candidate=go.AddComponent<OVRSpatialAnchor>();unbound.BindTo(candidate);
                    if(!await WaitTracked(candidate,operation))throw new IOException("Home reference is not tracked.");
                    var restored=new Dictionary<(int,int),Chunk>();
                    foreach(var s in record.surfaces)
                    {
                        var chunk=new Chunk();
                        MeshPoint Point(int i)=>new MeshPoint(s.vertices[i*3],s.vertices[i*3+1],s.vertices[i*3+2]);
                        for(var i=0;i<s.indices.Length;i+=3)
                        {var triangle=new DepthSurfaceTriangle(Point(s.indices[i]),Point(s.indices[i+1]),Point(s.indices[i+2]));
                            chunk.data.AddTriangle(triangle.A,triangle.B,triangle.C);chunk.samples.Add(SurfaceKey(triangle));
                            if(i%900==0){await Task.Yield();if(this==null || operation!=generation)return;}}
                        restored.Add((s.x,s.z),chunk);
                    }
                    var restoredMap=await Task.Run(()=>HomeMap.FromRecord(record,CancellationToken.None));
                    if(this==null || operation!=generation)return;
                    Release();anchor=candidate;candidate=null;map=restoredMap;items=record.items;floorVerified=record.floorVerified;
                    totalVertices=totalIndices=0;
                    foreach(var pair in restored){chunks.Add(pair.Key,pair.Value);changedChunks.Add(pair.Key);totalVertices+=pair.Value.data.Points.Count;totalIndices+=pair.Value.data.Indices.Count;}
                    invalid=false;dirty=record.version==1;havePose=false;revision++;CreateVisuals();
                    notice=record.version==1?"Home upgraded to finer mapping. Item locations kept. Rescan floor gaps to add detail.":"Home restored. Continuous obstacle and furniture checks are active.";
                }
            }
            catch(Exception e){notice=e.Message;Debug.LogWarning("[Home scan] "+e);continueScanning=false;}
            finally{if(candidate!=null)Destroy(candidate.gameObject);Busy=false;}
            if(continueScanning && this!=null && Ready)StartOrContinue();
        }
        private void CreateVisuals()
        {
            root=new GameObject("Home scan preview");root.transform.SetParent(anchor.transform,false);
            floorMesh=MakeMesh("Observed home floor",floorMaterial);blockedMesh=MakeMesh("Observed home obstacles",blockedMaterial);
            liveMesh=MakeMesh("Live home obstacles",liveMaterial);freedMesh=MakeMesh("Reopened home floor",freedMaterial);
            var go=new GameObject("Home guidance");go.transform.SetParent(root.transform,false);line=go.AddComponent<LineRenderer>();
            line.sharedMaterial=pathMaterial;line.useWorldSpace=false;line.widthMultiplier=.025f;line.positionCount=0;
            marker=GameObject.CreatePrimitive(PrimitiveType.Sphere);marker.name="Home item location";Destroy(marker.GetComponent<Collider>());
            marker.transform.SetParent(root.transform,false);marker.transform.localScale=Vector3.one*.07f;marker.GetComponent<Renderer>().sharedMaterial=lineMaterial;marker.SetActive(false);
        }
        private Mesh MakeMesh(string name,Material material)
        {
            var go=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(root.transform,false);
            var mesh=new Mesh {name=name,indexFormat=IndexFormat.UInt32};mesh.MarkDynamic();
            go.GetComponent<MeshFilter>().sharedMesh=mesh;var renderer=go.GetComponent<MeshRenderer>();renderer.sharedMaterial=material;
            renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;return mesh;
        }
        private void RefreshVisuals(Vector3 head)
        {
            // Upload at most two changed sections per refresh. Culling avoids
            // drawing the entire house; stored geometry remains available.
            var done=new List<(int,int)>();
            foreach(var key in changedChunks)
            {
                var c=chunks[key];if(c.mesh==null){c.mesh=MakeMesh("Home surface section",surfaceMaterial);c.go=root.transform.GetChild(root.transform.childCount-1).gameObject;}
                var v=new List<Vector3>();foreach(var p in c.data.Points)v.Add(new Vector3(p.X,p.Y,p.Z));
                var indices=new int[c.data.Indices.Count];for(var i=0;i<indices.Length;i++)indices[i]=c.data.Indices[i];
                c.mesh.Clear();c.mesh.SetVertices(v);c.mesh.SetTriangles(indices,0);c.mesh.RecalculateBounds();done.Add(key);if(done.Count==2)break;
            }
            foreach(var key in done)changedChunks.Remove(key);
            foreach(var pair in chunks)if(pair.Value.go!=null)pair.Value.go.SetActive(ShowSurfaces && Vector2.Distance(new Vector2(head.x,head.z),new Vector2(pair.Key.Item1*6+3,pair.Key.Item2*6+3))<12);
            var green=new List<Vector3>();var red=new List<Vector3>();var magenta=new List<Vector3>();var blue=new List<Vector3>();
            if(ShowFloor)foreach(var c in map.Cells)
            {
                var p=HomeMap.Centre(c.x,c.z);if(Math.Abs(p.X-head.x)>6 || Math.Abs(p.Z-head.z)>6)continue;
                if(HomeMap.Blocked(c))Square(c.dynamic?magenta:red,p);else if(c.floor==31)Square(c.cleared?blue:green,p);
            }
            Upload(floorMesh,green);Upload(blockedMesh,red);Upload(liveMesh,magenta);Upload(freedMesh,blue);
        }
        private static void Square(List<Vector3> v,MeshPoint p)
        {var d=HomeMap.CellSize*.43f;v.Add(new Vector3(p.X-d,.015f,p.Z-d));v.Add(new Vector3(p.X-d,.015f,p.Z+d));v.Add(new Vector3(p.X+d,.015f,p.Z+d));v.Add(new Vector3(p.X+d,.015f,p.Z-d));}
        private static void Upload(Mesh mesh,List<Vector3> v)
        {var indices=new int[v.Count/4*6];for(var i=0;i<v.Count/4;i++){var j=i*6;var a=i*4;indices[j]=a;indices[j+1]=a+1;indices[j+2]=a+2;indices[j+3]=a;indices[j+4]=a+2;indices[j+5]=a+3;}
            mesh.Clear();mesh.SetVertices(v);mesh.SetTriangles(indices,0);mesh.RecalculateBounds();}
        private void Invalidate()
        {
            if(!Active || (anchor==null && !Busy))return;
            generation++;invalid=true;Scanning=false;havePose=false;CancelItem();
            ClearDepthBatch();
            if(root!=null)root.SetActive(false);
            notice="Tracking changed. Scan paused and guidance hidden. Look around the starting area, then choose Continue to recover.";
            Save();
        }
        private void OnApplicationPause(bool paused){if(paused)Invalidate();}
        private void Release()
        {
            foreach(var c in chunks.Values)if(c.mesh!=null)Destroy(c.mesh);
            chunks.Clear();changedChunks.Clear();if(floorMesh!=null)Destroy(floorMesh);if(blockedMesh!=null)Destroy(blockedMesh);
            ClearDepthBatch();if(liveMesh!=null)Destroy(liveMesh);if(freedMesh!=null)Destroy(freedMesh);
            if(anchor!=null)Destroy(anchor.gameObject);anchor=null;root=null;line=null;marker=null;
        }
        private void OnDestroy()
        {
            generation++;routeCancel?.Cancel();if(display!=null)display.RecenteredPose-=Invalidate;OVRManager.TrackingLost-=Invalidate;
            dense.Dispose();Release();Destroy(floorMaterial);Destroy(blockedMaterial);Destroy(lineMaterial);Destroy(pathMaterial);Destroy(liveMaterial);Destroy(freedMaterial);
        }
    }
}
