using UnityEngine;

namespace Project.Core.Interfaces
{
    public interface INavMeshService
    {
        // Low-level complete-path query between floor positions. Object search uses the application hub to choose a standing spot.
        bool TryCalculatePath(Vector3 startWorldPos, Vector3 targetWorldPos, out Vector3[] waypoints);

        // Requests a refresh. The current prototype rebuilds the mapped room; it does not observe moved physical obstacles.
        void UpdateNavMeshRegion(Bounds dirtyBounds);
    }
}
