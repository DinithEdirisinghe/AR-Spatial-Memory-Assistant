using UnityEngine;

namespace Project.Core.Interfaces
{
    public interface INavMeshService
    {
        // Member 3 calls this with Member 2's target coordinates
        bool TryCalculatePath(Vector3 startWorldPos, Vector3 targetWorldPos, out Vector3[] waypoints);

        // Triggers localized incremental update around moved obstacles
        void UpdateNavMeshRegion(Bounds dirtyBounds);
    }
}