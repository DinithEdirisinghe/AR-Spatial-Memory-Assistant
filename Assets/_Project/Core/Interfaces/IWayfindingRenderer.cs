using UnityEngine;

namespace Project.Core.Interfaces
{
    public interface IWayfindingRenderer
    {
        // Takes Member 1's path and draws the occluded visual ribbon/trail
        void DisplayGuidancePath(Vector3[] waypoints);
        void ClearGuidancePath();
    }
}