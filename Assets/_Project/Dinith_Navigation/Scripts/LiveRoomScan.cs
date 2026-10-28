using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using Meta.XR;
using SpatialSense.ManualDemo;
using SpatialSense.Persistence;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace SpatialSense.RoomScan
{
    // Live scan workspace; route testing uses a separate observed-floor grid.
    public sealed class LiveRoomScan : MonoBehaviour
    {
        [SerializeField] private OVRCameraRig rig;
        [SerializeField] private Material meshMaterial;
        [SerializeField] private Font font;
        private const int MaxPatches = 600;
        private const float MaxRange = 3.5f, MaxEdge = 0.45f;
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<int> triangles = new List<int>();
        private SpatialMeshAccumulator accumulator;
        private int duplicateFaces;
        private EnvironmentRaycastManager raycaster;
        private OVRSpatialAnchor anchor;
        private Mesh mesh;
        private GameObject meshObject;
        private RoomScanMenu menu;
        private LiveScanNavigation navigation;
        private MetaRoomSource roomSource;
        private NativeItemWorkflow items;
        public NavigationEvaluation Evaluation { get; private set; }
        public MultiRoomNavigation RoomNavigation { get; private set; }
        public HomeScanner Home { get; private set; }
        public NativeItemWorkflow Items => items;
        public MetaRoomSource Room => roomSource;
        public bool UseAppScan { get; private set; }
        public int ScanRevision { get; private set; }
        public string ScanMethodName => UseAppScan ? "App live scan" : "Meta room scan";
        private string message = "Start a scan, or load a saved scan from the menu.";
        private string lastCapturedAt;
        private string processing;
        private bool dirty, statistics;
        public bool HasUnsavedExperimentalMesh => dirty;
        private int detail = 1;
        private int sampleCell,viewCells,viewWidth,viewHeight,lastSampleCells;
        private bool hasViewPose,viewHasGeometry;
        public bool MeshVisible { get; set; } = true;
        public bool IsScanning => scanning;
        public bool HasScan => anchor != null && triangles.Count > 0;
        public bool ScanUsable => HasScan && Tracked && !invalidated;
        public bool IsWorking => busy || (navigation != null && navigation.IsBuilding) || (roomSource != null && roomSource.IsBusy) || (items != null && items.IsBusy);
        public bool MenuOpen => menu != null && menu.Visible;
        public Transform ScanFrame => anchor != null ? anchor.transform : null;
        public IReadOnlyList<Vector3> CapturedVertices => vertices;
        public IReadOnlyList<int> CapturedTriangles => triangles;
        public LiveScanNavigation Navigation => navigation;
        public string DetailName => detail == 0 ? "Fast" : detail == 1 ? "Balanced" : "Detailed";
        public string Summary => UseAppScan ? "App live scan (MRUK): " + message : roomSource != null ? roomSource.Status : message;
        public bool StatisticsVisible => statistics;
        public string Statistics => UseAppScan ? $"MRUK app scan: {vertices.Count} vertices | {triangles.Count / 3} triangles | {DetailName}\n{(scanning ? "Capturing" : "Paused")} | {(dirty ? "Unsaved changes" : "Saved / unchanged")} | last batch {lastSampleCells} cells / {lastBatchMs:0.0} ms" : roomSource != null ? roomSource.Diagnostics : "Room capture is not initialized.";
        private string capturedAt;
        private string path;
        private bool scanning, busy, invalidated, savedAnchor;
        private int patches, generation;
        private float nextSample, lastHitTime;
        private Vector3 lastCapturePosition;
        private Quaternion lastCaptureRotation;
        private bool capturedPose;
        private OVRDisplay display;
        private float lastBatchMs;
        private float lastCleanupMs;

        public void Configure(OVRCameraRig cameraRig, Material material, Font displayFont)
        { rig = cameraRig; meshMaterial = material; font = displayFont; }

        private void Start()
        {
            if (rig == null || meshMaterial == null || font == null) { enabled = false; return; }
            path = Path.Combine(Application.persistentDataPath, "spatialsense-depth-mesh-v1.json");
            roomSource = gameObject.AddComponent<MetaRoomSource>(); roomSource.Configure(rig, meshMaterial);
            navigation = gameObject.AddComponent<LiveScanNavigation>(); navigation.Configure(this, rig, meshMaterial, roomSource);
            items = gameObject.AddComponent<NativeItemWorkflow>(); items.Configure(this, rig, meshMaterial, font);
            RoomNavigation = gameObject.AddComponent<MultiRoomNavigation>(); RoomNavigation.Configure(this, rig);
            Evaluation = gameObject.AddComponent<NavigationEvaluation>(); Evaluation.Configure(this, rig);
            Home = gameObject.AddComponent<HomeScanner>(); Home.Configure(this, rig, meshMaterial);
            roomSource.SetHomeMode(true);
            menu = gameObject.AddComponent<RoomScanMenu>(); menu.Configure(this, rig, meshMaterial, font);
            display = OVRManager.display;
            if (display != null) display.RecenteredPose += Invalidate;
            OVRManager.TrackingLost += Invalidate;
        }

        private bool Tracked => anchor != null && anchor.Created && anchor.Localized && anchor.IsTracked;

        private void Update()
        {
            if (rig == null || menu == null) return;
            if (OVRInput.GetDown(OVRInput.Button.Two, OVRInput.Controller.LTouch)) menu.Show(true);
            if (Home != null && Home.Active)
            {
                if (!Home.Busy && !MenuOpen)
                {
                    if (OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.RTouch)) { if(Home.Scanning)Home.Pause();else Home.StartOrContinue(); }
                    if (OVRInput.GetDown(OVRInput.Button.Two, OVRInput.Controller.RTouch)) Home.Pause();
                    if (OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.LTouch)) menu.CycleView();
                }
                if(meshObject!=null)meshObject.GetComponent<MeshRenderer>().enabled=false;
                return;
            }
            if (!IsWorking && (Evaluation == null || !Evaluation.IsLocked))
            {
                if (OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.RTouch)) { if (UseAppScan) ToggleCapture(); else roomSource.ScanRoom(); }
                else if (OVRInput.GetDown(OVRInput.Button.Two, OVRInput.Controller.RTouch)) { if (UseAppScan) SaveScan(); else roomSource.SaveRoomCopy(); }
                else if (OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.LTouch)) menu.CycleView();
            }
            if (meshObject != null && meshObject.GetComponent<MeshRenderer>() is MeshRenderer renderer)
                renderer.enabled = Tracked && !invalidated && OVRManager.hasVrFocus && MeshVisible;
            if (scanning && !Tracked) Invalidate();
            if (scanning && OVRManager.hasVrFocus && Time.unscaledTime >= nextSample)
            {
                nextSample = Time.unscaledTime + 0.33f;
                CapturePatch();
            }
        }

        public void StartNewScan() { if (!IsWorking) BeginScan(); }
        public void ToggleScanMethod()
        {
            if (IsWorking) return;
            PauseCapture(); items.Cancel();
            UseAppScan = !UseAppScan;
            navigation.SourceChanged(); menu.RefreshView();
        }
        public void NotifyScanChanged() { ScanRevision++; }
        public void SaveScan() { if (!IsWorking) FreezeAndSave(); }
        public void LoadScan() { if (!IsWorking) LoadSaved(); }
        public void CloseMenu() { menu.Show(false); }
        public void ShowFloorMap() { menu.ShowMap(); }
        public void ToggleStatistics() { statistics = !statistics; }
        public void MarkMetadataChanged() { dirty = true; }
        public void CycleDetail() { detail = (detail + 1) % 3; capturedPose = false; hasViewPose = false; message = "Scan detail: " + DetailName + "."; }
        public void PauseCapture()
        {
            scanning = false;
            DisableProbeDepth();
        }
        public bool EnableDepthForProbe() => EnsureDepth();
        public void DisableProbeDepth() { if ((Home == null || !Home.Active) && !scanning && (navigation == null || !navigation.LiveEnabled) && raycaster != null) raycaster.enabled = false; }
        public bool TryProbe(Ray ray, out Vector3 point, out Vector3 normal)
        {
            point = normal = default;
            if (raycaster == null || !raycaster.enabled) return false;
            if (!raycaster.Raycast(ray, out var hit, 4f) || !Finite(hit.point)) return false;
            point = hit.point; normal = hit.normal; return true;
        }
        public void ToggleCapture()
        {
            if (IsWorking) return;
            if (scanning) { PauseCapture(); message = "Capture paused. Continue, save, or test a route from the menu."; }
            else if (anchor == null) BeginScan();
            else ContinueCapture();
        }

        private async void ContinueCapture()
        {
            navigation.StopSelection();
            if (invalidated || !Tracked) { message = "Locate the saved scan again, or start a new scan."; return; }
            if (patches >= MaxPatches || (accumulator != null && accumulator.AtCapacity))
            { message = "This scan reached its capacity. Save/export it, then start a new scan."; return; }
            if (!EnsureDepth()) return;
            busy = true;
            var operation = generation;
            try
            {
                if (accumulator == null)
                {
                    var restored = new SpatialMeshAccumulator();
                    for (var i = 0; i < triangles.Count; i += 3)
                    {
                        restored.AddTriangle(ToPoint(vertices[triangles[i]]), ToPoint(vertices[triangles[i + 1]]), ToPoint(vertices[triangles[i + 2]]));
                        if (i % 288 == 0)
                        {
                            await Task.Yield();
                            if (this == null || operation != generation || !Tracked) throw new InvalidOperationException("Tracking changed while preparing scan continuation.");
                        }
                    }
                    accumulator = restored;
                    CopyAccumulator(); RefreshMesh(); dirty = true;
                }
                if (operation != generation || !Tracked) throw new InvalidOperationException("Scan tracking changed.");
                if (accumulator.AtCapacity) { DisableProbeDepth(); message = "This scan is full. Save/export it, then start a new scan."; return; }
                navigation.GeometryChanged();
                capturedPose = false; hasViewPose = false; scanning = true;
                processing = "SpatialWeld2cmNormal30degBudgetedCells";
                message = "Capture resumed. Look around the floor and nearby objects.";
                menu.Show(false);
            }
            catch (Exception error) { if (this != null) { DisableProbeDepth(); Report(error); } }
            finally { busy = false; }
        }

        private bool EnsureDepth()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Permission.HasUserAuthorizedPermission(OVRPermissionsRequester.ScenePermission))
            {
                Permission.RequestUserPermission(OVRPermissionsRequester.ScenePermission);
                message = "Allow spatial-data permission, then press A again.";
                return false;
            }
#endif
            if (!EnvironmentRaycastManager.IsSupported)
            {
                message = "Live environment raycasting is unavailable. Test a standalone Quest 3 build and check OpenXR/OS support.";
                return false;
            }
            if (raycaster == null)
            {
                var go = new GameObject("Live environment depth raycaster");
                go.transform.SetParent(transform, false);
                raycaster = go.AddComponent<EnvironmentRaycastManager>();
                raycaster.CustomTrackingSpace = rig.trackingSpace;
            }
            raycaster.enabled = true;
            return true;
        }

        private async void BeginScan()
        {
            navigation.StopSelection();
            if (busy || !EnsureDepth()) return;
            busy = true;
            scanning = false;
            var operation = ++generation;
            message = "Creating scan anchor... Look around and wait.";
            OVRSpatialAnchor candidate = null;
            try
            {
                var head = rig.centerEyeAnchor;
                var go = new GameObject("Live mesh anchor");
                go.transform.SetPositionAndRotation(head.position, Quaternion.identity);
                candidate = go.AddComponent<OVRSpatialAnchor>();
                if (!await WaitTracked(candidate, operation)) throw new InvalidOperationException("Scan anchor could not be tracked.");
                if (this == null) return;
                ReleaseCurrent();
                anchor = candidate;
                candidate = null;
                savedAnchor = false;
                vertices.Clear(); triangles.Clear(); patches = 0; capturedPose = false; hasViewPose = false; sampleCell = 0;
                accumulator = new SpatialMeshAccumulator(); duplicateFaces = 0;
                processing = "SpatialWeld2cmNormal30degBudgetedCells"; dirty = false;
                navigation.GeometryChanged(true);
                lastBatchMs = lastCleanupMs = 0f;
                invalidated = false;
                capturedAt = DateTime.UtcNow.ToString("O");
                lastCapturedAt = capturedAt;
                CreateMesh();
                scanning = true;
                lastHitTime = Time.unscaledTime;
                message = "Scan the floor and nearby objects within 3.5 m. A pauses; B saves.";
                menu.Show(false);
            }
            catch (Exception error) { if (this != null) Report(error); }
            finally
            {
                if (candidate != null) Destroy(candidate.gameObject);
                DisableProbeDepth();
                busy = false;
            }
        }

        private void CapturePatch()
        {
            var head = rig.centerEyeAnchor;
            var width = detail == 0 ? 8 : detail == 1 ? 10 : 14;
            var height = detail == 0 ? 6 : detail == 1 ? 8 : 10;
            var changed=!hasViewPose || width!=viewWidth || height!=viewHeight || Vector3.Distance(head.position,lastCapturePosition)>=0.12f
                || Quaternion.Angle(head.rotation,lastCaptureRotation)>=12f;
            if(capturedPose && !changed)return;
            if(changed)
            {
                viewCells=0;viewHasGeometry=false;capturedPose=false;hasViewPose=true;viewWidth=width;viewHeight=height;
                lastCapturePosition=head.position;lastCaptureRotation=head.rotation;
            }
            var position=head.position;var rotation=head.rotation;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var totalCells=(width-1)*(height-1);
            var batch=DepthPatchSampler.Capture(width,height,sampleCell,totalCells-viewCells,MaxEdge,8,(x,y)=>
            {
                var direction = rotation * new Vector3(Mathf.Lerp(-0.466f, 0.466f, x / (float)(width - 1)),
                    Mathf.Lerp(-0.364f, 0.364f, y / (float)(height - 1)), 1f).normalized;
                var valid=raycaster.Raycast(new Ray(position,direction),out var hit,MaxRange) && Finite(hit.point)
                    && Vector3.Distance(position,hit.point)>=0.3f;
                return new DepthPatchSample(valid,valid?ToPoint(anchor.transform.InverseTransformPoint(hit.point)):default);
            },()=>clock.Elapsed.TotalMilliseconds);
            sampleCell=batch.NextCell;viewCells+=batch.VisitedCells;lastSampleCells=batch.VisitedCells;
            viewHasGeometry|=batch.Triangles.Count>0;
            capturedPose=viewCells>=totalCells && viewHasGeometry;
            if(viewCells>=totalCells && !viewHasGeometry)hasViewPose=false;
            lastBatchMs=(float)batch.Milliseconds;
            if (batch.Triangles.Count == 0)
            {
                if (Time.unscaledTime - lastHitTime > 10f)
                    message = "No usable live surface patch yet. Look at a textured nearby surface; check permissions, OpenXR and headset logs if this continues.";
                return;
            }
            lastHitTime = Time.unscaledTime;
            clock.Restart();
            var previousIndexCount = accumulator.Indices.Count;
            foreach(var triangle in batch.Triangles)accumulator.AddTriangle(triangle.A,triangle.B,triangle.C);
            duplicateFaces = accumulator.DuplicateFaces;
            patches++;
            dirty = true; lastCapturedAt = DateTime.UtcNow.ToString("O");
            if (accumulator.Indices.Count != previousIndexCount)
            {
                CopyAccumulator();
                RefreshMesh();
                navigation.GeometryChanged();
            }
            lastCleanupMs = (float)clock.Elapsed.TotalMilliseconds;
            message = batch.BudgetReached ? $"Capturing smaller completed patches ({viewCells}/{totalCells} view cells). Hold briefly, then look around. A pauses; B saves."
                : "Capturing nearby surfaces and floor. A pauses; B saves.";
            if (patches >= MaxPatches || accumulator.AtCapacity)
            {
                scanning = false;
                DisableProbeDepth();
                message = "Scan limit reached. Press B to save, or A for a new scan.";
            }
        }

        private static MeshPoint ToPoint(Vector3 value) => new MeshPoint(value.x, value.y, value.z);

        private void CopyAccumulator()
        {
            vertices.Clear(); triangles.Clear();
            foreach (var point in accumulator.Points) vertices.Add(new Vector3(point.X, point.Y, point.Z));
            foreach (var index in accumulator.Indices) triangles.Add(index);
        }

        private async void FreezeAndSave()
        {
            if (busy) return;
            navigation.StopSelection();
            if (invalidated || !Tracked || triangles.Count == 0) { message = "No valid scan to save. Press A to capture a new scan."; return; }
            scanning = false;
            DisableProbeDepth();
            busy = true;
            var operation = generation;
            var persistedHere = false;
            var savingAnchor = anchor;
            var savingId = anchor.Uuid;
            message = "Saving mesh and anchor... Keep the app open.";
            try
            {
                if (!savedAnchor)
                {
                    var result = await savingAnchor.SaveAnchorAsync();
                    if (!result.Success) throw new IOException("Native mesh anchor save failed: " + result.Status);
                    persistedHere = true;
                }
                if (this == null) { if (persistedHere) await Erase(savingId); return; }
                if (operation != generation || invalidated || !Tracked) throw new InvalidOperationException("Tracking changed during save. Scan was not committed.");
                var coordinates = new float[vertices.Count * 3];
                for (var i = 0; i < vertices.Count; i++)
                { coordinates[i * 3] = vertices[i].x; coordinates[i * 3 + 1] = vertices[i].y; coordinates[i * 3 + 2] = vertices[i].z; }
                var record = new DepthMeshRecord { anchorUuid = anchor.Uuid.ToString("D"), capturedAtUtc = capturedAt,
                    processing = processing, capturedPatches = patches, lastCapturedAtUtc = lastCapturedAt,
                    hasFloor = navigation.AppHasFloor, floorY = navigation.AppFloorY,
                    vertices = coordinates, triangles = triangles.ToArray() };
                if (!record.IsValid()) throw new InvalidDataException("Mesh failed save validation.");
                string previousId = null;
                if (File.Exists(path))
                {
                    try { previousId = JsonUtility.FromJson<DepthMeshRecord>(File.ReadAllText(path)).anchorUuid; }
                    catch (Exception) { /* Keep unreadable old file as the atomic writer's backup. */ }
                }
                AtomicAnchorRecordStore.Write(path, JsonUtility.ToJson(record));
                savedAnchor = true;
                dirty = false;
                message = "Saved on this headset. Use Load saved scan after reopening.";
                if (Guid.TryParse(previousId, out var previous) && previous != anchor.Uuid) _ = Erase(previous);
                Debug.Log("[SpatialSense Scan] Saved " + path);
            }
            catch (Exception error)
            {
                if (persistedHere) _ = Erase(savingId);
                if (this != null) Report(error);
            }
            finally { busy = false; }
        }

        private async void LoadSaved()
        {
            if (busy) return;
            navigation.StopSelection();
            scanning = false;
            DisableProbeDepth();
            busy = true;
            var operation = ++generation;
            OVRSpatialAnchor candidate = null;
            message = "Loading saved mesh anchor... Look around the original scan area.";
            try
            {
                var file = new FileInfo(path);
                if (!file.Exists) throw new IOException("No saved mesh yet. Press A to scan, then B to save.");
                if (file.Length > 5 * 1024 * 1024) throw new InvalidDataException("Saved scan is too large.");
                var record = JsonUtility.FromJson<DepthMeshRecord>(File.ReadAllText(path));
                if (record == null || !record.IsValid()) throw new InvalidDataException("Saved scan is invalid; existing file retained.");
                if (anchor != null && savedAnchor && anchor.Uuid == Guid.Parse(record.anchorUuid))
                {
                    if (!await WaitTracked(anchor, operation)) throw new IOException("Saved mesh anchor is not tracked. Look around and retry.");
                    if (this == null) return;
                    invalidated = false;
                    ApplyRecord(record); RefreshMesh();
                    if (meshObject != null) meshObject.SetActive(true);
                    message = "Scan located. Check alignment before continuing or building the floor map.";
                    return;
                }
                var loaded = new List<OVRSpatialAnchor.UnboundAnchor>();
                var result = await OVRSpatialAnchor.LoadUnboundAnchorsAsync(new[] { Guid.Parse(record.anchorUuid) }, loaded);
                if (this == null) return;
                if (!result.Success || loaded.Count != 1) throw new IOException("Saved mesh anchor could not be loaded. Return to the original area and retry.");
                var unbound = loaded[0];
                if (!unbound.Localized && !await unbound.LocalizeAsync(15)) throw new IOException("Mesh anchor not localized. Look around and retry.");
                if (this == null) return;
                var deadline = Time.unscaledTime + 15f;
                Pose pose = default;
                while (!unbound.TryGetPose(out pose))
                {
                    if (this == null || generation != operation || Time.unscaledTime > deadline) throw new IOException("Mesh anchor pose unavailable.");
                    await Task.Yield();
                }
                var go = new GameObject("Restored depth mesh anchor");
                go.transform.SetPositionAndRotation(pose.position, pose.rotation);
                candidate = go.AddComponent<OVRSpatialAnchor>();
                unbound.BindTo(candidate);
                if (!await WaitTracked(candidate, operation)) throw new IOException("Restored mesh anchor is not tracked.");
                if (this == null) return;
                ReleaseCurrent();
                anchor = candidate; candidate = null; savedAnchor = true; invalidated = false;
                ApplyRecord(record);
                CreateMesh(); RefreshMesh();
                message = "Scan restored. Check alignment; then continue scanning or build the floor map.";
            }
            catch (Exception error) { if (this != null) Report(error); }
            finally { if (candidate != null) Destroy(candidate.gameObject); busy = false; }
        }

        private async Task<bool> WaitTracked(OVRSpatialAnchor value, int operation)
        {
            var deadline = Time.unscaledTime + 20f;
            while (this != null && operation == generation && value != null && Time.unscaledTime < deadline)
            {
                if (value.Created && value.Localized && value.IsTracked) return true;
                await Task.Yield();
            }
            return false;
        }

        private void ApplyRecord(DepthMeshRecord record)
        {
            vertices.Clear(); triangles.Clear(); accumulator = null; duplicateFaces = 0;
            for (var i = 0; i < record.vertices.Length; i += 3)
                vertices.Add(new Vector3(record.vertices[i], record.vertices[i + 1], record.vertices[i + 2]));
            triangles.AddRange(record.triangles);
            patches = record.capturedPatches > 0 ? record.capturedPatches : vertices.Count / 80;
            capturedAt = record.capturedAtUtc; lastCapturedAt = record.lastCapturedAtUtc;
            processing = record.processing; dirty = false;
            navigation.RestoreFloor(record.hasFloor, record.floorY);
        }

        private void CreateMesh()
        {
            meshObject = new GameObject("Live depth surface patches", typeof(MeshFilter), typeof(MeshRenderer));
            meshObject.transform.SetParent(anchor.transform, false);
            mesh = new Mesh { name = "Depth patch mesh", indexFormat = IndexFormat.UInt32 };
            mesh.MarkDynamic();
            meshObject.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = meshObject.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = meshMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private void RefreshMesh()
        { mesh.Clear(); mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.RecalculateBounds(); }

        private void Invalidate()
        {
            if (anchor == null && !busy) return;
            generation++; scanning = false; invalidated = true;
            navigation?.GeometryChanged();
            DisableProbeDepth();
            message = "Tracking or reference frame changed. Mesh hidden. Load saved scan, or press A for a new scan.";
        }

        private void OnApplicationPause(bool paused) { if (paused) Invalidate(); }
        private void Report(Exception error)
        { message = error.Message; Debug.LogWarning("[SpatialSense Scan] " + error, this); }
        private static bool Finite(Vector3 value) => !float.IsNaN(value.x) && !float.IsNaN(value.y) && !float.IsNaN(value.z)
            && !float.IsInfinity(value.x) && !float.IsInfinity(value.y) && !float.IsInfinity(value.z);
        private static async Task Erase(Guid id)
        {
            try
            {
                var result = await OVRSpatialAnchor.EraseAnchorsAsync(null, new[] { id });
                if (!result.Success) Debug.LogWarning("[SpatialSense Scan] Old scan anchor cleanup failed: " + result.Status);
            }
            catch (Exception error) { Debug.LogWarning("[SpatialSense Scan] Anchor cleanup: " + error.Message); }
        }

        private void ReleaseCurrent()
        {
            if (mesh != null) Destroy(mesh);
            if (anchor != null) Destroy(anchor.gameObject);
            mesh = null; meshObject = null; anchor = null;
        }

        public async void ExportMesh()
        {
            if (IsWorking || !ScanUsable) return;
            navigation.StopSelection();
            PauseCapture(); busy = true;
            var points = vertices.ToArray(); var indices = triangles.ToArray();
            var directory = Path.Combine(Application.persistentDataPath, "mesh-exports");
            var filename = "SpatialSense_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff") + ".obj";
            message = "Exporting a mesh file to this headset...";
            try
            {
                await Task.Run(() =>
                {
                    Directory.CreateDirectory(directory);
                    var text = new StringBuilder("# SpatialSense scan. Metres; right-handed OBJ (X=-anchor X, Y=anchor Y, Z=anchor Z).\n");
                    foreach (var p in points) text.AppendFormat(CultureInfo.InvariantCulture, "v {0:R} {1:R} {2:R}\n", -p.x, p.y, p.z);
                    for (var i = 0; i < indices.Length; i += 3) text.AppendFormat(CultureInfo.InvariantCulture, "f {0} {1} {2}\n", indices[i + 2] + 1, indices[i + 1] + 1, indices[i] + 1);
                    File.WriteAllText(Path.Combine(directory, filename), text.ToString());
                    var files = new DirectoryInfo(directory).GetFiles("SpatialSense_*.obj");
                    Array.Sort(files, (a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));
                    for (var i = 3; i < files.Length; i++) files[i].Delete();
                });
                if (this != null) message = "Mesh file exported locally. The three most recent exports are kept on this headset.";
            }
            catch (Exception error) { if (this != null) Report(error); }
            finally { busy = false; }
        }

        private void OnDestroy()
        {
            generation++;
            if (display != null) display.RecenteredPose -= Invalidate;
            OVRManager.TrackingLost -= Invalidate;
            ReleaseCurrent();
        }
    }
}
