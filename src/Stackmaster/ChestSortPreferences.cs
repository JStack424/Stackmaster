#nullable disable
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Stackmaster.Core;
using UnityEngine;

namespace Stackmaster
{
    internal static class ChestSortPreferences
    {
        private static readonly FieldInfo NetworkViewField = AccessTools.Field(typeof(Container), "m_nview");
        private static readonly HashSet<string> SessionDisabled = new HashSet<string>(StringComparer.Ordinal);
        private static bool _storageHealthy;
        private static bool _failureLogged;

        internal static void Initialize()
        {
            SessionDisabled.Clear();
            _storageHealthy = true;
            _failureLogged = false;
        }

        internal static bool TryGet(Container container, out string key, out bool enabled)
        {
            key = string.Empty;
            enabled = false;
            if (!TryCreateKey(container, out key))
            {
                LogFailureOnce("Opened chest has no stable local player/world/ZDO identity; chest auto-sort was skipped.");
                return false;
            }

            if (SessionDisabled.Contains(key))
            {
                return true;
            }
            if (!_storageHealthy)
            {
                return false;
            }

            try
            {
                var hasStoredValue = PlayerPrefs.HasKey(key);
                var storedValue = hasStoredValue ? PlayerPrefs.GetInt(key, -1) : 1;
                if (!ChestSortPreferencePolicy.TryInterpretStoredValue(hasStoredValue, storedValue, out enabled))
                {
                    DisableStorage("A saved chest auto-sort preference had an invalid value; chest auto-sort was disabled safely.");
                    return false;
                }
                return true;
            }
            catch (Exception exception)
            {
                DisableStorage("Local chest auto-sort preferences could not be read; chest auto-sort was disabled safely: " + exception.GetType().Name);
                return false;
            }
        }

        internal static bool TrySet(string key, bool enabled)
        {
            if (string.IsNullOrEmpty(key) || !_storageHealthy)
            {
                return false;
            }

            // The session memory is changed first so a failed local write can never re-enable
            // sorting for a chest the player just protected from automatic reordering.
            if (enabled)
            {
                SessionDisabled.Remove(key);
            }
            else
            {
                SessionDisabled.Add(key);
            }

            try
            {
                if (enabled)
                {
                    PlayerPrefs.DeleteKey(key);
                }
                else
                {
                    PlayerPrefs.SetInt(key, 0);
                }
                PlayerPrefs.Save();
                return true;
            }
            catch (Exception exception)
            {
                SessionDisabled.Add(key);
                DisableStorage("Local chest auto-sort preferences could not be saved; chest auto-sort was disabled safely: " + exception.GetType().Name);
                return false;
            }
        }

        internal static void Shutdown()
        {
            SessionDisabled.Clear();
            _storageHealthy = false;
        }

        private static bool TryCreateKey(Container container, out string key)
        {
            key = string.Empty;
            if (container == null || container.GetType() != typeof(Container) ||
                Player.m_localPlayer == null || ZNet.instance == null || NetworkViewField == null)
            {
                return false;
            }

            var view = NetworkViewField.GetValue(container) as ZNetView;
            if (view == null || !view.IsValid())
            {
                return false;
            }
            var zdo = view.GetZDO();
            if (zdo == null || zdo.m_uid.IsNone())
            {
                return false;
            }

            return ChestSortPreferencePolicy.TryCreateKey(
                Player.m_localPlayer.GetPlayerID(),
                ZNet.instance.GetWorldUID(),
                zdo.m_uid.ToString(),
                out key);
        }

        private static void DisableStorage(string message)
        {
            _storageHealthy = false;
            LogFailureOnce(message);
        }

        private static void LogFailureOnce(string message)
        {
            if (_failureLogged)
            {
                return;
            }
            _failureLogged = true;
            RuntimeContext.Plugin?.Log.LogWarning(message);
        }
    }
}
