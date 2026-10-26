using System;
using UnityEngine;

namespace Project.Core.Integration
{
    [Serializable]
    public struct ObjectObservation
    {
        public string ObservationId;
        public string ObjectId;
        public Vector3 WorldPosition;
        public float IdentityConfidence;
        public float PositionConfidence;
        public DateTime ObservedAtUtc;
        public string RoomId;
        public string WorldFrameId;
        public ObservationOrigin Origin;
    }

    public struct ObservationContext
    {
        public string RoomId;
        public string WorldFrameId;
    }

    public interface IObjectObservationSource
    {
        event Action<ObjectObservation> ObservationReceived;
    }

    public interface IObjectObservationSink
    {
        bool TrySubmitObservation(ObjectObservation observation, out string reason);
        bool TryGetObservationContext(out ObservationContext context);
    }

    public struct TrackedObjectSnapshot
    {
        public string ObjectId;
        public string DisplayName;
        public ObjectLocationState State;
        public ObjectObservation LastObservation;
        public string AnchorId;
        public bool HasLocatedPosition;
    }

    public struct ApplicationStateSnapshot
    {
        public ApplicationPhase Phase;
        public string SelectedObjectId;
        public string Message;
    }

    public interface ISpatialObservationRegistry : Project.Core.Interfaces.ISpatialRegistryService
    {
        bool TryGetSnapshot(string objectId, out TrackedObjectSnapshot snapshot);
        event Action<string> OnItemLocationInvalidated;
    }

    public interface IApplicationStateProvider
    {
        ApplicationStateSnapshot CurrentState { get; }
        event Action<ApplicationStateSnapshot> StateChanged;
    }
}
