using System;

namespace SpatialSense.Navigation
{
    // Liang-Barsky clipping in an obstacle's horizontal local coordinates.
    public static class RouteMath
    {
        public static float CarvingWidth(float physicalWidth, float scanBuffer)
        {
            if (physicalWidth < 0f || scanBuffer < 0f) throw new ArgumentException("Widths and buffers cannot be negative.");
            return Math.Max(physicalWidth, 0.03f) + 2f * scanBuffer;
        }

        public static bool SegmentViolatesClearance(float ax, float az, float bx, float bz,
            float minX, float minZ, float maxX, float maxZ, float radius)
        {
            if (radius < 0f) throw new ArgumentException("Clearance cannot be negative.");
            if (SegmentIntersectsRectangle(ax, az, bx, bz, minX, minZ, maxX, maxZ)) return true;
            var squared = radius * radius;
            return PointRectangleDistanceSquared(ax, az, minX, minZ, maxX, maxZ) <= squared
                || PointRectangleDistanceSquared(bx, bz, minX, minZ, maxX, maxZ) <= squared
                || PointSegmentDistanceSquared(minX, minZ, ax, az, bx, bz) <= squared
                || PointSegmentDistanceSquared(maxX, minZ, ax, az, bx, bz) <= squared
                || PointSegmentDistanceSquared(maxX, maxZ, ax, az, bx, bz) <= squared
                || PointSegmentDistanceSquared(minX, maxZ, ax, az, bx, bz) <= squared;
        }

        private static float PointRectangleDistanceSquared(float x, float z, float minX, float minZ, float maxX, float maxZ)
        {
            var dx = Math.Max(Math.Max(minX - x, 0f), x - maxX);
            var dz = Math.Max(Math.Max(minZ - z, 0f), z - maxZ);
            return dx * dx + dz * dz;
        }

        private static float PointSegmentDistanceSquared(float x, float z, float ax, float az, float bx, float bz)
        {
            var dx = bx - ax;
            var dz = bz - az;
            var lengthSquared = dx * dx + dz * dz;
            var t = lengthSquared < 0.0000001f ? 0f : Math.Max(0f, Math.Min(1f, ((x - ax) * dx + (z - az) * dz) / lengthSquared));
            var offsetX = x - ax - t * dx;
            var offsetZ = z - az - t * dz;
            return offsetX * offsetX + offsetZ * offsetZ;
        }

        public static bool SegmentIntersectsRectangle(float ax, float az, float bx, float bz,
            float minX, float minZ, float maxX, float maxZ)
        {
            if (minX > maxX || minZ > maxZ) throw new ArgumentException("Invalid rectangle.");
            float enter = 0f, leave = 1f;
            var dx = bx - ax;
            var dz = bz - az;
            return Clip(-dx, ax - minX, ref enter, ref leave)
                && Clip(dx, maxX - ax, ref enter, ref leave)
                && Clip(-dz, az - minZ, ref enter, ref leave)
                && Clip(dz, maxZ - az, ref enter, ref leave);
        }

        private static bool Clip(float direction, float distance, ref float enter, ref float leave)
        {
            if (Math.Abs(direction) < 0.000001f) return distance >= 0f;
            var ratio = distance / direction;
            if (direction < 0f)
            {
                if (ratio > leave) return false;
                if (ratio > enter) enter = ratio;
            }
            else
            {
                if (ratio < enter) return false;
                if (ratio < leave) leave = ratio;
            }
            return true;
        }
    }
}
