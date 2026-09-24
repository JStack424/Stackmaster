#nullable disable
using System;
using Stackmaster.Core;

namespace Stackmaster
{
    internal static class RuntimeContext
    {
        internal const string CharacterDataKey = "com.jstack424.stackmaster/protected-slots";

        private static CompatibilityResult _verifiedCompatibility = new CompatibilityResult(false, "not initialized");
        private static SessionLifecycleState _lifecycle = new SessionLifecycleState(false);
        private static ZNet _disconnectingNetwork;
        private static bool _disconnectCleanupCompleted;
        private static bool _cleanupInProgress;

        internal static Plugin Plugin { get; private set; }
        internal static CompatibilityResult Compatibility { get; private set; } = new CompatibilityResult(false, "not initialized");
        internal static bool IsAwaitingReconnect => _lifecycle.CanAttemptRearm;

        internal static void Initialize(Plugin plugin, CompatibilityResult compatibility)
        {
            Plugin = plugin;
            _verifiedCompatibility = compatibility;
            _lifecycle = new SessionLifecycleState(compatibility.IsCompatible);
            _disconnectingNetwork = null;
            _disconnectCleanupCompleted = false;
            _cleanupInProgress = false;
            Compatibility = compatibility;
            ChestSortPreferences.Initialize();
            StorageScopeProvider.Reset();
            NearbyResourceService.ResetCaches();
            NearbyBuildHudPatch.ResetCache();
            NearbyCraftingHudPatch.ResetCache();
            QuickGrabMaterialsBuildHint.Detach();
            NearbyHudFailOpen.ResetSession();
        }

        internal static void DisconnectSession()
        {
            if (_lifecycle.IsPermanentlyDisabled)
            {
                RunSafetyCleanup("session disconnect after permanent disable");
                return;
            }

            _disconnectingNetwork = ZNet.instance;
            _lifecycle.BeginDisconnect();
            Compatibility = new CompatibilityResult(false, "between server sessions");

            _disconnectCleanupCompleted = RunSafetyCleanup("session disconnect");
            if (!_disconnectCleanupCompleted && !_lifecycle.IsPermanentlyDisabled)
            {
                Disable("Session cleanup failed; restart required before any further Stackmaster actions.");
            }
        }

        internal static void TryRearmSession(ZNet network)
        {
            if (!_lifecycle.CanAttemptRearm || network == null || ReferenceEquals(network, _disconnectingNetwork))
            {
                return;
            }

            try
            {
                // A different ZNet instance proves the prior transport is over. Delayed replies,
                // reservations, and ownership records from it can no longer authorize mutation in
                // this session, so discard them before restoring any gameplay entrypoint.
                NearbyResourceService.DiscardEndedSessionState();
                OwnershipCoordinator.DiscardEndedSessionState();
                ResourceActionContext.Reset();
                VanillaPlayerCraftContext.End();
                StorageAction.RearmSession();
                NearbyResourceOwnership.RearmSession();
                QuickGrabMaterialsAction.RearmSession();
                ChestSortPreferences.Initialize();
                StorageScopeProvider.Reset();
                NearbyResourceService.ResetCaches();
                NearbyBuildHudPatch.ResetCache();
                NearbyCraftingHudPatch.ResetCache();
                NearbyHudFailOpen.ResetSession();
                InventoryIntegration.OnSessionRearmed();

                if (!_lifecycle.TryRearm(
                        isDifferentNetworkSession: true,
                        cleanupCompletedSafely: _disconnectCleanupCompleted))
                {
                    return;
                }

                _disconnectingNetwork = null;
                _disconnectCleanupCompleted = false;
                Compatibility = _verifiedCompatibility;
                Plugin?.OnSessionRearmed();
                Plugin?.Log.LogInfo("Stackmaster session state rearmed for the new server connection.");
            }
            catch (Exception exception)
            {
                Disable("Session rearm failed: " + exception.GetType().Name);
                Plugin?.Log.LogError("Stackmaster could not safely rearm after reconnect: " + exception);
            }
        }

        internal static void Disable(string reason)
        {
            _lifecycle.DisablePermanently();
            Compatibility = new CompatibilityResult(false, reason);
            if (_cleanupInProgress)
            {
                return;
            }

            RunSafetyCleanup(reason);
        }

        private static bool RunSafetyCleanup(string reason)
        {
            if (_cleanupInProgress)
            {
                return false;
            }

            var completedSafely = true;
            _cleanupInProgress = true;
            try
            {
                // Roll back mutations and clear logical reservations before handing any exact
                // acquired ZDO back to vanilla. Every stage runs even if another stage fails.
                completedSafely &= TryCleanup("Resource transaction shutdown", ResourceTransactionContext.Shutdown);
                completedSafely &= TryCleanup("Quick-grab shutdown", QuickGrabMaterialsAction.Shutdown);
                completedSafely &= TryCleanup("Storage-action shutdown", StorageAction.Shutdown);
                completedSafely &= TryCleanup("Crafting preflight shutdown", () => CraftingPreflightAction.Shutdown(reason));
                completedSafely &= TryCleanup("Nearby-resource ownership shutdown", NearbyResourceOwnership.Shutdown);
                completedSafely &= TryCleanup("Pending reservation shutdown cleanup",
                    NearbyResourceService.FlushPendingReservationReleasesBeforeOwnershipShutdown);
                completedSafely &= TryCleanup("Ownership shutdown", () => OwnershipCoordinator.Shutdown(reason));
                completedSafely &= TryCleanup("Resource action context cleanup", ResourceActionContext.Reset);
                completedSafely &= TryCleanup("Vanilla player-craft context cleanup", VanillaPlayerCraftContext.End);
                completedSafely &= TryCleanup("Inventory session cleanup", InventoryIntegration.OnSessionDisconnected);
                completedSafely &= TryCleanup("Chest-sort preference session cleanup", ChestSortPreferences.Shutdown);
                completedSafely &= TryCleanup("Storage scope cleanup", StorageScopeProvider.Reset);
                completedSafely &= TryCleanup("Nearby resource cache cleanup", NearbyResourceService.ResetCaches);
                completedSafely &= TryCleanup("Build HUD cache cleanup", NearbyBuildHudPatch.ResetCache);
                completedSafely &= TryCleanup("Crafting HUD cache cleanup", NearbyCraftingHudPatch.ResetCache);
                completedSafely &= TryCleanup("Quick-grab build-menu hint cleanup", QuickGrabMaterialsBuildHint.Detach);
                completedSafely &= TryCleanup("HUD diagnostic cache cleanup", NearbyHudFailOpen.ResetSession);
            }
            finally
            {
                _cleanupInProgress = false;
            }

            return completedSafely;
        }

        private static bool TryCleanup(string operation, Action action)
        {
            try
            {
                action();
                return true;
            }
            catch (Exception exception)
            {
                Plugin?.Log.LogError(operation + " failed safely: " + exception);
                _lifecycle.DisablePermanently();
                Compatibility = new CompatibilityResult(false,
                    operation + " failed; restart required before any further Stackmaster actions.");
                return false;
            }
        }

        internal static void Shutdown()
        {
            QuickGrabMaterialsAction.Shutdown();
            StorageAction.Shutdown();
            CraftingPreflightAction.Shutdown("plugin shutdown");
            NearbyResourceOwnership.Shutdown();
            ChestSortPreferences.Shutdown();
            StorageScopeProvider.Reset();
            NearbyResourceService.ResetCaches();
            NearbyBuildHudPatch.ResetCache();
            NearbyCraftingHudPatch.ResetCache();
            QuickGrabMaterialsBuildHint.Detach();
            NearbyHudFailOpen.ResetSession();
            ResourceActionContext.Reset();
            VanillaPlayerCraftContext.End();
            _lifecycle.ShutDown();
            _disconnectingNetwork = null;
            _disconnectCleanupCompleted = false;
            Plugin = null;
            Compatibility = new CompatibilityResult(false, "shut down");
        }

        internal static bool TryLoadProtection(Player player, out ProtectionState state)
        {
            state = new ProtectionState();
            if (!Compatibility.IsCompatible)
            {
                return false;
            }
            if (player == null || player.m_customData == null)
            {
                return true;
            }

            string raw;
            if (!player.m_customData.TryGetValue(CharacterDataKey, out raw))
            {
                return true;
            }
            if (ProtectionState.TryParse(raw, out state))
            {
                return true;
            }

            const string reason = "Saved protection data is malformed or from an unsupported version; restart required.";
            Disable(reason);
            Plugin.Log.LogError("Stackmaster disabled without changing the saved protection payload: " + reason);
            ShowCenter("Stackmaster disabled: protected-item data could not be read safely. Your saved data was not changed.");
            return false;
        }

        internal static void SaveProtection(Player player, ProtectionState state)
        {
            if (!Compatibility.IsCompatible)
            {
                throw new InvalidOperationException("Stackmaster is disabled; protection data was not changed.");
            }
            if (player == null || player.m_customData == null)
            {
                throw new InvalidOperationException("No loaded character is available for protection persistence.");
            }

            player.m_customData[CharacterDataKey] = state.Serialize();
        }

        internal static void ShowCenter(string message)
        {
            if (Player.m_localPlayer != null)
            {
                Player.m_localPlayer.Message(MessageHud.MessageType.Center, message, 0, null, false);
            }
        }

        internal static void ShowTopLeft(string message)
        {
            if (Player.m_localPlayer != null)
            {
                Player.m_localPlayer.Message(MessageHud.MessageType.TopLeft, message, 0, null, false);
            }
        }
    }
}
