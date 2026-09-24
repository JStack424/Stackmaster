#nullable disable
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace Stackmaster
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.jstack424.stackmaster";
        public const string PluginName = "Stackmaster";
        public const string PluginVersion = GeneratedBuildInfo.Version;

        private Harmony _harmony;
        private bool _compatibilityWarningShown;

        internal static Plugin Instance { get; private set; }
        internal ConfigEntry<bool> AutoSortEnabled { get; private set; }
        internal ConfigEntry<float> NearbyStorageRadius { get; private set; }
        internal ConfigEntry<KeyboardShortcut> StorageActionShortcut { get; private set; }
        internal ConfigEntry<bool> BuildingFromNearbyChestsEnabled { get; private set; }
        internal ConfigEntry<bool> CraftingFromNearbyChestsEnabled { get; private set; }
        internal ConfigEntry<bool> ShowStorageAmountsInRequirementMenus { get; private set; }
        internal BepInEx.Logging.ManualLogSource Log => Logger;

        private void Awake()
        {
            Instance = this;
            AutoSortEnabled = Config.Bind("General", "Auto-sort enabled", true,
                "Sort movable player inventory slots once when the inventory opens. Opened chests use their own local checkbox.");
            NearbyStorageRadius = Config.Bind("General", "Nearby-storage radius", 20f,
                new ConfigDescription("Player-centered vanilla-container fallback radius in metres, used by all chest-powered features only while outside every connected vanilla workbench mesh.",
                    new AcceptableValueRange<float>(1f, 50f)));
            StorageActionShortcut = Config.Bind("General", "Storage-action keybind",
                new KeyboardShortcut(KeyCode.E, KeyCode.LeftAlt),
                "While targeting or viewing an eligible container: deposit and replenish. The shortcut modifiers also control protected-item inventory clicks and build-menu quick-grab clicks.");
            BuildingFromNearbyChestsEnabled = ConfigMigration.BindRenamedDefaultEnabledBoolean(
                Config,
                "General",
                "Enable building from nearby chests",
                "Allow building from storage",
                "Allow exact building costs to be counted and consumed from eligible vanilla chests in the shared workbench-mesh or fallback-radius scope.");
            CraftingFromNearbyChestsEnabled = ConfigMigration.BindRenamedDefaultEnabledBoolean(
                Config,
                "General",
                "Enable crafting from nearby chests",
                "Allow crafting from storage",
                "Allow exact crafting and upgrade costs to be counted and consumed from eligible vanilla chests in the shared workbench-mesh or fallback-radius scope.");
            ShowStorageAmountsInRequirementMenus = Config.Bind("General", "Show storage amounts in craft and build menus", true,
                "Show required / total available counts from the player and eligible storage in crafting, upgrade, and building requirement rows. Affordability still follows the separate crafting and building permissions.");

            CompatibilityResult compatibility;
            try
            {
                compatibility = CompatibilityGate.Evaluate();
            }
            catch (System.Exception exception)
            {
                compatibility = new CompatibilityResult(false,
                    "runtime contract validation failed: " + exception.GetType().Name);
                Logger.LogError("Compatibility validation threw before patch installation: " + exception);
            }

            RuntimeContext.Initialize(this, compatibility);
            Logger.LogInfo($"{PluginName} {PluginVersion} loading. Runtime diagnostics only: {compatibility.Diagnostics}.");

            if (!compatibility.IsCompatible)
            {
                Logger.LogError("Compatibility gate failed. Stackmaster is fully disabled before any inventory hooks were installed: " + compatibility.Reason);
                return;
            }

            try
            {
                var patchPlan = PatchInstaller.Prepare();
                _harmony = new Harmony(PluginGuid);
                PatchInstaller.Install(_harmony, patchPlan);
                Logger.LogInfo("Compatibility contracts and Harmony target preflight passed; gameplay hooks enabled.");
            }
            catch (System.Exception exception)
            {
                RuntimeContext.Disable("Harmony patch installation failed: " + exception.GetType().Name);
                Logger.LogError("Stackmaster disabled after patch installation failed: " + exception);
                try
                {
                    _harmony?.UnpatchSelf();
                }
                catch (System.Exception cleanupException)
                {
                    Logger.LogError("Stackmaster patch cleanup also failed; every patch entrypoint remains fail-closed: " + cleanupException);
                }
                finally
                {
                    _harmony = null;
                }
            }
        }

        private void Update()
        {
            if (RuntimeContext.Compatibility.IsCompatible)
            {
                StorageAction.Update();
                return;
            }

            if (RuntimeContext.IsAwaitingReconnect)
            {
                return;
            }

            if (!_compatibilityWarningShown && Player.m_localPlayer != null && MessageHud.instance != null)
            {
                _compatibilityWarningShown = true;
                MessageHud.instance.ShowMessage(MessageHud.MessageType.Center,
                    "Stackmaster disabled for safety. Restart Valheim before using it again.", 0, null);
            }
        }

        internal void OnSessionRearmed()
        {
            _compatibilityWarningShown = false;
        }

        private void OnDisable()
        {
            // Unity can disable a plugin component without destroying it. Return every exact
            // Stackmaster lease before normal gameplay can continue without our Update loop.
            RuntimeContext.Disable("plugin disabled");
        }

        private void OnDestroy()
        {
            RuntimeContext.Disable("plugin unloading");
            try
            {
                if (OwnershipCoordinator.HasUnresolvedCleanup || NearbyResourceService.HasPendingReservationReleases)
                {
                    // Preserve delayed ownership and reservation cleanup under a session-lifetime Harmony id:
                    // suppress generated responses, observe delayed owner updates, and retry any
                    // exact local in-use release. Ordinary gameplay patches are still removed below.
                    var safetyHarmony = new Harmony(PluginGuid + ".ownership-cleanup-safety");
                    PatchInstaller.Install(safetyHarmony, PatchInstaller.PrepareCleanupSafety());
                    Logger.LogWarning("Preserved delayed ownership cleanup until session end.");
                }
                _harmony?.UnpatchSelf();
            }
            catch (System.Exception exception)
            {
                Logger.LogError("Stackmaster patch cleanup failed during unload; entrypoint guards remain disabled: " + exception);
            }
            finally
            {
                _harmony = null;
                InventoryIntegration.Shutdown();
                RuntimeContext.Shutdown();
                Instance = null;
            }
            Logger.LogInfo($"{PluginName} {PluginVersion} unloaded cleanly.");
        }
    }
}
