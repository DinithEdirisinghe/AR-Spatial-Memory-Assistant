using System;
using Meta.XR.MRUtilityKit;
using SpatialSense.ManualDemo;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace SpatialSense.RoomDemo
{
    [DisallowMultipleComponent]
    public sealed class RoomUnderstandingDemo : MonoBehaviour
    {
        [SerializeField] private MRUK roomKit;
        [SerializeField] private OVRCameraRig cameraRig;
        [SerializeField] private ManualObjectDemo objectDemo;
        [SerializeField] private Material outlineMaterial;
        [SerializeField] private Font displayFont;
        private Text statusText;
        private Text controlsText;
        private GameObject statusPanel;
        private bool panelVisible = true;

        public void SetPanelVisible(bool value)
        {
            panelVisible = value;
            if (statusPanel != null) statusPanel.SetActive(value);
        }
        private Transform outlineRoot;
        private MRUKRoom displayedRoom;
        private bool initialized;
        private bool loading;
        private bool loaded;
        private bool noRoomsFound;
        private bool needsRefresh;
        private bool outlinesVisible = true;
        private bool navigationMapControls;
        private bool navigationMapVisible;
        private float nextRoomCheck;
        private float loadStarted;
        private bool slowLoadReported;
#if UNITY_ANDROID && !UNITY_EDITOR
        private bool permissionPending;
        private float permissionStarted;
        private PermissionCallbacks permissionCallbacks;
#endif
        public bool IsRoomReady { get; private set; }
        public string StatusMessage { get; private set; } = "Loading the room...";
        public MRUKRoom CurrentRoom => IsRoomReady ? displayedRoom : null;
        public int RoomRevision { get; private set; }

        public void Configure(MRUK kit, OVRCameraRig rig, ManualObjectDemo demo, Material material, Font font)
        {
            roomKit = kit;
            cameraRig = rig;
            objectDemo = demo;
            outlineMaterial = material;
            displayFont = font;
        }

        private void Start()
        {
            if (roomKit == null || cameraRig == null || cameraRig.centerEyeAnchor == null
                || objectDemo == null || outlineMaterial == null || displayFont == null)
            {
                Debug.LogError("[SpatialSense] Room demo is missing references. Run SpatialSense > Create Room Understanding Demo.", this);
                enabled = false;
                return;
            }
            CreatePanel();
            roomKit.RoomCreatedEvent.AddListener(OnRoomChanged);
            roomKit.RoomUpdatedEvent.AddListener(OnRoomChanged);
            roomKit.RoomRemovedEvent.AddListener(OnRoomChanged);
            initialized = true;
            RequestLoad(false);
        }

        private void Update()
        {
            if (!initialized) return;
            if (OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.LTouch))
            {
                outlinesVisible = !outlinesVisible;
                if (outlineRoot != null) outlineRoot.gameObject.SetActive(outlinesVisible);
                UpdateControls();
            }
            if (OVRInput.GetDown(OVRInput.Button.Two, OVRInput.Controller.LTouch)) RequestLoad(noRoomsFound);
#if UNITY_ANDROID && !UNITY_EDITOR
            if (permissionPending && Permission.HasUserAuthorizedPermission(OVRPermissionsRequester.ScenePermission))
            {
                permissionPending = false;
                RequestLoad(false);
            }
            else if (permissionPending && Time.unscaledTime - permissionStarted > 30f)
            {
                permissionPending = false;
                SetStatus("Room access is not approved yet. Allow spatial/scene access in the headset, then press left Y.");
            }
#endif
            if (loading && !slowLoadReported && Time.unscaledTime - loadStarted > 30f)
            {
                slowLoadReported = true;
                SetStatus("Room loading is taking longer than expected. Look around in good lighting. If it remains stuck, restart the app.");
            }
            if (!loaded || loading || Time.unscaledTime < nextRoomCheck) return;
            nextRoomCheck = Time.unscaledTime + 0.5f;
            var current = roomKit.GetCurrentRoom();
            if (current != displayedRoom || needsRefresh || (current == null && IsRoomReady)) RefreshRoom(current);
        }

        private async void RequestLoad(bool offerRoomSetup)
        {
            if (!initialized || loading) return;
#if UNITY_ANDROID && !UNITY_EDITOR
            if (permissionPending) return;
            if (!Permission.HasUserAuthorizedPermission(OVRPermissionsRequester.ScenePermission))
            {
                ResetRoom();
                SetStatus("Allow room/spatial-data access in the headset permission prompt.");
                permissionPending = true;
                permissionStarted = Time.unscaledTime;
                permissionCallbacks = new PermissionCallbacks();
                permissionCallbacks.PermissionGranted += OnPermissionGranted;
                permissionCallbacks.PermissionDenied += OnPermissionDenied;
                Permission.RequestUserPermission(OVRPermissionsRequester.ScenePermission, permissionCallbacks);
                return;
            }
#endif
            loading = true;
            loadStarted = Time.unscaledTime;
            slowLoadReported = false;
            ResetRoom();
            SetStatus(offerRoomSetup ? "Opening room setup if no scan is saved. Complete the headset instructions." : "Loading the room saved on your Quest...");
            try
            {
                var result = await roomKit.LoadSceneFromDevice(requestSceneCaptureIfNoDataFound: offerRoomSetup);
                if (this == null) return;
                noRoomsFound = result == MRUK.LoadDeviceResult.NoRoomsFound;
                if (result == MRUK.LoadDeviceResult.Success && roomKit.Rooms.Count > 0)
                {
                    loaded = true;
                    RefreshRoom(roomKit.GetCurrentRoom());
                }
                else if (noRoomsFound || (result == MRUK.LoadDeviceResult.Success && roomKit.Rooms.Count == 0))
                {
                    noRoomsFound = true;
                    SetStatus("No saved room scan found. Press left Y to request room setup, or finish Space Setup in headset settings.");
                }
                else if (result == MRUK.LoadDeviceResult.NoScenePermission || result == MRUK.LoadDeviceResult.FailurePermissionInsufficient)
                    SetStatus("Room access was denied. Allow spatial/scene access in the headset app permissions, then press left Y.");
                else if (result == MRUK.LoadDeviceResult.NotInitialized)
                    SetStatus("Room service is not available. Test an Android build on Quest; look around, then press left Y to retry.");
                else
                    SetStatus($"Room loading failed ({result}). Check lighting, room setup and permissions, then press left Y.");
            }
            catch (Exception exception)
            {
                if (this != null)
                {
                    SetStatus("Room loading failed. Press left Y to retry; check Unity logs if it repeats.");
                    Debug.LogException(exception, this);
                }
            }
            finally
            {
                loading = false;
            }
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private void OnPermissionGranted(string permission)
        {
            if (this == null || !initialized) return;
            permissionPending = false;
            if (!loading && !loaded) RequestLoad(false);
        }

        private void OnPermissionDenied(string permission)
        {
            if (this == null || !initialized) return;
            permissionPending = false;
            SetStatus("Room access was denied. Allow spatial/scene access in headset app permissions, then press left Y.");
        }
#endif

        private void OnRoomChanged(MRUKRoom room) => needsRefresh = true;

        private void ResetRoom()
        {
            RoomRevision++;
            IsRoomReady = false;
            loaded = false;
            displayedRoom = null;
            objectDemo.ClearRegistration();
            ClearOutlines();
        }

        private void RefreshRoom(MRUKRoom room)
        {
            RoomRevision++;
            needsRefresh = false;
            if (displayedRoom != room || (room == null && IsRoomReady)) objectDemo.ClearRegistration();
            displayedRoom = room;
            ClearOutlines();
            IsRoomReady = false;
            if (room == null)
            {
                SetStatus("Room data loaded, but your current room is not located yet. Look around the scanned room; press left Y if needed.");
                return;
            }
            outlineRoot = new GameObject("Room outlines").transform;
            outlineRoot.SetParent(transform, false);
            var floors = 0;
            var walls = 0;
            var furniture = 0;
            foreach (var anchor in room.Anchors)
            {
                if (anchor == null || (anchor.Label & (MRUKAnchor.SceneLabels.GLOBAL_MESH | MRUKAnchor.SceneLabels.CEILING)) != 0) continue;
                var floor = (anchor.Label & MRUKAnchor.SceneLabels.FLOOR) != 0;
                var wall = (anchor.Label & (MRUKAnchor.SceneLabels.WALL_FACE | MRUKAnchor.SceneLabels.INVISIBLE_WALL_FACE
                    | MRUKAnchor.SceneLabels.INNER_WALL_FACE | MRUKAnchor.SceneLabels.DOOR_FRAME | MRUKAnchor.SceneLabels.WINDOW_FRAME)) != 0;
                var colour = floor ? Color.cyan : wall ? Color.white : new Color(1f, 0.65f, 0.05f);
                if (!DrawAnchor(anchor, colour)) continue;
                if (floor) floors++;
                else if (wall) walls++;
                else furniture++;
            }
            outlineRoot.gameObject.SetActive(outlinesVisible);
            IsRoomReady = floors > 0;
            SetStatus(IsRoomReady ? $"Current room ready: {floors} floor, {walls} wall/opening surfaces, {furniture} other objects. Check that outlines match your room."
                : "Room loaded, but no floor outline is available. Update the room scan, then press left Y.");
        }

        private bool DrawAnchor(MRUKAnchor anchor, Color colour)
        {
            Vector3[] points;
            var loop = false;
            if (anchor.VolumeBounds.HasValue)
            {
                var bounds = anchor.VolumeBounds.Value;
                var corners = new Vector3[8];
                for (var i = 0; i < 8; i++) corners[i] = new Vector3((i & 1) == 0 ? bounds.min.x : bounds.max.x,
                    (i & 2) == 0 ? bounds.min.y : bounds.max.y, (i & 4) == 0 ? bounds.min.z : bounds.max.z);
                var order = new[] { 0, 1, 3, 2, 0, 4, 5, 1, 5, 7, 3, 7, 6, 2, 6, 4 };
                points = new Vector3[order.Length];
                for (var i = 0; i < order.Length; i++) points[i] = corners[order[i]];
            }
            else if (anchor.PlaneRect.HasValue)
            {
                var boundary = anchor.PlaneBoundary2D;
                if (boundary != null && boundary.Count >= 3)
                {
                    points = new Vector3[boundary.Count];
                    for (var i = 0; i < points.Length; i++) points[i] = new Vector3(boundary[i].x, boundary[i].y, 0.008f);
                }
                else
                {
                    var rect = anchor.PlaneRect.Value;
                    points = new[] { new Vector3(rect.xMin, rect.yMin, 0.008f), new Vector3(rect.xMax, rect.yMin, 0.008f),
                        new Vector3(rect.xMax, rect.yMax, 0.008f), new Vector3(rect.xMin, rect.yMax, 0.008f) };
                }
                loop = true;
            }
            else return false;

            var lineObject = new GameObject($"Outline {anchor.Label}");
            lineObject.transform.SetParent(outlineRoot, false);
            var attachment = lineObject.AddComponent<RoomOutlineAttachment>();
            attachment.Configure(anchor.transform);
            var line = lineObject.AddComponent<LineRenderer>();
            line.sharedMaterial = outlineMaterial;
            line.useWorldSpace = false;
            line.widthMultiplier = 0.012f;
            line.loop = loop;
            line.positionCount = points.Length;
            line.SetPositions(points);
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            var properties = new MaterialPropertyBlock();
            properties.SetColor("_BaseColor", colour);
            line.SetPropertyBlock(properties);
            return true;
        }

        private void ClearOutlines()
        {
            if (outlineRoot == null) return;
            outlineRoot.gameObject.SetActive(false);
            Destroy(outlineRoot.gameObject);
            outlineRoot = null;
        }

        private void CreatePanel()
        {
            var panel = new GameObject("Room status", typeof(RectTransform), typeof(Canvas), typeof(Image));
            statusPanel = panel;
            panel.transform.SetParent(cameraRig.centerEyeAnchor, false);
            panel.transform.localPosition = new Vector3(0, 0.25f, 1.25f);
            panel.transform.localScale = Vector3.one * 0.0013f;
            panel.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            panel.GetComponent<RectTransform>().sizeDelta = new Vector2(640, 180);
            panel.GetComponent<Image>().color = new Color(0.025f, 0.05f, 0.08f, 0.86f);
            var title = ManualObjectDemo.AddText(panel.transform, "Title", displayFont, 22, new Vector2(600, 28), new Vector2(0, 65));
            title.text = "SpatialSense | Room understanding";
            statusText = ManualObjectDemo.AddText(panel.transform, "Status", displayFont, 18, new Vector2(600, 70), new Vector2(0, 12));
            controlsText = ManualObjectDemo.AddText(panel.transform, "Controls", displayFont, 16, new Vector2(600, 40), new Vector2(0, -55));
            UpdateControls();
            panel.SetActive(panelVisible);
        }

        public void SetNavigationMapControls(bool active, bool visible)
        {
            navigationMapControls = active;
            navigationMapVisible = visible;
            if (controlsText != null) UpdateControls();
        }

        private void UpdateControls() => controlsText.text = navigationMapControls
            ? $"LEFT: X room outlines | Y reload | Trigger map {(navigationMapVisible ? "on" : "off")}\nMAP: blue edges = walking space | red edges = reserved clearance"
            : $"LEFT: X outlines {(outlinesVisible ? "on" : "off")} | Y reload / room setup\nCyan: floor | White: walls/openings | Amber: other mapped objects";

        private void SetStatus(string message)
        {
            StatusMessage = message;
            if (statusText != null) statusText.text = message;
            Debug.Log($"[SpatialSense Room] {message}", this);
        }

        private void OnDestroy()
        {
            initialized = false;
            if (roomKit != null)
            {
                roomKit.RoomCreatedEvent.RemoveListener(OnRoomChanged);
                roomKit.RoomUpdatedEvent.RemoveListener(OnRoomChanged);
                roomKit.RoomRemovedEvent.RemoveListener(OnRoomChanged);
            }
#if UNITY_ANDROID && !UNITY_EDITOR
            if (permissionCallbacks != null)
            {
                permissionCallbacks.PermissionGranted -= OnPermissionGranted;
                permissionCallbacks.PermissionDenied -= OnPermissionDenied;
            }
#endif
        }
    }
}
