using System;

namespace SpatialSense.RoomScan
{
    [Serializable]
    public sealed class DepthMeshRecord
    {
        public const int MaxVertices = 16000;
        public const int MaxIndices = 84000;
        public int version = 1;
        public string source = "LiveEnvironmentRaycastPatches";
        public string anchorUuid;
        public string capturedAtUtc;
        public string processing;
        public int capturedPatches;
        public string lastCapturedAtUtc;
        public bool hasFloor;
        public float floorY;
        public float[] vertices;
        public int[] triangles;

        public bool IsValid()
        {
            if (version != 1 || source != "LiveEnvironmentRaycastPatches"
                || !Guid.TryParse(anchorUuid, out var id) || id == Guid.Empty
                || !DateTimeOffset.TryParse(capturedAtUtc, out _)
                || vertices == null || vertices.Length < 9 || vertices.Length % 3 != 0
                || vertices.Length > MaxVertices * 3 || triangles == null || triangles.Length < 3
                || triangles.Length % 3 != 0 || triangles.Length > MaxIndices) return false;
            if (hasFloor && (float.IsNaN(floorY) || float.IsInfinity(floorY) || Math.Abs(floorY) > 20f)) return false;
            foreach (var coordinate in vertices)
                if (float.IsNaN(coordinate) || float.IsInfinity(coordinate) || Math.Abs(coordinate) > 20f) return false;
            foreach (var index in triangles) if (index < 0 || index >= vertices.Length / 3) return false;
            for (var i = 0; i < triangles.Length; i += 3)
                if (triangles[i] == triangles[i + 1] || triangles[i] == triangles[i + 2]
                    || triangles[i + 1] == triangles[i + 2]) return false;
            return true;
        }
    }

    public static class DepthTrianglePolicy
    {
        // Do not bridge depth gaps, missing samples or separated surfaces.
        public static bool Accept(bool a, bool b, bool c, float abSquared, float acSquared,
            float bcSquared, float crossSquared, float maxEdge)
        {
            return a && b && c && maxEdge > 0f && !float.IsInfinity(maxEdge)
                && abSquared >= 0f && acSquared >= 0f && bcSquared >= 0f
                && abSquared <= maxEdge * maxEdge && acSquared <= maxEdge * maxEdge
                && bcSquared <= maxEdge * maxEdge && crossSquared > 0.00000001f
                && !float.IsInfinity(crossSquared);
        }
    }
}
