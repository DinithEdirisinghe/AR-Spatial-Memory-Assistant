using System;
using System.Collections.Generic;

namespace SpatialSense.RoomScan
{
    // A session-local correction of explicitly classified furniture, never a
    // deletion from Meta's room model. Unknown geometry stays protected.
    public sealed class BaselineFurnitureLayer
    {
        private readonly ScanNavigationGrid map;
        private readonly List<int> candidates = new List<int>();
        private readonly bool[] eligible, blocked;
        private readonly byte[] freeMasks, counts;
        private readonly double[] firstEvidence, lastSeen, lastEvidence;
        public int CandidateCount => candidates.Count;
        public int ClearedCount { get; private set; }
        public int ClearedLastBatch { get; private set; }
        public int RestoredLastBatch { get; private set; }
        public bool AtCapacity { get; private set; }

        public BaselineFurnitureLayer(ScanNavigationGrid grid)
        {
            map = grid; eligible = new bool[map.CellCount]; blocked = new bool[map.CellCount];
            freeMasks = new byte[map.CellCount]; counts = new byte[map.CellCount * 8];
            firstEvidence = new double[map.CellCount]; lastSeen = new double[map.CellCount * 8];
            lastEvidence = new double[map.CellCount];
            for (var cell = 0; cell < map.CellCount; cell++)
            {
                firstEvidence[cell] = double.NaN;
                if (!map.CanRefreshFurniture(cell)) continue;
                if (candidates.Count >= 4096) { AtCapacity = true; continue; }
                eligible[cell] = true; candidates.Add(cell);
            }
        }

        private int Levels(int cell) => Math.Min(7, Math.Max(1, (int)Math.Ceiling((map.FurnitureHigh(cell) - map.FurnitureLow(cell)) / 0.3f) + 1));
        private float Height(int cell, int level)
        {
            var levels = Levels(cell);
            return levels == 1 ? map.FurnitureLow(cell) : map.FurnitureLow(cell) + (map.FurnitureHigh(cell) - map.FurnitureLow(cell)) * level / (levels - 1);
        }
        public int CandidateCell(int index) => candidates[index];
        public int ProbeCount(int index) => Levels(candidates[index]) + 1;
        public MeshPoint ProbePoint(int candidateIndex, int slot)
        {
            var cell = candidates[candidateIndex]; var p = map.Centre(cell);
            return new MeshPoint(p.X, slot == Levels(cell) ? map.FloorY : Height(cell, slot), p.Z);
        }

        public bool Observe(IReadOnlyList<DepthObservationRay> rays, double now)
        {
            ClearedLastBatch = RestoredLastBatch = 0;
            if (double.IsNaN(now) || double.IsInfinity(now)) return false;
            Array.Clear(blocked, 0, blocked.Length); Array.Clear(freeMasks, 0, freeMasks.Length);
            foreach (var ray in rays)
            {
                if (!Finite(ray.Origin) || !Finite(ray.Hit)) continue;
                var endCell = map.Index(ray.Hit.X, ray.Hit.Z);
                if (endCell >= 0 && eligible[endCell])
                {
                    if (ray.Hit.Y >= map.FloorY + 0.15f && ray.Hit.Y <= map.FloorY + 1.9f) blocked[endCell] = true;
                    if (ray.HasNormal && Finite(ray.Normal) && ray.Normal.Y >= 0.9f && Math.Abs(ray.Hit.Y - map.FloorY) <= 0.08f)
                        freeMasks[endCell] |= 128;
                }
                var steps = (int)Math.Ceiling(Math.Max(Math.Abs(ray.Hit.X - ray.Origin.X), Math.Abs(ray.Hit.Z - ray.Origin.Z)) / ScanNavigationGrid.CellSize * 2);
                if (steps > 512) continue;
                var previous = -1;
                for (var step = 0; step <= steps; step++)
                {
                    var t = steps == 0 ? 0 : step / (float)steps;
                    var cell = map.Index(ray.Origin.X + (ray.Hit.X - ray.Origin.X) * t, ray.Origin.Z + (ray.Hit.Z - ray.Origin.Z) * t);
                    if (cell < 0 || cell == previous || !eligible[cell]) continue;
                    previous = cell; var p = map.Centre(cell);
                    for (var level = 0; level < Levels(cell); level++)
                        if (PassesVolume(ray, p.X, Height(cell, level), p.Z)) freeMasks[cell] |= (byte)(1 << level);
                }
            }
            var changed = false;
            foreach (var cell in candidates)
            {
                if (blocked[cell])
                {
                    Array.Clear(counts, cell * 8, 8); firstEvidence[cell] = double.NaN;
                    if (map.FurnitureCleared(cell))
                    { map.SetFurnitureCleared(cell, false); ClearedCount--; RestoredLastBatch++; changed = true; }
                    continue;
                }
                var mask = freeMasks[cell];
                if (mask == 0) continue; // Absence of depth never opens a cell.
                if (now - lastEvidence[cell] > 3 || now < lastEvidence[cell])
                { Array.Clear(counts, cell * 8, 8); firstEvidence[cell] = double.NaN; }
                lastEvidence[cell] = now;
                if (double.IsNaN(firstEvidence[cell]) || now < firstEvidence[cell]) firstEvidence[cell] = now;
                for (var slot = 0; slot < 8; slot++)
                {
                    if ((mask & (1 << slot)) == 0) continue;
                    var index = cell * 8 + slot;
                    if (now - lastSeen[index] > 3 || now < lastSeen[index]) counts[index] = 0;
                    if (counts[index] < 3) counts[index]++;
                    lastSeen[index] = now;
                }
                var verified = now - firstEvidence[cell] >= 0.6 && counts[cell * 8 + 7] >= 3 && now - lastSeen[cell * 8 + 7] <= 3;
                for (var slot = 0; slot < Levels(cell) && verified; slot++)
                    verified = counts[cell * 8 + slot] >= 3 && now - lastSeen[cell * 8 + slot] <= 3;
                if (verified && !map.FurnitureCleared(cell))
                { map.SetFurnitureCleared(cell, true); ClearedCount++; ClearedLastBatch++; changed = true; }
            }
            return changed;
        }

        private static bool PassesVolume(DepthObservationRay ray, float x, float y, float z)
        {
            var enter = 0f; var exit = 1f; var half = ScanNavigationGrid.CellSize * 0.5f;
            if (!Slab(ray.Origin.X, ray.Hit.X - ray.Origin.X, x - half, x + half, ref enter, ref exit)
                || !Slab(ray.Origin.Y, ray.Hit.Y - ray.Origin.Y, y - 0.15f, y + 0.15f, ref enter, ref exit)
                || !Slab(ray.Origin.Z, ray.Hit.Z - ray.Origin.Z, z - half, z + half, ref enter, ref exit)) return false;
            var dx = ray.Hit.X - ray.Origin.X; var dy = ray.Hit.Y - ray.Origin.Y; var dz = ray.Hit.Z - ray.Origin.Z;
            var length = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
            return enter > 0 && length > 0.08f && exit < 1 - 0.08f / length;
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
