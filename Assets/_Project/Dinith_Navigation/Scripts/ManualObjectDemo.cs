using System;
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
        [SerializeField] private SpatialSense.Persistence.PersistentObjectMemory persistentMemory;
        [SerializeField] private Project.Core.Integration.ManualObservationSource manualObservationSource;
        [SerializeField] private Project.Core.Integration.SimulatedVisionObservationSource simulatedVisionSource;
        private string inputFeedback;
        private float feedbackUntil;
        public event Action<string, string> FlowEvent;
        public bool IsMenuOpen => menuOpen;
        public bool IsFound => state == SearchState.Found;
        public bool WasCancelled { get; private set; }
        public SpatialSense.Persistence.PersistentObjectMemory SelectedMemory => persistentMemory;

        public void ConfigureObservationSources(Project.Core.Integration.ManualObservationSource manual,
            Project.Core.Integration.SimulatedVisionObservationSource simulated)
        {
            manualObservationSource = manual;
            simulatedVisionSource = simulated;
        }

        public void SetInputFeedback(string message) { inputFeedback = message; feedbackUntil = Time.unscaledTime + 5f; }
        public void ClearInputFeedback() { inputFeedback = null; feedbackUntil = 0; }
        [SerializeField, Min(0.1f)] private float confirmationDistance = 1f;
        private SearchState state;
        private Vector3 registeredPosition;
        private Text statusText;
        private Text distanceText;
        private Transform head;
        private GameObject instructionPanel;
        private bool menuOpen;
        private bool usingObjectMenu;
        private Text titleText;
        private Text controlsText;
        private string status = "No object registered. Hold the right controller beside your keys and press A.";

        public bool CanConfirm => !menuOpen && state == SearchState.Searching && head != null
            && (persistentMemory == null || persistentMemory.HasLocatedPosition)
            && (roomUnderstanding == null || roomUnderstanding.IsRoomReady)
            && (walkingRoute != null ? walkingRoute.CanConfirmAtTarget
                : Vector3.Distance(head.position, registeredPosition) <= confirmationDistance);

        public bool IsSearching => !menuOpen && state == SearchState.Searching && (persistentMemory == null || persistentMemory.HasLocatedPosition);
        public bool IsInitialized => head != null && statusText != null;
        public Vector3 RegisteredPosition => registeredPosition;
        public string CurrentObjectName => persistentMemory != null ? persistentMemory.ObjectName : "My keys";
        public void ConfigurePersistence(SpatialSense.Persistence.PersistentObjectMemory memory)
        {
            persistentMemory = memory;
            if (marker != null) marker.SetObjectName(CurrentObjectName);
            if (usingObjectMenu && titleText != null) titleText.text = "SpatialSense | " + CurrentObjectName;
            if (IsInitialized) ClearRegistration();
        }

        public void SetMenuOpen(bool value)
        {
            usingObjectMenu = true;
            if (titleText != null) titleText.text = "SpatialSense | " + CurrentObjectName;
            if (controlsText != null) controlsText.text = "RIGHT: A register+save | B find/retry | Trigger confirm\nRight thumbstick: cancel | Left thumbstick: objects menu";
            if (value && state == SearchState.Searching) CancelSearch();
            menuOpen = value;
            if (marker != null && IsInitialized)
            {
                if (value) marker.gameObject.SetActive(false);
                else if (state != SearchState.Empty && (persistentMemory == null || persistentMemory.HasLocatedPosition))
                    marker.Show(registeredPosition, state == SearchState.Found ? Color.cyan : Color.green);
            }
            if (instructionPanel != null) instructionPanel.SetActive(!value);
        }

        public void PrepareRegistration()
        {
            if (simulatedVisionSource != null) simulatedVisionSource.Cancel();
            ClearInputFeedback();
            CancelSearch();
            status = $"Hold the right controller beside {CurrentObjectName} and press A to register and save its location.";
            if (marker != null) marker.gameObject.SetActive(false);
        }

        public void BeginFind()
        {
            if (simulatedVisionSource != null) simulatedVisionSource.Cancel();
            ClearInputFeedback();
            if (persistentMemory != null)
            {
                if (!persistentMemory.TryGetLocatedPosition(out var position)) { persistentMemory.RetryLoad(); return; }
                ApplyAnchoredPosition(position);
            }
            Find();
        }

        public void ApplyAnchoredPosition(Vector3 position)
        {
            if (!IsInitialized || persistentMemory == null || !persistentMemory.HasLocatedPosition) return;
            registeredPosition = position;
            marker.SetEvidenceLabel(persistentMemory.RecordedOrigin == Project.Core.Integration.ObservationOrigin.SimulatedVision
                ? "SIMULATED vision sighting" : persistentMemory.RecordedOrigin == Project.Core.Integration.ObservationOrigin.Vision
                ? "Last seen by vision" : persistentMemory.RecordedOrigin == Project.Core.Integration.ObservationOrigin.ManualController
                ? "Manually registered location" : "Saved last-seen location");
            if (state == SearchState.Empty)
            {
                state = SearchState.Registered;
                if (!menuOpen) marker.Show(position, Color.green);
                else marker.gameObject.SetActive(false);
                status = $"{CurrentObjectName} ready. Press B to find it, or A to replace its saved location.";
            }
            else marker.transform.position = position;
        }
        public void ConfigureNavigation(SpatialSense.Navigation.WalkingRouteDemo navigation) => walkingRoute = navigation;

        public void ConfigureRoomLoading(SpatialSense.RoomDemo.RoomUnderstandingDemo roomDemo) => roomUnderstanding = roomDemo;

        public void ClearRegistration()
        {
            WasCancelled = false;
            state = SearchState.Empty;
            status = persistentMemory != null ? "Waiting to locate the saved object position."
                : "Room changed or reloaded. Register your keys again with A.";
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
            marker.SetObjectName(CurrentObjectName);
            marker.gameObject.SetActive(false);
            CreateInstructions();
            RefreshInstructions();
        }

        private void Update()
        {
            if (menuOpen) return;
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
                distanceText.text = persistentMemory != null ? "Saved locations stay on this headset."
                    : state == SearchState.Empty ? "Current session only" : "Registered position kept for this session";
                return;
            }
            if (persistentMemory != null && !persistentMemory.HasLocatedPosition)
            {
                if (!persistentMemory.IsBusy)
                {
                    if (OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.RTouch)) Register();
                    else if (OVRInput.GetDown(OVRInput.Button.Two, OVRInput.Controller.RTouch)) persistentMemory.RetryLoad();
                }
                RefreshInstructions();
                return;
            }
            if (OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.RTouch)) Register();
            else if (OVRInput.GetDown(OVRInput.Button.Two, OVRInput.Controller.RTouch)) BeginFind();
            else if (OVRInput.GetDown(OVRInput.Button.PrimaryThumbstick, OVRInput.Controller.RTouch)) CancelSearch();
            RefreshInstructions();
        }

        private void Register()
        {
            var controller = cameraRig.rightControllerAnchor;
            if (controller == null) return;
            if (persistentMemory != null)
            {
                if (manualObservationSource != null)
                {
                    if (!manualObservationSource.Report(CurrentObjectName, controller.position)) SetInputFeedback("Observation input is not ready. Wait for the room and try A again.");
                    return;
                }
                persistentMemory.RegisterAndSave(controller.position);
                return;
            }
            registeredPosition = controller.position;
            state = SearchState.Registered;
            if (persistentMemory == null || persistentMemory.HasLocatedPosition) marker.Show(registeredPosition, Color.green);
            else marker.gameObject.SetActive(false);
            status = "My keys registered here. Step away, then press B to find them. A replaces this location.";
            Debug.Log("[SpatialSense] Manually registered My keys for the current session.", this);
        }

        private void Find()
        {
            WasCancelled = false;
            if (state == SearchState.Empty)
            {
                status = $"Register first: hold the right controller beside {CurrentObjectName} and press A.";
                return;
            }
            state = SearchState.Searching;
            FlowEvent?.Invoke("search_started", CurrentObjectName);
            marker.Show(registeredPosition, Color.yellow);
            status = $"Find {CurrentObjectName}. Look for the yellow marker and press the trigger when you reach the viewing position.";
        }

        private void CancelSearch()
        {
            if (simulatedVisionSource != null && simulatedVisionSource.IsArmed)
            {
                simulatedVisionSource.Cancel();
                SetInputFeedback("Simulation cancelled. No observation was submitted.");
                WasCancelled = true;
            }
            if (state != SearchState.Searching) return;
            WasCancelled = true;
            FlowEvent?.Invoke("search_cancelled", CurrentObjectName);
            state = SearchState.Registered;
            marker.Show(registeredPosition, Color.green);
            status = "Search cancelled. Your registered location is kept. Press B to search again.";
        }

        public void ConfirmFound()
        {
            if (!CanConfirm) return;
            state = SearchState.Found;
            WasCancelled = false;
            FlowEvent?.Invoke("found_user_confirmed", CurrentObjectName);
            marker.Show(registeredPosition, Color.cyan);
            status = "Found - confirmed by you. Press B to repeat, or A to register a new location.";
            Debug.Log($"[SpatialSense] User confirmed finding {CurrentObjectName}. This is manual confirmation, not automatic recognition.", this);
        }

        private void RefreshInstructions()
        {
            if (simulatedVisionSource != null && simulatedVisionSource.IsArmed)
            {
                statusText.text = simulatedVisionSource.Prompt;
                distanceText.text = "TEST INPUT ONLY: controller position stands in for a camera sighting.";
                return;
            }
            if (!string.IsNullOrEmpty(inputFeedback) && Time.unscaledTime < feedbackUntil && (persistentMemory == null || !persistentMemory.IsBusy))
            {
                statusText.text = inputFeedback;
                distanceText.text = "No new location was applied by that input.";
                return;
            }
            if (persistentMemory != null && !persistentMemory.HasLocatedPosition)
            {
                statusText.text = persistentMemory.StatusMessage;
                distanceText.text = persistentMemory.IsBusy ? "Please wait for saving/localization to finish."
                    : "Find stays unavailable until the saved anchor is located.";
                return;
            }
            if (state == SearchState.Searching && walkingRoute != null)
            {
                statusText.text = walkingRoute.StatusText;
                distanceText.text = walkingRoute.DistanceText;
                return;
            }
            statusText.text = status;
            if (state == SearchState.Empty)
            {
                distanceText.text = persistentMemory != null ? "Saved locally on this headset using a spatial anchor."
                    : "Current session only | Location comes from your controller";
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
            instructionPanel = panel;
            panel.transform.SetParent(head, false);
            panel.transform.localPosition = new Vector3(0, -0.26f, 1.25f);
            panel.transform.localScale = Vector3.one * 0.0014f;
            panel.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            panel.GetComponent<RectTransform>().sizeDelta = new Vector2(640, 240);
            panel.GetComponent<Image>().color = new Color(0.025f, 0.05f, 0.08f, 0.86f);
            var title = AddText(panel.transform, "Title", displayFont, 24, new Vector2(600, 30), new Vector2(0, 94));
            titleText = title;
            title.text = persistentMemory != null ? "SpatialSense | Saved object demo"
                : walkingRoute != null ? "SpatialSense | Walking route v2" : "SpatialSense | My keys";
            statusText = AddText(panel.transform, "Status", displayFont, 20, new Vector2(600, 80), new Vector2(0, 35));
            distanceText = AddText(panel.transform, "Distance", displayFont, 18, new Vector2(600, 50), new Vector2(0, -38));
            var controls = AddText(panel.transform, "Controls", displayFont, 16, new Vector2(600, 42), new Vector2(0, -89));
            controlsText = controls;
            controls.text = persistentMemory != null
                ? "RIGHT: A register+save | B find/retry | Trigger confirm\nThumbstick: cancel search | Saves locally on this headset"
                : "RIGHT CONTROLLER: A register | B find | Trigger confirm\nClick thumbstick: cancel search | Saved for this session only";
            panel.SetActive(!menuOpen);
            if (usingObjectMenu) SetMenuOpen(menuOpen);
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
