using System;
using System.Collections.Generic;

namespace SpatialSense.RoomScan
{
    public readonly struct MeshPoint
    {
        public readonly float X, Y, Z;
        public MeshPoint(float x, float y, float z) { X = x; Y = y; Z = z; }
    }

    // A bounded mesh cleanup step, not volumetric reconstruction. Different
    // triangulations can still overlap; no unseen space or removed surface is inferred.
    public sealed class SpatialMeshAccumulator
    {
        private const float WeldDistance = 0.02f;
        private const float NormalAgreement = 0.8660254f;
        private readonly List<MeshPoint> points = new List<MeshPoint>();
        private readonly List<MeshPoint> normals = new List<MeshPoint>();
        private readonly List<int> indices = new List<int>();
        private readonly Dictionary<(int, int, int), List<int>> cells = new Dictionary<(int, int, int), List<int>>();
        private readonly HashSet<(int, int, int)> faces = new HashSet<(int, int, int)>();
        public IReadOnlyList<MeshPoint> Points => points;
        public IReadOnlyList<int> Indices => indices;
        public int ReusedVertices { get; private set; }
        public int DuplicateFaces { get; private set; }
        public bool AtCapacity { get; private set; }

        public bool AddTriangle(MeshPoint a, MeshPoint b, MeshPoint c)
        {
            if (!Finite(a) || !Finite(b) || !Finite(c)) return false;
            var normal = Cross(Subtract(b, a), Subtract(c, a));
            var lengthSquared = Dot(normal, normal);
            if (!(lengthSquared > 0.00000001f) || float.IsInfinity(lengthSquared)) return false;
            normal = Scale(normal, 1f / (float)Math.Sqrt(lengthSquared));
            var ia = Find(a, normal); var ib = Find(b, normal); var ic = Find(c, normal);
            // Use the positions that would actually be exported when checking weld collapse.
            var pa = ia >= 0 ? points[ia] : a;
            var pb = ib >= 0 ? points[ib] : b;
            var pc = ic >= 0 ? points[ic] : c;
            var weldedNormal = Cross(Subtract(pb, pa), Subtract(pc, pa));
            if (Dot(weldedNormal, weldedNormal) <= 0.00000001f || Dot(weldedNormal, normal) <= 0f
                || (ia >= 0 && ia == ib) || (ia >= 0 && ia == ic) || (ib >= 0 && ib == ic)) return false;
            if (ia >= 0 && ib >= 0 && ic >= 0 && faces.Contains(Key(ia, ib, ic)))
            { DuplicateFaces++; return false; }
            var needed = (ia < 0 ? 1 : 0) + (ib < 0 ? 1 : 0) + (ic < 0 ? 1 : 0);
            if (points.Count + needed > DepthMeshRecord.MaxVertices || indices.Count + 3 > DepthMeshRecord.MaxIndices)
            { AtCapacity = true; return false; }
            // Do not average positions: repeated jitter must not drag an established surface.
            if (ia < 0) ia = Add(a, normal); else ReusedVertices++;
            if (ib < 0) ib = Add(b, normal); else ReusedVertices++;
            if (ic < 0) ic = Add(c, normal); else ReusedVertices++;
            faces.Add(Key(ia, ib, ic));
            indices.Add(ia); indices.Add(ib); indices.Add(ic);
            return true;
        }

        private int Find(MeshPoint point, MeshPoint normal)
        {
            var cell = Cell(point);
            var nearest = -1;
            var best = WeldDistance * WeldDistance;
            // Search neighbouring cells as well, so hash boundaries are not seams.
            for (var x = -1; x <= 1; x++) for (var y = -1; y <= 1; y++) for (var z = -1; z <= 1; z++)
            {
                if (!cells.TryGetValue((cell.Item1 + x, cell.Item2 + y, cell.Item3 + z), out var bucket)) continue;
                foreach (var index in bucket)
                {
                    if (Dot(normals[index], normal) < NormalAgreement) continue;
                    var delta = Subtract(point, points[index]);
                    var distance = Dot(delta, delta);
                    if (distance <= best && (distance < best || nearest < 0 || index < nearest))
                    { best = distance; nearest = index; }
                }
            }
            return nearest;
        }

        private int Add(MeshPoint point, MeshPoint normal)
        {
            var index = points.Count;
            points.Add(point); normals.Add(normal);
            var key = Cell(point);
            if (!cells.TryGetValue(key, out var bucket)) cells.Add(key, bucket = new List<int>());
            bucket.Add(index);
            return index;
        }

        private static (int, int, int) Cell(MeshPoint p) => ((int)Math.Floor(p.X / WeldDistance),
            (int)Math.Floor(p.Y / WeldDistance), (int)Math.Floor(p.Z / WeldDistance));
        private static (int, int, int) Key(int a, int b, int c)
        { if (a > b) (a, b) = (b, a); if (b > c) (b, c) = (c, b); if (a > b) (a, b) = (b, a); return (a, b, c); }
        private static MeshPoint Subtract(MeshPoint a, MeshPoint b) => new MeshPoint(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        private static MeshPoint Scale(MeshPoint a, float s) => new MeshPoint(a.X * s, a.Y * s, a.Z * s);
        private static float Dot(MeshPoint a, MeshPoint b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        private static MeshPoint Cross(MeshPoint a, MeshPoint b) => new MeshPoint(a.Y * b.Z - a.Z * b.Y,
            a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
        private static bool Finite(MeshPoint p) => !float.IsNaN(p.X) && !float.IsNaN(p.Y) && !float.IsNaN(p.Z)
            && !float.IsInfinity(p.X) && !float.IsInfinity(p.Y) && !float.IsInfinity(p.Z)
            && Math.Abs(p.X) <= 20f && Math.Abs(p.Y) <= 20f && Math.Abs(p.Z) <= 20f;
    }
}
