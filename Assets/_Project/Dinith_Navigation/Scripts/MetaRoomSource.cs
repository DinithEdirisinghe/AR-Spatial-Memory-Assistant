using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Globalization;
using System.Threading.Tasks;
using Meta.XR.MRUtilityKit;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace SpatialSense.RoomScan
{
    // Meta owns the scene and its anchors. This component only queries them and
    // adds an application-owned, Y-up child frame for our route coordinates.
    public sealed class MetaRoomSource : MonoBehaviour
    {
        private MRUK kit;
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
        public bool MeshVisible { get; set; } = true;
        public string Diagnostics => $"Meta mesh: {meshVertices} vertices | {meshTriangles} triangles. Room floor is from Meta Space Setup.";
        public int Revision { get; private set; }
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
            Revision++;
            if (changed == room) { room = null; floor = null; }
            if (sessionRequested && !IsBusy) ActivateRoom();
        }

        public void Snapshot(out MeshPoint[] points, out int[] triangles, out int floorIndexCount)
        {
            if (!Ready) throw new InvalidOperationException(Status);
            var vertices = new List<MeshPoint>(); var indices = new List<int>();
            AddPlane(floor, vertices, indices);
            floorIndexCount = indices.Count;
            var ignore = MRUKAnchor.SceneLabels.FLOOR | MRUKAnchor.SceneLabels.CEILING | MRUKAnchor.SceneLabels.GLOBAL_MESH
                | MRUKAnchor.SceneLabels.DOOR_FRAME | MRUKAnchor.SceneLabels.WINDOW_FRAME;
            foreach (var anchor in room.Anchors)
            {
                if (anchor == null || (anchor.Label & ignore) != 0) continue;
                if (anchor.VolumeBounds.HasValue) AddVolume(anchor, vertices, indices);
                else if (anchor.PlaneBoundary2D != null && anchor.PlaneBoundary2D.Count >= 3) AddPlane(anchor, vertices, indices);
            }
            points = vertices.ToArray(); triangles = indices.ToArray();
        }

        private void AddPlane(MRUKAnchor anchor, List<MeshPoint> points, List<int> indices)
        {
            var boundary = anchor.PlaneBoundary2D;
            if (boundary == null || boundary.Count < 3) return;
            Triangulator.TriangulatePoints(boundary, null, out var vertices, out var triangles);
            var offset = points.Count;
            foreach (var point in vertices)
            {
                var p = frame.InverseTransformPoint(anchor.transform.TransformPoint(new Vector3(point.x, point.y, 0)));
                // This workspace supports one level floor, using Meta's semantic
                // floor boundary rather than classifying noisy depth triangles.
                if (anchor == floor) p.y = 0;
                points.Add(new MeshPoint(p.x, p.y, p.z));
            }
            foreach (var index in triangles) indices.Add(offset + index);
        }

        private void AddVolume(MRUKAnchor anchor, List<MeshPoint> points, List<int> indices)
        {
            var bounds = anchor.VolumeBounds.Value; var offset = points.Count;
            for (var i = 0; i < 8; i++)
            {
                var local = new Vector3((i & 1) == 0 ? bounds.min.x : bounds.max.x,
                    (i & 2) == 0 ? bounds.min.y : bounds.max.y, (i & 4) == 0 ? bounds.min.z : bounds.max.z);
                var p = frame.InverseTransformPoint(anchor.transform.TransformPoint(local)); points.Add(new MeshPoint(p.x, p.y, p.z));
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

        private void Invalidate() => Invalidate(true);
        private void Invalidate(bool schedule)
        {
            invalidated = true; Revision++;
            if (schedule && sessionRequested) { reloadPending = true; reloadAt = Time.unscaledTime + 1f; }
        }
        private void OnApplicationPause(bool paused)
        { if (!sessionRequested) return; if (paused) Invalidate(false); else { reloadPending = true; reloadAt = Time.unscaledTime + 1f; } }
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
