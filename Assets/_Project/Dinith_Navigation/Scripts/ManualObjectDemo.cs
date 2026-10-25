using UnityEngine;
using UnityEngine.UI;

namespace SpatialSense.ManualDemo
{
    [DisallowMultipleComponent]
    public sealed class ManualObjectDemo : MonoBehaviour
    {
        private enum SearchState { Empty, Registered, Searching, Found }
        [SerializeField] private OVRCameraRig cameraRig;
        [SerializeField] private ManualObjectMarker marker;
        [SerializeField] private Font displayFont;
        [SerializeField] private SpatialSense.RoomDemo.RoomUnderstandingDemo roomUnderstanding;
        [SerializeField] private SpatialSense.Navigation.WalkingRouteDemo walkingRoute;
        [SerializeField, Min(0.1f)] private float confirmationDistance = 1f;
        private SearchState state;
        private Vector3 registeredPosition;
        private Text statusText;
        private Text distanceText;
        private Transform head;
        private string status = "No object registered. Hold the right controller beside your keys and press A.";

        public bool CanConfirm => state == SearchState.Searching && head != null
            && (roomUnderstanding == null || roomUnderstanding.IsRoomReady)
            && (walkingRoute != null ? walkingRoute.CanConfirmAtTarget
                : Vector3.Distance(head.position, registeredPosition) <= confirmationDistance);

        public bool IsSearching => state == SearchState.Searching;
        public Vector3 RegisteredPosition => registeredPosition;
        public void ConfigureNavigation(SpatialSense.Navigation.WalkingRouteDemo navigation) => walkingRoute = navigation;

        public void ConfigureRoomLoading(SpatialSense.RoomDemo.RoomUnderstandingDemo roomDemo) => roomUnderstanding = roomDemo;

        public void ClearRegistration()
        {
            state = SearchState.Empty;
            status = "Room changed or reloaded. Register your keys again with A.";
            if (marker != null) marker.gameObject.SetActive(false);
        }

        public void Configure(OVRCameraRig rig, ManualObjectMarker target, Font font)
        {
            cameraRig = rig;
            marker = target;
            displayFont = font;
        }

        private void Start()
        {
            if (cameraRig == null || marker == null || displayFont == null || cameraRig.centerEyeAnchor == null)
            {
                Debug.LogError("[SpatialSense] Missing demo references. Run SpatialSense > Create Manual Object Demo.", this);
                enabled = false;
                return;
            }
            head = cameraRig.centerEyeAnchor;
            marker.Initialize(this, head, displayFont);
            marker.gameObject.SetActive(false);
            CreateInstructions();
            RefreshInstructions();
        }

        private void Update()
        {
            if (roomUnderstanding != null && !roomUnderstanding.IsRoomReady)
            {
                statusText.text = "Waiting for the room. Check the room status panel above.";
                distanceText.text = "Registration will be available when the current room and floor are ready.";
                return;
            }
            var controllerReady = OVRInput.IsControllerConnected(OVRInput.Controller.RTouch)
                && OVRInput.GetControllerPositionTracked(OVRInput.Controller.RTouch)
                && OVRInput.GetControllerOrientationTracked(OVRInput.Controller.RTouch);
            if (!controllerReady)
            {
                statusText.text = "Right controller tracking unavailable. Pick it up and keep it in view.";
                distanceText.text = state == SearchState.Empty ? "Current session only" : "Registered position kept for this session";
                return;
            }
            if (OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.RTouch)) Register();
            else if (OVRInput.GetDown(OVRInput.Button.Two, OVRInput.Controller.RTouch)) Find();
            else if (OVRInput.GetDown(OVRInput.Button.PrimaryThumbstick, OVRInput.Controller.RTouch)) CancelSearch();
            RefreshInstructions();
        }

        private void Register()
        {
            var controller = cameraRig.rightControllerAnchor;
            if (controller == null) return;
            registeredPosition = controller.position;
            state = SearchState.Registered;
            marker.Show(registeredPosition, Color.green);
            status = "My keys registered here. Step away, then press B to find them. A replaces this location.";
            Debug.Log("[SpatialSense] Manually registered My keys for the current session.", this);
        }

        private void Find()
        {
            if (state == SearchState.Empty)
            {
                status = "Register first: hold the right controller beside your keys and press A.";
                return;
            }
            state = SearchState.Searching;
            marker.Show(registeredPosition, Color.yellow);
            status = "Find My keys. Look for the yellow marker. Within 1 m, point at it and press the index trigger.";
        }

        private void CancelSearch()
        {
            if (state != SearchState.Searching) return;
            state = SearchState.Registered;
            marker.Show(registeredPosition, Color.green);
            status = "Search cancelled. Your registered location is kept. Press B to search again.";
        }

        public void ConfirmFound()
        {
            if (!CanConfirm) return;
            state = SearchState.Found;
            marker.Show(registeredPosition, Color.cyan);
            status = "Found - confirmed by you. Press B to repeat, or A to register a new location.";
            Debug.Log("[SpatialSense] User confirmed finding My keys. This is manual confirmation, not automatic recognition.", this);
        }

        private void RefreshInstructions()
        {
            if (state == SearchState.Searching && walkingRoute != null)
            {
                statusText.text = walkingRoute.StatusText;
                distanceText.text = walkingRoute.DistanceText;
                return;
            }
            statusText.text = status;
            if (state == SearchState.Empty)
            {
                distanceText.text = "Current session only | Location comes from your controller";
                return;
            }
            var offset = registeredPosition - head.position;
            var distance = offset.magnitude;
            var flatForward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            var flatOffset = Vector3.ProjectOnPlane(offset, Vector3.up);
            var angle = flatForward.sqrMagnitude > 0.001f && flatOffset.sqrMagnitude > 0.001f
                ? Vector3.SignedAngle(flatForward, flatOffset, Vector3.up) : 0f;
            var direction = Mathf.Abs(angle) < 15f ? "Ahead" : Mathf.Abs(angle) > 150f ? "Behind you"
                : angle > 0 ? "To your right" : "To your left";
            distanceText.text = $"{distance:F1} m straight-line distance | {direction}\nLocation cue only - check your surroundings before walking.";
        }

        private void CreateInstructions()
        {
            var panel = new GameObject("Search instructions", typeof(RectTransform), typeof(Canvas), typeof(Image));
            panel.transform.SetParent(head, false);
            panel.transform.localPosition = new Vector3(0, -0.26f, 1.25f);
            panel.transform.localScale = Vector3.one * 0.0014f;
            panel.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            panel.GetComponent<RectTransform>().sizeDelta = new Vector2(640, 240);
            panel.GetComponent<Image>().color = new Color(0.025f, 0.05f, 0.08f, 0.86f);
            var title = AddText(panel.transform, "Title", displayFont, 24, new Vector2(600, 30), new Vector2(0, 94));
            title.text = walkingRoute != null ? "SpatialSense | Walking route v2" : "SpatialSense | My keys";
            statusText = AddText(panel.transform, "Status", displayFont, 20, new Vector2(600, 80), new Vector2(0, 35));
            distanceText = AddText(panel.transform, "Distance", displayFont, 18, new Vector2(600, 50), new Vector2(0, -38));
            var controls = AddText(panel.transform, "Controls", displayFont, 16, new Vector2(600, 42), new Vector2(0, -89));
            controls.text = "RIGHT CONTROLLER: A register | B find | Trigger confirm\nClick thumbstick: cancel search | Saved for this session only";
        }

        internal static Text AddText(Transform parent, string name, Font font, int size, Vector2 dimensions, Vector2 position)
        {
            var textObject = new GameObject(name, typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(parent, false);
            var rect = textObject.GetComponent<RectTransform>();
            rect.sizeDelta = dimensions;
            rect.anchoredPosition = position;
            var text = textObject.GetComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleLeft;
            text.raycastTarget = false;
            return text;
        }
    }
}
