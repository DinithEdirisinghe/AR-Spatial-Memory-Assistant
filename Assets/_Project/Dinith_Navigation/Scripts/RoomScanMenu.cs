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
        private readonly GameObject[] tabObjects = new GameObject[10];
        private int page = 9, viewMode = 2;
        private bool evaluationResults;
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
            var title = Text(panel.transform, "Title", 26, 690, 40, 0, 280); title.text = "SpatialSense | Live home v12.1";
            status = Text(panel.transform, "Status", 19, 690, 90, 0, 210);
            var tabs = new[] { "Home", "Items", "Route", "Scan", "App tools", "Dynamic", "Furniture", "Options", "Tests", "Rooms" };
            var tabPages = new[] {9,1,2,0,3,4,5,6,7,8};
            for (var i = 0; i < tabs.Length; i++)
            {
                var target = tabPages[i];
                var tab = Button(tabs[i], -280 + 140 * (i % 5), 140 - 55 * (i / 5), 132, 44, () => { page = target; newConfirmDeadline = liveResetDeadline = 0; Refresh(); });
                tabObjects[i]=tab;
                tab.GetComponentInChildren<Text>().fontSize = 19;
            }
            for (var i = 0; i < 8; i++)
            {
                var slot = i;
                var button = Button("Action " + i, (i % 2 == 0 ? -175 : 175), 20 - i / 2 * 60, 330, 52, () => Execute(slot));
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
            hud.text = scanner.Home.Active ? scanner.Home.Summary+"\nLeft stick: menu | A: scan / pause | B: finish & save"
                : scanner.Evaluation.IsLocked ? scanner.Evaluation.Summary + "\n" + scanner.Navigation.Status + "\nLeft stick: Tests menu | right stick: abort trial"
                : scanner.RoomNavigation.IsActive ? scanner.RoomNavigation.Status + "\n" + scanner.Navigation.LiveSummary + "\nLeft stick: Rooms menu | right stick: cancel room action"
                : scanner.IsScanning ? scanner.Summary + "\n" + scanner.Navigation.Status + "\nLeft stick: menu | A: pause | B: save"
                : scanner.Items.HasActiveFlow ? scanner.Items.Status + "\n" + scanner.Navigation.LiveSummary + "\nLeft stick: menu | right stick: cancel"
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

        public bool CanClick => Visible && (scanner.Home.Active ? !scanner.Home.Busy : (!scanner.IsWorking || (page == 7 && scanner.Evaluation.IsPreparing)));
        private void Execute(int slot)
        { if (CanClick && actions[slot] != null) { actions[slot](); if (Visible) Refresh(); } }
        private void Set(int index, string label, Action action)
        {
            labels[index].text = label; actions[index] = action;
            labels[index].fontSize = label.Contains("\n") ? 18 : 21;
            buttons[index].enabled = action != null;
            backgrounds[index].color = action != null && CanClick ? new Color(0.12f, 0.28f, 0.38f) : new Color(0.09f, 0.11f, 0.13f);
        }
        private void Refresh()
        {
            for(var i=0;i<tabObjects.Length;i++)if(tabObjects[i]!=null)tabObjects[i].SetActive(!scanner.Home.Active || i<3);
            if(page==9 || scanner.Home.Active) { RefreshHome(); return; }
            status.text = page == 8 ? scanner.RoomNavigation.Summary : page == 7 ? scanner.Evaluation.Summary : page == 1 ? scanner.Items.Status : scanner.ScanMethodName + "\n" + (page == 5 ? scanner.Navigation.Status : scanner.Summary);
            for (var i = 0; i < 8; i++) Set(i, "", null);
            var nav = scanner.Navigation;
            if (page == 0)
            {
                Set(0, "Method: " + scanner.ScanMethodName, scanner.ToggleScanMethod);
                if (scanner.UseAppScan)
                {
                    Set(1, scanner.IsScanning ? "Pause MRUK live capture" : "Start / continue MRUK scan", scanner.ToggleCapture);
                    Set(2, "Save app mesh + floor", scanner.ScanUsable ? scanner.SaveScan : (Action)null);
                    Set(3, "Load saved app scan", scanner.LoadScan);
                    Set(4, Time.unscaledTime < newConfirmDeadline ? "CONFIRM new app scan" : "New app scan", ConfirmNew);
                    Set(5, "Set app floor height", scanner.ScanUsable ? nav.BeginFloorSelection : (Action)null);
                }
                else
                {
                    Set(1, "Open Meta room setup", scanner.Room.ScanRoom);
                    Set(2, "Save native mesh + room", scanner.Room.Ready ? scanner.Room.SaveRoomCopy : (Action)null);
                    Set(3, "Load previous Meta room", scanner.Room.LoadRoom);
                    Set(4, "Rebuild walking map", nav.HasFloor ? nav.BuildMap : (Action)null);
                    Set(5, "Show mesh statistics", scanner.ToggleStatistics);
                }
                Set(6, "View: " + ViewName, CycleView);
                Set(7, "Close menu", () => Show(false));
                footer.text = scanner.StatisticsVisible ? scanner.Statistics : scanner.UseAppScan
                    ? "App live scan uses MRUK depth inside this app. Look around, pause, set floor, then rebuild its map. Method switches to Meta's full-screen room setup."
                    : "Meta room scan opens Quest's full-screen Space Setup. Complete it and return. Method switches to our in-app MRUK live-depth scan.";
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
            else if (page == 5)
            {
                Set(0, nav.FurnitureUpdatesEnabled ? "Furniture updates: ON" : "Furniture updates: OFF", nav.HasMap ? nav.ToggleFurnitureUpdates : (Action)null);
                Set(1, "Show walking floor only", ShowWalkingFloor);
                Set(2, nav.LiveEnabled ? "Dynamic routing: ON" : "Dynamic routing: OFF", nav.HasMap ? nav.ToggleLive : (Action)null);
                Set(3, $"Extra margin: {nav.Clearance * 100:0} cm", nav.CycleClearance);
                Set(4, "Reset to scanned map", nav.HasFloor ? nav.BuildMap : (Action)null);
                Set(5, "Mark test / reset counters", nav.ResetLiveMeasurements);
                Set(6, "View: " + ViewName, CycleView);
                Set(7, "Close menu", () => Show(false));
                footer.text = nav.FurnitureSummary + "\nBlue = reopened floor; magenta = new obstacle. Cyan mesh still shows the original scan.";
            }
            else if (page == 7)
            {
                var test = scanner.Evaluation;
                if (test.IsPreparing)
                {
                    Set(6, "Cancel preparation", test.AbortTrial);
                    Set(7, "Close menu", () => Show(false));
                    footer.text = "Resetting the walking map and applying this trial's mode. Timing starts when ready.";
                }
                else if (test.IsRunning)
                {
                    Set(0, test.AddedMarked ? "Added/moved: marked" : "Mark added / moved", test.AddedMarked ? (Action)null : test.MarkAdded);
                    Set(1, test.RemovedMarked ? "Removed/cleared: marked" : "Mark removed / cleared", test.RemovedMarked ? (Action)null : test.MarkRemoved);
                    Set(2, "Found item: success", test.CanSucceed ? test.Succeed : (Action)null);
                    Set(3, "End: could not finish", test.FailTrial);
                    Set(4, "Flag false obstacle", test.FlagFalseBlock);
                    Set(5, "Flag missed obstacle", test.FlagMissedObstacle);
                    Set(6, "Abort trial", test.AbortTrial);
                    Set(7, "Close menu", () => Show(false));
                    footer.text = "Markers are manual, approximate times. Report false/missed obstacles when noticed. Success requires confirmation near the saved item.";
                }
                else if (evaluationResults)
                {
                    Set(0, test.IsExporting ? "Exporting..." : "Export CSV files", test.CanExport ? test.ExportCsv : (Action)null);
                    Set(1, "Back to test setup", () => evaluationResults = false);
                    Set(2, "Recorded trials: " + test.CompletedCount, null);
                    Set(7, "Close menu", () => Show(false));
                    footer.text = test.LatestResult + "\nExports stay on this headset in evaluation/exports. Use the provided PC download helper.";
                }
                else
                {
                    Set(0, "Mode:\n" + test.ModeName, test.IsExporting ? (Action)null : test.CycleMode);
                    Set(1, "Scenario:\n" + test.ScenarioName, test.IsExporting ? (Action)null : test.CycleScenario);
                    Set(2, "Scan:\n" + scanner.ScanMethodName, test.IsExporting ? (Action)null : test.CycleScanMethod);
                    Set(3, "Item: " + test.ItemName, test.IsExporting ? (Action)null : test.CycleItem);
                    Set(4, "Start trial", test.CanStart ? test.StartTrial : (Action)null);
                    Set(5, "Locate saved item", scanner.Items.CanFind ? scanner.Items.RetryLocate : (Action)null);
                    Set(6, "Results / export CSV", () => evaluationResults = true);
                    Set(7, "Close menu", () => Show(false));
                    footer.text = test.SetupHelp;
                }
            }
            else if(page==8)
            {
                var rooms=scanner.RoomNavigation;
                Set(0,"Load saved Meta rooms",scanner.Room.LoadRoom);
                Set(1,rooms.IsTeaching?"Finish doorway: side 2":"Link doorway: side 1",rooms.MarkDoorway);
                Set(2,rooms.IsActive?"Cancel room action":"Cancel",()=>rooms.Cancel("Room action cancelled."));
                Set(3,"Doorway:\n"+rooms.SelectedConnection,rooms.CycleConnection);
                Set(4,rooms.ForgetArmed?"CONFIRM forget doorway":"Forget selected doorway",rooms.ForgetConnection);
                Set(5,"Find selected item",scanner.Items.CanFind?scanner.Items.Find:(Action)null);
                Set(6,rooms.CanAvoidDoorway?"Doorway blocked: try another":"Show current walking map",rooms.CanAvoidDoorway?rooms.AvoidCurrentDoorway:(Action)ShowWalkingFloor);
                Set(7,"Close menu",()=>Show(false));
                footer.text=rooms.IsTeaching?"Walk directly through one same-level doorway. Finish on ordinary green floor inside the next located room. Right stick cancels."
                    :"Meta scan: mark side 1 just inside a doorway, walk through, then finish side 2. Links and room navigation maps are saved. App scans remain local.";
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
            if (scanner.Evaluation.IsLocked && page != 7)
            {
                for (var i = 0; i < 8; i++) Set(i, labels[i].text, null);
                Set(7, "Close menu", () => Show(false));
                footer.text = "A trial is active. Use Tests for markers, outcomes or cancellation. Finish it before changing scan/navigation settings.";
            }
        }
        private void RefreshHome()
        {
            var home=scanner.Home;
            status.text=home.Summary;
            for(var i=0;i<8;i++)Set(i,"",null);
            if(!home.Active)
            {
                Set(0,"Use Home scanning",()=>{home.Enter();if(home.Active){page=9;ApplyView();}});
                Set(7,"Close menu",()=>Show(false));
                footer.text="One continuous live scan across connected rooms on the same floor. Previous Meta and app scans stay separate.";return;
            }
            if(page==1)
            {
                Set(0,home.ItemLabel(0),()=>home.Select(0));Set(1,home.ItemLabel(1),()=>home.Select(1));Set(2,home.ItemLabel(2),()=>home.Select(2));
                Set(3,"Register / update",home.Ready?home.Register:(Action)null);
                Set(4,"Find selected item",home.CanFind?home.Find:(Action)null);
                Set(5,"Found it",home.CanConfirm?home.Found:(Action)null);
                Set(6,"Cancel item action",home.CancelItem);Set(7,"Close menu",()=>Show(false));
                footer.text="Home items use this home's saved reference, without Meta room IDs. Register beside the item using right grip. Previous mode's items are separate.";return;
            }
            if(page==2)
            {
                Set(0,home.DynamicEnabled?"Obstacle checks: ON":"Obstacle checks: OFF",home.ToggleDynamic);
                Set(1,home.FurnitureEnabled?"Furniture updates: ON":"Furniture updates: OFF",home.ToggleFurniture);
                Set(2,$"Extra margin: {home.Clearance*100:0.#} cm",home.CycleClearance);
                Set(3,"View: "+ViewName,CycleView);
                Set(4,"Find "+home.Name,home.CanFind?home.Find:(Action)null);
                Set(5,"Clear guidance",home.CancelItem);
                Set(6,"Back to Home",()=>page=9);
                Set(7,"Close menu",()=>Show(false));
                footer.text="Floor squares: 7.5 cm. Magenta = new obstacle; blue = verified reopened floor. Look at moved furniture's old space and its floor.";return;
            }
            if(page!=9 && page!=2)
            {
                Set(0,"Back to Home",()=>page=9);
                Set(1,"Use previous scan tools",()=>{home.Leave();if(!home.Active){page=0;ApplyView();}});
                Set(7,"Close menu",()=>Show(false));
                footer.text="Home scanning connects observed floor automatically. Previous tools retain Meta room setup, app scans, dynamic furniture tests and manual doorway links.";return;
            }
            Set(0,home.Scanning?"Finish scanning & save":home.HasWorkspace?"Continue scanning home":"Start scanning your home",home.Scanning?home.Pause:(Action)home.StartOrContinue);
            Set(1,"My saved items",()=>page=1);
            Set(2,"View: "+ViewName,CycleView);
            Set(3,"Recover saved home",home.HasSaved?()=>home.Load():(Action)null);
            Set(4,"Find "+home.Name,home.CanFind?home.Find:(Action)null);
            Set(5,Time.unscaledTime<newConfirmDeadline?"CONFIRM replace home":"Start a different home",()=>
            {if(Time.unscaledTime<newConfirmDeadline){newConfirmDeadline=0;home.NewHome();}else newConfirmDeadline=Time.unscaledTime+5;});
            Set(6,"Previous scan tools",()=>{home.Leave();if(!home.Active){page=0;ApplyView();}});
            Set(7,"Close menu",()=>Show(false));
            footer.text="Live obstacles and furniture checks stay active after scanning. Green = walkable; red = scanned obstacle; magenta = new obstacle; blue = reopened floor. Route tab adjusts checks and margin.";
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
        public void ShowWalkingFloor() { viewMode = 1; ApplyView(); }
        public void RefreshView() { ApplyView(); if (Visible) Refresh(); }
        private void ApplyView()
        {
            if(scanner.Home!=null && scanner.Home.Active)
            {scanner.Home.ShowSurfaces=viewMode==0 || viewMode==2;scanner.Home.ShowFloor=viewMode==1 || viewMode==2;return;}
            scanner.MeshVisible = scanner.UseAppScan && (viewMode == 0 || viewMode == 2);
            scanner.Room.MeshVisible = !scanner.UseAppScan && (viewMode == 0 || viewMode == 2);
            scanner.Navigation.MapVisible = viewMode == 1 || viewMode == 2;
        }
        private void OnDestroy() { if (pointerMaterial != null) Destroy(pointerMaterial); if (hud != null) Destroy(hud.transform.parent.gameObject); }
    }

}
