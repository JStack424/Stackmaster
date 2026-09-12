namespace Stackmaster.Core
{
    /// <summary>
    /// Separates live-container snapshots used by mutating storage actions from detached,
    /// serialized snapshots used only for nearby-resource availability.
    /// </summary>
    public sealed class ResourceSnapshotReadPlan
    {
        internal ResourceSnapshotReadPlan(bool captureLiveInventory, bool useDetachedInventory)
        {
            CaptureLiveInventory = captureLiveInventory;
            UseDetachedInventory = useDetachedInventory;
        }

        public bool CaptureLiveInventory { get; }
        public bool UseDetachedInventory { get; }
    }

    public static class ResourceSnapshotPolicy
    {
        public static ResourceSnapshotReadPlan Evaluate(
            bool resourceReadOnly,
            bool liveInventoryKnown,
            bool accessGranted,
            bool inUse,
            bool detachedInventoryDecoded)
        {
            return new ResourceSnapshotReadPlan(
                captureLiveInventory: !resourceReadOnly && liveInventoryKnown && accessGranted && !inUse,
                useDetachedInventory: resourceReadOnly && detachedInventoryDecoded && accessGranted);
        }
    }
}
