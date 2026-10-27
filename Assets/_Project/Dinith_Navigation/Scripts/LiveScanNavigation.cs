using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Project.Core.Integration;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpatialSense.RoomScan
{
    public sealed class LiveScanNavigation : MonoBehaviour
    {
        private enum AimMode { None, Floor, Destination }
        private LiveRoomScan scanner;
        private MetaRoomSource roomSource;
        private bool AppMode => scanner != null && scanner.UseAppScan;
        private Transform Frame => AppMode ? scanner.ScanFrame : roomSource.Frame;
        private bool RoomReady => AppMode ? scanner.ScanUsable : roomSource != null && roomSource.Ready;
        private int CurrentRevision => AppMode ? scanner.ScanRevision : roomSource.Revision;
        private bool appHasFloor;
        private float appFloorY;
        public bool AppHasFloor => appHasFloor;
        public float AppFloorY => appFloorY;
        private LiveObstacleLayer liveLayer;
        private readonly List<DepthObservationRay> liveRays = new List<DepthObservationRay>();
        private float nextLiveCheck;
        private ApplicationDiagnostics diagnostics;
        private bool liveRequested = true, liveReplanPending, routeWasBlocked;
        private int sweepCursor, targetCursor, lastRayCount, routeUpdates, blockedEvents, recoveredEvents;
        private float lastDepthAt, lastSamplingMs, lastResponseMs = -1, nextEndpointRetry;
        private double liveChangeAt;
        private string testId = Guid.NewGuid().ToString("N");
        private ulong lastPathHash;
        private Vector3 itemTarget;
        private bool hasItemTarget;
        private GameObject liveRoot;
        private Mesh liveMesh;
        private Material liveMaterial;
        public bool LiveOverlayVisible { get; private set; } = true;
        public string LiveSummary => !LiveEnabled ? "Dynamic routing OFF; confirmed live blocks remain." :
            $"Dynamic ON | new blocks {liveLayer?.ActiveCount ?? 0} | checking {liveLayer?.PendingCount ?? 0}"
            + (liveLayer != null && liveLayer.AtCapacity ? " | capacity reached" : Time.unscaledTime - lastDepthAt > 3f ? " | no recent depth: look ahead" : "");
        public string LiveMeasurements => $"Changed routes {routeUpdates} | blocked {blockedEvents} | recovered {recoveredEvents}\n"
            + $"Last response {(lastResponseMs < 0 ? "--" : lastResponseMs.ToString("0.0") + " ms")} | sample {lastSamplingMs:0.0} ms / {lastRayCount} hits";
        [Serializable] private sealed class LiveMeasure
        {
            public string scanMethod, testId;
            public int activeCells, addedCells, clearedCells, validRays, obstacleRevision, routeGeneration;
            public float extraMargin, responseMs, samplingMs;
            public double observedToConfirmedMs;
        }
        private int sourceRevision = -1, attemptedRevision = -1;
        private bool automaticBuild;
        public bool LiveEnabled { get; private set; }
        private OVRCameraRig rig;
        private Material baseMaterial, routeMaterial, probeMaterial;
        private readonly List<Material> materials = new List<Material>();
        private readonly List<Mesh> visualMeshes = new List<Mesh>();
        private ScanNavigationGrid map;
        private GameObject mapRoot, routeRoot;
        private LineRenderer route, marker, probe;
        private AimMode aim;
        private Vector3 hitPoint;
        private bool validHit, hasGoal;
        private MeshPoint goal;
        private float nextProbe, nextPlan;
        private CancellationTokenSource buildCancellation;
        private int buildGeneration;
        private int routeGeneration, obstacleRevision;
        private CancellationTokenSource itemPlanningCancellation;
        public string RouteOwner { get; private set; }
        public bool RouteAvailable { get; private set; }
        public bool HasDestination => hasGoal;
        public Vector3 StandingPosition => hasGoal && RoomReady ? Frame.TransformPoint(new Vector3(goal.X, goal.Y, goal.Z)) : default;
        public bool HasFloor => RoomReady && (!AppMode || appHasFloor);
        public float FloorY => AppMode ? appFloorY : 0f;
        public bool IsBuilding { get; private set; }
        public bool HasMap => map != null;
        public bool MapVisible { get; set; }
        private static readonly float[] MarginOptions = { 0f, 0.03f, 0.05f, 0.1f, 0.2f };
        public float Clearance { get; private set; } = 0.03f;
        public bool StrictCoverage { get; private set; }
        public string CoverageName => StrictCoverage ? "Strict" : "Normal";
        public string Status { get; private set; } = "Loading Meta room floor...";
        public string MapSummary => map == null ? scanner.Summary : $"{scanner.ScanMethodName}: green {map.WalkableCount} | obstacles {map.ObstacleCount - map.MarginCount} | margin {map.MarginCount} | live {liveLayer?.ActiveCount ?? 0}";
        public string Diagnostics => map == null ? "Build a floor map to see rejection counts."
            : $"Obstacle evidence: {map.ObstacleEvidenceCount} cells | obstacle + clearance: {map.ObstacleCount} | floor near gaps: {map.CoverageEdgeCount}";

        public void Configure(LiveRoomScan source, OVRCameraRig cameraRig, Material material, MetaRoomSource metaRoom)
        {
            scanner = source; rig = cameraRig; baseMaterial = material; roomSource = metaRoom;
            diagnostics = gameObject.GetComponent<ApplicationDiagnostics>() ?? gameObject.AddComponent<ApplicationDiagnostics>();
            liveRequested = PlayerPrefs.GetInt("SpatialSense.DynamicRouting", 1) != 0;
            liveMaterial = new Material(baseMaterial); liveMaterial.SetColor("_BaseColor", new Color(1f, 0.1f, 0.75f, 0.8f));
            var preferred = PlayerPrefs.GetFloat("SpatialSense.ExtraObstacleMargin", 0.03f);
            foreach (var option in MarginOptions) if (Mathf.Abs(preferred - option) < 0.001f) Clearance = option;
            routeMaterial = MakeMaterial(new Color(0.12f, 0.45f, 1f, 1f));
            probeMaterial = MakeMaterial(new Color(1f, 0.8f, 0.1f, 1f));
            var go = new GameObject("Floor selection pointer");
            go.transform.SetParent(transform, false);
            probe = go.AddComponent<LineRenderer>();
            StyleLine(probe, probeMaterial, 0.007f); probe.useWorldSpace = true; probe.positionCount = 2; probe.enabled = false;
        }

        private void Update()
        {
            if (scanner == null) return;
            if (sourceRevision != CurrentRevision)
            { sourceRevision = CurrentRevision; ClearMap(); Status = scanner.Summary; }
            if (map != null && !RoomReady)
            { ClearMap(); attemptedRevision = -1; Status = "Scan tracking unavailable. Guidance cleared; locate the scan again."; }
            if (HasFloor && !scanner.IsScanning && map == null && !scanner.IsWorking && attemptedRevision != sourceRevision)
            { attemptedRevision = sourceRevision; automaticBuild = true; BuildMap(); }
            if (mapRoot != null) mapRoot.SetActive(MapVisible && RoomReady && OVRManager.hasVrFocus);
            if (routeRoot != null) routeRoot.SetActive(RoomReady && OVRManager.hasVrFocus);
            if (liveRoot != null) liveRoot.SetActive(LiveOverlayVisible && RoomReady && OVRManager.hasVrFocus);
            if (aim != AimMode.None)
            {
                if (!RoomReady || scanner.MenuOpen || !OVRManager.hasVrFocus) { CancelAim(); return; }
                var tracked = OVRInput.GetControllerPositionTracked(OVRInput.Controller.RTouch)
                    && OVRInput.GetControllerOrientationTracked(OVRInput.Controller.RTouch);
                if (tracked && Time.unscaledTime >= nextProbe)
                {
                    nextProbe = Time.unscaledTime + 0.08f;
                    var controller = rig.rightControllerAnchor;
                    var ray = new Ray(controller.position, controller.forward);
                    if (aim == AimMode.Floor)
                    {
                        validHit = scanner.TryProbe(ray, out hitPoint, out var normal) && Vector3.Dot(normal, Vector3.up) > 0.9f;
                        probe.SetPosition(0, ray.GetPoint(0.04f));
                        probe.SetPosition(1, validHit ? hitPoint : ray.GetPoint(3f));
                        probe.startColor = probe.endColor = validHit ? Color.green : Color.yellow;
                    }
                    else
                    {
                        var origin = Frame.InverseTransformPoint(ray.origin);
                        var direction = Frame.InverseTransformDirection(ray.direction);
                        var distance = direction.y < -0.01f ? (FloorY - origin.y) / direction.y : -1f;
                        var local = origin + direction * distance;
                        var cell = map != null ? map.Index(local.x, local.z) : -1;
                        validHit = distance > 0 && distance <= 5f && cell >= 0 && map.Cells[cell] == 2;
                        hitPoint = Frame.TransformPoint(local);
                        probe.SetPosition(0, ray.GetPoint(0.04f));
                        probe.SetPosition(1, validHit ? hitPoint : ray.GetPoint(3f));
                        probe.startColor = probe.endColor = validHit ? Color.green : Color.yellow;
                    }
                    probeMaterial.SetColor("_BaseColor", validHit ? Color.green : Color.yellow);
                }
                probe.enabled = tracked;
                if (!tracked) validHit = false;
                if (OVRInput.GetDown(OVRInput.Button.PrimaryThumbstick, OVRInput.Controller.RTouch))
                { CancelAim(); Status = "Selection cancelled. Open the menu to choose an action."; }
                else if (validHit && OVRInput.GetDown(OVRInput.Button.PrimaryHandTrigger, OVRInput.Controller.RTouch)) ConfirmAim();
            }
            if (hasGoal && map != null && Time.unscaledTime >= nextPlan && RoomReady)
            { nextPlan = Time.unscaledTime + 0.75f; PlanRoute(); }
            if (LiveEnabled && map != null && RoomReady && OVRManager.hasVrFocus && Time.unscaledTime >= nextLiveCheck)
            { nextLiveCheck = Time.unscaledTime + 0.08f; CheckLiveObstacles(); }
        }

        public void BeginFloorSelection()
        {
            if (!AppMode) { roomSource.LoadRoom(); return; }
            if (!RoomReady) { Status = "Capture or load an app scan first."; return; }
            scanner.PauseCapture();
            if (!scanner.EnableDepthForProbe()) return;
            ClearRoute(); aim = AimMode.Floor;
            Status = "Aim down at real floor. Right grip sets floor height; right stick cancels.";
            scanner.CloseMenu();
        }

        public void BeginDestinationSelection()
        {
            if (map == null) { Status = "Build the floor map first."; return; }
            ClearRoute();
            aim = AimMode.Destination;
            Status = "Aim at a green floor cell. Right side grip confirms; right stick click cancels.";
            scanner.CloseMenu();
        }

        private void ConfirmAim()
        {
            var local = Frame.InverseTransformPoint(hitPoint);
            if (aim == AimMode.Floor)
            {
                ClearMap(); appHasFloor = true; appFloorY = local.y;
                attemptedRevision = -1;
                scanner.MarkMetadataChanged();
                Status = "Floor selected. Open the menu and choose Route > Build floor map.";
            }
            else
            {
                goal = new MeshPoint(local.x, FloorY, local.z); hasGoal = true;
                EnsureRouteVisual(); PlanRoute();
            }
            CancelAim();
        }

        public async void BuildMap()
        {
            var showMap = !automaticBuild; automaticBuild = false;
            if (IsBuilding || scanner.IsWorking) return;
            if (!HasFloor) { Status = AppMode ? "Capture/load an app scan, then set its floor first." : roomSource.Status; return; }
            scanner.PauseCapture();
            CancelAim(); ClearMap();
            MeshPoint[] points; int[] indices; int floorIndices;
            try
            {
                if (AppMode)
                {
                    points = new MeshPoint[scanner.CapturedVertices.Count];
                    for (var i = 0; i < points.Length; i++) { var p = scanner.CapturedVertices[i]; points[i] = new MeshPoint(p.x, p.y, p.z); }
                    indices = new int[scanner.CapturedTriangles.Count];
                    for (var i = 0; i < indices.Length; i++) indices[i] = scanner.CapturedTriangles[i];
                    floorIndices = -1;
                }
                else roomSource.Snapshot(out points, out indices, out floorIndices);
            }
            catch (Exception error) { Status = "Could not read " + scanner.ScanMethodName + " geometry: " + error.Message; return; }
            var generation = ++buildGeneration;
            buildCancellation = new CancellationTokenSource();
            var token = buildCancellation.Token;
            var height = FloorY; var clearance = Clearance; var revision = CurrentRevision;
            var strict = !AppMode || StrictCoverage;
            IsBuilding = true; Status = "Building routes from " + scanner.ScanMethodName + "...";
            try
            {
                var result = await Task.Run(() => ScanNavigationGrid.Build(points, indices, height, clearance, token, strict, floorIndices), token);
                if (this == null || token.IsCancellationRequested || generation != buildGeneration || !RoomReady || revision != CurrentRevision) return;
                map = result;
                liveLayer = new LiveObstacleLayer(map);
                CreateMapVisual();
                if (showMap) { MapVisible = true; scanner.ShowFloorMap(); }
                Status = map.WalkableCount == 0 ? "No route area yet. Red: obstacle. Orange: extra margin. Purple: floor edge. Check counts below."
                    : scanner.ScanMethodName + " map ready. Choose a destination or Find an item.";
                if (liveRequested && map.WalkableCount > 0) EnableLiveChecks();
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { if (this != null) Status = error.Message; }
            finally
            {
                if (this != null && generation == buildGeneration)
                { IsBuilding = false; buildCancellation?.Dispose(); buildCancellation = null; }
            }
        }

        public void CycleClearance()
        {
            var current = Array.FindIndex(MarginOptions, value => Mathf.Abs(value - Clearance) < 0.001f);
            Clearance = MarginOptions[(current + 1) % MarginOptions.Length];
            PlayerPrefs.SetFloat("SpatialSense.ExtraObstacleMargin", Clearance); PlayerPrefs.Save();
            ClearMap();
            Status = $"Extra margin: {Clearance * 100:0} cm. Updating the map; live checks reset.";
            if (RoomReady) BuildMap();
        }

        public void CycleCoverage()
        {
            StrictCoverage = !StrictCoverage;
            ClearMap();
            Status = "Floor coverage: " + CoverageName + ". Rebuild the floor map to compare using the same scan.";
        }

        public void RestoreFloor(bool hasFloor, float floorY)
        {
            appHasFloor = hasFloor; appFloorY = floorY;
            GeometryChanged();
        }

        public void GeometryChanged(bool resetFloor = false)
        {
            scanner.NotifyScanChanged();
            if (resetFloor) appHasFloor = false;
            if (AppMode) { sourceRevision = CurrentRevision; attemptedRevision = -1; ClearMap(); }
        }

        public void SourceChanged()
        {
            ClearMap(); sourceRevision = CurrentRevision; attemptedRevision = -1;
            Status = "Using " + scanner.ScanMethodName + ". Walking maps use only the selected source.";
        }

        public void ClearRoute()
        {
            itemPlanningCancellation?.Cancel();
            routeGeneration++; RouteOwner = null; RouteAvailable = false;
            hasGoal = false; hasItemTarget = false; liveReplanPending = routeWasBlocked = false; lastPathHash = 0; CancelAim();
            if (route != null) route.positionCount = 0;
            if (marker != null) marker.enabled = false;
            Status = "Route cleared.";
        }

        public Task<(bool success, string reason)> FindItem(string objectId, Vector3 worldPosition)
            => FindItemAttempt(objectId, worldPosition, 0);

        private async Task<(bool success, string reason)> FindItemAttempt(string objectId, Vector3 worldPosition, int attempt)
        {
            if (map == null || !RoomReady) return (false, "Load the room and build its walking map before finding an item.");
            ClearRoute();
            var request = routeGeneration; var revision = CurrentRevision; var obstacles = obstacleRevision;
            var snapshot = map.RoutingSnapshot();
            var cancellation = new CancellationTokenSource(); itemPlanningCancellation = cancellation;
            var token = cancellation.Token;
            var start = Frame.InverseTransformPoint(rig.centerEyeAnchor.position);
            var item = Frame.InverseTransformPoint(worldPosition);
            Status = "Finding a reachable standing position near the saved item...";
            (bool success, List<MeshPoint> path, MeshPoint standing, string reason) result;
            try
            {
                result = await Task.Run(() =>
                {
                    var success = snapshot.TryRouteToItem(start.x, start.z, item.x, item.z, out var path, out var standing, out var reason, token);
                    return (success, path, standing, reason);
                }, token);
            }
            catch (OperationCanceledException) { return (false, "Item search cancelled."); }
            finally { if (itemPlanningCancellation == cancellation) itemPlanningCancellation = null; cancellation.Dispose(); }
            if (this == null || request != routeGeneration || !RoomReady || revision != CurrentRevision)
                return (false, "Item search cancelled because the selection or room changed.");
            if (obstacles != obstacleRevision)
                return attempt < 2 ? await FindItemAttempt(objectId, worldPosition, attempt + 1)
                    : (false, "The room is still changing. Look at the route area, then choose Find again.");
            if (!result.success) { Status = result.reason; return (false, result.reason); }
            goal = result.standing; hasGoal = true; RouteOwner = objectId; itemTarget = item; hasItemTarget = true;
            EnsureRouteVisual(); PlanRoute();
            return (RouteAvailable, RouteAvailable ? result.reason : Status);
        }

        public void StopSelection()
        {
            var selecting = aim != AimMode.None;
            CancelAim();
            if (selecting) Status = "Selection cancelled. Choose a new action from the menu.";
        }

        private void PlanRoute()
        {
            if (map == null || !RoomReady) return;
            var start = Frame.InverseTransformPoint(rig.centerEyeAnchor.position);
            EnsureRouteVisual();
            if (!map.TryRoute(start.x, start.z, goal.X, goal.Z, out var points, out var reason))
            {
                SetRouteBlocked(reason);
                if (hasItemTarget && itemPlanningCancellation == null && Time.unscaledTime >= nextEndpointRetry)
                    ReplanItemEndpoint();
                else if (!hasItemTarget) FinishLiveResponse(false);
                return;
            }
            if (routeWasBlocked)
            { recoveredEvents++; RecordLive("route_recovered"); }
            routeWasBlocked = false;
            RouteAvailable = true;
            route.positionCount = points.Count;
            for (var i = 0; i < points.Count; i++) route.SetPosition(i, new Vector3(points[i].X, points[i].Y, points[i].Z));
            var target = map.Centre(map.Index(goal.X, goal.Z));
            marker.enabled = true;
            for (var i = 0; i <= 32; i++)
            { var angle = i * Mathf.PI * 2f / 32; marker.SetPosition(i, new Vector3(target.X + Mathf.Cos(angle) * 0.15f, FloorY + 0.04f, target.Z + Mathf.Sin(angle) * 0.15f)); }
            Status = Vector2.Distance(new Vector2(start.x, start.z), new Vector2(target.X, target.Z)) < 0.3f
                ? RouteOwner != null ? "At the item. Look for its saved-location marker; choose Found it when confirmed." : "Destination reached."
                : "Route: " + scanner.ScanMethodName + (LiveEnabled ? " + live obstacle checks." : ". Live obstacle checks OFF.");
            ulong hash = 1469598103934665603UL;
            foreach (var point in points) { hash ^= (uint)map.Index(point.X, point.Z); hash = unchecked(hash * 1099511628211UL); }
            if (liveReplanPending && lastPathHash != 0 && hash != lastPathHash) routeUpdates++;
            lastPathHash = hash;
            FinishLiveResponse(true);
        }

        private void SetRouteBlocked(string reason)
        {
            route.positionCount = 0; marker.enabled = false; RouteAvailable = false;
            if (!routeWasBlocked) { blockedEvents++; RecordLive("route_blocked"); }
            routeWasBlocked = true;
            Status = "Route blocked. " + reason + (LiveEnabled ? " Looking for another route; observe the cleared space after removing an obstruction." : " Dynamic checks are OFF.");
        }

        private async void ReplanItemEndpoint()
        {
            // Keep the search and its saved-item target alive while a temporary
            // obstacle blocks the old standing point. Never clear item memory.
            nextEndpointRetry = Time.unscaledTime + 0.75f;
            var request = routeGeneration; var revision = CurrentRevision; var obstacles = obstacleRevision;
            var snapshot = map.RoutingSnapshot(); var target = itemTarget;
            var start = Frame.InverseTransformPoint(rig.centerEyeAnchor.position);
            var cancellation = new CancellationTokenSource(); itemPlanningCancellation = cancellation;
            var token = cancellation.Token;
            try
            {
                var result = await Task.Run(() =>
                {
                    var success = snapshot.TryRouteToItem(start.x, start.z, target.x, target.z, out _, out var standing, out var reason, token);
                    return (success, standing, reason);
                }, token);
                if (this == null || request != routeGeneration || !RoomReady || revision != CurrentRevision || obstacles != obstacleRevision) return;
                if (result.success) { goal = result.standing; PlanRoute(); }
                else { SetRouteBlocked(result.reason); FinishLiveResponse(false); }
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                if (this != null && request == routeGeneration)
                { SetRouteBlocked("Could not update the item approach. It will retry."); FinishLiveResponse(false); Debug.LogWarning(error); }
            }
            finally { if (itemPlanningCancellation == cancellation) itemPlanningCancellation = null; cancellation.Dispose(); }
        }

        private void FinishLiveResponse(bool available)
        {
            if (!liveReplanPending) return;
            lastResponseMs = (float)((Time.realtimeSinceStartupAsDouble - liveChangeAt) * 1000);
            RecordLive(available ? "live_route_response" : "live_route_no_path");
            liveReplanPending = false;
        }

        private void RecordLive(string eventName)
        {
            diagnostics?.Record(eventName, RouteOwner, JsonUtility.ToJson(new LiveMeasure
            {
                scanMethod = scanner.ScanMethodName, testId = testId, obstacleRevision = obstacleRevision, routeGeneration = routeGeneration,
                activeCells = liveLayer?.ActiveCount ?? 0,
                addedCells = liveLayer?.AddedLastBatch ?? 0, clearedCells = liveLayer?.ClearedLastBatch ?? 0,
                validRays = lastRayCount, extraMargin = Clearance, responseMs = lastResponseMs, samplingMs = lastSamplingMs,
                observedToConfirmedMs = liveLayer?.LastConfirmationDelayMs ?? -1
            }));
        }

        private void EnsureRouteVisual()
        {
            if (routeRoot != null) return;
            routeRoot = new GameObject("Route from captured floor"); routeRoot.transform.SetParent(Frame, false);
            route = routeRoot.AddComponent<LineRenderer>(); StyleLine(route, routeMaterial, 0.025f); route.useWorldSpace = false; route.positionCount = 0;
            var ring = new GameObject("Destination ring"); ring.transform.SetParent(routeRoot.transform, false);
            marker = ring.AddComponent<LineRenderer>(); StyleLine(marker, routeMaterial, 0.015f); marker.useWorldSpace = false; marker.positionCount = 33; marker.enabled = false;
        }

        private void CreateMapVisual()
        {
            ClearMapVisual();
            mapRoot = new GameObject("Captured floor evidence map"); mapRoot.transform.SetParent(Frame, false);
            mapRoot.SetActive(MapVisible && RoomReady);
            var colours = new[] { new Color(0.45f, 0.45f, 0.45f, 0.18f), new Color(1f, 0.15f, 0.1f, 0.35f), new Color(0.15f, 1f, 0.3f, 0.3f), new Color(0.7f, 0.25f, 1f, 0.35f), new Color(1f, 0.55f, 0.05f, 0.3f) };
            for (var state = 0; state < 5; state++)
            {
                var vertices = new List<Vector3>(); var triangles = new List<int>();
                for (var i = 0; i < map.CellCount; i++)
                {
                    if (map.Cells[i] != state) continue;
                    var p = map.Centre(i); var half = ScanNavigationGrid.CellSize * 0.47f; var offset = vertices.Count;
                    vertices.Add(new Vector3(p.X - half, p.Y, p.Z - half)); vertices.Add(new Vector3(p.X + half, p.Y, p.Z - half));
                    vertices.Add(new Vector3(p.X + half, p.Y, p.Z + half)); vertices.Add(new Vector3(p.X - half, p.Y, p.Z + half));
                    triangles.AddRange(new[] { offset, offset + 2, offset + 1, offset, offset + 3, offset + 2 });
                }
                var go = new GameObject("Floor state " + state, typeof(MeshFilter), typeof(MeshRenderer)); go.transform.SetParent(mapRoot.transform, false);
                var mesh = new Mesh { indexFormat = IndexFormat.UInt32 }; mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.RecalculateBounds(); visualMeshes.Add(mesh);
                go.GetComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = go.GetComponent<MeshRenderer>(); renderer.sharedMaterial = MakeMaterial(colours[state]);
                renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            }
        }

        private Material MakeMaterial(Color colour)
        { var material = new Material(baseMaterial); material.SetColor("_BaseColor", colour); materials.Add(material); return material; }
        private static void StyleLine(LineRenderer line, Material material, float width)
        { line.sharedMaterial = material; line.startWidth = line.endWidth = width; line.shadowCastingMode = ShadowCastingMode.Off; line.receiveShadows = false; }
        private void CancelAim()
        {
            var wasAiming = aim != AimMode.None;
            aim = AimMode.None; validHit = false; if (probe != null) probe.enabled = false;
            if (wasAiming && scanner != null) scanner.DisableProbeDepth();
        }
        private void ClearMap()
        {
            LiveEnabled = false; liveLayer = null; scanner?.DisableProbeDepth();
            if (liveRoot != null) Destroy(liveRoot);
            if (liveMesh != null) Destroy(liveMesh);
            liveRoot = null; liveMesh = null; targetCursor = sweepCursor = 0;
            buildGeneration++; buildCancellation?.Cancel(); buildCancellation?.Dispose(); buildCancellation = null; IsBuilding = false;
            map = null; ClearRoute();
            ClearMapVisual();
            if (routeRoot != null) Destroy(routeRoot);
            routeRoot = null; route = marker = null;
        }
        private void ClearMapVisual()
        {
            if (mapRoot != null) Destroy(mapRoot);
            foreach (var mesh in visualMeshes) if (mesh != null) Destroy(mesh);
            visualMeshes.Clear(); mapRoot = null;
            // Keep the two line materials; floor materials belong to the cleared map.
            for (var i = materials.Count - 1; i >= 2; i--) { Destroy(materials[i]); materials.RemoveAt(i); }
        }
        public void ToggleLive()
        {
            if (map == null) return;
            if (LiveEnabled)
            {
                liveRequested = false; LiveEnabled = false; scanner.DisableProbeDepth();
                Status = "Dynamic checks paused. Confirmed blocks remain until observed clear, reset, or the map is rebuilt.";
            }
            else { liveRequested = true; EnableLiveChecks(); }
            PlayerPrefs.SetInt("SpatialSense.DynamicRouting", liveRequested ? 1 : 0); PlayerPrefs.Save();
            RecordLive(LiveEnabled ? "dynamic_checks_enabled" : "dynamic_checks_disabled");
        }
        private void EnableLiveChecks()
        {
            if (!scanner.EnableDepthForProbe())
            { LiveEnabled = false; Status = "Dynamic depth unavailable. Check spatial-data permission and try Dynamic routing again."; return; }
            LiveEnabled = true; nextLiveCheck = 0; lastDepthAt = Time.unscaledTime;
            Status = "Dynamic checks ON. Look toward changed furniture; magenta tiles mark confirmed new obstacles.";
        }
        public void ToggleLiveOverlay()
        { LiveOverlayVisible = !LiveOverlayVisible; }
        public void ResetLiveMeasurements()
        {
            routeUpdates = blockedEvents = recoveredEvents = 0; lastResponseMs = -1;
            testId = Guid.NewGuid().ToString("N");
            RecordLive("dynamic_test_marked");
            Status = "Test marked; counters reset. Observe a new obstruction, then observe the space again after removing it.";
        }
        private void RefreshLiveVisual()
        {
            if (liveRoot == null)
            {
                liveRoot = new GameObject("New live obstacles - magenta", typeof(MeshFilter), typeof(MeshRenderer));
                liveRoot.transform.SetParent(Frame, false);
                liveMesh = new Mesh { indexFormat = IndexFormat.UInt32 }; liveMesh.MarkDynamic();
                liveRoot.GetComponent<MeshFilter>().sharedMesh = liveMesh;
                var renderer = liveRoot.GetComponent<MeshRenderer>(); renderer.sharedMaterial = liveMaterial;
                renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            }
            var vertices = new List<Vector3>(); var triangles = new List<int>();
            // Draw only the changed cells. Keep the large baseline floor mesh
            // instead of destroying/rebuilding all five map meshes on each hit.
            for (var i = 0; i < map.CellCount; i++)
            {
                if (map.BaseCell(i) != 2 || map.Cells[i] == 2) continue;
                var p = map.Centre(i); var half = ScanNavigationGrid.CellSize * 0.47f; var offset = vertices.Count;
                vertices.Add(new Vector3(p.X - half, FloorY + 0.055f, p.Z - half));
                vertices.Add(new Vector3(p.X + half, FloorY + 0.055f, p.Z - half));
                vertices.Add(new Vector3(p.X + half, FloorY + 0.055f, p.Z + half));
                vertices.Add(new Vector3(p.X - half, FloorY + 0.055f, p.Z + half));
                triangles.AddRange(new[] { offset, offset + 2, offset + 1, offset, offset + 3, offset + 2 });
            }
            liveMesh.Clear(); liveMesh.SetVertices(vertices); liveMesh.SetTriangles(triangles, 0); liveMesh.RecalculateBounds();
            liveRoot.SetActive(LiveOverlayVisible && RoomReady && OVRManager.hasVrFocus);
        }
        public void ClearLiveLayer()
        {
            if (map == null) return;
            liveLayer = new LiveObstacleLayer(map); map.ApplyLiveObstacles(liveLayer.ActivePoints(), Clearance);
            obstacleRevision++;
            RefreshLiveVisual(); if (hasGoal) PlanRoute();
            RecordLive("live_layer_manually_reset");
            Status = "Live layer reset. Furniture from the selected scan remains blocked.";
        }
        private void CheckLiveObstacles()
        {
            liveRays.Clear(); var head = rig.centerEyeAnchor;
            var origin = Frame.InverseTransformPoint(head.position);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            for (var query = 0; query < 24; query++)
            {
                Vector3 direction;
                if (query % 4 == 0 && liveLayer.TrackedCount > 0 && TryEvidenceDirection(head, out var targetDirection))
                    direction = targetDirection;
                else
                {
                    // Resume across batches, including after a budget overrun.
                    // The coprime stride covers the complete 12 x 8 field.
                    var index = sweepCursor; sweepCursor = (sweepCursor + 37) % 96;
                    direction = head.rotation * new Vector3(Mathf.Lerp(-0.55f, 0.55f, (index % 12) / 11f), Mathf.Lerp(-0.5f, 0.4f, (index / 12) / 7f), 1).normalized;
                }
                if (scanner.TryProbe(new Ray(head.position, direction), out var point, out _))
                {
                    // Avoid treating very near controller/headset returns as
                    // room furniture. Floor-height rejection happens in layer.
                    var distance = Vector3.Distance(head.position, point);
                    if (distance >= 0.4f && distance <= 3.5f)
                    { var p = Frame.InverseTransformPoint(point); liveRays.Add(new DepthObservationRay(new MeshPoint(origin.x, origin.y, origin.z), new MeshPoint(p.x, p.y, p.z))); }
                }
                if (clock.Elapsed.TotalMilliseconds > 3) break;
            }
            lastSamplingMs = (float)clock.Elapsed.TotalMilliseconds; lastRayCount = liveRays.Count;
            if (lastRayCount > 0) lastDepthAt = Time.unscaledTime;
            if (!liveLayer.Observe(liveRays, Time.realtimeSinceStartupAsDouble)) return;
            liveChangeAt = Time.realtimeSinceStartupAsDouble; liveReplanPending = hasGoal; lastResponseMs = -1;
            map.ApplyLiveObstacles(liveLayer.ActivePoints(), Clearance);
            obstacleRevision++;
            RefreshLiveVisual();
            RecordLive("live_obstacle_change");
            if (hasGoal) PlanRoute();
        }
        private bool TryEvidenceDirection(Transform head, out Vector3 direction)
        {
            direction = default;
            for (var attempt = 0; attempt < Math.Min(liveLayer.TrackedCount, 12); attempt++)
            {
                var p = liveLayer.ObservationPoint(targetCursor % liveLayer.TrackedCount); targetCursor++;
                var world = Frame.TransformPoint(new Vector3(p.X, p.Y, p.Z));
                var local = head.InverseTransformPoint(world);
                if (local.z < 0.4f || local.sqrMagnitude > 3.5f * 3.5f || Mathf.Abs(local.x / local.z) > 0.55f || Mathf.Abs(local.y / local.z) > 0.5f) continue;
                direction = (world - head.position).normalized; return true;
            }
            return false;
        }
        private void OnDestroy()
        { ClearMap(); if (liveMaterial != null) Destroy(liveMaterial); foreach (var material in materials) if (material != null) Destroy(material); }
    }
}
