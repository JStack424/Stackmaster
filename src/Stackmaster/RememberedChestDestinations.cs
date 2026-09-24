#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Stackmaster.Core;
using UnityEngine;

namespace Stackmaster
{
    /// <summary>
    /// Local, per-character/per-world routing hints learned only from an explicitly opened or
    /// targeted chest. A hint never authorizes a transfer; normal discovery, ownership, access,
    /// scope, capacity, stack-compatibility, and exact execution checks still decide every move.
    /// </summary>
    internal static class RememberedChestDestinations
    {
        private const string PreferencePrefix = "com.jstack424.stackmaster/remembered-destinations/v1/";
        private static readonly FieldInfo NetworkViewField = AccessTools.Field(typeof(Container), "m_nview");
        private static RememberedDestinationState _state = new RememberedDestinationState();
        private static string _loadedKey;
        private static bool _storageHealthy = true;
        private static bool _failureLogged;

        internal static void Initialize()
        {
            _state = new RememberedDestinationState();
            _loadedKey = null;
            _storageHealthy = true;
            _failureLogged = false;
        }

        internal static void Shutdown()
        {
            _state = new RememberedDestinationState();
            _loadedKey = null;
            _storageHealthy = false;
        }

        internal static void ObserveDirect(Container container)
        {
            try
            {
                ObserveDirectCore(container);
            }
            catch (Exception exception)
            {
                LogFailureOnce("A Quick Stack destination observation was skipped safely: " + exception.GetType().Name);
            }
        }

        private static void ObserveDirectCore(Container container)
        {
            var player = Player.m_localPlayer;
            if (!_storageHealthy || player == null || container == null || container.GetType() != typeof(Container) ||
                NetworkViewField == null || !EnsureLoaded(player) || !ContainerDiscovery.CheckAccess(player, container))
            {
                return;
            }

            var view = NetworkViewField.GetValue(container) as ZNetView;
            var zdo = view != null && view.IsValid() && view.IsOwner() && container.IsOwner()
                ? view.GetZDO()
                : null;
            var inventory = zdo != null && !zdo.m_uid.IsNone() ? container.GetInventory() : null;
            if (inventory == null)
            {
                return;
            }

            var changed = false;
            var now = DateTime.UtcNow.Ticks;
            foreach (var item in inventory.GetAllItems())
            {
                changed |= _state.Remember(InventorySnapshots.PersistentItemKey(item), zdo.m_uid.ToString(), now);
            }
            changed |= _state.Prune(now) > 0;
            if (changed)
            {
                Save();
            }
        }

        internal static IReadOnlyDictionary<string, string> ResolveFallbacks(
            Player player,
            InventorySnapshot playerSnapshot,
            DiscoveryResult discovery)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!_storageHealthy || player == null || playerSnapshot == null || discovery == null || !EnsureLoaded(player))
            {
                return result;
            }

            var now = DateTime.UtcNow.Ticks;
            if (_state.Prune(now) > 0)
            {
                Save();
            }

            // Only an exact currently discovered eligible ZDO-backed container can become a
            // planner fallback. Missing/unloaded/out-of-scope/busy/inaccessible hints stay inert.
            var eligibleIds = new HashSet<string>(discovery.Containers
                .Where(handle => handle != null && handle.Snapshot.IsEligible &&
                                 handle.NetworkView != null && handle.NetworkView.IsValid() &&
                                 handle.NetworkView.GetZDO() != null &&
                                 string.Equals(handle.NetworkView.GetZDO().m_uid.ToString(), handle.Id, StringComparison.Ordinal))
                .Select(handle => handle.Id), StringComparer.Ordinal);
            var inventory = player.GetInventory();
            foreach (var itemSnapshot in playerSnapshot.Items)
            {
                var position = InventorySnapshots.PositionForSlot(inventory, itemSnapshot.Slot);
                var item = inventory.GetItemAt(position.x, position.y);
                RememberedDestinationRecord record;
                if (item == null ||
                    !_state.TryGet(InventorySnapshots.PersistentItemKey(item), out record) ||
                    !eligibleIds.Contains(record.ContainerId))
                {
                    continue;
                }

                // Key by this exact player stack, not the broader stack-compatibility group.
                // That keeps two otherwise compatible variants from borrowing each other's hint.
                result[itemSnapshot.StackId] = record.ContainerId;
            }
            return result;
        }

        private static bool EnsureLoaded(Player player)
        {
            if (!_storageHealthy || player == null || ZNet.instance == null)
            {
                return false;
            }

            var key = PreferencePrefix + player.GetPlayerID().ToString(CultureInfo.InvariantCulture) + "/" +
                      ZNet.instance.GetWorldUID().ToString(CultureInfo.InvariantCulture);
            if (string.Equals(_loadedKey, key, StringComparison.Ordinal))
            {
                return true;
            }

            try
            {
                var raw = PlayerPrefs.GetString(key, string.Empty);
                RememberedDestinationState parsed;
                if (!RememberedDestinationState.TryParse(raw, out parsed))
                {
                    parsed = new RememberedDestinationState();
                    LogFailureOnce("Saved Quick Stack destination hints were malformed and were ignored safely.");
                }
                _state = parsed;
                _loadedKey = key;
                if (_state.Prune(DateTime.UtcNow.Ticks) > 0)
                {
                    Save();
                }
                return _storageHealthy;
            }
            catch (Exception exception)
            {
                DisableStorage("Quick Stack destination hints could not be read and were disabled for this session: " + exception.GetType().Name);
                return false;
            }
        }

        private static void Save()
        {
            if (!_storageHealthy || string.IsNullOrEmpty(_loadedKey))
            {
                return;
            }
            try
            {
                PlayerPrefs.SetString(_loadedKey, _state.Serialize());
                PlayerPrefs.Save();
            }
            catch (Exception exception)
            {
                DisableStorage("Quick Stack destination hints could not be saved and were disabled for this session: " + exception.GetType().Name);
            }
        }

        private static void DisableStorage(string message)
        {
            _storageHealthy = false;
            _state = new RememberedDestinationState();
            _loadedKey = null;
            LogFailureOnce(message);
        }

        private static void LogFailureOnce(string message)
        {
            if (_failureLogged) return;
            _failureLogged = true;
            RuntimeContext.Plugin?.Log.LogWarning(message);
        }
    }
}
