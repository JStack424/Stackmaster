using System;
using System.Collections.Generic;
using System.Linq;

namespace Stackmaster.Core
{
    public enum StorageScopeKind
    {
        Unavailable,
        NearbyRadius,
        WorkbenchMesh
    }

    public readonly struct ScopePoint
    {
        public ScopePoint(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public double X { get; }
        public double Y { get; }
        public double Z { get; }
    }

    public sealed class WorkbenchZone
    {
        public WorkbenchZone(string id, ScopePoint center, double buildRange)
        {
            Id = id ?? string.Empty;
            Center = center;
            BuildRange = buildRange;
        }

        public string Id { get; }
        public ScopePoint Center { get; }
        public double BuildRange { get; }
    }

    public sealed class StorageScopePlan
    {
        private readonly ScopePoint _playerPosition;
        private readonly double _fallbackRadius;
        private readonly IReadOnlyList<WorkbenchZone> _connectedZones;

        internal StorageScopePlan(
            StorageScopeKind kind,
            ScopePoint playerPosition,
            double fallbackRadius,
            IReadOnlyList<WorkbenchZone> connectedZones)
        {
            Kind = kind;
            _playerPosition = playerPosition;
            _fallbackRadius = fallbackRadius;
            _connectedZones = connectedZones ?? Array.Empty<WorkbenchZone>();
        }

        public StorageScopeKind Kind { get; }
        public ScopePoint PlayerPosition => _playerPosition;
        public double FallbackRadius => _fallbackRadius;
        public IReadOnlyList<WorkbenchZone> ConnectedZones => _connectedZones;

        public bool Contains(ScopePoint point)
        {
            if (Kind == StorageScopeKind.Unavailable) return false;
            if (Kind == StorageScopeKind.NearbyRadius)
            {
                return StorageScopePolicy.DistanceSquared3D(_playerPosition, point) <= _fallbackRadius * _fallbackRadius;
            }

            return _connectedZones.Any(zone =>
                StorageScopePolicy.DistanceSquaredXZ(zone.Center, point) < zone.BuildRange * zone.BuildRange);
        }
    }

    public static class StorageScopePolicy
    {
        public static StorageScopePlan Unavailable(ScopePoint playerPosition, double fallbackRadius)
            => new StorageScopePlan(StorageScopeKind.Unavailable, playerPosition, fallbackRadius, Array.Empty<WorkbenchZone>());

        public static StorageScopePlan Resolve(
            ScopePoint playerPosition,
            double fallbackRadius,
            IEnumerable<WorkbenchZone> loadedWorkbenchZones)
        {
            if (!IsFinite(fallbackRadius) || fallbackRadius <= 0)
            {
                return Unavailable(playerPosition, fallbackRadius);
            }

            var validZones = (loadedWorkbenchZones ?? Array.Empty<WorkbenchZone>())
                .Where(IsValid)
                .ToArray();
            if (validZones.GroupBy(zone => zone.Id, StringComparer.Ordinal).Any(group => group.Count() != 1))
            {
                return Unavailable(playerPosition, fallbackRadius);
            }
            var zones = validZones
                .OrderBy(zone => zone.Id, StringComparer.Ordinal)
                .ToArray();
            var connected = new List<WorkbenchZone>();
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var queue = new Queue<WorkbenchZone>();

            foreach (var seed in zones.Where(zone => Contains(zone, playerPosition)))
            {
                if (visited.Add(seed.Id)) queue.Enqueue(seed);
            }
            if (queue.Count == 0)
            {
                return new StorageScopePlan(
                    StorageScopeKind.NearbyRadius,
                    playerPosition,
                    fallbackRadius,
                    Array.Empty<WorkbenchZone>());
            }

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                connected.Add(current);
                foreach (var candidate in zones)
                {
                    if (visited.Contains(candidate.Id) || !Overlaps(current, candidate)) continue;
                    visited.Add(candidate.Id);
                    queue.Enqueue(candidate);
                }
            }

            return new StorageScopePlan(
                StorageScopeKind.WorkbenchMesh,
                playerPosition,
                fallbackRadius,
                connected.OrderBy(zone => zone.Id, StringComparer.Ordinal).ToArray());
        }

        public static bool Contains(WorkbenchZone zone, ScopePoint point)
            => IsValid(zone) && DistanceSquaredXZ(zone.Center, point) < zone.BuildRange * zone.BuildRange;

        public static bool Overlaps(WorkbenchZone first, WorkbenchZone second)
        {
            if (!IsValid(first) || !IsValid(second)) return false;
            var combinedRange = first.BuildRange + second.BuildRange;
            return DistanceSquaredXZ(first.Center, second.Center) < combinedRange * combinedRange;
        }

        internal static double DistanceSquaredXZ(ScopePoint first, ScopePoint second)
        {
            var x = first.X - second.X;
            var z = first.Z - second.Z;
            return x * x + z * z;
        }

        internal static double DistanceSquared3D(ScopePoint first, ScopePoint second)
        {
            var x = first.X - second.X;
            var y = first.Y - second.Y;
            var z = first.Z - second.Z;
            return x * x + y * y + z * z;
        }

        private static bool IsValid(WorkbenchZone zone)
            => zone != null && !string.IsNullOrWhiteSpace(zone.Id) &&
               IsFinite(zone.Center.X) && IsFinite(zone.Center.Y) && IsFinite(zone.Center.Z) &&
               IsFinite(zone.BuildRange) && zone.BuildRange > 0;

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
