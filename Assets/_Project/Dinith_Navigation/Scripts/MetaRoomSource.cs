using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Globalization;
using System.Threading.Tasks;
using System.Security.Cryptography;
using Meta.XR.MRUtilityKit;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace SpatialSense.RoomScan
{
    public sealed class RoomSourceSnapshot
    {
        public string RoomId, FloorId, LayoutId;
        public MeshPoint[] Points;
        public int[] Triangles;
        public bool[] Furniture;
        public int FloorIndices;
        public float[] FromFloor;
    }
    // Meta owns the scene and its anchors. This component only queries them and
    // adds an application-owned, Y-up child frame for our route coordinates.
    public sealed class MetaRoomSource : MonoBehaviour
    {
        private MRUK kit;
        private bool homeMode;
        public void SetHomeMode(bool active) { homeMode=active; if(kit!=null)kit.EnableWorldLock=!active; }
        private MRUKRoom room;
        private MRUKAnchor floor;
        private OVRCameraRig rig;
        private Transform frame;
        private bool invalidated, reloadPending;
        private bool sessionRequested;
        private bool returningFromCapture;
        private float reloadAt;
        private OVRDisplay display;
        private Material meshMaterial;
        private GameObject meshView;
        private MeshRenderer meshRenderer;
        private Mesh nativeMesh;
        private int meshVertices, meshTriangles;
        private readonly Dictionary<Guid,string> layoutStamps=new Dictionary<Guid,string>();
        public bool MeshVisible { get; set; } = true;
        public string Diagnostics => $"Meta mesh: {meshVertices} vertices | {meshTriangles} triangles. Room floor is from Meta Space Setup.";
        public int Revision { get; private set; }
        public int DataRevision { get; private set; }
        public int TrackingRevision { get; private set; }
        public bool CatalogReady => kit != null && sessionRequested && !IsBusy && !invalidated;
        public Guid FloorUuid => Ready ? floor.Anchor.Uuid : Guid.Empty;
        public string LayoutId => Ready ? GetLayoutStamp(room,floor) : null;
        public float[] CurrentFromFloor => Ready ? MatrixValues(frame.worldToLocalMatrix * floor.transform.localToWorldMatrix) : null;
        public List<string> LoadedRoomIds()
        {
            var ids = new List<string>();
            if (kit != null) foreach (var loaded in kit.Rooms) if (loaded != null) ids.Add(loaded.Anchor.Uuid.ToString("D"));
            ids.Sort(StringComparer.Ordinal); return ids;
        }
        public bool TryCaptureFloorPoint(Vector3 world, out RoomDoorEndpoint endpoint)
        {
            endpoint = null; if (!Ready) return false;
            var local = floor.transform.InverseTransformPoint(world);
            endpoint = new RoomDoorEndpoint { roomId=RoomUuid.ToString("D"), floorId=FloorUuid.ToString("D"), layoutId=LayoutId, x=local.x,y=local.y,z=local.z };
            return endpoint.IsValid();
        }
        public bool TryResolveDoor(RoomDoorEndpoint endpoint, out Vector3 world)
        {
            world=default;
            if(!Ready || endpoint==null || endpoint.roomId!=RoomUuid.ToString("D") || endpoint.floorId!=FloorUuid.ToString("D") || endpoint.layoutId!=LayoutId) return false;
            world=floor.transform.TransformPoint(new Vector3(endpoint.x,endpoint.y,endpoint.z)); return true;
        }
        public bool IsBusy { get; private set; }
        public Transform Frame => frame;
        public Guid RoomUuid => Ready ? room.Anchor.Uuid : Guid.Empty;
        public bool ContainsWorldPoint(Vector3 point) => Ready && room.IsPositionInRoom(point, testVerticalBounds: true);
        public bool Ready => !IsBusy && !invalidated && room != null && floor != null && frame != null
            && kit != null && kit.GetCurrentRoom() == room && kit.IsWorldLockActive;
        public string Status { get; private set; } = "Choose Scan room with Meta to run the native capture flow.";

        public void Configure(OVRCameraRig cameraRig, Material material)
        {
            rig = cameraRig;
            meshMaterial = new Material(material);
            meshMaterial.SetColor("_BaseColor", new Color(0.05f, 0.85f, 0.85f, 0.35f));
            kit = FindAnyObjectByType<MRUK>();
            if (kit == null)
            {
                var go = new GameObject("Meta room model"); go.SetActive(false); go.transform.SetParent(transform, false);
                kit = go.AddComponent<MRUK>();
                kit.SceneSettings = new MRUK.MRUKSettings { DataSource = MRUK.SceneDataSource.Device, LoadSceneOnStartup = false };
                kit.EnableWorldLock = true; go.SetActive(true);
            }
            kit.EnableWorldLock = true;
            kit.RoomCreatedEvent.AddListener(OnRoomChanged);
            kit.RoomUpdatedEvent.AddListener(OnRoomChanged);
            kit.RoomRemovedEvent.AddListener(OnRoomChanged);
            display = OVRManager.display;
            if (display != null) display.RecenteredPose += Invalidate;
            OVRManager.TrackingLost += Invalidate;
            // A new app session starts with the capture choice, not an implicit
            // load of a pre-existing room. Loading previous data is explicit.
            reloadPending = false;
        }

        private void Update()
        {
            if(homeMode) {if(meshRenderer!=null)meshRenderer.enabled=false;return;}
            if (meshRenderer != null) meshRenderer.enabled = Ready && MeshVisible && OVRManager.hasVrFocus;
            if (kit == null || IsBusy) return;
            if (reloadPending && Time.unscaledTime >= reloadAt) { reloadPending = false; LoadRoom(); }
            if (sessionRequested && !invalidated && kit.GetCurrentRoom() != room) ActivateRoom();
        }

        public async void LoadRoom()
        {
            if (IsBusy || kit == null) return;
            sessionRequested = true;
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Permission.HasUserAuthorizedPermission(OVRPermissionsRequester.ScenePermission))
            {
                Permission.RequestUserPermission(OVRPermissionsRequester.ScenePermission);
                Status = "Allow spatial-data access, then choose Load Meta room.";
                return;
            }
#endif
            IsBusy = true; Invalidate(false); Status = "Loading Meta's saved floor and furniture...";
            layoutStamps.Clear();
            var fromCapture = returningFromCapture; returningFromCapture = false;
            ResetView(); room = null; floor = null;
            try
            {
                var result = MRUK.LoadDeviceResult.NotInitialized;
                for (var retry = 0; retry < 4; retry++)
                {
                    result = await kit.LoadSceneFromDevice(requestSceneCaptureIfNoDataFound: false);
                    if (this == null) return;
                    if (result != MRUK.LoadDeviceResult.NotInitialized) break;
                    await Task.Delay(500);
                }
                if (this == null) return;
                if (result != MRUK.LoadDeviceResult.Success)
                    throw new InvalidOperationException(result == MRUK.LoadDeviceResult.NoRoomsFound
                        ? "No Meta room scan found. Choose Scan room with Meta and complete Space Setup."
                        : "Meta room load failed: " + result + ". Check spatial permissions and retry.");
                invalidated = false;
                ActivateRoom();
                if (fromCapture && meshVertices > 0) Status = "Native mesh loaded after Meta setup. If setup was cancelled, this may be your previous room.";
            }
            catch (Exception error) { if (this != null) { Status = error.Message; Debug.LogWarning("[SpatialSense Meta room] " + error); } }
            finally { IsBusy = false; }
        }

        public async void ScanRoom()
        {
            if (IsBusy) return;
            sessionRequested = true;
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Permission.HasUserAuthorizedPermission(OVRPermissionsRequester.ScenePermission))
            { Permission.RequestUserPermission(OVRPermissionsRequester.ScenePermission); Status = "Allow spatial-data access, then choose Scan room with Meta again."; return; }
#endif
            IsBusy = true; Invalidate(false); Status = "Complete Meta Space Setup, then return to this app.";
            var shouldReload = false;
            try
            {
                var result = await OVRScene.RequestSpaceSetup();
                if (this == null) return;
                shouldReload = result;
                Status = result ? "Space Setup closed. Reloading available room data..." : "Space Setup could not start. Open Space Setup in headset settings.";
                // Cancellation can also return true. Always inspect the available model.
            }
            catch (Exception error) { if (this != null) Status = error.Message; }
            finally
            {
                IsBusy = false;
                if (this != null) { returningFromCapture = shouldReload; reloadPending = shouldReload; reloadAt = Time.unscaledTime + 0.5f; }
            }
        }

        private void ActivateRoom()
        {
            var current = kit.GetCurrentRoom();
            if (current == null) { Status = "Room data loaded, but the current room is not located. Look around, then choose Load Meta room."; return; }
            if (current == room && floor != null && frame != null && floor == current.FloorAnchors.Find(item => item == floor)) return;
            ResetView();
            room = current;
            floor = room.FloorAnchors.Count > 0 ? room.FloorAnchors[0] : null;
            if (floor == null || floor.PlaneBoundary2D == null || floor.PlaneBoundary2D.Count < 3)
            { Status = "Meta room has no usable floor polygon. Update it with Scan room with Meta."; Revision++; return; }
            var go = new GameObject("Meta floor navigation coordinates");
            frame = go.transform;
            frame.SetPositionAndRotation(floor.transform.position, Quaternion.identity);
            frame.SetParent(floor.transform, true);
            ShowNativeMesh();
            invalidated = false; Revision++;
            Status = kit.IsWorldLockActive ? (meshVertices > 0 ? "Meta room mesh loaded. This is the mesh exposed by Meta Space Setup."
                : "Meta floor/furniture loaded, but no global mesh is available. Complete Space Setup to capture a mesh.")
                : "Meta room loaded. Look around to align it before routing; reload if it does not locate.";
        }

        private void ShowNativeMesh()
        {
            meshVertices = meshTriangles = 0;
            var global = room.GlobalMeshAnchor;
            if (global == null) return;
            // Explicitly query fresh geometry; do not reuse MRUKAnchor's managed
            // GlobalMesh cache after a native capture/update.
            var mesh = global.LoadGlobalMeshTriangles();
            if (mesh == null) return;
            nativeMesh = mesh;
            if (mesh.vertexCount == 0) return;
            meshView = new GameObject("Native Meta scene mesh", typeof(MeshFilter), typeof(MeshRenderer));
            meshView.transform.SetParent(global.transform, false);
            meshView.GetComponent<MeshFilter>().sharedMesh = mesh;
            meshRenderer = meshView.GetComponent<MeshRenderer>(); meshRenderer.sharedMaterial = meshMaterial;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshVertices = mesh.vertexCount; meshTriangles = (int)mesh.GetIndexCount(0) / 3;
        }

        private void OnRoomChanged(MRUKRoom changed)
        {
            if(changed!=null)layoutStamps.Remove(changed.Anchor.Uuid);
            Revision++; DataRevision++;
            if (changed == room) { room = null; floor = null; }
            if (sessionRequested && !IsBusy) ActivateRoom();
        }

        public void Snapshot(out MeshPoint[] points, out int[] triangles, out int floorIndexCount)
            => Snapshot(out points, out triangles, out floorIndexCount, out _);

        private static bool IsMovable(MRUKAnchor anchor)
        {
            const MRUKAnchor.SceneLabels movable = MRUKAnchor.SceneLabels.TABLE | MRUKAnchor.SceneLabels.COUCH | MRUKAnchor.SceneLabels.BED;
            return anchor.Label != 0 && (anchor.Label & movable) != 0 && (anchor.Label & ~movable) == 0;
        }

        public void Snapshot(out MeshPoint[] points, out int[] triangles, out int floorIndexCount, out bool[] furnitureTriangles)
        {
            if (!Ready) throw new InvalidOperationException(Status);
            SnapshotFor(room, floor, frame.worldToLocalMatrix, out points, out triangles, out floorIndexCount, out furnitureTriangles);
        }
        private static void SnapshotFor(MRUKRoom source, MRUKAnchor selectedFloor, Matrix4x4 coordinates,
            out MeshPoint[] points, out int[] triangles, out int floorIndexCount, out bool[] furnitureTriangles)
        {
            var vertices = new List<MeshPoint>(); var indices = new List<int>();
            var movable = new List<bool>();
            AddPlaneFor(selectedFloor, coordinates, selectedFloor, vertices, indices);
            floorIndexCount = indices.Count;
            for (var i = 0; i < indices.Count / 3; i++) movable.Add(false);
            var ignore = MRUKAnchor.SceneLabels.FLOOR | MRUKAnchor.SceneLabels.CEILING | MRUKAnchor.SceneLabels.GLOBAL_MESH
                | MRUKAnchor.SceneLabels.DOOR_FRAME | MRUKAnchor.SceneLabels.WINDOW_FRAME;
            foreach (var anchor in source.Anchors)
            {
                if (anchor == null || (anchor.Label & ignore) != 0) continue;
                var begin = indices.Count;
                if (anchor.VolumeBounds.HasValue) AddVolumeFor(anchor, coordinates, vertices, indices);
                else if (anchor.PlaneBoundary2D != null && anchor.PlaneBoundary2D.Count >= 3) AddPlaneFor(anchor, coordinates, selectedFloor, vertices, indices);
                for (var i = begin; i < indices.Count; i += 3) movable.Add(IsMovable(anchor));
            }
            points = vertices.ToArray(); triangles = indices.ToArray();
            furnitureTriangles = movable.ToArray();
        }
        public List<RoomSourceSnapshot> CaptureRoomMaps()
        {
            if(!CatalogReady) throw new InvalidOperationException("Load the saved Meta rooms first.");
            var result=new List<RoomSourceSnapshot>();
            foreach(var loaded in kit.Rooms)
            {
                if(loaded==null || loaded.FloorAnchors.Count==0) continue;
                var selected=loaded.FloorAnchors[0];
                if(selected.PlaneBoundary2D==null || selected.PlaneBoundary2D.Count<3) continue;
                var coordinates=loaded==room && frame!=null ? frame.worldToLocalMatrix
                    : Matrix4x4.TRS(selected.transform.position,Quaternion.identity,Vector3.one).inverse;
                SnapshotFor(loaded,selected,coordinates,out var points,out var triangles,out var floors,out var furniture);
                result.Add(new RoomSourceSnapshot { RoomId=loaded.Anchor.Uuid.ToString("D"),FloorId=selected.Anchor.Uuid.ToString("D"),LayoutId=GetLayoutStamp(loaded,selected),
                    Points=points,Triangles=triangles,FloorIndices=floors,Furniture=furniture,FromFloor=MatrixValues(coordinates*selected.transform.localToWorldMatrix) });
            }
            return result;
        }
        private static float[] MatrixValues(Matrix4x4 m)=>new[] { m.m00,m.m01,m.m02,m.m03,m.m10,m.m11,m.m12,m.m13,m.m20,m.m21,m.m22,m.m23 };
        private string GetLayoutStamp(MRUKRoom source,MRUKAnchor selectedFloor)
        {
            if(layoutStamps.TryGetValue(source.Anchor.Uuid,out var stamp))return stamp;
            stamp=LayoutStamp(source,selectedFloor);layoutStamps[source.Anchor.Uuid]=stamp;return stamp;
        }
        private static string LayoutStamp(MRUKRoom source,MRUKAnchor selectedFloor)
        {
            var anchors=new List<MRUKAnchor> { selectedFloor };
            const MRUKAnchor.SceneLabels structure=MRUKAnchor.SceneLabels.WALL_FACE | MRUKAnchor.SceneLabels.INNER_WALL_FACE
                | MRUKAnchor.SceneLabels.INVISIBLE_WALL_FACE | MRUKAnchor.SceneLabels.DOOR_FRAME;
            foreach(var anchor in source.Anchors) if(anchor!=null && (anchor.Label & structure)!=0)anchors.Add(anchor);
            anchors.Sort((a,b)=>string.CompareOrdinal(a.Anchor.Uuid.ToString("D"),b.Anchor.Uuid.ToString("D")));
            var text=new StringBuilder();
            foreach(var anchor in anchors)
            {
                text.Append(anchor.Anchor.Uuid.ToString("D"));text.Append('/').Append((int)anchor.Label);
                foreach(var value in MatrixValues(selectedFloor.transform.worldToLocalMatrix*anchor.transform.localToWorldMatrix))
                    text.Append('/').Append(value.ToString("F2",CultureInfo.InvariantCulture));
                if(anchor.PlaneBoundary2D!=null)foreach(var p in anchor.PlaneBoundary2D)
                    text.Append('/').Append(p.x.ToString("F2",CultureInfo.InvariantCulture)).Append(',').Append(p.y.ToString("F2",CultureInfo.InvariantCulture));
            }
            using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(text.ToString())),0,16).Replace("-","").ToLowerInvariant();
        }

        public bool[] ClassifyAppFurniture(IReadOnlyList<Vector3> vertices, IReadOnlyList<int> indices, Transform appFrame)
        {
            var result = new bool[indices.Count / 3];
            if (!Ready || appFrame == null) return result;
            var candidates = new List<MRUKAnchor>(); var protectedAnchors = new List<MRUKAnchor>();
            foreach (var anchor in room.Anchors)
            {
                if (anchor == null) continue;
                if (IsMovable(anchor)) candidates.Add(anchor);
                else if ((anchor.Label & (MRUKAnchor.SceneLabels.FLOOR | MRUKAnchor.SceneLabels.CEILING | MRUKAnchor.SceneLabels.GLOBAL_MESH)) == 0)
                    protectedAnchors.Add(anchor);
            }
            for (var i = 0; i < indices.Count; i += 3)
            {
                var a = appFrame.TransformPoint(vertices[indices[i]]); var b = appFrame.TransformPoint(vertices[indices[i + 1]]); var c = appFrame.TransformPoint(vertices[indices[i + 2]]);
                var protectedSurface = false;
                foreach (var anchor in protectedAnchors)
                    if (ContainsSemanticPoint(anchor, a) || ContainsSemanticPoint(anchor, b) || ContainsSemanticPoint(anchor, c)) { protectedSurface = true; break; }
                if (protectedSurface) continue;
                foreach (var anchor in candidates)
                    if (ContainsSemanticPoint(anchor, a) && ContainsSemanticPoint(anchor, b) && ContainsSemanticPoint(anchor, c)) { result[i / 3] = true; break; }
            }
            return result;
        }

        private static bool ContainsSemanticPoint(MRUKAnchor anchor, Vector3 point)
        {
            var p = anchor.transform.InverseTransformPoint(point);
            if (anchor.VolumeBounds.HasValue)
            { var bounds = anchor.VolumeBounds.Value; bounds.Expand(0.04f); return bounds.Contains(p); }
            var polygon = anchor.PlaneBoundary2D;
            if (polygon == null || polygon.Count < 3 || Mathf.Abs(p.z) > 0.12f) return false;
            var min = polygon[0]; var max = min;
            foreach (var vertex in polygon) { min = Vector2.Min(min, vertex); max = Vector2.Max(max, vertex); }
            return p.x >= min.x - 0.04f && p.x <= max.x + 0.04f && p.y >= min.y - 0.04f && p.y <= max.y + 0.04f;
        }

        private static void AddPlaneFor(MRUKAnchor anchor, Matrix4x4 coordinates, MRUKAnchor selectedFloor, List<MeshPoint> points, List<int> indices)
        {
            var boundary = anchor.PlaneBoundary2D;
            if (boundary == null || boundary.Count < 3) return;
            Triangulator.TriangulatePoints(boundary, null, out var vertices, out var triangles);
            var offset = points.Count;
            foreach (var point in vertices)
            {
                var p = coordinates.MultiplyPoint3x4(anchor.transform.TransformPoint(new Vector3(point.x, point.y, 0)));
                // This workspace supports one level floor, using Meta's semantic
                // floor boundary rather than classifying noisy depth triangles.
                if (anchor == selectedFloor) p.y = 0;
                points.Add(new MeshPoint(p.x, p.y, p.z));
            }
            foreach (var index in triangles) indices.Add(offset + index);
        }

        private static void AddVolumeFor(MRUKAnchor anchor, Matrix4x4 coordinates, List<MeshPoint> points, List<int> indices)
        {
            var bounds = anchor.VolumeBounds.Value; var offset = points.Count;
            for (var i = 0; i < 8; i++)
            {
                var local = new Vector3((i & 1) == 0 ? bounds.min.x : bounds.max.x,
                    (i & 2) == 0 ? bounds.min.y : bounds.max.y, (i & 4) == 0 ? bounds.min.z : bounds.max.z);
                var p = coordinates.MultiplyPoint3x4(anchor.transform.TransformPoint(local)); points.Add(new MeshPoint(p.x, p.y, p.z));
            }
            foreach (var index in new[] { 0,2,3,0,3,1,4,5,7,4,7,6,0,1,5,0,5,4,2,6,7,2,7,3,0,4,6,0,6,2,1,3,7,1,7,5 }) indices.Add(offset + index);
        }

        public async void SaveRoomCopy()
        {
            if (!Ready) return;
            IsBusy = true;
            try
            {
                var json = kit.SaveSceneToJsonString(includeGlobalMesh: true, rooms: new List<MRUKRoom> { room });
                var global = room.GlobalMeshAnchor;
                var mesh = nativeMesh != null && nativeMesh.vertexCount > 0 ? nativeMesh : null;
                var points = mesh != null ? mesh.vertices : null;
                var triangles = mesh != null ? mesh.triangles : null;
                if (points != null) for (var i = 0; i < points.Length; i++) points[i] = frame.InverseTransformPoint(global.transform.TransformPoint(points[i]));
                var root = Application.persistentDataPath;
                Status = "Saving Meta's room model and native mesh locally...";
                await Task.Run(() =>
                {
                    SpatialSense.Persistence.AtomicAnchorRecordStore.Write(Path.Combine(root, "spatialsense-meta-room.json"), json);
                    if (points == null || triangles == null) return;
                    var obj = new StringBuilder("# Meta Scene Mesh. Metres; right-handed coordinates: X=-Meta floor frame X, Y=up, Z=Meta floor frame Z.\n");
                    foreach (var p in points) obj.AppendFormat(CultureInfo.InvariantCulture, "v {0:R} {1:R} {2:R}\n", -p.x, p.y, p.z);
                    for (var i = 0; i < triangles.Length; i += 3) obj.AppendFormat(CultureInfo.InvariantCulture, "f {0} {1} {2}\n", triangles[i + 2] + 1, triangles[i + 1] + 1, triangles[i] + 1);
                    SpatialSense.Persistence.AtomicAnchorRecordStore.Write(Path.Combine(root, "spatialsense-native-room.obj"), obj.ToString());
                });
                if (this != null) Status = points != null ? "Meta room JSON and native mesh OBJ saved on this headset." : "Meta room JSON saved. No native scene mesh was available for OBJ export.";
            }
            catch (Exception error) { if (this != null) Status = "Room copy failed: " + error.Message; }
            finally { IsBusy = false; }
        }

        private void Invalidate() { TrackingRevision++; Invalidate(true); }
        private void Invalidate(bool schedule)
        {
            invalidated = true; Revision++; DataRevision++;
            if (schedule && sessionRequested) { reloadPending = true; reloadAt = Time.unscaledTime + 1f; }
        }
        private void OnApplicationPause(bool paused)
        { if (!sessionRequested) return; if (paused) { TrackingRevision++; Invalidate(false); } else { reloadPending = true; reloadAt = Time.unscaledTime + 1f; } }
        private void ResetView()
        {
            if (frame != null) Destroy(frame.gameObject);
            if (meshView != null) Destroy(meshView);
            if (nativeMesh != null) Destroy(nativeMesh);
            frame = null; meshView = null; meshRenderer = null; nativeMesh = null;
            meshVertices = meshTriangles = 0;
        }
        private void OnDestroy()
        {
            if (kit != null) { kit.RoomCreatedEvent.RemoveListener(OnRoomChanged); kit.RoomUpdatedEvent.RemoveListener(OnRoomChanged); kit.RoomRemovedEvent.RemoveListener(OnRoomChanged); }
            if (display != null) display.RecenteredPose -= Invalidate;
            OVRManager.TrackingLost -= Invalidate;
            ResetView();
            if (meshMaterial != null) Destroy(meshMaterial);
        }
    }
}
