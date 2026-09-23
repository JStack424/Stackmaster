using System;

namespace Stackmaster.Core
{
    public enum DisplayCaptureReuseKind
    {
        RecaptureAll,
        ReuseComplete,
        RefreshPlayerOnly
    }

    /// <summary>
    /// Pure reuse rules for a short-lived, display-only nearby-resource snapshot.
    /// Mutation/action paths never enter this policy.
    /// </summary>
    public static class DisplayCaptureEpochPolicy
    {
        public const double MaximumAgeSeconds = 0.20;

        public static DisplayCaptureReuseKind SelectReuse(
            bool scopeReusable,
            string cachedPlayerInventorySignature,
            string currentPlayerInventorySignature)
        {
            if (!scopeReusable) return DisplayCaptureReuseKind.RecaptureAll;
            return string.Equals(
                    cachedPlayerInventorySignature,
                    currentPlayerInventorySignature,
                    StringComparison.Ordinal)
                ? DisplayCaptureReuseKind.ReuseComplete
                : DisplayCaptureReuseKind.RefreshPlayerOnly;
        }

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

        private static double DistanceSquared3D(ScopePoint first, ScopePoint second)
        {
            var x = first.X - second.X;
            var y = first.Y - second.Y;
            var z = first.Z - second.Z;
            return x * x + y * y + z * z;
        }
    }
}
