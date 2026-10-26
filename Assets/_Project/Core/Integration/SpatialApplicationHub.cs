using System;
using System.Collections.Generic;
using Project.Core.Interfaces;
using SpatialSense.ManualDemo;
using SpatialSense.Navigation;
using SpatialSense.ObjectMenu;
using SpatialSense.Persistence;
using SpatialSense.RoomDemo;
using UnityEngine;

namespace Project.Core.Integration
{
    public sealed class SpatialApplicationHub : MonoBehaviour, IObjectObservationSink, ISpatialObservationRegistry, IApplicationStateProvider
    {
        [SerializeField] private PersistentObjectMemory[] memories;
        [SerializeField] private RoomUnderstandingDemo roomDemo;
        [SerializeField] private ManualObjectDemo flow;
        [SerializeField] private WalkingRouteDemo navigation;
        [SerializeField] private MultiObjectMenu menu;
        [SerializeField] private ManualObservationSource manualSource;
        [SerializeField] private SimulatedVisionObservationSource simulatedSource;
        [SerializeField] private ApplicationDiagnostics diagnostics;
        private readonly Dictionary<string, TrackedObjectSnapshot> previous = new Dictionary<string, TrackedObjectSnapshot>();
        private readonly HashSet<string> seenObservations = new HashSet<string>();
        private readonly Queue<string> observationOrder = new Queue<string>();
        private readonly HashSet<IObjectObservationSource> sources = new HashSet<IObjectObservationSource>();
        private string frameId;
        private int frameRevision = -1;
        private float nextPoll;
        private bool initialized;
        public event Action<SpatialItemRecord> OnItemLocationUpdated;
        public event Action<string> OnItemLocationInvalidated;
        public event Action<ApplicationStateSnapshot> StateChanged;
        public ApplicationStateSnapshot CurrentState { get; private set; }

        public void Configure(PersistentObjectMemory[] slots, RoomUnderstandingDemo room, ManualObjectDemo objectFlow,
            WalkingRouteDemo routes, MultiObjectMenu objectMenu, ManualObservationSource manual,
            SimulatedVisionObservationSource simulated, ApplicationDiagnostics logs)
        {
            memories = slots; roomDemo = room; flow = objectFlow; navigation = routes; menu = objectMenu;
            manualSource = manual; simulatedSource = simulated; diagnostics = logs;
        }

        private void Start()
        {
            if (memories == null || memories.Length != 3 || roomDemo == null || flow == null || navigation == null
                || Array.Exists(memories, memory => memory == null)
                || manualSource == null || simulatedSource == null || diagnostics == null)
            {
                Debug.LogError("[SpatialSense Hub] Run SpatialSense > Create Master Integration Scene.", this);
                enabled = false;
                return;
            }
            initialized = true;
            RegisterObservationSource(manualSource);
            RegisterObservationSource(simulatedSource);
            foreach (var memory in memories)
            {
                memory.LocationCommitted += OnLocationCommitted;
                memory.MemoryStatusChanged += OnMemoryStatus;
            }
            navigation.Diagnostic += OnNavigationDiagnostic;
            flow.FlowEvent += OnFlowEvent;
            diagnostics.Record("session_started", null, "Master integration; controller and simulated vision sources enabled.");
        }

        public void RegisterObservationSource(IObjectObservationSource source)
        {
            if (source != null && sources.Add(source)) source.ObservationReceived += ReceiveObservation;
        }

        public void UnregisterObservationSource(IObjectObservationSource source)
        {
            if (source != null && sources.Remove(source)) source.ObservationReceived -= ReceiveObservation;
        }

        public bool TryGetObservationContext(out ObservationContext context)
        {
            context = default;
            if (!initialized || roomDemo == null || !roomDemo.IsRoomReady || roomDemo.CurrentRoom == null) return false;
            if (frameRevision != roomDemo.RoomRevision || string.IsNullOrEmpty(frameId))
            {
                frameRevision = roomDemo.RoomRevision;
                frameId = Guid.NewGuid().ToString("D");
            }
            context = new ObservationContext { RoomId = roomDemo.CurrentRoom.Anchor.Uuid.ToString("D"), WorldFrameId = frameId };
            return true;
        }

        private void ReceiveObservation(ObjectObservation observation)
        {
            if (!TrySubmitObservation(observation, out var reason)) flow.SetInputFeedback("Observation not applied: " + reason);
        }

        public bool TrySubmitObservation(ObjectObservation observation, out string reason)
        {
            reason = null;
            if (!isActiveAndEnabled || !TryGetObservationContext(out var context)) reason = "Room is not ready.";
            else if (observation.Origin == ObservationOrigin.StoredAnchor || !Enum.IsDefined(typeof(ObservationOrigin), observation.Origin)) reason = "A fresh input source is required.";
            else if (!ObservationPolicy.Validate(observation.ObjectId, observation.ObservationId,
                observation.WorldPosition.x, observation.WorldPosition.y, observation.WorldPosition.z,
                observation.IdentityConfidence, observation.PositionConfidence, observation.ObservedAtUtc, DateTime.UtcNow,
                observation.RoomId, context.RoomId, observation.WorldFrameId, context.WorldFrameId, out reason)) { }
            else if (seenObservations.Contains(Guid.Parse(observation.ObservationId).ToString("D"))) reason = "Duplicate observation.";
            else
            {
                observation.ObservationId = Guid.Parse(observation.ObservationId).ToString("D");
                foreach (var memory in memories) if (memory.IsBusy) { reason = "An anchor operation is busy; retry after it finishes."; break; }
                if (reason == null && !roomDemo.CurrentRoom.IsPositionInRoom(observation.WorldPosition, false)) reason = "Position is outside the mapped room.";
                var target = FindMemory(observation.ObjectId);
                if (reason == null && observation.Origin != ObservationOrigin.ManualController && target != null
                    && target.TryGetStoredObservation(out var last) && observation.ObservedAtUtc < last.ObservedAtUtc)
                    reason = "Observation is older than the recorded sighting.";
                if (reason == null && (target == null || !target.TryRegisterObservation(observation))) reason = "The save operation could not start.";
                if (reason == null)
                {
                    seenObservations.Add(observation.ObservationId);
                    observationOrder.Enqueue(observation.ObservationId);
                    if (observationOrder.Count > 512) seenObservations.Remove(observationOrder.Dequeue());
                    simulatedSource.Cancel();
                    diagnostics.Record("observation_accepted", observation.ObjectId, observation.Origin + "; saving a new anchored location.");
                    return true;
                }
            }
            if (diagnostics != null) diagnostics.Record("observation_rejected", observation.ObjectId, reason);
            return false;
        }

        private PersistentObjectMemory FindMemory(string id)
        {
            var name = ObservationPolicy.NameFor(id);
            if (memories != null) foreach (var memory in memories) if (memory != null && memory.ObjectName == name) return memory;
            return null;
        }

        public bool TryGetSnapshot(string objectId, out TrackedObjectSnapshot snapshot)
        {
            snapshot = default;
            if (!initialized) return false;
            var memory = FindMemory(objectId);
            if (memory == null) return false;
            memory.TryGetStoredObservation(out var observation);
            var located = memory.TryGetLocatedPosition(out var position);
            observation.WorldPosition = position;
            if (located && TryGetObservationContext(out var context)) observation.WorldFrameId = context.WorldFrameId;
            snapshot = new TrackedObjectSnapshot
            {
                ObjectId = objectId, DisplayName = memory.ObjectName, State = memory.Availability,
                HasLocatedPosition = located, LastObservation = observation, AnchorId = memory.AnchorId
            };
            return true;
        }

        public bool TryGetTargetCoordinates(string instanceId, out Vector3 worldPosition)
        {
            worldPosition = default;
            if (!TryGetSnapshot(instanceId, out var snapshot) || !snapshot.HasLocatedPosition) return false;
            worldPosition = snapshot.LastObservation.WorldPosition;
            return true;
        }

        public bool TryGetItemRecord(string instanceId, out SpatialItemRecord record)
        {
            record = default;
            if (!TryGetSnapshot(instanceId, out var snapshot) || !snapshot.HasLocatedPosition) return false;
            record = new SpatialItemRecord
            {
                InstanceId = instanceId, Category = instanceId, WorldPosition = snapshot.LastObservation.WorldPosition,
                Confidence = Mathf.Min(snapshot.LastObservation.IdentityConfidence, snapshot.LastObservation.PositionConfidence),
                LastSeenTimestamp = snapshot.LastObservation.ObservedAtUtc
            };
            return true;
        }

        public bool RequestFind(string objectId, out string reason)
        {
            reason = null;
            var memory = FindMemory(objectId);
            if (!initialized || !isActiveAndEnabled || memory == null || Array.Exists(memories, slot => slot.IsBusy))
            { reason = "Selection is unavailable or busy."; return false; }
            if (menu != null)
            {
                if (!menu.TrySelectObject(memory.ObjectName)) { reason = "Selection is unavailable or busy."; return false; }
            }
            else if (flow.SelectedMemory != memory)
            {
                foreach (var slot in memories) slot.SetSelected(false);
                flow.ConfigurePersistence(memory);
                memory.SetSelected(true);
            }
            if (!memory.HasLocatedPosition) { memory.RetryLoad(); reason = "Locate the saved anchor before finding."; return false; }
            if (menu != null) menu.CloseForExternalAction();
            else flow.SetMenuOpen(false);
            flow.BeginFind();
            return true;
        }

        private void Update()
        {
            if (!initialized || Time.unscaledTime < nextPoll) return;
            nextPoll = Time.unscaledTime + 0.2f;
            foreach (var id in ObservationPolicy.ObjectIds)
            {
                if (!TryGetSnapshot(id, out var snapshot)) continue;
                previous.TryGetValue(id, out var old);
                if (old.HasLocatedPosition && !snapshot.HasLocatedPosition) OnItemLocationInvalidated?.Invoke(id);
                if (snapshot.HasLocatedPosition && (!old.HasLocatedPosition || old.AnchorId != snapshot.AnchorId
                    || Vector3.Distance(old.LastObservation.WorldPosition, snapshot.LastObservation.WorldPosition) > 0.02f))
                {
                    if (TryGetItemRecord(id, out var record)) OnItemLocationUpdated?.Invoke(record);
                }
                if (old.State != snapshot.State) diagnostics.Record("object_state", id, snapshot.State.ToString());
                previous[id] = snapshot;
            }
            var selected = flow.SelectedMemory;
            var phase = !roomDemo.IsRoomReady ? ApplicationPhase.LoadingRoom
                : selected != null && selected.IsBusy ? selected.Availability == ObjectLocationState.Saving ? ApplicationPhase.SavingObject
                    : selected.Availability == ObjectLocationState.Removing ? ApplicationPhase.RemovingObject : ApplicationPhase.LocatingObject
                : simulatedSource.IsArmed ? ApplicationPhase.AwaitingObservation : flow.IsMenuOpen ? ApplicationPhase.SelectingObject
                : flow.IsSearching ? navigation.HasRouteFailure ? ApplicationPhase.Unavailable : ApplicationPhase.Searching
                : flow.IsFound ? ApplicationPhase.Found : flow.WasCancelled ? ApplicationPhase.Cancelled
                : selected != null && selected.HasLocatedPosition ? ApplicationPhase.Ready : ApplicationPhase.Unavailable;
            var next = new ApplicationStateSnapshot
            {
                Phase = phase, SelectedObjectId = ObservationPolicy.IdFor(flow.CurrentObjectName),
                Message = !roomDemo.IsRoomReady ? roomDemo.StatusMessage : flow.IsSearching ? navigation.StatusText : selected != null ? selected.StatusMessage : "Starting"
            };
            if (next.Phase != CurrentState.Phase || next.SelectedObjectId != CurrentState.SelectedObjectId || next.Message != CurrentState.Message)
            {
                CurrentState = next;
                StateChanged?.Invoke(next);
                diagnostics.Record("app_state", next.SelectedObjectId, next.Phase + ": " + next.Message);
            }
        }

        private void OnLocationCommitted(PersistentObjectMemory memory)
        {
            if (flow.CurrentObjectName == memory.ObjectName) flow.ClearInputFeedback();
            diagnostics.Record("anchor_committed", ObservationPolicy.IdFor(memory.ObjectName), memory.RecordedOrigin + "; native anchor and local record committed.");
        }
        private void OnMemoryStatus(PersistentObjectMemory memory) => diagnostics.Record("memory", ObservationPolicy.IdFor(memory.ObjectName), memory.StatusMessage);
        private void OnNavigationDiagnostic(string message) => diagnostics.Record("navigation", ObservationPolicy.IdFor(flow.CurrentObjectName), message);
        private void OnFlowEvent(string eventName, string objectName) => diagnostics.Record(eventName, ObservationPolicy.IdFor(objectName), "User action");

        private void OnDestroy()
        {
            foreach (var source in sources) source.ObservationReceived -= ReceiveObservation;
            sources.Clear();
            if (memories != null) foreach (var memory in memories) if (memory != null)
            {
                memory.LocationCommitted -= OnLocationCommitted;
                memory.MemoryStatusChanged -= OnMemoryStatus;
            }
            if (navigation != null) navigation.Diagnostic -= OnNavigationDiagnostic;
            if (flow != null) flow.FlowEvent -= OnFlowEvent;
        }
    }
}
