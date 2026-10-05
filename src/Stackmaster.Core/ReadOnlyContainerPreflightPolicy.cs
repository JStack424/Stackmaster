using System;

namespace Stackmaster.Core
{
    public enum ReadOnlyContainerPreflightFailure
    {
        None,
        MissingHandle,
        MissingContainer,
        ChangedContainerType,
        MissingStorageScope,
        OutsideStorageScope,
        MissingNetworkView,
        InvalidNetworkView,
        MissingNetworkState,
        ChangedIdentity,
        UnreadableSnapshot,
        MissingDetachedInventory
    }

    public static class ReadOnlyContainerPreflightPolicy
    {
        public static ReadOnlyContainerPreflightFailure Evaluate(
            bool hasHandle,
            bool hasContainer,
            bool isVanillaContainer,
            bool hasStorageScope,
            bool isInsideStorageScope,
            bool hasNetworkView,
            bool isNetworkViewValid,
            bool hasNetworkState,
            bool identityMatches,
            bool isSnapshotReadable,
            bool hasDetachedInventory)
        {
            if (!hasHandle) return ReadOnlyContainerPreflightFailure.MissingHandle;
            if (!hasContainer) return ReadOnlyContainerPreflightFailure.MissingContainer;
            if (!isVanillaContainer) return ReadOnlyContainerPreflightFailure.ChangedContainerType;
            if (!hasStorageScope) return ReadOnlyContainerPreflightFailure.MissingStorageScope;
            if (!isInsideStorageScope) return ReadOnlyContainerPreflightFailure.OutsideStorageScope;
            if (!hasNetworkView) return ReadOnlyContainerPreflightFailure.MissingNetworkView;
            if (!isNetworkViewValid) return ReadOnlyContainerPreflightFailure.InvalidNetworkView;
            if (!hasNetworkState) return ReadOnlyContainerPreflightFailure.MissingNetworkState;
            if (!identityMatches) return ReadOnlyContainerPreflightFailure.ChangedIdentity;
            if (!isSnapshotReadable) return ReadOnlyContainerPreflightFailure.UnreadableSnapshot;
            if (!hasDetachedInventory) return ReadOnlyContainerPreflightFailure.MissingDetachedInventory;
            return ReadOnlyContainerPreflightFailure.None;
        }

        public static string FailureMessage(ReadOnlyContainerPreflightFailure failure)
        {
            switch (failure)
            {
                case ReadOnlyContainerPreflightFailure.None:
                    return string.Empty;
                case ReadOnlyContainerPreflightFailure.MissingHandle:
                    return "required nearby container handle was unavailable before ownership";
                case ReadOnlyContainerPreflightFailure.MissingContainer:
                    return "nearby container disappeared before ownership";
                case ReadOnlyContainerPreflightFailure.ChangedContainerType:
                    return "nearby container type changed before ownership";
                case ReadOnlyContainerPreflightFailure.MissingStorageScope:
                    return "active storage scope became unavailable before ownership";
                case ReadOnlyContainerPreflightFailure.OutsideStorageScope:
                    return "nearby container left the active storage scope before ownership";
                case ReadOnlyContainerPreflightFailure.MissingNetworkView:
                    return "nearby container network view disappeared before ownership";
                case ReadOnlyContainerPreflightFailure.InvalidNetworkView:
                    return "nearby container network view became invalid before ownership";
                case ReadOnlyContainerPreflightFailure.MissingNetworkState:
                    return "nearby container network state disappeared before ownership";
                case ReadOnlyContainerPreflightFailure.ChangedIdentity:
                    return "nearby container identity changed before ownership";
                case ReadOnlyContainerPreflightFailure.UnreadableSnapshot:
                    return "nearby container inventory snapshot became unreadable before ownership";
                case ReadOnlyContainerPreflightFailure.MissingDetachedInventory:
                    return "nearby container detached inventory disappeared before ownership";
                default:
                    throw new ArgumentOutOfRangeException(nameof(failure), failure, "Unknown read-only container preflight failure.");
            }
        }
    }
}
