#nullable disable

namespace Stackmaster.Core
{
    /// <summary>
    /// Confirms whether one protected player stack has actually left the player's inventory.
    /// Merely starting a drag, failing a transfer, moving inside the player inventory, or moving
    /// only part of a stack must not clear protection while the original stack remains.
    /// </summary>
    public static class ProtectionExitPolicy
    {
        public static bool ShouldClear(
            bool wasProtected,
            bool destinationWasExternal,
            bool sourceStillContainsProtectedItem)
        {
            return wasProtected && destinationWasExternal && !sourceStillContainsProtectedItem;
        }
    }
}
