using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace SpatialSense.RoomScan
{
    public sealed class NavigationEvaluation : MonoBehaviour
    {
        private LiveRoomScan app;
        private OVRCameraRig rig;
        private NavigationTrialStore store;
        private NavigationTrial trial;
        private readonly string session = Guid.NewGuid().ToString("N");
        private bool ready, saveFailed, previousDynamic, previousFurniture, restoring, navigationIntervention;
        private string preparedItem, preparedRoom, preparedAnchor;
        private bool preparedAppScan;
        private float preparedMargin;
        private Vector3 preparedPosition;
        private double started, prepareDeadline, nextSample, nextSave, nextFindRetry;
        public NavigationTrialMode Mode { get; private set; } = NavigationTrialMode.FurnitureUpdates;
        public NavigationTrialScenario Scenario { get; private set; }
        public bool IsPreparing { get; private set; }
        public bool IsExporting { get; private set; }
        public bool IsRunning => trial != null && trial.Running;
        public bool IsLocked => IsPreparing || IsRunning;
        public int CompletedCount { get; private set; }
        public string Status { get; private set; } = "Starting evaluation storage...";
        public string LatestResult { get; private set; } = "No completed trial yet.";
        public string ExportFolder => store == null ? "" : Path.Combine(store.Root, "exports");
        public string ModeName => Mode == NavigationTrialMode.FixedMap ? "Fixed map" : Mode == NavigationTrialMode.LiveObstacles ? "Live obstacles" : "Furniture updates";
        public string ScenarioName => Scenario == NavigationTrialScenario.ClearPath ? "Clear path" : Scenario == NavigationTrialScenario.AddedObstacle ? "Add obstacle" : Scenario == NavigationTrialScenario.RemovedObstacle ? "Remove obstacle" : "Move scanned furniture";
        public string ItemName => app.Items.SelectedId;
        public bool CanStart => ready && !IsLocked && !IsExporting && !saveFailed && !app.IsWorking && !app.IsScanning
            && (app.RoomNavigation==null || (!app.RoomNavigation.IsActive && !app.RoomNavigation.IsBuilding))
            && app.Room.Ready && app.Navigation.HasFloor && app.Items.CanFind
            && app.Items.TryGetTargetCoordinates(app.Items.SelectedId, out _);
        public bool CanExport => ready && !IsLocked && !IsExporting;
        public bool CanSucceed => IsRunning && app.Items.CanConfirm;
        public bool AddedMarked => IsRunning && trial.Record.addedMarkerSeconds >= 0;
        public bool RemovedMarked => IsRunning && trial.Record.removedMarkerSeconds >= 0;
        public string Summary => IsRunning ? $"Trial {trial.Record.trialId.Substring(0, 8)} | {ModeName} | {Elapsed():0.0} s"
            + $"\nFalse blocks {trial.Record.falseBlocks} | misses {trial.Record.missedObstacles}" + (saveFailed ? " | SAVE FAILED: end trial and retry export" : "")
            : IsPreparing ? "Preparing a fresh map for this trial..." : Status;
        public string SetupHelp => !ready ? Status : saveFailed ? "A trial could not be saved. Use Results > Export CSV to retry before starting another."
            : !app.Room.Ready ? "Load the matching Meta room for item memory. Both scanning methods can then be tested."
            : !string.IsNullOrEmpty(app.Items.SelectedSavedRoomId) && app.Items.SelectedSavedRoomId!=app.Room.RoomUuid.ToString("D")
                ? "These trials are single-room. Use Items > Find to return to the saved item's room first."
            : !app.Navigation.HasFloor ? "Capture/load your selected scan and set its floor before testing."
            : !app.Items.CanFind ? "Register an item first, then return to Tests."
            : !app.Items.TryGetTargetCoordinates(app.Items.SelectedId, out _) ? "Locate this item's saved anchor before starting."
            : "Restore the physical room for this scenario, stand at the same starting point, then Start. Fixed map does not react to room changes.";

        public async void Configure(LiveRoomScan source, OVRCameraRig cameraRig)
        {
            app = source; rig = cameraRig; app.Navigation.TrialSignal += OnSignal;
            try
            {
                store = new NavigationTrialStore(Path.Combine(Application.persistentDataPath, "evaluation"));
                var recovered = await Task.Run(() => store.RecoverInterrupted(Parse, Serialize));
                if (this == null) return;
                var latestUtc = "";
                foreach (var record in store.Read(Parse))
                {
                    if (record.outcome == "Running") continue;
                    CompletedCount++;
                    if (string.CompareOrdinal(record.startedUtc, latestUtc) >= 0)
                    { latestUtc = record.startedUtc; LatestResult = record.outcome + " | " + record.mode + " | " + record.elapsedSeconds.ToString("0.0") + " s"; }
                }
                ready = true;
                Status = recovered > 0 ? $"Recovered {recovered} interrupted checkpoint(s) as aborted. Choose a mode and scenario."
                    : "Choose a mode and scenario. Register/locate your test item before starting.";
            }
            catch (Exception error) { Status = "Evaluation storage unavailable: " + error.Message; Debug.LogWarning(error); }
        }
        private static NavigationTrialRecord Parse(string text) => JsonUtility.FromJson<NavigationTrialRecord>(text);
        private static string Serialize(NavigationTrialRecord record) => JsonUtility.ToJson(record);
        public void CycleMode() { if (!IsLocked && !IsExporting) Mode = (NavigationTrialMode)(((int)Mode + 1) % 3); }
        public void CycleScenario() { if (!IsLocked && !IsExporting) Scenario = (NavigationTrialScenario)(((int)Scenario + 1) % 4); }
        public void CycleItem() { if (!IsLocked && !IsExporting) app.Items.Select((app.Items.SelectedSlot + 1) % 3); }
        public void CycleScanMethod() { if (!IsLocked && !IsExporting) app.ToggleScanMethod(); }

        public void StartTrial()
        {
            if (!CanStart) { Status = SetupHelp; return; }
            previousDynamic = app.Navigation.LiveRequested; previousFurniture = app.Navigation.FurnitureUpdatesEnabled;
            preparedItem = app.Items.SelectedId; preparedRoom = app.Room.RoomUuid.ToString("D"); preparedAppScan = app.UseAppScan;
            app.Items.TryGetSnapshot(preparedItem, out var item); preparedAnchor = item.AnchorId;
            preparedMargin = app.Navigation.Clearance; app.Items.TryGetTargetCoordinates(preparedItem, out preparedPosition);
            app.Items.Cancel(); IsPreparing = true; prepareDeadline = Time.realtimeSinceStartupAsDouble + 30;
            app.Navigation.PrepareEvaluationMode(Mode);
            Status = "Preparing a clean map; trial timing has not started.";
        }

        private void Update()
        {
            if (app == null) return;
            if (IsLocked && OVRInput.GetDown(OVRInput.Button.PrimaryThumbstick, OVRInput.Controller.RTouch))
            { AbortTrial(); return; }
            if (IsPreparing)
            {
                if (!ContextMatches(false)) { StopPreparation("Room, item, scan or tracking changed while preparing."); return; }
                if (Time.realtimeSinceStartupAsDouble > prepareDeadline) { StopPreparation("Map preparation timed out. Check the selected scan."); return; }
                if (app.Navigation.IsBuilding) return;
                if (!app.Navigation.EvaluationReady) { StopPreparation("Map preparation failed: " + app.Navigation.Status); return; }
                if (Mode != NavigationTrialMode.FixedMap && !app.Navigation.LiveEnabled)
                { StopPreparation("Live depth unavailable. Allow spatial-data access and try again."); return; }
                if (Mode == NavigationTrialMode.FurnitureUpdates && app.Navigation.EvaluationFurnitureCandidates == 0)
                { StopPreparation("No eligible scanned furniture here. Check labels/floor support, or choose Live obstacles."); return; }
                BeginMeasurement();
            }
            if (!IsRunning) return;
            if (navigationIntervention || !ContextMatches(true)) { Finish("Aborted", "configuration_or_tracking_changed"); return; }
            if (app.Items.FoundConfirmed) { Finish("Success", "user_confirmed_found_near_saved_item"); return; }
            var now = Time.realtimeSinceStartupAsDouble;
            if (now >= nextSample) { nextSample = now + 0.1; Sample(); }
            // An initially blocked item search is part of the trial, not a reason
            // to discard it. Retry as the same room becomes navigable.
            if (!app.Navigation.HasDestination && !app.Items.HasActiveFlow && app.Items.CanFind && now >= nextFindRetry)
            {
                nextFindRetry = now + 1;
                trial.Observe(Elapsed(), new NavigationTrialSignal { Name = "guidance_retry", ConfirmationMs = -1, ResponseMs = -1 });
                app.Items.Find();
            }
            if (now >= nextSave) { nextSave = now + 2; SaveCheckpoint(); }
        }

        private bool ContextMatches(bool running)
        {
            if (!app.Room.Ready || !OVRManager.hasVrFocus || !OVRPlugin.GetNodePositionTracked(OVRPlugin.Node.EyeCenter)
                || app.IsScanning || app.UseAppScan != preparedAppScan || app.Items.SelectedId != preparedItem
                || app.Room.RoomUuid.ToString("D") != preparedRoom || Mathf.Abs(app.Navigation.Clearance - preparedMargin) > 0.0001f
                || !app.Items.TryGetTargetCoordinates(preparedItem, out var position) || Vector3.Distance(position, preparedPosition) > 0.25f
                || !app.Items.TryGetSnapshot(preparedItem, out var item) || item.AnchorId != preparedAnchor) return false;
            if (!running) return true;
            var r = trial.Record;
            return app.Navigation.EvaluationReady && app.Navigation.EvaluationMapGeneration == r.mapGeneration
                && app.Navigation.EvaluationSourceRevision == r.sourceRevision && app.Navigation.EvaluationSemanticRevision == r.semanticRevision
                && app.Navigation.LiveOverlayVisible == r.highlightsEnabled && app.Navigation.MapVisible
                && (!app.Navigation.HasDestination || app.Navigation.RouteOwner == preparedItem)
                && app.Navigation.LiveEnabled == (Mode != NavigationTrialMode.FixedMap)
                && app.Navigation.FurnitureUpdatesEnabled == (Mode == NavigationTrialMode.FurnitureUpdates);
        }

        private void BeginMeasurement()
        {
            app.Navigation.ResetEvaluationEvidence();
            var record = new NavigationTrialRecord { trialId = Guid.NewGuid().ToString("N"), sessionId = session,
                startedUtc = DateTime.UtcNow.ToString("O"), mode = Mode.ToString(), scenario = Scenario.ToString(),
                scanMethod = app.ScanMethodName, roomId = preparedRoom, itemId = preparedItem,
                itemAnchorId = preparedAnchor, strictCoverage = !app.UseAppScan || app.Navigation.StrictCoverage, highlightsEnabled = app.Navigation.LiveOverlayVisible,
                build = "v10.1/" + Application.buildGUID,
                mapGeneration = app.Navigation.EvaluationMapGeneration, sourceRevision = app.Navigation.EvaluationSourceRevision,
                semanticRevision = app.Navigation.EvaluationSemanticRevision, floorCells = app.Navigation.EvaluationFloorCells,
                furnitureCandidates = app.Navigation.EvaluationFurnitureCandidates, cellMetres = ScanNavigationGrid.CellSize, marginMetres = preparedMargin };
            trial = new NavigationTrial(record); started = Time.realtimeSinceStartupAsDouble; IsPreparing = false; navigationIntervention = false;
            trial.Observe(0, new NavigationTrialSignal { Name = "trial_started", ConfirmationMs = -1, ResponseMs = -1 }); Sample();
            if (!SaveCheckpoint()) { Finish("Aborted", "initial_checkpoint_write_failed"); return; }
            nextSave = started + 2; nextFindRetry = started + 1;
            app.Items.Find(); app.CloseMenu(); Status = "Trial running. Use Tests for markers and outcome.";
        }
        private double Elapsed() => Math.Max(0, Time.realtimeSinceStartupAsDouble - started);
        private void Sample()
        {
            var p = rig.centerEyeAnchor.position;
            trial.Sample(Elapsed(), OVRPlugin.GetNodePositionTracked(OVRPlugin.Node.EyeCenter), p.x, p.z,
                app.Navigation.RouteAvailable, app.Navigation.PlannedRouteMetres, app.Navigation.RecentLiveDepth);
        }
        private void OnSignal(NavigationTrialSignal signal)
        {
            if (!IsRunning) return;
            trial.Observe(Elapsed(), signal);
            if (signal.Name == "live_layer_manually_reset" || signal.Name == "furniture_updates_enabled" || signal.Name == "furniture_updates_disabled"
                || signal.Name == "dynamic_checks_enabled" || signal.Name == "dynamic_checks_disabled") navigationIntervention = true;
        }
        public void MarkAdded() { if (IsRunning && trial.MarkAdded(Elapsed())) SaveCheckpoint(); }
        public void MarkRemoved() { if (IsRunning && trial.MarkRemoved(Elapsed())) SaveCheckpoint(); }
        public void FlagFalseBlock() { if (IsRunning) { trial.Flag(Elapsed(), true); SaveCheckpoint(); } }
        public void FlagMissedObstacle() { if (IsRunning) { trial.Flag(Elapsed(), false); SaveCheckpoint(); } }
        public void Succeed()
        {
            if (!CanSucceed) return;
            Sample(); app.Items.ConfirmFound(); Finish("Success", "user_confirmed_found_near_saved_item");
        }
        public void FailTrial() { if (IsRunning) Finish("Failed", "user_reported_could_not_finish"); }
        public void AbortTrial()
        { if (IsPreparing) StopPreparation("Preparation cancelled; no timed trial started."); else if (IsRunning) Finish("Aborted", "user_cancelled"); }
        private void StopPreparation(string message)
        { IsPreparing = false; RestoreSettings(); Status = message; }
        private void Finish(string outcome, string reason)
        {
            if (!IsRunning) return;
            if (app != null && rig != null && rig.centerEyeAnchor != null) Sample();
            if (!trial.Finish(Elapsed(), outcome, reason, DateTime.UtcNow.ToString("O"))) return;
            CompletedCount++; LatestResult = outcome + " | " + ModeName + " | " + trial.Record.elapsedSeconds.ToString("0.0") + " s";
            var saved = SaveCheckpoint();
            if (outcome != "Success" && app != null && app.Items != null) app.Items.Cancel();
            RestoreSettings();
            Status = saved ? LatestResult + ". Saved on this headset; Results > Export CSV." : "Trial kept in memory but save failed. Retry Export CSV before starting another trial.";
        }
        private bool SaveCheckpoint()
        {
            try { store.Save(trial.Record, Serialize(trial.Record)); saveFailed = false; return true; }
            catch (Exception error) { saveFailed = true; Status = "Trial save failed: " + error.Message; Debug.LogWarning(error); return false; }
        }
        private void RestoreSettings()
        {
            if (restoring || app == null || app.Navigation == null) return;
            restoring = true;
            try { app.Navigation.RestoreEvaluationSettings(previousDynamic, previousFurniture); }
            finally { restoring = false; }
        }
        public async void ExportCsv()
        {
            if (!CanExport) return;
            if (saveFailed && trial != null && !SaveCheckpoint()) return;
            IsExporting = true; Status = "Exporting saved trials and timestamped events...";
            try
            {
                var result = await Task.Run(() => store.Export(Parse));
                if (this == null) return;
                Status = $"Exported {result.Trials} trials and {result.Events} events to evaluation/exports."
                    + (result.Skipped > 0 ? $" {result.Skipped} record(s) skipped; see the completion file." : "");
                Debug.Log("[SpatialSense Evaluation] " + result.SummaryPath);
            }
            catch (Exception error) { if (this != null) { Status = "CSV export failed: " + error.Message; Debug.LogWarning(error); } }
            finally { if (this != null) IsExporting = false; }
        }
        private void OnApplicationPause(bool paused) { if (paused) { if (IsPreparing) StopPreparation("Preparation interrupted."); else if (IsRunning) Finish("Aborted", "application_paused"); } }
        private void OnApplicationQuit() { if (IsRunning) Finish("Aborted", "application_closed"); }
        private void OnDestroy()
        {
            if (IsRunning) Finish("Aborted", "application_closed");
            if (app != null && app.Navigation != null) app.Navigation.TrialSignal -= OnSignal;
        }
    }
}
