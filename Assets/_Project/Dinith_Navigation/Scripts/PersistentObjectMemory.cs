using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using SpatialSense.ManualDemo;
using SpatialSense.RoomDemo;
using UnityEngine;
using Project.Core.Integration;

namespace SpatialSense.Persistence
{
    [DisallowMultipleComponent]
    public sealed class PersistentObjectMemory : MonoBehaviour
    {
        [SerializeField] private RoomUnderstandingDemo roomDemo;
        [SerializeField] private ManualObjectDemo objectDemo;
        private SpatialSense.RoomScan.MetaRoomSource nativeRoom;
        private bool RoomReady => nativeRoom != null ? nativeRoom.Ready : roomDemo != null && roomDemo.IsRoomReady;
        private Guid RoomUuid => nativeRoom != null ? nativeRoom.RoomUuid : roomDemo != null && roomDemo.CurrentRoom != null ? roomDemo.CurrentRoom.Anchor.Uuid : Guid.Empty;
        private int RoomRevision => nativeRoom != null ? nativeRoom.Revision : roomDemo != null ? roomDemo.RoomRevision : -1;
        public bool IsInitialized => started;
        [SerializeField] private string objectName = "My keys";
        [SerializeField] private bool selected = true;
        private SavedObjectRecord record;
        private OVRSpatialAnchor activeAnchor;
        private bool started;
        private bool autoLoad = true;
        private bool restoreAllowed = true;
        private bool wasUsable;
        private float operationStarted;
        private bool slowOperationReported;
        private string recordPath;
        private Guid pendingDeletion;
        private string busyLabel;
        private ObjectObservation? pendingObservation;
        private bool externalSave;
        public event Action<PersistentObjectMemory> LocationCommitted;
        public event Action<PersistentObjectMemory> MemoryStatusChanged;
        public string AnchorId => record != null ? record.anchorUuid : null;
        public ObservationOrigin RecordedOrigin => record != null && Enum.TryParse<ObservationOrigin>(record.observationOrigin, out var origin)
            && Enum.IsDefined(typeof(ObservationOrigin), origin) ? origin : ObservationOrigin.StoredAnchor;
        public ObjectLocationState Availability => IsBusy ? busyLabel == "Saving" ? ObjectLocationState.Saving
            : busyLabel == "Removing" ? ObjectLocationState.Removing : ObjectLocationState.Locating
            : pendingDeletion != Guid.Empty ? ObjectLocationState.CleanupPending : record == null ? ObjectLocationState.NotSaved
            : !RoomReady ? ObjectLocationState.WaitingForRoom : !RoomMatches() || !restoreAllowed ? ObjectLocationState.Unavailable
            : HasLocatedPosition ? ObjectLocationState.Ready : activeAnchor != null ? ObjectLocationState.Locating : ObjectLocationState.Saved;

        public bool TryGetStoredObservation(out ObjectObservation observation)
        {
            observation = default;
            if (record == null) return false;
            if (!DateTimeOffset.TryParse(record.observedAtUtc, out var observed)) DateTimeOffset.TryParse(record.savedAtUtc, out observed);
            var origin = RecordedOrigin;
            observation = new ObjectObservation
            {
                ObservationId = record.observationId, ObjectId = ObservationPolicy.IdFor(objectName),
                WorldPosition = activeAnchor != null ? activeAnchor.transform.position : default,
                RoomId = record.roomUuid, Origin = origin, ObservedAtUtc = observed.UtcDateTime,
                IdentityConfidence = FiniteConfidence(record.identityConfidence), PositionConfidence = FiniteConfidence(record.positionConfidence)
            };
            return true;
        }

        private static float FiniteConfidence(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 0f : Mathf.Clamp01(value);

        public bool TryRegisterObservation(ObjectObservation observation)
        {
            if (!started || IsBusy || !RoomReady || ObservationPolicy.NameFor(observation.ObjectId) != objectName) return false;
            pendingObservation = observation;
            externalSave = true;
            RegisterAndSave(observation.WorldPosition);
            externalSave = false;
            if (!IsBusy) pendingObservation = null;
            return IsBusy;
        }
        public string ObjectName => objectName;
        public bool TryGetLocatedPosition(out Vector3 position)
        {
            if (!HasLocatedPosition) { position = default; return false; }
            position = activeAnchor.transform.position;
            return true;
        }
        public bool HasSavedRecord => record != null;
        public bool CanForget => started && !IsBusy && (record != null || pendingDeletion != Guid.Empty);
        public string ForgetToken => record != null ? record.anchorUuid : pendingDeletion.ToString("D");
        public string ListStatus => IsBusy ? busyLabel : HasLocatedPosition ? "Ready"
            : pendingDeletion != Guid.Empty ? "Removed - cleanup pending"
            : record == null ? "Not saved" : !RoomReady ? "Waiting for room"
            : !RoomMatches() ? "Unavailable in this room" : !restoreAllowed ? "Unavailable - retry"
            : activeAnchor != null ? "Locating" : "Saved - select to locate";

        public void ConfigureSlot(string name, bool isSelected)
        {
            if (!SavedObjectCatalog.IsKnownName(name)) throw new ArgumentException("Unknown object slot.");
            objectName = name;
            selected = isSelected;
        }

        public void SetSelected(bool value)
        {
            selected = value;
            wasUsable = false;
            if (value && record != null && (!restoreAllowed || activeAnchor == null)) autoLoad = true;
        }
        public bool IsBusy { get; private set; }
        public string StatusMessage { get; private set; } = "Waiting for the room before loading saved locations.";
        public bool HasLocatedPosition => isActiveAndEnabled && started && !IsBusy && restoreAllowed && RoomMatches()
            && activeAnchor != null && activeAnchor.Created && activeAnchor.Localized && activeAnchor.IsTracked;

        public void Configure(RoomUnderstandingDemo rooms, ManualObjectDemo objects)
        {
            roomDemo = rooms;
            objectDemo = objects;
        }

        public void ConfigureForNative(SpatialSense.RoomScan.MetaRoomSource source, string name, bool isSelected)
        {
            nativeRoom = source;
            ConfigureSlot(name, isSelected);
        }

        private void Start()
        {
            if (nativeRoom == null && (roomDemo == null || objectDemo == null))
            {
                Debug.LogError("[SpatialSense Memory] Run SpatialSense > Create Saved Object Demo.", this);
                enabled = false;
                return;
            }
            recordPath = Path.Combine(Application.persistentDataPath, SavedObjectCatalog.FileNameFor(objectName));
            ReadRecord();
            started = true;
        }

        private void Update()
        {
            if (!started || !selected || (objectDemo != null && !objectDemo.IsInitialized)) return;
            if (IsBusy)
            {
                if (!slowOperationReported && Time.unscaledTime - operationStarted > 30f)
                {
                    slowOperationReported = true;
                    SetStatus("The anchor operation is taking longer than expected. Look around in good lighting; restart the app if it stays stuck.");
                }
                return;
            }
            if (HasLocatedPosition)
            {
                if (!wasUsable) SetStatus($"{objectName} ready. Press B to find it, or A to replace its saved location.");
                objectDemo?.ApplyAnchoredPosition(activeAnchor.transform.position);
                wasUsable = true;
                return;
            }
            if (wasUsable)
            {
                wasUsable = false;
                objectDemo?.ClearRegistration();
            }
            if (!RoomReady) return;
            if (record != null && !RoomMatches())
            {
                SetStatus("This saved location belongs to another room scan. Return to that room, or press A to register and save here.");
                return;
            }
            if (autoLoad && record != null)
            {
                autoLoad = false;
                RetryLoad();
            }
            else if (restoreAllowed && activeAnchor != null)
                SetStatus("Locating saved position... Look around the room. Find stays unavailable until anchor tracking returns. B retries.");
        }

        private bool RoomMatches()
        {
            return record != null && RoomReady && RoomUuid != Guid.Empty && record.roomUuid == RoomUuid.ToString("D");
        }

        private void ReadRecord()
        {
            try
            {
                if (!File.Exists(recordPath))
                {
                    record = null;
                    SetStatus($"No saved location for {objectName}. Hold the controller beside it and press A to register and save.");
                    return;
                }
                var loaded = JsonUtility.FromJson<SavedObjectRecord>(File.ReadAllText(recordPath));
                if (loaded == null || !loaded.IsValid() || loaded.objectName != objectName)
                    throw new InvalidDataException("Unsupported, invalid or mismatched object record.");
                if (loaded.forgotten)
                {
                    record = null;
                    Guid.TryParse(loaded.anchorUuid, out pendingDeletion);
                    SetStatus(pendingDeletion != Guid.Empty ? $"{objectName} removed from the saved list. Select Forget to retry anchor cleanup."
                        : $"{objectName} is not saved. Press A to register it.");
                    return;
                }
                record = loaded;
                pendingDeletion = Guid.Empty;
                SetStatus("Saved location found. Waiting for the room, then locating its anchor...");
            }
            catch (Exception error)
            {
                record = null;
                SetStatus("The saved record could not be read. Press A to register a new location. The existing file is kept until a new save succeeds.");
                Debug.LogWarning($"[SpatialSense Memory] Read failed: {error.Message}", this);
            }
        }

        public async void RetryLoad()
        {
            if (!started || !selected || IsBusy || !RoomReady) return;
            if (record == null) ReadRecord();
            if (record == null) return;
            if (!RoomMatches())
            {
                SetStatus("Saved location belongs to another room scan. Return there, or press A to register here.");
                return;
            }
            BeginOperation("Locating saved position... Look around the room.");
            restoreAllowed = true;
            OVRSpatialAnchor candidate = null;
            try
            {
                var uuid = Guid.Parse(record.anchorUuid);
                if (activeAnchor != null && activeAnchor.Uuid == uuid) candidate = activeAnchor;
                else
                {
                    var anchors = new List<OVRSpatialAnchor.UnboundAnchor>();
                    var result = await OVRSpatialAnchor.LoadUnboundAnchorsAsync(new[] { uuid }, anchors);
                    if (this == null) return;
                    if (!result.Success || anchors.Count != 1) throw new InvalidOperationException($"Saved anchor could not be loaded ({result.Status}).");
                    var unbound = anchors[0];
                    if (!unbound.Localized && !await unbound.LocalizeAsync(15)) throw new InvalidOperationException("Anchor localization timed out.");
                    if (this == null) return;
                    var pose = default(Pose);
                    var poseReady = false;
                    var poseDeadline = Time.unscaledTime + 15f;
                    while (this != null && Time.unscaledTime < poseDeadline)
                    {
                        if (unbound.TryGetPose(out pose)) { poseReady = true; break; }
                        await Task.Yield();
                    }
                    if (this == null) return;
                    if (!poseReady) throw new InvalidOperationException("Anchor pose is not tracked yet.");
                    // Bind on the same frame the component is added, before its Start creates a new anchor.
                    var anchorObject = new GameObject("Saved " + objectName + " anchor");
                    anchorObject.transform.SetPositionAndRotation(pose.position, pose.rotation);
                    anchorObject.transform.SetParent(transform, true);
                    candidate = anchorObject.AddComponent<OVRSpatialAnchor>();
                    unbound.BindTo(candidate);
                }
                if (!await WaitForTrackedPose(candidate, 15f)) throw new InvalidOperationException("Anchor is not tracked. Look around and press B to retry.");
                if (this == null) return;
                if (!RoomMatches()) throw new InvalidOperationException("Room changed while locating the saved anchor.");
                activeAnchor = candidate;
                candidate = null;
                SetStatus($"{objectName} restored. Press B to find it, or A to replace its location.");
            }
            catch (Exception error)
            {
                if (this != null)
                {
                    restoreAllowed = false;
                    SetStatus("Could not locate the saved position. Look around and press B to retry, or A to register a new location.");
                    Debug.LogWarning($"[SpatialSense Memory] Restore failed: {error.Message}", this);
                }
            }
            finally
            {
                if (candidate != null && candidate != activeAnchor) Destroy(candidate.gameObject);
                IsBusy = false;
            }
        }

        public async void RegisterAndSave(Vector3 position)
        {
            if (!started || (!selected && !externalSave) || IsBusy || !RoomReady || RoomUuid == Guid.Empty) return;
            var roomUuid = RoomUuid;
            var observationRevision = RoomRevision;
            if (roomUuid == Guid.Empty)
            {
                SetStatus("Room identity is unavailable. Reload the room with left Y before saving.");
                return;
            }
            autoLoad = false;
            BeginOperation($"Saving {objectName}... Look around the room and wait for confirmation before closing the app.", "Saving");
            var oldRecord = record;
            var oldAnchor = activeAnchor;
            OVRSpatialAnchor candidate = null;
            var persisted = false;
            var committed = false;
            try
            {
                var anchorObject = new GameObject("Saved " + objectName + " anchor");
                anchorObject.transform.SetPositionAndRotation(position, Quaternion.identity);
                anchorObject.transform.SetParent(transform, true);
                candidate = anchorObject.AddComponent<OVRSpatialAnchor>();
                if (!await WaitForTrackedPose(candidate, 25f)) throw new InvalidOperationException("New anchor could not be created and tracked.");
                if (this == null) return;
                if (pendingObservation.HasValue && RoomRevision != observationRevision)
                    throw new InvalidOperationException("Coordinate frame changed while creating the observation anchor.");
                var result = await candidate.SaveAnchorAsync();
                persisted = result.Success;
                if (this == null) return;
                if (!persisted) throw new InvalidOperationException($"Native anchor save failed ({result.Status}).");
                if (!RoomReady || RoomUuid != roomUuid
                    || (pendingObservation.HasValue && RoomRevision != observationRevision))
                    throw new InvalidOperationException("Room changed during save. Previous saved location retained.");
                var replacement = new SavedObjectRecord
                {
                    version = 1, objectName = objectName,
                    anchorUuid = candidate.Uuid.ToString("D"), roomUuid = roomUuid.ToString("D"),
                    savedAtUtc = DateTimeOffset.UtcNow.ToString("O")
                };
                var sighting = pendingObservation;
                replacement.observationId = sighting.HasValue ? sighting.Value.ObservationId : Guid.NewGuid().ToString("D");
                replacement.observationOrigin = sighting.HasValue ? sighting.Value.Origin.ToString() : ObservationOrigin.ManualController.ToString();
                replacement.observedAtUtc = sighting.HasValue ? sighting.Value.ObservedAtUtc.ToString("O") : replacement.savedAtUtc;
                replacement.identityConfidence = sighting.HasValue ? sighting.Value.IdentityConfidence : 1f;
                replacement.positionConfidence = sighting.HasValue ? sighting.Value.PositionConfidence : 1f;
                if (!replacement.IsValid()) throw new InvalidDataException("New anchor record was invalid.");
                // Only replace metadata after the SDK confirms native persistence.
                AtomicAnchorRecordStore.Write(recordPath, JsonUtility.ToJson(replacement, true));
                record = replacement;
                activeAnchor = candidate;
                committed = true;
                restoreAllowed = true;
                if (LocationCommitted != null) foreach (Action<PersistentObjectMemory> listener in LocationCommitted.GetInvocationList())
                    try { listener(this); } catch (Exception error) { Debug.LogException(error, this); }
                SetStatus($"{objectName} saved on this headset. Press B to find it after restarting too.");
                if (oldRecord != null && oldRecord.anchorUuid != record.anchorUuid)
                    CleanupSupersededAnchor(Guid.Parse(oldRecord.anchorUuid), oldAnchor);
                if (pendingDeletion != Guid.Empty) CleanupSupersededAnchor(pendingDeletion, null);
                pendingDeletion = Guid.Empty;
                candidate = null;
            }
            catch (Exception error)
            {
                if (this != null)
                {
                    restoreAllowed = false;
                    SetStatus(oldRecord != null
                        ? "Save failed. The previous saved location is kept. B restores it; A tries a new registration."
                        : "Save failed. Nothing is registered for finding yet. Look around and press A to try again.");
                    Debug.LogWarning($"[SpatialSense Memory] Save failed: {error.Message}", this);
                }
            }
            finally
            {
                if (candidate != null && !committed)
                {
                    if (persisted) CleanupSupersededAnchor(candidate.Uuid, candidate);
                    else Destroy(candidate.gameObject);
                }
                IsBusy = false;
                pendingObservation = null;
            }
        }

        public async void ForgetSaved()
        {
            if (!selected || !CanForget) return;
            var uuid = record != null ? Guid.Parse(record.anchorUuid) : pendingDeletion;
            BeginOperation($"Removing {objectName} from the saved list...", "Removing");
            try
            {
                // Commit a tombstone before native erasure, so the object cannot reappear after an interrupted deletion.
                var removed = new SavedObjectRecord
                {
                    version = 1, objectName = objectName, forgotten = true,
                    anchorUuid = uuid.ToString("D"), savedAtUtc = DateTimeOffset.UtcNow.ToString("O")
                };
                AtomicAnchorRecordStore.Write(recordPath, JsonUtility.ToJson(removed, true));
                record = null;
                pendingDeletion = uuid;
                restoreAllowed = false;
                autoLoad = false;
                if (activeAnchor != null) Destroy(activeAnchor.gameObject);
                activeAnchor = null;
                var result = await OVRSpatialAnchor.EraseAnchorsAsync(null, new[] { uuid });
                if (this == null) return;
                if (!result.Success)
                {
                    SetStatus($"{objectName} is removed from the list, but anchor cleanup failed. Select Forget again to retry.");
                    return;
                }
                removed.anchorUuid = null;
                AtomicAnchorRecordStore.Write(recordPath, JsonUtility.ToJson(removed, true));
                pendingDeletion = Guid.Empty;
                SetStatus($"{objectName} forgotten. Its saved location will not be restored. A registers it again.");
            }
            catch (Exception error)
            {
                restoreAllowed = false;
                SetStatus(record != null ? "Could not remove the saved record. It is preserved; try Forget again."
                    : "Removed from the list; cleanup did not finish. Select Forget again to retry.");
                Debug.LogWarning($"[SpatialSense Memory] Forget {objectName}: {error.Message}", this);
            }
            finally { IsBusy = false; }
        }

        private void BeginOperation(string message, string label = "Locating")
        {
            IsBusy = true;
            operationStarted = Time.unscaledTime;
            slowOperationReported = false;
            wasUsable = false;
            busyLabel = label;
            if (selected) objectDemo?.ClearRegistration();
            SetStatus(message);
        }

        private async Task<bool> WaitForTrackedPose(OVRSpatialAnchor anchor, float timeout)
        {
            var deadline = Time.unscaledTime + timeout;
            while (this != null && anchor != null && Time.unscaledTime < deadline)
            {
                if (anchor.Created && anchor.Localized && anchor.IsTracked) return true;
                await Task.Yield();
            }
            return false;
        }

        private async void CleanupSupersededAnchor(Guid uuid, OVRSpatialAnchor instance)
        {
            try
            {
                var result = await OVRSpatialAnchor.EraseAnchorsAsync(null, new[] { uuid });
                if (!result.Success) Debug.LogWarning($"[SpatialSense Memory] Old anchor cleanup failed: {result.Status}. Current saved record is unaffected.");
            }
            catch (Exception error) { Debug.LogWarning($"[SpatialSense Memory] Old anchor cleanup: {error.Message}"); }
            finally { if (instance != null && instance != activeAnchor) Destroy(instance.gameObject); }
        }

        private void SetStatus(string message)
        {
            if (StatusMessage == message) return;
            StatusMessage = message;
            if (MemoryStatusChanged != null) foreach (Action<PersistentObjectMemory> listener in MemoryStatusChanged.GetInvocationList())
                try { listener(this); } catch (Exception error) { Debug.LogException(error, this); }
            Debug.Log($"[SpatialSense Memory] {message}", this);
        }
    }
}
