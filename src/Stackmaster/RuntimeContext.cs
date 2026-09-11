#nullable disable
using System;
using Stackmaster.Core;

namespace Stackmaster
{
    internal static class RuntimeContext
    {
        internal const string CharacterDataKey = "com.jstack424.stackmaster/protected-slots";

        internal static Plugin Plugin { get; private set; }
        internal static CompatibilityResult Compatibility { get; private set; } = new CompatibilityResult(false, "not initialized");

        internal static void Initialize(Plugin plugin, CompatibilityResult compatibility)
        {
            Plugin = plugin;
            Compatibility = compatibility;
        }

        internal static void Disable(string reason)
        {
            Compatibility = new CompatibilityResult(false, reason);
        }

        internal static void Shutdown()
        {
            Plugin = null;
            Compatibility = new CompatibilityResult(false, "shut down");
        }

        internal static ProtectionState LoadProtection(Player player)
        {
            if (player == null || player.m_customData == null)
            {
                return new ProtectionState();
            }

            string raw;
            return player.m_customData.TryGetValue(CharacterDataKey, out raw)
                ? ProtectionState.Parse(raw)
                : new ProtectionState();
        }

        internal static void SaveProtection(Player player, ProtectionState state)
        {
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
