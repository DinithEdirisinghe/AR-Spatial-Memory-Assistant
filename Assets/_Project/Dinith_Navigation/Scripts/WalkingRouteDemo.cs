using System;
using System.Collections;
using System.Collections.Generic;
using Meta.XR.MRUtilityKit;
using SpatialSense.ManualDemo;
using SpatialSense.RoomDemo;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

namespace SpatialSense.Navigation
{
    [DisallowMultipleComponent]
    public sealed class WalkingRouteDemo : MonoBehaviour, Project.Core.Interfaces.INavMeshService
    {
        [SerializeField] private RoomUnderstandingDemo roomDemo;
        [SerializeField] private ManualObjectDemo objectDemo;
        [SerializeField] private OVRCameraRig cameraRig;
        [SerializeField] private SceneNavigation sceneNavigation;
        [SerializeField] private Material routeMaterial;
        [SerializeField, Min(0f)] private float scanBuffer = 0.03f;
        [SerializeField] private MonoBehaviour guidanceProvider;
        [SerializeField] private MonoBehaviour spatialRegistryProvider;
        private Project.Core.Interfaces.ISpatialRegistryService spatialRegistry;
        public MonoBehaviour GuidanceProvider => guidanceProvider;
        private Project.Core.Interfaces.IWayfindingRenderer guidance;
        public event Action<string> Diagnostic;
        public bool HasRouteFailure => !completeRoute && lastFailure != null;
        public void ConfigureGuidance(MonoBehaviour provider)
        {
            if (!(provider is Project.Core.Interfaces.IWayfindingRenderer)) throw new ArgumentException("Provider must implement IWayfindingRenderer.");
            guidanceProvider = provider;
        }
        public void ConfigureRegistry(MonoBehaviour provider)
        {
            if (!(provider is Project.Core.Interfaces.ISpatialRegistryService)) throw new ArgumentException("Provider must implement ISpatialRegistryService.");
            spatialRegistryProvider = provider;
        }
        private readonly List<NavMeshObstacle> footprints = new List<NavMeshObstacle>();
        private GameObject mapPreview;
        private readonly List<Mesh> previewMeshes = new List<Mesh>();
        private bool showMap;
        private string lastDiagnostic;
        private string lastFailure;
        private NavMeshQueryFilter filter;
        private MRUKRoom room;
        private int builtRevision = -1;
        private int customAgentId = -1;
        private bool building;
        private bool navigationReady;
        private bool completeRoute;
        private bool goalValid;
        private Vector3 goal;
        private Vector3 plannedTarget;
        private float floorY;
        private float nextPlan;
        private float routeLength;
        private float nextBuildAttempt;
        private const MRUKAnchor.SceneLabels Walls = MRUKAnchor.SceneLabels.WALL_FACE
            | MRUKAnchor.SceneLabels.INVISIBLE_WALL_FACE | MRUKAnchor.SceneLabels.INNER_WALL_FACE;

        public string StatusText { get; private set; } = "Preparing the walking map...";
        public string DistanceText { get; private set; } = "Waiting for the room and floor.";
        public bool CanConfirmAtTarget => objectDemo != null && objectDemo.IsSearching && completeRoute
            && navigationReady && roomDemo.IsRoomReady && roomDemo.RoomRevision == builtRevision
            && routeLength <= 0.65f
            && HorizontalDistance(cameraRig.centerEyeAnchor.position, goal) <= 0.55f;

        public void Configure(RoomUnderstandingDemo roomLoading, ManualObjectDemo objects, OVRCameraRig rig,
            SceneNavigation navigation, Material material)
        {
            roomDemo = roomLoading;
            objectDemo = objects;
            cameraRig = rig;
            sceneNavigation = navigation;
            routeMaterial = material;
        }

        private void Start()
        {
            if (roomDemo == null || objectDemo == null || cameraRig == null || sceneNavigation == null || routeMaterial == null)
            {
                Debug.LogError("[SpatialSense] Run SpatialSense > Create Walking Route Demo to configure navigation.", this);
                enabled = false;
                return;
            }
            guidance = guidanceProvider as Project.Core.Interfaces.IWayfindingRenderer;
            spatialRegistry = spatialRegistryProvider as Project.Core.Interfaces.ISpatialRegistryService;
            if (guidance == null)
            {
                var fallback = gameObject.AddComponent<Project.Core.Integration.DefaultGuidanceRenderer>();
                fallback.Configure(routeMaterial);
                guidance = fallback;
            }
            roomDemo.SetNavigationMapControls(true, false);
        }

        private void Update()
        {
            if (OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.LTouch))
            {
                showMap = !showMap;
                if (mapPreview != null) mapPreview.SetActive(showMap);
                roomDemo.SetNavigationMapControls(true, showMap);
            }
            if (!roomDemo.IsRoomReady || roomDemo.RoomRevision != builtRevision)
            {
                navigationReady = false;
                HideRoute();
                goalValid = false;
                if (mapPreview != null) mapPreview.SetActive(false);
                if (!roomDemo.IsRoomReady)
                {
                    StatusText = "Waiting for the current room and its floor.";
                    DistanceText = "Room reload clears registered locations.";
                    return;
                }
                if (!building && Time.unscaledTime >= nextBuildAttempt) StartCoroutine(BuildWalkingMap());
                return;
            }
            if (building || !navigationReady) return;
            if (!objectDemo.IsSearching)
            {
                lastFailure = null;
                HideRoute();
                goalValid = false;
                nextPlan = 0f;
                return;
            }
            if (!TryGetSelectedTarget(out var currentTarget))
            {
                HideRoute();
                StatusText = "Selected object location is unavailable. Wait for its anchor to be located.";
                return;
            }
            if ((plannedTarget - currentTarget).sqrMagnitude > 0.0025f)
            {
                goalValid = false;
                nextPlan = 0f;
            }
            if (Time.unscaledTime >= nextPlan)
            {
                nextPlan = Time.unscaledTime + 0.5f;
                PlanRoute();
            }
            if (completeRoute)
            {
                StatusText = CanConfirmAtTarget ? $"At the viewing spot. Locate {objectDemo.CurrentObjectName}, point at its marker and press the trigger to confirm."
                    : "Follow the blue route toward the green standing-position ring.";
                DistanceText = $"Walking route: {routeLength:F1} m | Blue floor arrows lead to the green ring.\nSaved room scan | Left trigger: inspect walking map";
            }
        }

        private IEnumerator BuildWalkingMap()
        {
            building = true;
            navigationReady = false;
            StatusText = "Building the walking map around mapped furniture...";
            DistanceText = "Please wait. A route appears only after the map is ready.";
            var revision = roomDemo.RoomRevision;
            room = roomDemo.CurrentRoom;
            var oldAgent = customAgentId;
            var oldSurface = sceneNavigation.GetComponent<NavMeshSurface>();
            var oldData = oldSurface != null ? oldSurface.navMeshData : null;
            customAgentId = -1;
            try
            {
                if (room == null || room.FloorAnchors.Count == 0) throw new InvalidOperationException("No current floor.");
                floorY = room.FloorAnchors[0].transform.position.y;
                sceneNavigation.BuildSceneNavMeshForRoom(room);
                var surface = sceneNavigation.GetComponent<NavMeshSurface>();
                if (surface == null || surface.navMeshData == null) throw new InvalidOperationException("Walking map was not generated.");
                customAgentId = surface.agentTypeID;
                filter = new NavMeshQueryFilter { agentTypeID = customAgentId, areaMask = NavMesh.AllAreas };
                NormalizeFurnitureFootprints();
            }
            catch (Exception error)
            {
                Debug.LogException(error, this);
                StatusText = "No route available: the walking map could not be built. Check the room scan and press left Y.";
                DistanceText = "No walking guidance is shown.";
                nextBuildAttempt = Time.unscaledTime + 5f;
                building = false;
                yield break;
            }
            finally
            {
                if (oldAgent >= 0 && oldAgent != customAgentId) NavMesh.RemoveSettings(oldAgent);
                if (oldData != null) Destroy(oldData);
            }
            // Carving is applied by Unity on subsequent frames, not within the build call.
            yield return null;
            yield return new WaitForSecondsRealtime(0.3f);
            yield return null;
            if (roomDemo.IsRoomReady && roomDemo.RoomRevision == revision)
            {
                builtRevision = revision;
                navigationReady = true;
                CreateMapPreview();
                goalValid = false;
                nextPlan = 0f;
            }
            building = false;
        }

        private void NormalizeFurnitureFootprints()
        {
            footprints.Clear();
            foreach (var pair in sceneNavigation.Obstacles)
            {
                var anchor = pair.Key;
                var obstacle = pair.Value.GetComponent<NavMeshObstacle>();
                if (anchor == null || obstacle == null) continue;
                var bounds = anchor.VolumeBounds ?? new Bounds(new Vector3(anchor.PlaneRect.Value.center.x,
                    anchor.PlaneRect.Value.center.y, 0), new Vector3(anchor.PlaneRect.Value.width, anchor.PlaneRect.Value.height, 0.03f));
                var right = Vector3.ProjectOnPlane(anchor.transform.right, Vector3.up);
                if (right.sqrMagnitude < 0.001f) right = Vector3.ProjectOnPlane(anchor.transform.up, Vector3.up);
                var rotation = right.sqrMagnitude > 0.001f
                    ? Quaternion.LookRotation(Vector3.Cross(right.normalized, Vector3.up), Vector3.up) : Quaternion.identity;
                var inverse = Quaternion.Inverse(rotation);
                var min = Vector3.one * float.PositiveInfinity;
                var max = Vector3.one * float.NegativeInfinity;
                for (var i = 0; i < 8; i++)
                {
                    var local = new Vector3((i & 1) == 0 ? bounds.min.x : bounds.max.x,
                        (i & 2) == 0 ? bounds.min.y : bounds.max.y, (i & 4) == 0 ? bounds.min.z : bounds.max.z);
                    var point = inverse * anchor.transform.TransformPoint(local);
                    min = Vector3.Min(min, point);
                    max = Vector3.Max(max, point);
                }
                if (min.y > floorY + sceneNavigation.AgentHeight)
                {
                    obstacle.enabled = false;
                    continue;
                }
                // Unity carving already incorporates the baked agent radius. Only add a small scan buffer here.
                var top = Mathf.Max(floorY + sceneNavigation.AgentHeight, max.y);
                var center = new Vector3((min.x + max.x) * 0.5f, (floorY - 0.05f + top) * 0.5f, (min.z + max.z) * 0.5f);
                obstacle.transform.SetPositionAndRotation(rotation * center, rotation);
                obstacle.center = Vector3.zero;
                obstacle.size = new Vector3(RouteMath.CarvingWidth(max.x - min.x, scanBuffer),
                    top - floorY + 0.05f, RouteMath.CarvingWidth(max.z - min.z, scanBuffer));
                obstacle.carving = true;
                obstacle.carveOnlyStationary = false;
                footprints.Add(obstacle);
            }
        }

        private void PlanRoute()
        {
            HideRoute();
            if (!TryGetSelectedTarget(out plannedTarget)) { NoRoute("Selected object location is not currently located."); return; }
            var projectedStart = new Vector3(cameraRig.centerEyeAnchor.position.x, floorY, cameraRig.centerEyeAnchor.position.z);
            if (!room.IsPositionInRoom(projectedStart, false))
            {
                NoRoute("The saved floor boundary does not contain your position. Check room alignment; left trigger shows the map.");
                return;
            }
            if (CrossesFootprint(projectedStart, projectedStart))
            {
                NoRoute("Your tracked position is inside mapped obstacle clearance. Check the red outlines with the left trigger.");
                return;
            }
            if (!TrySampleFloor(projectedStart, 0.45f, out var start))
            {
                NoRoute("No walking surface was found beneath you. Left trigger shows the saved walking map.");
                return;
            }
            NavMeshPath chosen = null;
            if (goalValid && TryCompletePath(start, goal, out chosen)) { DrawRoute(chosen); return; }
            goalValid = false;
            var best = float.PositiveInfinity;
            var sampledCount = 0;
            var visibleCount = 0;
            var connectedCount = 0;
            // First consider the closest navmesh point; a proposed goal can project out from under a table.
            var proposals = new List<Vector3> { new Vector3(plannedTarget.x, floorY, plannedTarget.z) };
            var objectFloor = proposals[0];
            if (NavMesh.SamplePosition(objectFloor, out var nearest, 2.2f, filter)) proposals.Add(nearest.position);
            var radii = new[] { 0.3f, 0.6f, 0.95f, 1.3f, 1.65f, 2f };
            foreach (var radius in radii)
            for (var i = 0; i < 16; i++)
            {
                var angle = i * Mathf.PI * 2f / 16f;
                proposals.Add(new Vector3(plannedTarget.x + Mathf.Cos(angle) * radius, floorY, plannedTarget.z + Mathf.Sin(angle) * radius));
            }
            foreach (var proposal in proposals)
            {
                if (!TrySampleFloor(proposal, 0.35f, out var candidate, true) || HorizontalDistance(candidate, plannedTarget) > 2.2f) continue;
                sampledCount++;
                if (!CanViewTarget(candidate)) continue;
                visibleCount++;
                if (!TryCompletePath(start, candidate, out var path)) continue;
                connectedCount++;
                var score = PathLength(path.corners) + HorizontalDistance(candidate, plannedTarget) * 2f;
                if (score >= best) continue;
                best = score;
                chosen = path;
                goal = candidate;
            }
            if (chosen == null)
            {
                var reason = sampledCount == 0 ? "The map has no nearby standing space around this object."
                    : visibleCount == 0 ? "Nearby standing spots are separated from the object by mapped walls."
                    : "Nearby standing spots exist, but the walking map does not connect them to you.";
                NoRoute(reason + " Left trigger shows the map.");
                LogDiagnostic($"No route: sampled={sampledCount}, visible={visibleCount}, complete={connectedCount}, obstacles={footprints.Count}, start={start:F2}, target={plannedTarget:F2}");
                return;
            }
            goalValid = true;
            DrawRoute(chosen);
        }

        private bool TrySampleFloor(Vector3 point, float radius, out Vector3 sampled, bool proposedGoal = false)
        {
            sampled = default;
            if (!NavMesh.SamplePosition(point, out var hit, radius, filter) || Mathf.Abs(hit.position.y - floorY) > 0.15f
                || !room.IsPositionInRoom(hit.position, false) || CrossesFootprint(hit.position, hit.position)
                || (!proposedGoal && CrossesFootprint(point, hit.position))) return false;
            sampled = hit.position;
            return true;
        }

        private bool TryCompletePath(Vector3 start, Vector3 end, out NavMeshPath path)
        {
            path = new NavMeshPath();
            if (!NavMesh.CalculatePath(start, end, filter, path) || path.status != NavMeshPathStatus.PathComplete) return false;
            var corners = path.corners;
            if (corners.Length == 0 || Vector3.Distance(corners[corners.Length - 1], end) > 0.15f) return false;
            for (var i = 1; i < corners.Length; i++)
                if (CrossesFootprint(corners[i - 1], corners[i])) return false;
            return true;
        }

        private bool CanViewTarget(Vector3 position)
        {
            var viewpoint = position + Vector3.up * Mathf.Clamp(cameraRig.centerEyeAnchor.position.y - floorY, 1f, 2.2f);
            var delta = plannedTarget - viewpoint;
            return delta.magnitude < 0.06f || !room.Raycast(new Ray(viewpoint, delta.normalized), delta.magnitude - 0.06f,
                new LabelFilter(Walls), out RaycastHit _);
        }

        private bool CrossesFootprint(Vector3 start, Vector3 end)
        {
            foreach (var obstacle in footprints)
            {
                if (obstacle == null || !obstacle.enabled) continue;
                var a = obstacle.transform.InverseTransformPoint(start);
                var b = obstacle.transform.InverseTransformPoint(end);
                var half = obstacle.size * 0.5f;
                if (RouteMath.SegmentViolatesClearance(a.x, a.z, b.x, b.z,
                    -half.x, -half.z, half.x, half.z, Mathf.Max(0, sceneNavigation.AgentRadius - 0.02f))) return true;
            }
            return false;
        }

        private void DrawRoute(NavMeshPath path)
        {
            lastFailure = null;
            completeRoute = true;
            var corners = path.corners;
            routeLength = PathLength(corners);
            guidance?.DisplayGuidancePath(corners);
            LogDiagnostic($"Complete route: corners={corners.Length}, length={routeLength:F1}m, obstacles={footprints.Count}, goal={goal:F2}");
        }

        private void CreateMapPreview()
        {
            ClearMapPreview();
            mapPreview = new GameObject("Walking map preview");
            mapPreview.transform.SetParent(transform, false);
            var map = NavMesh.CalculateTriangulation();
            var worldVertices = new Vector3[map.vertices.Length];
            for (var i = 0; i < worldVertices.Length; i++) worldVertices[i] = map.vertices[i] + Vector3.up * 0.025f;
            var edges = new List<int>();
            for (var i = 0; i + 2 < map.indices.Length; i += 3)
            {
                edges.Add(map.indices[i]); edges.Add(map.indices[i + 1]);
                edges.Add(map.indices[i + 1]); edges.Add(map.indices[i + 2]);
                edges.Add(map.indices[i + 2]); edges.Add(map.indices[i]);
            }
            AddPreviewLines("Walkable floor", new Color(0.2f, 0.6f, 1f), worldVertices, edges.ToArray());
            var blockedVertices = new List<Vector3>();
            var blockedEdges = new List<int>();
            foreach (var obstacle in footprints)
            {
                if (obstacle == null || !obstacle.enabled) continue;
                var half = obstacle.size * 0.5f;
                var first = blockedVertices.Count;
                for (var corner = 0; corner < 4; corner++)
                for (var step = 0; step <= 8; step++)
                {
                    var angle = (corner * 90f + step * 90f / 8f) * Mathf.Deg2Rad;
                    var center = new Vector3(corner == 0 || corner == 3 ? half.x : -half.x, 0, corner < 2 ? half.z : -half.z);
                    var point = obstacle.transform.TransformPoint(center + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * sceneNavigation.AgentRadius);
                    point.y = floorY + 0.065f;
                    blockedVertices.Add(point);
                }
                var count = blockedVertices.Count - first;
                for (var i = 0; i < count; i++) { blockedEdges.Add(first + i); blockedEdges.Add(first + (i + 1) % count); }
            }
            AddPreviewLines("Reserved obstacle clearance", Color.red, blockedVertices.ToArray(), blockedEdges.ToArray());
            mapPreview.SetActive(showMap);
            Debug.Log($"[SpatialSense Navigation] Map v2 built: vertices={map.vertices.Length}, triangles={map.indices.Length / 3}, obstacles={footprints.Count}, radius={sceneNavigation.AgentRadius:F2}, scanBuffer={scanBuffer:F2}", this);
        }

        private void AddPreviewLines(string name, Color colour, Vector3[] worldVertices, int[] indices)
        {
            var meshObject = MakeMeshObject(name, colour, mapPreview.transform);
            var vertices = new Vector3[worldVertices.Length];
            for (var i = 0; i < vertices.Length; i++) vertices[i] = meshObject.transform.InverseTransformPoint(worldVertices[i]);
            var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            mesh.vertices = vertices;
            mesh.SetIndices(indices, MeshTopology.Lines, 0);
            mesh.RecalculateBounds();
            meshObject.GetComponent<MeshFilter>().sharedMesh = mesh;
            previewMeshes.Add(mesh);
        }

        private GameObject MakeMeshObject(string name, Color colour, Transform parent)
        {
            var meshObject = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            meshObject.transform.SetParent(parent, false);
            var renderer = meshObject.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = routeMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            var properties = new MaterialPropertyBlock();
            properties.SetColor("_BaseColor", colour);
            renderer.SetPropertyBlock(properties);
            return meshObject;
        }

        private void ClearMapPreview()
        {
            if (mapPreview != null) { mapPreview.SetActive(false); Destroy(mapPreview); }
            foreach (var mesh in previewMeshes) if (mesh != null) Destroy(mesh);
            previewMeshes.Clear();
        }

        private void LogDiagnostic(string message)
        {
            if (lastDiagnostic == message) return;
            lastDiagnostic = message;
            Diagnostic?.Invoke(message);
            Debug.Log($"[SpatialSense Navigation] {message}", this);
        }

        private LineRenderer MakeLine(string label, Color colour, float width)
        {
            var line = new GameObject(label).AddComponent<LineRenderer>();
            line.transform.SetParent(transform, false);
            line.sharedMaterial = routeMaterial;
            line.useWorldSpace = true;
            line.widthMultiplier = width;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            var properties = new MaterialPropertyBlock();
            properties.SetColor("_BaseColor", colour);
            line.SetPropertyBlock(properties);
            line.enabled = false;
            return line;
        }

        private void NoRoute(string reason)
        {
            goalValid = false;
            StatusText = "No route available. " + reason;
            DistanceText = "No walking guidance is shown. Left Y reloads the saved room.";
            if (lastFailure != StatusText)
            {
                Diagnostic?.Invoke(StatusText);
                Debug.Log($"[SpatialSense Navigation] {StatusText}", this);
                lastFailure = StatusText;
            }
        }

        private void HideRoute()
        {
            completeRoute = false;
            guidance?.ClearGuidancePath();
        }

        private static float HorizontalDistance(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));

        private bool TryGetSelectedTarget(out Vector3 target)
        {
            target = objectDemo.RegisteredPosition;
            return spatialRegistry == null || spatialRegistry.TryGetTargetCoordinates(Project.Core.Integration.ObservationPolicy.IdFor(objectDemo.CurrentObjectName), out target);
        }

        public bool TryCalculatePath(Vector3 startWorldPos, Vector3 targetWorldPos, out Vector3[] waypoints)
        {
            waypoints = Array.Empty<Vector3>();
            if (!navigationReady || roomDemo == null || !roomDemo.IsRoomReady || roomDemo.RoomRevision != builtRevision
                || !Finite(startWorldPos) || !Finite(targetWorldPos)) return false;
            // Low-level queries require a reachable floor endpoint, not a tabletop object coordinate.
            var projectedStart = new Vector3(startWorldPos.x, floorY, startWorldPos.z);
            var projectedTarget = new Vector3(targetWorldPos.x, floorY, targetWorldPos.z);
            if (!TrySampleFloor(projectedStart, 0.45f, out var start) || !TrySampleFloor(projectedTarget, 0.2f, out var end)
                || !TryCompletePath(start, end, out var path)) return false;
            waypoints = path.corners;
            return true;
        }

        public void UpdateNavMeshRegion(Bounds dirtyBounds)
        {
            builtRevision = -1;
            navigationReady = false;
            goalValid = false;
            HideRoute();
            LogDiagnostic("Refresh requested: prototype rebuilds the current mapped room; localized incremental updates are not implemented.");
        }

        private static bool Finite(Vector3 p) => !float.IsNaN(p.x) && !float.IsNaN(p.y) && !float.IsNaN(p.z)
            && !float.IsInfinity(p.x) && !float.IsInfinity(p.y) && !float.IsInfinity(p.z);

        private static float PathLength(Vector3[] corners)
        {
            var length = 0f;
            for (var i = 1; i < corners.Length; i++) length += Vector3.Distance(corners[i - 1], corners[i]);
            return length;
        }

        private void OnDisable()
        {
            StopAllCoroutines();
            building = false;
            navigationReady = false;
            builtRevision = -1;
            HideRoute();
        }

        private void OnDestroy()
        {
            ClearMapPreview();
            if (sceneNavigation != null)
            {
                var surface = sceneNavigation.GetComponent<NavMeshSurface>();
                var data = surface != null ? surface.navMeshData : null;
                sceneNavigation.RemoveNavMeshData();
                if (data != null) Destroy(data);
            }
            if (customAgentId >= 0) NavMesh.RemoveSettings(customAgentId);
        }
    }
}
