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
        internal BepInEx.Logging.ManualLogSource Log => Logger;

        private void Awake()
        {
            Instance = this;
            AutoSortEnabled = Config.Bind("General", "Auto-sort enabled", true,
                "Sort movable player and opened-container inventory slots once when the inventory opens.");
            NearbyStorageRadius = Config.Bind("General", "Nearby-storage radius", 20f,
                new ConfigDescription("Player-centered vanilla-container search radius in metres.",
                    new AcceptableValueRange<float>(1f, 50f)));
            StorageActionShortcut = Config.Bind("General", "Storage-action keybind",
                new KeyboardShortcut(KeyCode.E, KeyCode.LeftAlt),
                "While targeting or viewing an eligible container: deposit matching items and replenish protected stack targets.");
            BuildingFromNearbyChestsEnabled = Config.Bind("General", "Enable building from nearby chests", true,
                "Count and consume exact building costs from eligible nearby vanilla chests within the nearby-storage radius.");
            CraftingFromNearbyChestsEnabled = Config.Bind("General", "Enable crafting from nearby chests", true,
                "Count and consume exact crafting costs from eligible nearby vanilla chests within the nearby-storage radius.");

            var compatibility = CompatibilityGate.Evaluate();
            RuntimeContext.Initialize(this, compatibility);
            Logger.LogInfo($"{PluginName} {PluginVersion} loading for game {Application.version}, Unity {Application.unityVersion}.");

            if (!compatibility.IsCompatible)
            {
                Logger.LogError("Compatibility gate failed. Stackmaster is fully disabled before any inventory hooks were installed: " + compatibility.Reason);
                return;
            }

            try
            {
                _harmony = new Harmony(PluginGuid);
                PatchInstaller.Install(_harmony);
                Logger.LogInfo("Compatibility gate passed for Steam build 25253764 reference surface; gameplay hooks enabled.");
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

            if (!_compatibilityWarningShown && Player.m_localPlayer != null && MessageHud.instance != null)
            {
                _compatibilityWarningShown = true;
                MessageHud.instance.ShowMessage(MessageHud.MessageType.Center,
                    "Stackmaster disabled for safety. Restart Valheim before using it again.", 0, null);
            }
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
                if (OwnershipCoordinator.HasUnresolvedCleanup)
                {
                    // Preserve both halves of late cleanup under a session-lifetime Harmony id:
                    // suppress the generated response and observe its delayed ZDO owner update.
                    // Ordinary gameplay patches are still removed below.
                    var safetyHarmony = new Harmony(PluginGuid + ".ownership-cleanup-safety");
                    var responseTarget = AccessTools.DeclaredMethod(typeof(Container), "RPC_StackResponse",
                        new[] { typeof(long), typeof(bool) });
                    var responsePrefix = AccessTools.DeclaredMethod(typeof(ContainerStackResponsePatch), "Prefix");
                    var updateTarget = AccessTools.DeclaredMethod(typeof(ZNet), "Update", System.Type.EmptyTypes);
                    var updatePostfix = AccessTools.DeclaredMethod(typeof(OwnershipSafetyUpdatePatch), "Postfix");
                    if (responseTarget == null || responsePrefix == null || updateTarget == null || updatePostfix == null)
                    {
                        throw new System.MissingMethodException("Could not preserve delayed ownership cleanup.");
                    }
                    safetyHarmony.Patch(responseTarget, prefix: new HarmonyMethod(responsePrefix));
                    safetyHarmony.Patch(updateTarget, postfix: new HarmonyMethod(updatePostfix));
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
