using SpatialSense.ManualDemo;
using SpatialSense.Persistence;
using SpatialSense.RoomDemo;
using UnityEngine;
using UnityEngine.UI;

namespace SpatialSense.ObjectMenu
{
    public sealed class MultiObjectMenu : MonoBehaviour
    {
        [SerializeField] private PersistentObjectMemory[] memories;
        [SerializeField] private ManualObjectDemo objectDemo;
        [SerializeField] private RoomUnderstandingDemo roomDemo;
        [SerializeField] private OVRCameraRig cameraRig;
        [SerializeField] private Font displayFont;
        [SerializeField] private Project.Core.Integration.SimulatedVisionObservationSource simulatedVision;
        private Image simulateImage;
        public void ConfigureIntegration(Project.Core.Integration.SimulatedVisionObservationSource simulated) => simulatedVision = simulated;

        public bool TrySelectObject(string name)
        {
            if (!initialized || Busy) return false;
            for (var i = 0; i < memories.Length; i++) if (memories[i].ObjectName == name)
            {
                if (selectedSlot != i)
                {
                    CancelForget();
                    Selected.SetSelected(false);
                    selectedSlot = i;
                    Selected.SetSelected(true);
                    objectDemo.ConfigurePersistence(Selected);
                }
                return true;
            }
            return false;
        }

        public void CloseForExternalAction() { if (initialized) SetVisible(false); }
        private GameObject panel;
        private readonly Text[] rowTexts = new Text[3];
        private readonly Image[] rowImages = new Image[3];
        private Text message;
        private Text findText;
        private Text forgetText;
        private Text closeText;
        private Image registerImage;
        private Image findImage;
        private Image forgetImage;
        private int selectedSlot;
        private string forgetToken;
        private int forgetSlot = -1;
        private float nextRefresh;
        private bool visible;
        private bool initialized;
        private PersistentObjectMemory Selected => memories[selectedSlot];
        private bool Busy
        {
            get
            {
                if (memories == null) return true;
                foreach (var memory in memories) if (memory == null || memory.IsBusy) return true;
                return false;
            }
        }

        public void Configure(PersistentObjectMemory[] objects, ManualObjectDemo flow, RoomUnderstandingDemo room,
            OVRCameraRig rig, Font font)
        {
            memories = objects;
            objectDemo = flow;
            roomDemo = room;
            cameraRig = rig;
            displayFont = font;
        }

        private void Start()
        {
            if (memories == null || memories.Length != 3 || objectDemo == null || roomDemo == null
                || cameraRig == null || cameraRig.centerEyeAnchor == null || displayFont == null)
            {
                Debug.LogError("[SpatialSense Objects] Run SpatialSense > Create Multi Object Demo.", this);
                enabled = false;
                return;
            }
            for (var i = 0; i < memories.Length; i++)
            {
                if (memories[i] == null || memories[i].ObjectName != SavedObjectCatalog.Names[i])
                {
                    Debug.LogError("[SpatialSense Objects] Missing or mismatched object slot.", this);
                    enabled = false;
                    return;
                }
                memories[i].SetSelected(i == selectedSlot);
            }
            objectDemo.ConfigurePersistence(Selected);
            CreatePanel();
            initialized = true;
            SetVisible(true);
        }

        private void Update()
        {
            if (!initialized) return;
            if (OVRInput.GetDown(OVRInput.Button.PrimaryThumbstick, OVRInput.Controller.LTouch)) SetVisible(!visible);
            if (!visible || Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + 0.2f;
            Refresh();
        }

        public bool CanUse(ObjectMenuAction action, int slot)
        {
            if (!initialized || !visible) return false;
            if (action == ObjectMenuAction.Close) return true;
            if (Busy) return false;
            switch (action)
            {
                case ObjectMenuAction.SelectObject: return slot >= 0 && slot < memories.Length;
                case ObjectMenuAction.Register: return roomDemo.IsRoomReady;
                case ObjectMenuAction.Find: return roomDemo.IsRoomReady && Selected.HasSavedRecord;
                case ObjectMenuAction.Forget: return Selected.CanForget;
                case ObjectMenuAction.SimulateVision: return simulatedVision != null && roomDemo.IsRoomReady;
                default: return false;
            }
        }

        public void Execute(ObjectMenuAction action, int slot)
        {
            if (!CanUse(action, slot)) return;
            switch (action)
            {
                case ObjectMenuAction.SelectObject:
                    CancelForget();
                    if (selectedSlot == slot) break;
                    Selected.SetSelected(false);
                    selectedSlot = slot;
                    Selected.SetSelected(true);
                    objectDemo.ConfigurePersistence(Selected);
                    break;
                case ObjectMenuAction.Register:
                    SetVisible(false);
                    objectDemo.PrepareRegistration();
                    break;
                case ObjectMenuAction.Find:
                    if (Selected.HasLocatedPosition)
                    {
                        SetVisible(false);
                        objectDemo.BeginFind();
                    }
                    else { CancelForget(); Selected.RetryLoad(); }
                    break;
                case ObjectMenuAction.Forget:
                    if (forgetSlot == selectedSlot && forgetToken == Selected.ForgetToken)
                    {
                        CancelForget();
                        Selected.ForgetSaved();
                    }
                    else
                    {
                        forgetSlot = selectedSlot;
                        forgetToken = Selected.ForgetToken;
                    }
                    break;
                case ObjectMenuAction.Close:
                    if (forgetSlot >= 0) CancelForget();
                    else SetVisible(false);
                    break;
                case ObjectMenuAction.SimulateVision:
                    SetVisible(false);
                    if (!simulatedVision.Arm(Selected.ObjectName)) objectDemo.SetInputFeedback("The simulated observation source is not ready. Wait for the room.");
                    break;
            }
            if (visible) Refresh();
        }

        private void SetVisible(bool value)
        {
            CancelForget();
            if (value && simulatedVision != null) simulatedVision.Cancel();
            visible = value;
            panel.SetActive(value);
            objectDemo.SetMenuOpen(value);
            roomDemo.SetPanelVisible(!value);
            if (!value) return;
            var head = cameraRig.centerEyeAnchor;
            var forward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
            forward.Normalize();
            panel.transform.SetPositionAndRotation(head.position + forward * 1.35f, Quaternion.LookRotation(forward, Vector3.up));
            Refresh();
        }

        private void CancelForget() { forgetSlot = -1; forgetToken = null; }

        private void Refresh()
        {
            for (var i = 0; i < memories.Length; i++)
            {
                var label = i == 0 ? "Keys" : i == 1 ? "Wallet" : "Remote";
                rowTexts[i].text = $"{label}{(i == selectedSlot ? "  [selected]" : "")}\n{memories[i].ListStatus}";
                rowImages[i].color = i == selectedSlot ? new Color(0.12f, 0.36f, 0.55f) : new Color(0.12f, 0.17f, 0.22f);
            }
            message.text = forgetSlot >= 0
                ? $"Forget {Selected.ObjectName}? Its saved location will be removed. Press Confirm forget, or Cancel."
                : !roomDemo.IsRoomReady ? roomDemo.StatusMessage
                : Selected.ObjectName + ": " + Selected.StatusMessage;
            findText.text = Selected.HasLocatedPosition ? "Find" : "Locate / retry";
            forgetText.text = forgetSlot >= 0 ? "Confirm forget" : "Forget";
            closeText.text = forgetSlot >= 0 ? "Cancel" : "Close";
            var enabledColour = new Color(0.15f, 0.3f, 0.4f);
            var disabledColour = new Color(0.12f, 0.13f, 0.14f);
            registerImage.color = CanUse(ObjectMenuAction.Register, -1) ? enabledColour : disabledColour;
            findImage.color = CanUse(ObjectMenuAction.Find, -1) ? enabledColour : disabledColour;
            forgetImage.color = CanUse(ObjectMenuAction.Forget, -1) ? new Color(0.4f, 0.17f, 0.14f) : disabledColour;
            if (simulateImage != null) simulateImage.color = CanUse(ObjectMenuAction.SimulateVision, -1) ? enabledColour : disabledColour;
        }

        private void CreatePanel()
        {
            panel = new GameObject("My objects menu", typeof(RectTransform), typeof(Canvas), typeof(Image));
            panel.transform.SetParent(transform, false);
            panel.transform.localScale = Vector3.one * 0.0013f;
            panel.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            var integration = simulatedVision != null;
            var offset = integration ? 40f : 0f;
            panel.GetComponent<RectTransform>().sizeDelta = new Vector2(620, integration ? 680 : 600);
            panel.GetComponent<Image>().color = new Color(0.025f, 0.05f, 0.08f, 0.97f);
            var title = ManualObjectDemo.AddText(panel.transform, "Title", displayFont, 26, new Vector2(560, 36), new Vector2(0, 265 + offset));
            title.text = integration ? "SpatialSense | Team base" : "SpatialSense | My objects";
            var help = ManualObjectDemo.AddText(panel.transform, "Help", displayFont, 18, new Vector2(560, 36), new Vector2(0, 224 + offset));
            help.text = "Point with the right controller and press the trigger.";
            for (var i = 0; i < 3; i++)
                rowTexts[i] = MakeButton("Object " + i, ObjectMenuAction.SelectObject, i, new Vector2(0, 160 - i * 72 + offset), new Vector2(560, 62), out rowImages[i]);
            message = ManualObjectDemo.AddText(panel.transform, "Status", displayFont, 18, new Vector2(560, 80), new Vector2(0, -68 + offset));
            var register = MakeButton("Register", ObjectMenuAction.Register, -1, new Vector2(-145, -155 + offset), new Vector2(270, 52), out registerImage);
            register.text = "Register / update";
            findText = MakeButton("Find", ObjectMenuAction.Find, -1, new Vector2(145, -155 + offset), new Vector2(270, 52), out findImage);
            forgetText = MakeButton("Forget", ObjectMenuAction.Forget, -1, new Vector2(-145, -220 + offset), new Vector2(270, 52), out forgetImage);
            closeText = MakeButton("Close", ObjectMenuAction.Close, -1, new Vector2(145, -220 + offset), new Vector2(270, 52), out _);
            if (integration)
            {
                var test = MakeButton("Simulated vision", ObjectMenuAction.SimulateVision, -1, new Vector2(0, -245), new Vector2(560, 46), out simulateImage);
                test.text = "Simulated vision update (test input)";
            }
            var controls = ManualObjectDemo.AddText(panel.transform, "Controls", displayFont, 16, new Vector2(560, 32), new Vector2(0, integration ? -305 : -275));
            controls.text = "Click LEFT thumbstick to open/close and reposition this menu.";
        }

        private Text MakeButton(string name, ObjectMenuAction action, int slot, Vector2 position, Vector2 size, out Image image)
        {
            var button = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(BoxCollider), typeof(ObjectMenuButton));
            button.transform.SetParent(panel.transform, false);
            var rect = button.GetComponent<RectTransform>();
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            image = button.GetComponent<Image>();
            var collider = button.GetComponent<BoxCollider>();
            collider.size = new Vector3(size.x, size.y, 12f);
            button.GetComponent<ObjectMenuButton>().Configure(this, action, slot);
            var text = ManualObjectDemo.AddText(button.transform, "Label", displayFont, 21, size - new Vector2(20, 0), Vector2.zero);
            text.alignment = TextAnchor.MiddleCenter;
            return text;
        }
    }
}
