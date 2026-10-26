using System;
using UnityEngine;

namespace Project.Core.Integration
{
    public sealed class ManualObservationSource : MonoBehaviour, IObjectObservationSource
    {
        [SerializeField] private SpatialApplicationHub hub;
        public event Action<ObjectObservation> ObservationReceived;
        public void Configure(SpatialApplicationHub application) => hub = application;

        public bool Report(string objectName, Vector3 worldPosition)
        {
            if (hub == null || !hub.TryGetObservationContext(out var context) || ObservationReceived == null) return false;
            ObservationReceived.Invoke(new ObjectObservation
            {
                ObservationId = Guid.NewGuid().ToString("D"), ObjectId = ObservationPolicy.IdFor(objectName),
                WorldPosition = worldPosition, ObservedAtUtc = DateTime.UtcNow,
                IdentityConfidence = 1f, PositionConfidence = 1f, Origin = ObservationOrigin.ManualController,
                RoomId = context.RoomId, WorldFrameId = context.WorldFrameId
            });
            return true;
        }
    }
}
