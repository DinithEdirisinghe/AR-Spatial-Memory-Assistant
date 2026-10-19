using System;
using UnityEngine;

namespace Project.Core.Interfaces
{
    [System.Serializable]
    public struct SpatialItemRecord
    {
        public string InstanceId;       // e.g., "indoor_shoes_01"
        public string Category;         // e.g., "shoe"
        public Vector3 WorldPosition;
        public float Confidence;
        public DateTime LastSeenTimestamp;
    }

    public interface ISpatialRegistryService
    {
        bool TryGetTargetCoordinates(string instanceId, out Vector3 worldPosition);
        bool TryGetItemRecord(string instanceId, out SpatialItemRecord record);
        event Action<SpatialItemRecord> OnItemLocationUpdated;
    }
}