using BepInEx;
using UnityEngine;

namespace Stackmaster
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.jstack424.stackmaster";
        public const string PluginName = "Stackmaster";
        public const string PluginVersion = GeneratedBuildInfo.Version;

        private void Awake()
        {
            Logger.LogInfo($"{PluginName} {PluginVersion} loaded.");
            Logger.LogInfo($"Game version: {Application.version}; Unity version: {Application.unityVersion}.");
            Logger.LogWarning("Compatibility gate: blocked by design in the skeleton; all gameplay behavior is disabled.");
        }

        private void OnDestroy()
        {
            Logger.LogInfo($"{PluginName} {PluginVersion} unloaded cleanly.");
        }
    }
}
