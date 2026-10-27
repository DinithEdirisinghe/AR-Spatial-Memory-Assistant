using System;
using SpatialSense.Bootstrap;
using SpatialSense.ManualDemo;
using UnityEngine;
using UnityEngine.UI;

namespace SpatialSense.RoomScan
{
    public sealed class RoomScanMenu : MonoBehaviour
    {
        private LiveRoomScan scanner;
        private OVRCameraRig rig;
        private Font font;
        private GameObject panel;
        private Text status, footer, hud;
        private readonly Text[] labels = new Text[8];
        private readonly Image[] backgrounds = new Image[8];
        private readonly Action[] actions = new Action[8];
        private Material pointerMaterial;
        private QuestControllerPointer pointer;
        private readonly ScanPanelButton[] buttons = new ScanPanelButton[8];
        private int page, viewMode;
        private float nextRefresh, newConfirmDeadline, liveResetDeadline;
        public bool Visible => panel != null && panel.activeSelf;

        public void Configure(LiveRoomScan source, OVRCameraRig cameraRig, Material material, Font displayFont)
        {
            scanner = source; rig = cameraRig; font = displayFont;
            pointerMaterial = new Material(material);
            pointerMaterial.SetColor("_BaseColor", Color.white);
            pointerMaterial.SetFloat("_Surface", 0); pointerMaterial.SetFloat("_SrcBlend", 1); pointerMaterial.SetFloat("_DstBlend", 0);
            pointerMaterial.SetFloat("_ZWrite", 1); pointerMaterial.DisableKeyword("_SURFACE_TYPE_TRANSPARENT"); pointerMaterial.renderQueue = 2000;
            var pointerObject = new GameObject("Scan menu controller pointer"); pointerObject.SetActive(false); pointerObject.transform.SetParent(transform, false);
            pointer = pointerObject.AddComponent<QuestControllerPointer>(); pointer.Configure(rig, pointerMaterial); pointerObject.SetActive(true);
            panel = new GameObject("Room scan control panel", typeof(RectTransform), typeof(Canvas), typeof(Image));
            panel.transform.SetParent(transform, false); panel.transform.localScale = Vector3.one * 0.0011f;
            panel.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            panel.GetComponent<RectTransform>().sizeDelta = new Vector2(760, 650);
            panel.GetComponent<Image>().color = new Color(0.02f, 0.04f, 0.07f, 0.96f);
            var title = Text(panel.transform, "Title", 26, 690, 40, 0, 280); title.text = "SpatialSense | Dynamic routing v7";
            status = Text(panel.transform, "Status", 19, 690, 90, 0, 210);
            var tabs = new[] { "Scan", "Items", "Route", "App tools", "Dynamic", "Options" };
            for (var i = 0; i < tabs.Length; i++)
            {
                var target = i;
                Button(tabs[i], -285 + 114 * i, 130, 108, 44, () => { page = target; newConfirmDeadline = liveResetDeadline = 0; Refresh(); });
            }
            for (var i = 0; i < 8; i++)
            {
                var slot = i;
                var button = Button("Action " + i, (i % 2 == 0 ? -175 : 175), 62 - i / 2 * 65, 330, 52, () => Execute(slot));
                labels[i] = button.GetComponentInChildren<Text>(); backgrounds[i] = button.GetComponent<Image>();
                buttons[i] = button.GetComponent<ScanPanelButton>();
            }
            footer = Text(panel.transform, "Help", 18, 690, 100, 0, -245);
            var hudGo = new GameObject("Scan status", typeof(RectTransform), typeof(Canvas));
            hudGo.transform.SetParent(rig.centerEyeAnchor, false); hudGo.transform.localPosition = new Vector3(0, -0.38f, 1.2f);
            hudGo.transform.localScale = Vector3.one * 0.001f; hudGo.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            hud = Text(hudGo.transform, "Status", 19, 790, 145, 0, 0);
            Show(true); ApplyView();
        }

        private Text Text(Transform parent, string name, int size, float width, float height, float x, float y)
            => ManualObjectDemo.AddText(parent, name, font, size, new Vector2(width, height), new Vector2(x, y));

        private GameObject Button(string name, float x, float y, float width, float height, Action action)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(BoxCollider));
            go.transform.SetParent(panel.transform, false);
            var rect = go.GetComponent<RectTransform>(); rect.sizeDelta = new Vector2(width, height); rect.anchoredPosition = new Vector2(x, y);
            go.GetComponent<Image>().color = new Color(0.12f, 0.28f, 0.38f);
            go.GetComponent<BoxCollider>().size = new Vector3(width, height, 12);
            go.AddComponent<ScanPanelButton>().Configure(this, action);
            var label = Text(go.transform, "Label", 21, width - 20, height - 4, 0, 0); label.text = name; label.alignment = TextAnchor.MiddleCenter;
            return go;
        }

        private void Update()
        {
            if (scanner == null) return;
            if (OVRInput.GetDown(OVRInput.Button.PrimaryThumbstick, OVRInput.Controller.LTouch)) Show(!Visible);
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + 0.2f;
            if (Visible) Refresh();
            hud.gameObject.SetActive(!Visible);
            hud.text = scanner.Items.HasActiveFlow ? scanner.Items.Status + "\n" + scanner.Navigation.LiveSummary + "\nLeft stick: menu | right stick: cancel"
                : scanner.Summary + "\n" + scanner.Navigation.Status + "\n" + scanner.Navigation.LiveSummary + "\nLeft stick: menu | A: capture/pause | B: save | X: view";
        }

        public void Show(bool visible)
        {
            if (panel == null) return;
            panel.SetActive(visible);
            if (pointer != null) pointer.enabled = visible;
            if (!visible) { newConfirmDeadline = 0; return; }
            var head = rig.centerEyeAnchor;
            var forward = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.1f) forward = Vector3.forward;
            panel.transform.SetPositionAndRotation(head.position + forward * 1.5f - Vector3.up * 0.1f, Quaternion.LookRotation(forward));
            Refresh();
        }

        public bool CanClick => Visible && !scanner.IsWorking;
        private void Execute(int slot)
        { if (CanClick && actions[slot] != null) { actions[slot](); if (Visible) Refresh(); } }
        private void Set(int index, string label, Action action)
        {
            labels[index].text = label; actions[index] = action;
            labels[index].fontSize = label.Contains("\n") ? 18 : 21;
            buttons[index].enabled = action != null;
            backgrounds[index].color = action != null && !scanner.IsWorking ? new Color(0.12f, 0.28f, 0.38f) : new Color(0.09f, 0.11f, 0.13f);
        }
        private void Refresh()
        {
            status.text = page == 1 ? scanner.Items.Status : scanner.ScanMethodName + "\n" + scanner.Summary;
            for (var i = 0; i < 8; i++) Set(i, "", null);
            var nav = scanner.Navigation;
            if (page == 0)
            {
                Set(0, "Method: " + scanner.ScanMethodName, scanner.ToggleScanMethod);
                if (scanner.UseAppScan)
                {
                    Set(1, scanner.IsScanning ? "Pause app capture" : "Start / continue app scan", scanner.ToggleCapture);
                    Set(2, "Save app mesh + floor", scanner.ScanUsable ? scanner.SaveScan : (Action)null);
                    Set(3, "Load saved app scan", scanner.LoadScan);
                    Set(4, Time.unscaledTime < newConfirmDeadline ? "CONFIRM new app scan" : "New app scan", ConfirmNew);
                    Set(5, "Set app floor height", scanner.ScanUsable ? nav.BeginFloorSelection : (Action)null);
                }
                else
                {
                    Set(1, "Scan room with Meta", scanner.Room.ScanRoom);
                    Set(2, "Save native mesh + room", scanner.Room.Ready ? scanner.Room.SaveRoomCopy : (Action)null);
                    Set(3, "Load previous Meta room", scanner.Room.LoadRoom);
                    Set(4, "Rebuild walking map", nav.HasFloor ? nav.BuildMap : (Action)null);
                    Set(5, "Show mesh statistics", scanner.ToggleStatistics);
                }
                Set(6, "View: " + ViewName, CycleView);
                Set(7, "Close menu", () => Show(false));
                footer.text = scanner.StatisticsVisible ? scanner.Statistics : scanner.UseAppScan
                    ? "App scan: look around the floor and furniture; pause, set floor, then Route > Rebuild. App tools has detail/export controls. Switching methods keeps both scans."
                    : "Meta scan opens native Space Setup. Complete it and return. Use Method to compare with the app's own live scan. Each method builds its own walking map.";
            }
            else if (page == 1)
            {
                var items = scanner.Items;
                Set(0, items.SlotLabel(0), () => items.Select(0));
                Set(1, items.SlotLabel(1), () => items.Select(1));
                Set(2, items.SlotLabel(2), () => items.Select(2));
                Set(3, "Register / update", items.CanRegister ? items.Register : (Action)null);
                Set(4, "Find selected item", items.CanFind ? items.Find : (Action)null);
                Set(5, "Found it", items.CanConfirm ? items.ConfirmFound : (Action)null);
                Set(6, "Cancel item action", items.Cancel);
                Set(7, items.ForgetArmed ? "CONFIRM forget" : "Forget selected item", items.CanForget ? items.Forget : (Action)null);
                footer.text = !scanner.Room.Ready ? "Item memory needs a Meta room identity. Options > Load Meta room first. App scan/maps work independently."
                    : items.SelectedSummary + "\nRegister: controller beside item, then right grip. Find uses the selected scan method's map.";
            }
            else if (page == 2)
            {
                Set(0, "Rebuild walking map", nav.HasFloor ? nav.BuildMap : (Action)null);
                Set(1, "Choose destination", nav.HasMap ? nav.BeginDestinationSelection : (Action)null);
                Set(2, nav.LiveEnabled ? "Dynamic routing: ON" : "Dynamic routing: OFF", nav.HasMap ? nav.ToggleLive : (Action)null);
                Set(3, "Clear route", nav.ClearRoute);
                Set(4, $"Extra margin: {nav.Clearance * 100:0} cm", nav.CycleClearance);
                Set(5, "View: " + ViewName, CycleView);
                Set(6, scanner.UseAppScan ? "Set app floor height" : "Load Meta room", scanner.UseAppScan ? (scanner.ScanUsable ? nav.BeginFloorSelection : (Action)null) : scanner.Room.LoadRoom);
                Set(7, "Close menu", () => Show(false));
                footer.text = nav.Status + "\n" + nav.MapSummary;
            }
            else if (page == 3)
            {
                Set(0, "Detail: " + scanner.DetailName, scanner.UseAppScan ? scanner.CycleDetail : (Action)null);
                Set(1, "Export app mesh (OBJ)", scanner.UseAppScan && scanner.ScanUsable ? scanner.ExportMesh : (Action)null);
                Set(2, "Set app floor height", scanner.UseAppScan && scanner.ScanUsable ? nav.BeginFloorSelection : (Action)null);
                Set(3, "Coverage: " + nav.CoverageName, scanner.UseAppScan ? nav.CycleCoverage : (Action)null);
                Set(4, "Show mesh statistics", scanner.ToggleStatistics);
                Set(5, "View: " + ViewName, CycleView);
                Set(6, "Rebuild walking map", scanner.UseAppScan && nav.HasFloor ? nav.BuildMap : (Action)null);
                Set(7, "Close menu", () => Show(false));
                footer.text = scanner.UseAppScan ? scanner.Statistics + "\n" + nav.Status
                    : "Choose App live scan in the Scan tab to enable these controls. Native and app meshes are stored separately.";
            }
            else if (page == 4)
            {
                Set(0, nav.LiveEnabled ? "Dynamic routing: ON" : "Dynamic routing: OFF", nav.HasMap ? nav.ToggleLive : (Action)null);
                Set(1, nav.LiveOverlayVisible ? "Obstacle highlight: ON" : "Obstacle highlight: OFF", nav.ToggleLiveOverlay);
                Set(2, "Mark test / reset counters", nav.ResetLiveMeasurements);
                Set(3, Time.unscaledTime < liveResetDeadline ? "CONFIRM reset live blocks" : "Reset live blocks", nav.HasMap ? ConfirmLiveReset : (Action)null);
                Set(4, $"Extra margin: {nav.Clearance * 100:0} cm", nav.CycleClearance);
                Set(5, "Show floor + mesh", ShowMap);
                Set(6, "View: " + ViewName, CycleView);
                Set(7, "Close menu", () => Show(false));
                footer.text = nav.LiveSummary + "\n" + nav.LiveMeasurements;
            }
            else
            {
                Set(0, nav.LiveEnabled ? "Live checks: ON" : "Live checks: OFF", nav.HasMap ? nav.ToggleLive : (Action)null);
                Set(1, "View: " + ViewName, CycleView);
                Set(2, $"Extra margin: {nav.Clearance * 100:0} cm", nav.CycleClearance);
                Set(3, "Save native mesh + room", scanner.Room.Ready ? scanner.Room.SaveRoomCopy : (Action)null);
                Set(4, "Load previous Meta room", scanner.Room.LoadRoom);
                Set(5, Time.unscaledTime < liveResetDeadline ? "CONFIRM reset live blocks" : "Reset live blocks", nav.HasMap ? ConfirmLiveReset : (Action)null);
                Set(6, scanner.StatisticsVisible ? "Hide scan statistics" : "Show scan statistics", scanner.ToggleStatistics);
                Set(7, "Close menu", () => Show(false));
                footer.text = scanner.StatisticsVisible ? scanner.Statistics + "\n" + nav.Diagnostics
                    : "Load Meta room supplies item-memory identity for either map method. Live checks add nearby obstacle restrictions to the selected map.";
            }
        }
        private void ConfirmNew()
        {
            if (Time.unscaledTime < newConfirmDeadline) { newConfirmDeadline = 0; scanner.StartNewScan(); }
            else newConfirmDeadline = Time.unscaledTime + 5f;
        }
        private void ConfirmLiveReset()
        {
            if (Time.unscaledTime < liveResetDeadline) { liveResetDeadline = 0; scanner.Navigation.ClearLiveLayer(); }
            else liveResetDeadline = Time.unscaledTime + 5f;
        }
        private string ViewName => new[] { "surfaces", "floor map", "both", "hidden" }[viewMode];
        public void CycleView() { viewMode = (viewMode + 1) % 4; ApplyView(); }
        public void ShowMap() { viewMode = 2; ApplyView(); }
        public void RefreshView() { ApplyView(); if (Visible) Refresh(); }
        private void ApplyView()
        {
            scanner.MeshVisible = scanner.UseAppScan && (viewMode == 0 || viewMode == 2);
            scanner.Room.MeshVisible = !scanner.UseAppScan && (viewMode == 0 || viewMode == 2);
            scanner.Navigation.MapVisible = viewMode == 1 || viewMode == 2;
        }
        private void OnDestroy() { if (pointerMaterial != null) Destroy(pointerMaterial); if (hud != null) Destroy(hud.transform.parent.gameObject); }
    }

}
