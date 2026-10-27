using System;
using System.Collections.Generic;
using System.Threading;

namespace SpatialSense.RoomScan
{
    // Floor evidence and obstacle evidence are separate. Missing floor stays unknown.
    public sealed class ScanNavigationGrid
    {
        public const float CellSize = 0.075f;
        public readonly int Width, Height;
        public readonly float OriginX, OriginZ, FloorY;
        public readonly byte[] Cells; // 0 unknown, 1 obstacle, 2 route area, 3 coverage edge, 4 extra obstacle margin
        public int ObservedCount { get; private set; }
        public int WalkableCount { get; private set; }
        public int ObstacleCount { get; private set; }
        public int ObstacleEvidenceCount { get; private set; }
        public int MarginCount { get; private set; }
        public int CoverageEdgeCount { get; private set; }
        public int CellCount => Width * Height;
        private byte[] baseCells;
        public byte BaseCell(int index) => baseCells[index];
        public ScanNavigationGrid RoutingSnapshot()
        {
            var copy = new ScanNavigationGrid(Width, Height, OriginX, OriginZ, FloorY);
            Array.Copy(Cells, copy.Cells, Cells.Length);
            copy.baseCells = (byte[])baseCells.Clone();
            return copy;
        }
        private ScanNavigationGrid(int width, int height, float x, float z, float floorY)
        { Width = width; Height = height; OriginX = x; OriginZ = z; FloorY = floorY; Cells = new byte[width * height]; }

        public static ScanNavigationGrid Build(IReadOnlyList<MeshPoint> vertices, IReadOnlyList<int> indices,
            float floorY, float clearance, CancellationToken cancellation = default, bool strictCoverage = false, int authoritativeFloorIndices = -1)
        {
            if (vertices == null || indices == null || indices.Count < 3 || indices.Count % 3 != 0
                || !Finite(floorY) || !Finite(clearance) || clearance < 0f || clearance > 0.6f)
                throw new ArgumentException("Invalid scan or clearance.");
            if (authoritativeFloorIndices < -1 || authoritativeFloorIndices > indices.Count
                || (authoritativeFloorIndices >= 0 && authoritativeFloorIndices % 3 != 0)) throw new ArgumentException("Invalid semantic floor range.");
            var minX = float.PositiveInfinity; var maxX = float.NegativeInfinity;
            var minZ = float.PositiveInfinity; var maxZ = float.NegativeInfinity;
            for (var i = 0; i < indices.Count; i += 3)
            {
                cancellation.ThrowIfCancellationRequested();
                var a = vertices[indices[i]]; var b = vertices[indices[i + 1]]; var c = vertices[indices[i + 2]];
                if ((authoritativeFloorIndices >= 0 && i >= authoritativeFloorIndices) || !FloorTriangle(a, b, c, floorY)) continue;
                minX = Math.Min(minX, Math.Min(a.X, Math.Min(b.X, c.X)));
                maxX = Math.Max(maxX, Math.Max(a.X, Math.Max(b.X, c.X)));
                minZ = Math.Min(minZ, Math.Min(a.Z, Math.Min(b.Z, c.Z)));
                maxZ = Math.Max(maxZ, Math.Max(a.Z, Math.Max(b.Z, c.Z)));
            }
            if (float.IsInfinity(minX)) throw new InvalidOperationException("No floor surface at the selected height. Scan more floor, then rebuild the map.");
            minX = (float)Math.Floor(minX / CellSize) * CellSize - CellSize;
            minZ = (float)Math.Floor(minZ / CellSize) * CellSize - CellSize;
            var width = (int)Math.Ceiling((maxX - minX) / CellSize) + 1;
            var height = (int)Math.Ceiling((maxZ - minZ) / CellSize) + 1;
            if (width > 256 || height > 256) throw new InvalidOperationException("This scan is too wide for the room route test. Use a smaller room area.");
            var map = new ScanNavigationGrid(width, height, minX, minZ, floorY);
            var evidence = new byte[map.CellCount];
            var obstacles = new bool[map.CellCount];
            for (var i = 0; i < indices.Count; i += 3)
            {
                cancellation.ThrowIfCancellationRequested();
                var a = vertices[indices[i]]; var b = vertices[indices[i + 1]]; var c = vertices[indices[i + 2]];
                var floor = (authoritativeFloorIndices < 0 || i < authoritativeFloorIndices) && FloorTriangle(a, b, c, floorY);
                var low = Math.Min(a.Y, Math.Min(b.Y, c.Y)); var high = Math.Max(a.Y, Math.Max(b.Y, c.Y));
                if (!floor && (high <= floorY + 0.12f || low >= floorY + 1.9f)) continue;
                // Include both neighbours of a grid boundary; a zero-width wall
                // must not disappear because of floating point division/rounding.
                var x0 = map.X(Math.Min(a.X, Math.Min(b.X, c.X)) - 0.0001f);
                var x1 = map.X(Math.Max(a.X, Math.Max(b.X, c.X)) + 0.0001f);
                var z0 = map.Z(Math.Min(a.Z, Math.Min(b.Z, c.Z)) - 0.0001f);
                var z1 = map.Z(Math.Max(a.Z, Math.Max(b.Z, c.Z)) + 0.0001f);
                for (var z = Math.Max(0, z0); z <= Math.Min(height - 1, z1); z++)
                for (var x = Math.Max(0, x0); x <= Math.Min(width - 1, x1); x++)
                {
                    var index = z * width + x;
                    var left = minX + x * CellSize; var bottom = minZ + z * CellSize;
                    if (floor)
                    {
                        // Four inset corners and the centre, potentially covered by different triangles.
                        for (var sample = 0; sample < 5; sample++)
                        {
                            var px = left + CellSize * (sample == 4 ? 0.5f : (sample & 1) == 0 ? 0.02f : 0.98f);
                            var pz = bottom + CellSize * (sample == 4 ? 0.5f : (sample & 2) == 0 ? 0.02f : 0.98f);
                            if (Inside(px, pz, a, b, c)) evidence[index] |= (byte)(1 << sample);
                        }
                    }
                    else if (Overlaps(a, b, c, left, bottom, CellSize)) obstacles[index] = true;
                }
            }
            var radius = (int)Math.Ceiling(clearance / CellSize) + 1;
            foreach (var obstacle in obstacles) if (obstacle) map.ObstacleEvidenceCount++;
            for (var z = 0; z < height; z++) for (var x = 0; x < width; x++)
            {
                cancellation.ThrowIfCancellationRequested();
                var index = z * width + x;
                if (!Supported(evidence[index], strictCoverage))
                { if (obstacles[index]) { map.Cells[index] = 1; map.ObstacleCount++; } continue; }
                map.ObservedCount++;
                var blocked = obstacles[index];
                var incomplete = false;
                for (var dz = -radius; dz <= radius; dz++) for (var dx = -radius; dx <= radius; dx++)
                {
                    // Clearance is a radius about the actual route centre, not
                    // about the complete square plus a second half-cell margin.
                    var gapX = Math.Max(0f, Math.Abs(dx) * CellSize - CellSize * 0.5f);
                    var gapZ = Math.Max(0f, Math.Abs(dz) * CellSize - CellSize * 0.5f);
                    if (gapX * gapX + gapZ * gapZ > clearance * clearance) continue;
                    var nx = x + dx; var nz = z + dz;
                    if (nx < 0 || nz < 0 || nx >= width || nz >= height
                        || !Supported(evidence[nz * width + nx], strictCoverage)) incomplete = true;
                    if (nx >= 0 && nz >= 0 && nx < width && nz < height && obstacles[nz * width + nx]) blocked = true;
                }
                map.Cells[index] = (byte)(obstacles[index] ? 1 : blocked ? 4 : incomplete ? 3 : 2);
                if (blocked) { map.ObstacleCount++; if (!obstacles[index]) map.MarginCount++; }
                else if (incomplete) map.CoverageEdgeCount++;
                else map.WalkableCount++;
            }
            map.baseCells = (byte[])map.Cells.Clone();
            return map;
        }

        public void ApplyLiveObstacles(IReadOnlyList<MeshPoint> points, float clearance)
        {
            Array.Copy(baseCells, Cells, Cells.Length);
            var radius = (int)Math.Ceiling(clearance / CellSize) + 1;
            foreach (var point in points)
            {
                var cell = Index(point.X, point.Z); if (cell < 0) continue;
                var x = cell % Width; var z = cell / Width;
                for (var dz = -radius; dz <= radius; dz++) for (var dx = -radius; dx <= radius; dx++)
                {
                    var gx = Math.Max(0, Math.Abs(dx) * CellSize - CellSize / 2);
                    var gz = Math.Max(0, Math.Abs(dz) * CellSize - CellSize / 2);
                    if (gx * gx + gz * gz > clearance * clearance) continue;
                    var nx = x + dx; var nz = z + dz;
                    if (nx >= 0 && nz >= 0 && nx < Width && nz < Height && baseCells[nz * Width + nx] == 2)
                    {
                        var index = nz * Width + nx;
                        if (dx == 0 && dz == 0) Cells[index] = 1;
                        else if (Cells[index] != 1) Cells[index] = 4;
                    }
                }
            }
            WalkableCount = ObstacleCount = CoverageEdgeCount = MarginCount = 0;
            foreach (var cell in Cells) { if (cell == 2) WalkableCount++; else if (cell == 1 || cell == 4) { ObstacleCount++; if (cell == 4) MarginCount++; } else if (cell == 3) CoverageEdgeCount++; }
        }

        public int Index(float x, float z)
        { var ix = X(x); var iz = Z(z); return ix >= 0 && iz >= 0 && ix < Width && iz < Height ? iz * Width + ix : -1; }
        public MeshPoint Centre(int index) => new MeshPoint(OriginX + (index % Width + 0.5f) * CellSize,
            FloorY + 0.03f, OriginZ + (index / Width + 0.5f) * CellSize);
        private int X(float x) => (int)Math.Floor((x - OriginX) / CellSize);
        private int Z(float z) => (int)Math.Floor((z - OriginZ) / CellSize);

        public bool TryRouteToItem(float startX, float startZ, float itemX, float itemZ, out List<MeshPoint> path,
            out MeshPoint standingPoint, out string reason, CancellationToken cancellation = default)
        {
            path = new List<MeshPoint>(); standingPoint = default;
            var start = Index(startX, startZ);
            if (start < 0 || Cells[start] != 2)
            { reason = "Your current floor position is outside the route area. Move to a green area and try Find again."; return false; }
            if (!Finite(itemX) || !Finite(itemZ)) { reason = "The item's saved position is unavailable."; return false; }
            var costs = new int[CellCount]; var parents = new int[CellCount]; var closed = new bool[CellCount];
            for (var i = 0; i < CellCount; i++) { costs[i] = int.MaxValue; parents[i] = -1; }
            var queue = new SortedSet<(int cost, int index)>(); costs[start] = 0; queue.Add((0, start));
            var best = -1; var nearest = float.PositiveInfinity;
            var candidates = new List<(int cell, float distance)>();
            while (queue.Count > 0)
            {
                cancellation.ThrowIfCancellationRequested();
                var first = queue.Min; queue.Remove(first); var cell = first.index;
                if (closed[cell]) continue;
                closed[cell] = true;
                var centre = Centre(cell); var dxItem = centre.X - itemX; var dzItem = centre.Z - itemZ;
                var distance = (float)Math.Sqrt(dxItem * dxItem + dzItem * dzItem);
                if (distance >= 0.25f && distance <= 1.1f)
                {
                    nearest = Math.Min(nearest, distance);
                    candidates.Add((cell, distance));
                }
                var x = cell % Width; var z = cell / Width;
                for (var dz = -1; dz <= 1; dz++) for (var dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dz == 0) continue;
                    var nx = x + dx; var nz = z + dz;
                    if (nx < 0 || nz < 0 || nx >= Width || nz >= Height) continue;
                    var next = nz * Width + nx;
                    if (closed[next] || Cells[next] != 2) continue;
                    if (dx != 0 && dz != 0 && (Cells[z * Width + nx] != 2 || Cells[nz * Width + x] != 2)) continue;
                    var cost = first.cost + (dx == 0 || dz == 0 ? 10 : 14);
                    if (cost >= costs[next]) continue;
                    costs[next] = cost; parents[next] = cell; queue.Add((cost, next));
                }
            }
            // Reach the item neighbourhood first. Only use walking distance to
            // choose between equally close cells (within half a grid cell).
            // The old travel + 0.35 * distance score stopped about a metre early.
            var bestCost = int.MaxValue;
            foreach (var candidate in candidates)
            {
                cancellation.ThrowIfCancellationRequested();
                if (candidate.distance > nearest + CellSize * 0.5f) continue;
                if (costs[candidate.cell] < bestCost)
                { best = candidate.cell; bestCost = costs[best]; }
            }
            if (best < 0)
            { reason = "No reachable standing position near this item. Check its saved location, room map and margin."; return false; }
            for (var cell = best; cell >= 0; cell = parents[cell]) path.Add(Centre(cell));
            path.Reverse(); standingPoint = Centre(best);
            reason = "Walk to the ring, then look for the item's last saved location."; return true;
        }

        public bool TryRoute(float startX, float startZ, float endX, float endZ, out List<MeshPoint> path, out string reason)
        {
            path = new List<MeshPoint>();
            var start = Index(startX, startZ); var goal = Index(endX, endZ);
            if (start < 0 || Cells[start] == 0) { reason = "Floor around your current position is not captured. Scan it, pause, then rebuild the map."; return false; }
            if (Cells[start] == 3) { reason = "Your floor point is captured, but nearby floor coverage is incomplete (purple). Scan those gaps or compare Normal/Strict coverage."; return false; }
            if (Cells[start] != 2) { reason = "Your starting area is on an obstacle (red) or its extra margin (orange)."; return false; }
            if (goal < 0 || Cells[goal] == 0) { reason = "The destination floor is not captured. Scan it before testing a route."; return false; }
            if (Cells[goal] == 3) { reason = "The destination has incomplete floor coverage nearby (purple). Scan the surrounding gaps."; return false; }
            if (Cells[goal] != 2) { reason = "The destination is on an obstacle (red) or its extra margin (orange)."; return false; }
            var costs = new int[CellCount]; var parent = new int[CellCount]; var closed = new bool[CellCount];
            for (var i = 0; i < CellCount; i++) { costs[i] = int.MaxValue; parent[i] = -1; }
            var open = new SortedSet<(int total, int remaining, int index)>();
            costs[start] = 0; open.Add((Heuristic(start, goal), Heuristic(start, goal), start));
            while (open.Count > 0)
            {
                var first = open.Min; open.Remove(first); var current = first.index;
                if (closed[current]) continue;
                if (current == goal)
                {
                    for (var p = goal; p >= 0; p = parent[p]) path.Add(Centre(p));
                    path.Reverse(); reason = "Route uses the paused scan. Changes to furniture are not monitored."; return true;
                }
                closed[current] = true;
                var x = current % Width; var z = current / Width;
                for (var dz = -1; dz <= 1; dz++) for (var dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dz == 0) continue;
                    var nx = x + dx; var nz = z + dz;
                    if (nx < 0 || nz < 0 || nx >= Width || nz >= Height) continue;
                    var next = nz * Width + nx;
                    if (Cells[next] != 2 || closed[next]) continue;
                    if (dx != 0 && dz != 0 && (Cells[z * Width + nx] != 2 || Cells[nz * Width + x] != 2)) continue;
                    var candidate = costs[current] + (dx == 0 || dz == 0 ? 10 : 14);
                    if (candidate >= costs[next]) continue;
                    costs[next] = candidate; parent[next] = current;
                    var h = Heuristic(next, goal); open.Add((candidate + h, h, next));
                }
            }
            reason = "No connected route with this extra margin. Check floor gaps, red obstacles and orange margins.";
            return false;
        }

        private int Heuristic(int a, int b)
        { var dx = Math.Abs(a % Width - b % Width); var dz = Math.Abs(a / Width - b / Width); return 10 * Math.Max(dx, dz) + 4 * Math.Min(dx, dz); }
        private static bool Supported(byte samples, bool strict)
        {
            if (strict) return samples == 31;
            // Centre must have direct evidence; permit one missed corner of a
            // cell, not a whole unobserved cell or stripe of missing floor.
            if ((samples & 16) == 0) return false;
            var corners = samples & 15;
            var count = 0;
            while (corners != 0) { count += corners & 1; corners >>= 1; }
            return count >= 3;
        }
        private static bool FloorTriangle(MeshPoint a, MeshPoint b, MeshPoint c, float y)
        {
            if (Math.Abs(a.Y - y) > 0.08f || Math.Abs(b.Y - y) > 0.08f || Math.Abs(c.Y - y) > 0.08f) return false;
            var abX = b.X - a.X; var abY = b.Y - a.Y; var abZ = b.Z - a.Z;
            var acX = c.X - a.X; var acY = c.Y - a.Y; var acZ = c.Z - a.Z;
            var nx = abY * acZ - abZ * acY; var ny = abZ * acX - abX * acZ; var nz = abX * acY - abY * acX;
            return ny * ny > 0.93f * (nx * nx + ny * ny + nz * nz) && ny * ny > 0.00000001f;
        }
        private static float Cross(float ax, float az, float bx, float bz) => ax * bz - az * bx;
        private static bool Inside(float x, float z, MeshPoint a, MeshPoint b, MeshPoint c)
        {
            var area = Cross(b.X - a.X, b.Z - a.Z, c.X - a.X, c.Z - a.Z);
            if (Math.Abs(area) < 0.0000001f) return false;
            var u = Cross(b.X - x, b.Z - z, c.X - x, c.Z - z) / area;
            var v = Cross(c.X - x, c.Z - z, a.X - x, a.Z - z) / area;
            return u >= -0.00001f && v >= -0.00001f && u + v <= 1.00001f;
        }
        private static bool Overlaps(MeshPoint a, MeshPoint b, MeshPoint c, float x, float z, float size)
        {
            var minX = Math.Min(a.X, Math.Min(b.X, c.X)); var maxX = Math.Max(a.X, Math.Max(b.X, c.X));
            var minZ = Math.Min(a.Z, Math.Min(b.Z, c.Z)); var maxZ = Math.Max(a.Z, Math.Max(b.Z, c.Z));
            if (maxX < x - 0.00001f || minX > x + size + 0.00001f || maxZ < z - 0.00001f || minZ > z + size + 0.00001f) return false;
            var points = new[] { a, b, c };
            for (var i = 0; i < 3; i++)
            {
                var p = points[i]; var q = points[(i + 1) % 3];
                var axisX = -(q.Z - p.Z); var axisZ = q.X - p.X;
                var pa = a.X * axisX + a.Z * axisZ; var pb = b.X * axisX + b.Z * axisZ; var pc = c.X * axisX + c.Z * axisZ;
                var centre = (x + size / 2) * axisX + (z + size / 2) * axisZ;
                var radius = size / 2 * (Math.Abs(axisX) + Math.Abs(axisZ));
                if (Math.Max(pa, Math.Max(pb, pc)) < centre - radius - 0.00001f || Math.Min(pa, Math.Min(pb, pc)) > centre + radius + 0.00001f) return false;
            }
            return true;
        }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
