using System;
using System.Collections.Generic;

namespace SpatialSense.RoomScan
{
    public readonly struct DepthObservationRay
    {
        public readonly MeshPoint Origin, Hit;
        public readonly MeshPoint Normal;
        public readonly bool HasNormal;
        public DepthObservationRay(MeshPoint origin, MeshPoint hit) { Origin = origin; Hit = hit; Normal = default; HasNormal = false; }
        public DepthObservationRay(MeshPoint origin, MeshPoint hit, MeshPoint normal)
        { Origin = origin; Hit = hit; Normal = normal; HasNormal = true; }
    }

    // Confirm new blocks over two batches. Missing observations do not remove
    // them; three batches with actual rays through the old volume can clear one.
    public sealed class LiveObstacleLayer
    {
        private readonly ScanNavigationGrid map;
        private readonly List<int> tracked = new List<int>();
        private readonly byte[] votes, clears;
        private readonly float[] heights;
        private readonly int[] pendingAge;
        private readonly double[] firstSeen;
        private readonly bool[] hits, free;
        public int ActiveCount { get; private set; }
        public int TrackedCount => tracked.Count;
        public int PendingCount => tracked.Count - ActiveCount;
        public int AddedLastBatch { get; private set; }
        public int ClearedLastBatch { get; private set; }
        public bool AtCapacity => tracked.Count >= 1024;
        public double LastConfirmationDelayMs { get; private set; } = -1;
        public MeshPoint ObservationPoint(int index)
        { var cell = tracked[index]; var centre = map.Centre(cell); return new MeshPoint(centre.X, heights[cell], centre.Z); }
        public LiveObstacleLayer(ScanNavigationGrid grid)
        {
            map = grid; votes = new byte[map.CellCount]; clears = new byte[map.CellCount];
            heights = new float[map.CellCount]; hits = new bool[map.CellCount]; free = new bool[map.CellCount];
            pendingAge = new int[map.CellCount]; firstSeen = new double[map.CellCount];
        }
        public bool Observe(IReadOnlyList<DepthObservationRay> rays, double observedAtSeconds = double.NaN)
        {
            AddedLastBatch = ClearedLastBatch = 0; LastConfirmationDelayMs = -1;
            Array.Clear(hits, 0, hits.Length); Array.Clear(free, 0, free.Length);
            foreach (var ray in rays)
            {
                if (!Finite(ray.Origin) || !Finite(ray.Hit)) continue;
                var h = ray.Hit.Y - map.FloorY;
                var cell = map.Index(ray.Hit.X, ray.Hit.Z);
                if (h >= 0.2f && h <= 1.9f && cell >= 0 && map.BaseCell(cell) == 2)
                {
                    if (votes[cell] == 0 && !AtCapacity)
                    { tracked.Add(cell); heights[cell] = ray.Hit.Y; votes[cell] = 1; pendingAge[cell] = 0; firstSeen[cell] = observedAtSeconds; }
                    hits[cell] = true;
                    // A head movement can shift a repeat hit into an adjacent
                    // 7.5 cm cell. Corroborate nearby evidence at the same height
                    // without filling unobserved cells or enlarging clearance.
                    foreach (var index in tracked)
                    {
                        var p = map.Centre(index); var dx = p.X - ray.Hit.X; var dz = p.Z - ray.Hit.Z;
                        if (dx * dx + dz * dz <= 0.12f * 0.12f && Math.Abs(heights[index] - ray.Hit.Y) <= 0.15f)
                            hits[index] = true;
                    }
                }
                foreach (var index in tracked)
                {
                    if (index == cell) continue;
                    var centre = map.Centre(index);
                    if (PassesOldVolume(ray, centre.X, heights[index], centre.Z)) free[index] = true;
                }
            }
            var changed = false;
            for (var i = tracked.Count - 1; i >= 0; i--)
            {
                var index = tracked[i];
                if (hits[index])
                {
                    clears[index] = 0;
                    if (votes[index] < 3)
                    {
                        votes[index]++;
                        if (votes[index] == 3)
                        {
                            ActiveCount++; AddedLastBatch++; changed = true;
                            if (!double.IsNaN(firstSeen[index]) && observedAtSeconds >= firstSeen[index])
                                LastConfirmationDelayMs = Math.Max(LastConfirmationDelayMs, (observedAtSeconds - firstSeen[index]) * 1000);
                        }
                    }
                }
                else if (free[index])
                {
                    if (++clears[index] >= 3)
                    {
                        if (votes[index] >= 3) { ActiveCount--; ClearedLastBatch++; changed = true; }
                        votes[index] = clears[index] = 0; tracked.RemoveAt(i);
                    }
                }
                else if (votes[index] < 3 && ++pendingAge[index] >= 20)
                { votes[index] = clears[index] = 0; tracked.RemoveAt(i); }
            }
            return changed;
        }
        public List<MeshPoint> ActivePoints()
        {
            var points = new List<MeshPoint>();
            var occupied = new HashSet<int>();
            foreach (var cell in tracked)
            {
                if (votes[cell] < 3) continue;
                occupied.Add(cell);
                var x = cell % map.Width; var z = cell / map.Width; var start = map.Centre(cell);
                // Join only close, independently confirmed surface samples.
                // This prevents threading a route between sparse hits on one
                // chair face; do not extend a single hit into a large obstacle.
                for (var dz = -3; dz <= 3; dz++) for (var dx = -3; dx <= 3; dx++)
                {
                    var nx = x + dx; var nz = z + dz;
                    if (nx < 0 || nz < 0 || nx >= map.Width || nz >= map.Height || dx * dx + dz * dz > 9) continue;
                    var neighbour = nz * map.Width + nx;
                    if (neighbour <= cell || votes[neighbour] < 3 || Math.Abs(heights[cell] - heights[neighbour]) > 0.15f) continue;
                    var end = map.Centre(neighbour); var steps = Math.Max(Math.Abs(dx), Math.Abs(dz)) * 2;
                    for (var step = 1; step < steps; step++)
                    {
                        var t = step / (float)steps;
                        var index = map.Index(start.X + (end.X - start.X) * t, start.Z + (end.Z - start.Z) * t);
                        if (index >= 0 && map.BaseCell(index) == 2) occupied.Add(index);
                    }
                }
            }
            foreach (var cell in occupied) points.Add(map.Centre(cell));
            return points;
        }
        private static bool PassesOldVolume(DepthObservationRay ray, float x, float y, float z)
        {
            var enter = 0f; var exit = 1f;
            var halfCell = ScanNavigationGrid.CellSize * 0.5f;
            if (!Slab(ray.Origin.X, ray.Hit.X - ray.Origin.X, x - halfCell, x + halfCell, ref enter, ref exit)
                || !Slab(ray.Origin.Y, ray.Hit.Y - ray.Origin.Y, y - 0.12f, y + 0.12f, ref enter, ref exit)
                || !Slab(ray.Origin.Z, ray.Hit.Z - ray.Origin.Z, z - halfCell, z + halfCell, ref enter, ref exit)) return false;
            return enter > 0f && exit < 0.95f;
        }
        private static bool Slab(float origin, float direction, float min, float max, ref float enter, ref float exit)
        {
            if (Math.Abs(direction) < 0.00001f) return origin >= min && origin <= max;
            var a = (min - origin) / direction; var b = (max - origin) / direction;
            if (a > b) (a, b) = (b, a);
            enter = Math.Max(enter, a); exit = Math.Min(exit, b); return enter <= exit;
        }
        private static bool Finite(MeshPoint p) => !float.IsNaN(p.X) && !float.IsNaN(p.Y) && !float.IsNaN(p.Z)
            && !float.IsInfinity(p.X) && !float.IsInfinity(p.Y) && !float.IsInfinity(p.Z);
    }
}
