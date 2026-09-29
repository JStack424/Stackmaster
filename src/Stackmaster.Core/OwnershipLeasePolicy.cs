namespace Stackmaster.Core
{
    public sealed class OwnershipReleaseDecision
    {
        public OwnershipReleaseDecision(bool shouldRelease, long targetOwner)
        {
            ShouldRelease = shouldRelease;
            TargetOwner = targetOwner;
        }

        public bool ShouldRelease { get; }
        public long TargetOwner { get; }
    }

    public enum OwnershipLeasePurpose
    {
        Retry,
        Building,
        Crafting
    }

    public static class OwnershipLeaseRetentionPolicy
    {
        public static bool ShouldRenewForSuccessfulBuild(
            bool demonstrablyAcquiredByStackmaster,
            bool usedByPlacement,
            bool placementSucceeded)
            => demonstrablyAcquiredByStackmaster && usedByPlacement && placementSucceeded;

        public static float RenewedExpiry(float now, float leaseSeconds)
            => now + leaseSeconds;

        public static bool IsExpired(float now, float expiresAt)
            => now >= expiresAt;

        public static bool ShouldYieldToManualOpen(
            OwnershipLeasePurpose purpose,
            long requesterSession,
            long acquiredSession,
            bool logicallyReserved)
            => purpose == OwnershipLeasePurpose.Building &&
               requesterSession != acquiredSession &&
               !logicallyReserved;
    }

    public enum UnownedAssignmentDecision
    {
        Wait,
        ContinueLocally,
        Reject
    }

    public static class UnownedAssignmentPolicy
    {
        public static UnownedAssignmentDecision Decide(
            long originalOwner,
            ushort originalOwnerRevision,
            long currentOwner,
            ushort currentOwnerRevision,
            long localSession)
        {
            if (originalOwner != 0L)
            {
                return UnownedAssignmentDecision.Reject;
            }
            if (currentOwner == 0L)
            {
                return UnownedAssignmentDecision.Wait;
            }

            return currentOwner == localSession &&
                   currentOwnerRevision == OwnershipLeasePolicy.NextOwnerRevision(originalOwnerRevision)
                ? UnownedAssignmentDecision.ContinueLocally
                : UnownedAssignmentDecision.Reject;
        }
    }

    public static class OwnershipLeasePolicy
    {
        public static ushort NextOwnerRevision(ushort current)
            => unchecked((ushort)(current + 1));

        public static OwnershipReleaseDecision Decide(
            bool identityMatches,
            bool sessionMatches,
            bool locallyOwned,
            bool ownerRevisionMatches)
        {
            if (!identityMatches || !sessionMatches || !locallyOwned || !ownerRevisionMatches)
            {
                return new OwnershipReleaseDecision(false, 0);
            }

            // Zero is Valheim's native unowned state. ZDOMan's regular ownership pass
            // assigns an unowned persistent object to a suitable active peer. Never restore
            // a possibly stale former peer and never manually assign a topology-dependent server.
            return new OwnershipReleaseDecision(true, 0);
        }
    }
}
