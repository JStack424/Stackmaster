using System;

namespace Stackmaster.Core
{
    /// <summary>
    /// Supported-range rule for chest-backed resources inside a connected Workbench mesh.
    ///
    /// Valheim strips ZDO ownership once a chest leaves the player's active area and only
    /// reassigns an ownerless chest inside that area, so distant mesh chests can be readable
    /// yet impossible to acquire reliably. Rather than racing that ownership sweep, the mesh
    /// gets one fixed, player-visible limit: a chest farther away than this contributes no
    /// resources at all. The player-centred fallback radius is a separate, already bounded
    /// scope and is never changed by this rule.
    /// </summary>
    public static class ResourceDistancePolicy
    {
        public const double MaximumMeshResourceDistance = 100.0;

        /// <summary>
        /// True when a source at <paramref name="distance"/> may contribute resources under
        /// <paramref name="scopeKind"/>. Only the Workbench mesh is distance-capped; every
        /// other scope keeps its existing eligibility. A non-finite distance is never within
        /// the limit, so an unmeasurable source fails closed into the excluded set.
        /// </summary>
        public static bool IsWithinLimit(StorageScopeKind scopeKind, double distance)
        {
            if (scopeKind != StorageScopeKind.WorkbenchMesh) return true;
            return IsFinite(distance) && distance <= MaximumMeshResourceDistance;
        }

        /// <summary>
        /// True when a source is excluded solely because of the mesh distance limit. The
        /// caller uses this only to explain an otherwise satisfiable action; excluded stock
        /// itself never becomes selectable.
        /// </summary>
        public static bool IsBeyondLimit(StorageScopeKind scopeKind, double distance)
            => scopeKind == StorageScopeKind.WorkbenchMesh && !IsWithinLimit(scopeKind, distance);

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
