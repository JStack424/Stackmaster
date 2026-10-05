namespace Stackmaster.Core
{
    public enum OwnershipPreparationTimeoutKind
    {
        OwnerResponse,
        VanillaOwnerAssignment
    }

    public static class OwnershipPreparationPolicy
    {
        public static bool RequiresPreparation(
            bool hasOwner,
            bool networkViewIsOwner,
            bool containerIsOwner)
            => !hasOwner || !networkViewIsOwner || !containerIsOwner;

        public static OwnershipPreparationTimeoutKind TimeoutKind(bool waitingForVanillaOwnerAssignment)
            => waitingForVanillaOwnerAssignment
                ? OwnershipPreparationTimeoutKind.VanillaOwnerAssignment
                : OwnershipPreparationTimeoutKind.OwnerResponse;
    }
}
