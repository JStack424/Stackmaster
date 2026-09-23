using System;

namespace Stackmaster.Core
{
    /// <summary>
    /// Pure reuse rules for a short-lived, display-only nearby-resource snapshot.
    /// Mutation/action paths never enter this policy.
    /// </summary>
    public static class DisplayCaptureEpochPolicy
    {
        public const double MaximumAgeSeconds = 0.20;

        public static bool CanReuseScope(
            StorageScopeKind cachedKind,
            string cachedStructuralSignature,
            ScopePoint cachedPlayerPosition,
            double membershipStabilityDistance,
            double capturedAtSeconds,
            StorageScopeKind currentKind,
            string currentStructuralSignature,
            ScopePoint currentPlayerPosition,
            double nowSeconds)
        {
            if (double.IsNaN(capturedAtSeconds) || double.IsInfinity(capturedAtSeconds) ||
                double.IsNaN(nowSeconds) || double.IsInfinity(nowSeconds) ||
                nowSeconds < capturedAtSeconds || nowSeconds >= capturedAtSeconds + MaximumAgeSeconds ||
                cachedKind != currentKind ||
                !string.Equals(cachedStructuralSignature, currentStructuralSignature, StringComparison.Ordinal))
            {
                return false;
            }

            if (currentKind != StorageScopeKind.NearbyRadius)
            {
                return currentKind == StorageScopeKind.WorkbenchMesh;
            }

            if (double.IsNaN(membershipStabilityDistance) || membershipStabilityDistance < 0)
            {
                return false;
            }

            var movedSquared = DistanceSquared3D(cachedPlayerPosition, currentPlayerPosition);
            if (double.IsPositiveInfinity(membershipStabilityDistance))
            {
                return true;
            }
            return movedSquared < membershipStabilityDistance * membershipStabilityDistance;
        }

        public static bool PayloadMatches(byte[] expected, byte[] current)
        {
            if (ReferenceEquals(expected, current)) return true;
            if (expected == null || current == null || expected.Length != current.Length) return false;
            for (var index = 0; index < expected.Length; index++)
            {
                if (expected[index] != current[index]) return false;
            }
            return true;
        }

        private static double DistanceSquared3D(ScopePoint first, ScopePoint second)
        {
            var x = first.X - second.X;
            var y = first.Y - second.Y;
            var z = first.Z - second.Z;
            return x * x + y * y + z * z;
        }
    }
}
