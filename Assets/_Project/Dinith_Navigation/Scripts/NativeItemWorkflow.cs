using System;
using System.Collections.Generic;
using Project.Core.Integration;
using Project.Core.Interfaces;
using SpatialSense.Persistence;
using UnityEngine;

namespace SpatialSense.RoomScan
{
    public sealed class NativeItemWorkflow : MonoBehaviour, IObjectObservationSink, ISpatialObservationRegistry, IApplicationStateProvider
    {
        private LiveRoomScan app;
        private OVRCameraRig rig;
        private PersistentObjectMemory[] memories;
        private ApplicationDiagnostics diagnostics;
        private GameObject marker, labelRoot;
        private Material markerMaterial;
        private UnityEngine.UI.Text markerLabel;
        private bool armed, gripReleased, searching, planning, pendingFind, found, mapRequested;
        private int mainThread;
        private int selected, workflowGeneration, contextRevision = -1, armRevision;
        private string frameToken, operation;
        private bool saveCommitted;
        private float operationStarted, nextPoll, forgetDeadline;
        private string forgetToken;
        private readonly HashSet<string> accepted = new HashSet<string>();
        private readonly Queue<string> acceptedOrder = new Queue<string>();
        private readonly Dictionary<string, TrackedObjectSnapshot> previous = new Dictionary<string, TrackedObjectSnapshot>();
        public event Action<SpatialItemRecord> OnItemLocationUpdated;
        public event Action<string> OnItemLocationInvalidated;
        public event Action<ApplicationStateSnapshot> StateChanged;
        public ApplicationStateSnapshot CurrentState { get; private set; }
        public string SelectedId => ObservationPolicy.IdFor(Selected.ObjectName);
        private PersistentObjectMemory Selected => memories[selected];
        public bool IsBusy
        {
            get { if (memories == null) return false; foreach (var memory in memories) if (memory.IsBusy) return true; return false; }
        }
        private bool Initialized => memories != null && Array.TrueForAll(memories, memory => memory != null && memory.IsInitialized);
        public bool HasActiveFlow => armed || searching || planning || pendingFind || found || operation != null || ForgetArmed;
        public bool CanRegister => Initialized && app.Room.Ready && !IsBusy && !planning;
        public bool CanFind => Initialized && app.Room.Ready && Selected.HasSavedRecord && !IsBusy && !planning;
        public bool CanForget => Initialized && !IsBusy && Selected.CanForget;
        public bool ForgetArmed => forgetToken != null && Time.unscaledTime < forgetDeadline && forgetToken == Selected.ForgetToken;
        public string Status { get; private set; } = "Select an item. Register its location or choose Find.";
        public string SelectedSummary
        {
            get
            {
                if (memories == null) return "Starting item memory...";
                var text = Selected.ObjectName + ": " + Selected.ListStatus;
                if (Selected.TryGetStoredObservation(out var sighting))
                    text += "\nLast saved: " + sighting.ObservedAtUtc.ToLocalTime().ToString("g") + " | "
                        + (sighting.Origin == ObservationOrigin.ManualController ? "manual registration" : sighting.Origin.ToString());
                return text;
            }
        }
        public bool CanConfirm
        {
            get
            {
                if (!searching || !app.Room.Ready || !app.Navigation.HasDestination || app.Navigation.RouteOwner != SelectedId
                    || !Selected.TryGetLocatedPosition(out var item)) return false;
                var head = rig.centerEyeAnchor.position; var standing = app.Navigation.StandingPosition;
                return Horizontal(head, standing) <= 0.65f && Horizontal(head, item) <= 1.35f;
            }
        }

        public void Configure(LiveRoomScan application, OVRCameraRig cameraRig, Material material, Font font)
        {
            app = application; rig = cameraRig;
            mainThread = System.Threading.Thread.CurrentThread.ManagedThreadId;
            diagnostics = gameObject.GetComponent<ApplicationDiagnostics>() ?? gameObject.AddComponent<ApplicationDiagnostics>();
            memories = new PersistentObjectMemory[SavedObjectCatalog.Names.Count];
            for (var i = 0; i < memories.Length; i++)
            {
                var go = new GameObject(SavedObjectCatalog.Names[i] + " memory"); go.transform.SetParent(transform, false);
                memories[i] = go.AddComponent<PersistentObjectMemory>();
                memories[i].ConfigureForNative(app.Room, SavedObjectCatalog.Names[i], i == selected);
                memories[i].LocationCommitted += OnCommitted;
            }
            markerMaterial = new Material(material); markerMaterial.SetColor("_BaseColor", Color.yellow);
            marker = GameObject.CreatePrimitive(PrimitiveType.Sphere); marker.name = "Selected item's last saved location";
            marker.transform.SetParent(transform, false); marker.transform.localScale = Vector3.one * 0.07f;
            Destroy(marker.GetComponent<Collider>()); marker.GetComponent<Renderer>().sharedMaterial = markerMaterial;
            labelRoot = new GameObject("Last saved item label", typeof(RectTransform), typeof(Canvas));
            labelRoot.transform.SetParent(transform, false); labelRoot.transform.localScale = Vector3.one * 0.0012f;
            labelRoot.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            markerLabel = SpatialSense.ManualDemo.ManualObjectDemo.AddText(labelRoot.transform, "Item", font, 22, new Vector2(400, 80), Vector2.zero);
            markerLabel.alignment = TextAnchor.MiddleCenter;
            marker.SetActive(false); labelRoot.SetActive(false);
        }

        public string SlotLabel(int slot)
        {
            var name = slot == 0 ? "Keys" : slot == 1 ? "Wallet" : "Remote";
            return name + (slot == selected ? " [selected]" : "") + "\n" + memories[slot].ListStatus;
        }
        public void Select(int slot)
        {
            if (slot < 0 || slot >= memories.Length || IsBusy || slot == selected) return;
            UpdateContext();
            Cancel(); app.Navigation.ClearRoute(); Selected.SetSelected(false); selected = slot; Selected.SetSelected(true);
            Status = "Selected " + Selected.ObjectName + ". Register, locate or Find its saved location.";
        }
        public void Register()
        {
            if (!CanRegister) return;
            UpdateContext();
            Cancel(); app.Navigation.ClearRoute();
            armRevision = app.Room.Revision; armed = true;
            gripReleased = !OVRInput.Get(OVRInput.Button.PrimaryHandTrigger, OVRInput.Controller.RTouch);
            Status = "Hold the right controller beside " + Selected.ObjectName + ", release and squeeze its SIDE GRIP to register. Right stick cancels.";
            app.CloseMenu();
        }
        public void Find()
        {
            if (!CanFind) return;
            UpdateContext();
            Cancel(); pendingFind = true; found = false;
            if (!Selected.HasLocatedPosition)
            {
                Selected.RetryLoad(); operation = "locate"; operationStarted = Time.unscaledTime;
                Status = "Locating " + Selected.ObjectName + "'s saved anchor. Look around and wait.";
            }
            Status = Selected.HasLocatedPosition ? "Preparing the walking map for the saved item..." : Status;
            app.CloseMenu();
        }
        public void RetryLocate()
        {
            if (!Initialized || IsBusy || !app.Room.Ready) return;
            UpdateContext();
            Cancel(); Selected.RetryLoad(); operation = "locate"; operationStarted = Time.unscaledTime;
            Status = "Locating the saved item anchor...";
        }
        public void ConfirmFound()
        {
            if (!CanConfirm) return;
            searching = false; found = true; app.Navigation.ClearRoute();
            Status = "Found " + Selected.ObjectName + ". Confirmation recorded. Its saved location is unchanged.";
            diagnostics.Record("item_found", SelectedId, "User confirmation near reachable standing point; not automatic recognition.");
        }
        public void Forget()
        {
            if (!CanForget) return;
            UpdateContext();
            if (!ForgetArmed)
            { Cancel(); forgetToken = Selected.ForgetToken; forgetDeadline = Time.unscaledTime + 5f; Status = "Press Confirm forget within 5 seconds to remove this item's saved location."; return; }
            Cancel(); Selected.ForgetSaved(); operation = "forget"; operationStarted = Time.unscaledTime;
            Status = "Removing only " + Selected.ObjectName + "'s saved location...";
        }
        public void Cancel()
        {
            workflowGeneration++;
            if (HasActiveFlow) diagnostics?.Record("item_action_cancelled", SelectedId, IsBusy ? "Guidance cancelled; native anchor operation continues." : "User action or context change.");
            if (planning || pendingFind || (app != null && app.Navigation.RouteOwner == SelectedId)) app.Navigation.ClearRoute();
            if (!IsBusy) operation = null;
            armed = searching = planning = pendingFind = found = mapRequested = false; forgetToken = null;
            Status = IsBusy ? "The saved-anchor operation is still finishing. Wait for its result." : "Item action cancelled. Existing saved records are kept.";
        }

        private void Update()
        {
            if (!Initialized) return;
            UpdateContext();
            if (forgetToken != null && !ForgetArmed) { forgetToken = null; Status = "Forget confirmation expired. Nothing else was removed."; }
            if (HasActiveFlow && OVRInput.GetDown(OVRInput.Button.PrimaryThumbstick, OVRInput.Controller.RTouch)) Cancel();
            if (armed)
            {
                if (!app.Room.Ready || app.Room.Revision != armRevision || app.MenuOpen) Cancel();
                else
                {
                    if (!OVRInput.Get(OVRInput.Button.PrimaryHandTrigger, OVRInput.Controller.RTouch)) gripReleased = true;
                    if (gripReleased && OVRInput.GetControllerPositionTracked(OVRInput.Controller.RTouch)
                        && OVRInput.GetDown(OVRInput.Button.PrimaryHandTrigger, OVRInput.Controller.RTouch)) RegisterController();
                }
            }
            if (operation != null && !IsBusy)
            {
                var completed = operation; operation = null;
                if (completed == "save") Status = saveCommitted && Selected.HasSavedRecord
                    ? Selected.ObjectName + " saved. Move away, then choose Find."
                    : "Registration did not save. The previous record is kept; try Find to restore it or register again.";
                else if (completed == "forget") Status = Selected.HasSavedRecord ? "The saved item was not removed. Retry Forget."
                    : Selected.CanForget ? "Item removed; anchor cleanup is pending. Forget again retries cleanup." : "Saved item location removed.";
                else if (!Selected.HasLocatedPosition)
                { pendingFind = false; Status = "Could not locate this item in the current room scan. Load its original room, or register it again."; }
                else Status = Selected.ObjectName + " located. Choose Find when ready.";
            }
            if (operation != null && Time.unscaledTime - operationStarted > 30f)
                Status = "The saved-anchor operation is taking longer than expected. Look around and wait; restart if it remains stuck.";
            if ((searching || planning || pendingFind) && !app.Room.Ready) Cancel();
            if (searching && !Selected.HasLocatedPosition)
            { Cancel(); Status = "Item tracking was lost. Guidance cleared; choose Find to retry localization."; }
            if (searching && app.Navigation.RouteOwner != SelectedId)
            { searching = false; Status = "Item search ended. The saved location is unchanged."; }
            if (pendingFind && operation == null && !IsBusy && Selected.HasLocatedPosition)
            {
                if (app.Navigation.HasMap) PlanFind();
                else if (!app.Navigation.IsBuilding)
                {
                    if (!mapRequested) { mapRequested = true; app.Navigation.BuildMap(); Status = "Building the room walking map..."; }
                    else { pendingFind = false; Status = app.Navigation.Status; }
                }
            }
            if (searching) Status = !app.Navigation.RouteAvailable ? app.Navigation.Status
                : CanConfirm ? "Near " + Selected.ObjectName + "'s last saved position. Check the item, then choose Found it."
                : "Follow the blue path to the ring. The marker shows the last saved location, not a new detection.";
            if (Time.unscaledTime >= nextPoll) { nextPoll = Time.unscaledTime + 0.2f; PublishState(); }
        }

        private void LateUpdate()
        {
            if (!Initialized) return;
            var located = Selected.TryGetLocatedPosition(out var point);
            var visible = !armed && app.Room.Ready && located && OVRManager.hasVrFocus;
            marker.SetActive(visible); labelRoot.SetActive(visible);
            if (!visible) return;
            marker.transform.position = point;
            var forward = point - rig.centerEyeAnchor.position;
            labelRoot.transform.SetPositionAndRotation(point + Vector3.up * 0.16f, forward.sqrMagnitude > 0.001f ? Quaternion.LookRotation(forward) : rig.centerEyeAnchor.rotation);
            markerLabel.text = Selected.ObjectName + "\nLast saved location";
            markerMaterial.SetColor("_BaseColor", found ? Color.green : searching ? Color.cyan : Color.yellow);
        }

        private void RegisterController()
        {
            armed = false;
            if (!TryGetObservationContext(out var context)) { Status = "Room context changed. Choose Register again."; return; }
            var observation = new ObjectObservation
            {
                ObjectId = SelectedId, ObservationId = Guid.NewGuid().ToString("D"), RoomId = context.RoomId,
                WorldFrameId = context.WorldFrameId, ObservedAtUtc = DateTime.UtcNow,
                WorldPosition = rig.rightControllerAnchor.position, IdentityConfidence = 1, PositionConfidence = 1,
                Origin = ObservationOrigin.ManualController
            };
            if (!TrySubmitObservation(observation, out var reason)) { Status = reason; return; }
            operation = "save"; operationStarted = Time.unscaledTime;
            Status = "Saving " + Selected.ObjectName + "... Wait for confirmation before closing the app.";
        }
        private async void PlanFind()
        {
            pendingFind = false; planning = true;
            var generation = workflowGeneration; var id = SelectedId;
            Selected.TryGetLocatedPosition(out var point);
            Status = "Finding a reachable standing point near " + Selected.ObjectName + "...";
            try
            {
                var result = await app.Navigation.FindItem(id, point);
                if (this == null || generation != workflowGeneration) return;
                searching = result.success; Status = result.reason;
                diagnostics.Record(result.success ? "item_search_started" : "item_search_failed", id, result.reason);
            }
            catch (Exception error) { if (this != null && generation == workflowGeneration) { Status = "Could not plan the item route. Choose Find to retry."; Debug.LogWarning(error); } }
            finally { if (this != null && generation == workflowGeneration) planning = false; }
        }

        private void UpdateContext()
        {
            if (contextRevision == app.Room.Revision) return;
            contextRevision = app.Room.Revision; frameToken = Guid.NewGuid().ToString("D");
            if (HasActiveFlow) { Cancel(); Status = "Room context changed. Guidance cleared; choose Find or Register again."; }
        }
        public bool TryGetObservationContext(out ObservationContext context)
        {
            context = default; if (System.Threading.Thread.CurrentThread.ManagedThreadId != mainThread || app == null || !app.Room.Ready) return false;
            UpdateContext(); context = new ObservationContext { RoomId = app.Room.RoomUuid.ToString("D"), WorldFrameId = frameToken }; return true;
        }
        public bool TrySubmitObservation(ObjectObservation observation, out string reason)
        {
            reason = null;
            if (System.Threading.Thread.CurrentThread.ManagedThreadId != mainThread) { reason = "Submit observations on the Unity main thread."; return false; }
            if (!Initialized || IsBusy || armed || !TryGetObservationContext(out var context))
            { reason = "Item memory is busy, registration is armed, or the room is not located."; return false; }
            if (!Enum.IsDefined(typeof(ObservationOrigin), observation.Origin) || observation.Origin == ObservationOrigin.StoredAnchor)
            { reason = "A valid observation source is required."; return false; }
            if (!ObservationPolicy.Validate(observation.ObjectId, observation.ObservationId, observation.WorldPosition.x, observation.WorldPosition.y,
                observation.WorldPosition.z, observation.IdentityConfidence, observation.PositionConfidence, observation.ObservedAtUtc, DateTime.UtcNow,
                observation.RoomId, context.RoomId, observation.WorldFrameId, context.WorldFrameId, out reason)) return false;
            var observationKey = Guid.Parse(observation.ObservationId).ToString("D");
            if (accepted.Contains(observationKey)) { reason = "Observation already submitted."; return false; }
            if (!app.Room.ContainsWorldPoint(observation.WorldPosition)) { reason = "The item position is outside the current Meta room."; return false; }
            var memory = memories[Array.IndexOf(new[] { "keys", "wallet", "remote" }, observation.ObjectId)];
            if (memory.TryGetStoredObservation(out var old) && observation.Origin != ObservationOrigin.ManualController && observation.ObservedAtUtc < old.ObservedAtUtc)
            { reason = "Observation is older than the stored sighting."; return false; }
            if (memory == Selected && (planning || searching || pendingFind)) Cancel();
            if (memory == Selected) { saveCommitted = false; operation = "save"; operationStarted = Time.unscaledTime; }
            if (!memory.TryRegisterObservation(observation))
            { if (memory == Selected) operation = null; reason = "The anchor save could not be started."; return false; }
            accepted.Add(observationKey); acceptedOrder.Enqueue(observationKey);
            while (acceptedOrder.Count > 256) accepted.Remove(acceptedOrder.Dequeue());
            return true;
        }
        public bool TryGetSnapshot(string objectId, out TrackedObjectSnapshot snapshot)
        {
            snapshot = default; if (!Initialized) return false;
            foreach (var memory in memories) if (ObservationPolicy.IdFor(memory.ObjectName) == objectId)
            {
                memory.TryGetStoredObservation(out var observation);
                var located = memory.TryGetLocatedPosition(out var point); observation.WorldPosition = located ? point : default;
                snapshot = new TrackedObjectSnapshot { ObjectId = objectId, DisplayName = memory.ObjectName, State = memory.Availability,
                    AnchorId = memory.AnchorId, HasLocatedPosition = located, LastObservation = observation }; return true;
            }
            return false;
        }
        public bool TryGetTargetCoordinates(string instanceId, out Vector3 worldPosition)
        { worldPosition = default; if (!TryGetSnapshot(instanceId, out var snapshot) || !snapshot.HasLocatedPosition) return false; worldPosition = snapshot.LastObservation.WorldPosition; return true; }
        public bool TryGetItemRecord(string instanceId, out SpatialItemRecord record)
        {
            record = default; if (!TryGetSnapshot(instanceId, out var snapshot) || !snapshot.HasLocatedPosition) return false;
            record = new SpatialItemRecord { InstanceId = instanceId, Category = instanceId, WorldPosition = snapshot.LastObservation.WorldPosition,
                Confidence = Mathf.Min(snapshot.LastObservation.IdentityConfidence, snapshot.LastObservation.PositionConfidence), LastSeenTimestamp = snapshot.LastObservation.ObservedAtUtc }; return true;
        }
        private void OnCommitted(PersistentObjectMemory memory)
        {
            if (memory == Selected && operation == "save") saveCommitted = true;
            if (memory == Selected && (searching || planning || pendingFind))
            { Cancel(); Status = "This item's saved location changed. Choose Find to use the new location."; }
            diagnostics.Record("item_anchor_committed", ObservationPolicy.IdFor(memory.ObjectName), memory.RecordedOrigin.ToString());
        }
        private void PublishState()
        {
            foreach (var id in ObservationPolicy.ObjectIds)
            {
                if (!TryGetSnapshot(id, out var snapshot)) continue;
                previous.TryGetValue(id, out var old);
                if (old.HasLocatedPosition && !snapshot.HasLocatedPosition) OnItemLocationInvalidated?.Invoke(id);
                if (snapshot.HasLocatedPosition && (!old.HasLocatedPosition || snapshot.AnchorId != old.AnchorId
                    || Vector3.Distance(snapshot.LastObservation.WorldPosition, old.LastObservation.WorldPosition) > 0.02f))
                    if (TryGetItemRecord(id, out var record)) OnItemLocationUpdated?.Invoke(record);
                previous[id] = snapshot;
            }
            var phase = !app.Room.Ready ? ApplicationPhase.LoadingRoom : Selected.IsBusy
                ? Selected.Availability == ObjectLocationState.Saving ? ApplicationPhase.SavingObject
                    : Selected.Availability == ObjectLocationState.Removing ? ApplicationPhase.RemovingObject : ApplicationPhase.LocatingObject
                : operation == "save" ? ApplicationPhase.SavingObject
                : operation == "forget" ? ApplicationPhase.RemovingObject : operation == "locate" || planning || pendingFind ? ApplicationPhase.LocatingObject
                : armed ? ApplicationPhase.AwaitingObservation : found ? ApplicationPhase.Found : searching ? ApplicationPhase.Searching
                : Selected.HasLocatedPosition ? ApplicationPhase.Ready : Selected.HasSavedRecord ? ApplicationPhase.Unavailable : ApplicationPhase.SelectingObject;
            var next = new ApplicationStateSnapshot { Phase = phase, SelectedObjectId = SelectedId, Message = Status };
            if (CurrentState.Phase != next.Phase || CurrentState.SelectedObjectId != next.SelectedObjectId || CurrentState.Message != next.Message)
            { CurrentState = next; StateChanged?.Invoke(next); diagnostics.Record("item_state", SelectedId, phase + ": " + Status); }
        }
        private static float Horizontal(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
        private void OnDestroy()
        {
            workflowGeneration++;
            if (memories != null) foreach (var memory in memories) if (memory != null) memory.LocationCommitted -= OnCommitted;
            if (markerMaterial != null) Destroy(markerMaterial);
        }
    }
}
