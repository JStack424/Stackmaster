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
            // Roll back and clear reservations before handing any exact acquired ZDO back to
            // vanilla. Neither cleanup failure may escape a shutdown Harmony prefix or prevent
            // the other cleanup stage from running.
            try
            {
                ResourceTransactionContext.Shutdown();
            }
            catch (Exception exception)
            {
                Plugin?.Log.LogError("Resource transaction shutdown failed safely: " + exception);
            }

            try
            {
                OwnershipCoordinator.Shutdown(reason);
            }
            catch (Exception exception)
            {
                Plugin?.Log.LogError("Ownership shutdown failed safely: " + exception);
            }
            finally
            {
                Compatibility = new CompatibilityResult(false, reason);
            }
        }

        internal static void Shutdown()
        {
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
