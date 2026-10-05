namespace Stackmaster.Core
{
    public enum OwnershipPreparationTimeoutKind
    {
        OwnerResponse,
        VanillaOwnerAssignment,
        BatchAbortedBeforeRequest
    }

    public static class OwnershipPreparationPolicy
    {
        public static bool RequiresPreparation(
            bool hasOwner,
            bool networkViewIsOwner,
            bool containerIsOwner)
            => !hasOwner || !networkViewIsOwner || !containerIsOwner;

        public static OwnershipPreparationTimeoutKind TimeoutKind(
            bool isActiveRequest,
            bool waitingForVanillaOwnerAssignment)
            => !isActiveRequest
                ? OwnershipPreparationTimeoutKind.BatchAbortedBeforeRequest
                : waitingForVanillaOwnerAssignment
                    ? OwnershipPreparationTimeoutKind.VanillaOwnerAssignment
                    : OwnershipPreparationTimeoutKind.OwnerResponse;
    }
}
