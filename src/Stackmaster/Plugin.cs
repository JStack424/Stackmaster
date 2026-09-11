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
                "While targeting an eligible container: deposit matching items and replenish protected slot targets.");

            var compatibility = CompatibilityGate.Evaluate();
            RuntimeContext.Initialize(this, compatibility);
            Logger.LogInfo($"{PluginName} {PluginVersion} loading for game {Application.version}, Unity {Application.unityVersion}.");

            if (!compatibility.IsCompatible)
            {
                Logger.LogError("Compatibility gate failed. Stackmaster is fully disabled before any inventory hooks were installed: " + compatibility.Reason);
                return;
            }

            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll(typeof(Plugin).Assembly);
            Logger.LogInfo("Compatibility gate passed for Steam build 25253764 reference surface; gameplay hooks enabled.");
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
                    "Stackmaster disabled: this game build is not safely supported.", 0, null);
            }
        }

        private void OnDestroy()
        {
            if (_harmony != null)
            {
                _harmony.UnpatchSelf();
            }

            RuntimeContext.Shutdown();
            Instance = null;
            Logger.LogInfo($"{PluginName} {PluginVersion} unloaded cleanly.");
        }
    }
}
