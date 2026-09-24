#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Stackmaster.Core;

namespace Stackmaster
{
    internal sealed class FailedDepositCandidate
    {
        internal FailedDepositCandidate(ItemDrop.ItemData item, ItemStackSnapshot snapshot)
        {
            Item = item;
            Snapshot = snapshot;
        }

        internal ItemDrop.ItemData Item { get; }
        internal ItemStackSnapshot Snapshot { get; }
    }

    /// <summary>
    /// Ephemeral UI-only state keyed by exact ItemData object identity. It never touches item
    /// custom data, so warnings cannot change stacking, serialization, or network semantics.
    /// </summary>
    internal static class FailedDepositWarnings
    {
        private static readonly MethodInfo GetHoveredElementMethod = AccessTools.Method(typeof(InventoryGrid), "GetHoveredElement", Type.EmptyTypes);
        private static readonly Dictionary<ItemDrop.ItemData, int> Warnings =
            new Dictionary<ItemDrop.ItemData, int>(ReferenceComparer<ItemDrop.ItemData>.Instance);
        private static int _playerSortDepth;

        internal static IReadOnlyList<FailedDepositCandidate> CaptureCandidates(Player player, InventorySnapshot snapshot)
        {
            var result = new List<FailedDepositCandidate>();
            var inventory = player?.GetInventory();
            if (inventory == null || snapshot == null) return result;
            foreach (var itemSnapshot in snapshot.Items)
            {
                if (FailedDepositPolicy.AttemptedQuantity(itemSnapshot) == 0) continue;
                var position = InventorySnapshots.PositionForSlot(inventory, itemSnapshot.Slot);
                var item = inventory.GetItemAt(position.x, position.y);
                if (item != null)
                {
                    result.Add(new FailedDepositCandidate(item, itemSnapshot));
                }
            }
            return result;
        }

        internal static void Replace(Player player, IEnumerable<FailedDepositCandidate> candidates)
        {
            Warnings.Clear();
            var inventory = player?.GetInventory();
            if (inventory != null && candidates != null)
            {
                foreach (var candidate in candidates)
                {
                    if (candidate?.Item == null || !inventory.ContainsItem(candidate.Item)) continue;
                    var failed = FailedDepositPolicy.FailedRemainder(candidate.Snapshot, candidate.Item.m_stack);
                    if (failed > 0)
                    {
                        Warnings[candidate.Item] = failed;
                    }
                }
            }
            RequestRefreshIfNeeded();
        }

        internal static bool TryGet(ItemDrop.ItemData item, out int failedQuantity)
        {
            failedQuantity = 0;
            if (item == null || !Warnings.TryGetValue(item, out failedQuantity)) return false;
            var inventory = Player.m_localPlayer?.GetInventory();
            if (inventory == null || !inventory.ContainsItem(item))
            {
                Warnings.Remove(item);
                failedQuantity = 0;
                return false;
            }
            failedQuantity = Math.Min(failedQuantity, item.m_stack);
            if (failedQuantity <= 0)
            {
                Warnings.Remove(item);
                return false;
            }
            return true;
        }

        internal static void BeginPlayerSort()
        {
            _playerSortDepth++;
        }

        internal static void EndPlayerSort()
        {
            _playerSortDepth = Math.Max(0, _playerSortDepth - 1);
        }

        internal static void ReconcileAfterSuccessfulSort(
            IEnumerable<ItemDrop.ItemData> originalItems,
            IEnumerable<ItemDrop.ItemData> survivingItems)
        {
            if (originalItems == null || survivingItems == null) return;
            var survivors = survivingItems.Where(item => item != null).ToArray();
            var survivorSet = new HashSet<ItemDrop.ItemData>(survivors, ReferenceComparer<ItemDrop.ItemData>.Instance);
            foreach (var removed in originalItems
                .Where(item => item != null && !survivorSet.Contains(item) && Warnings.ContainsKey(item))
                .ToArray())
            {
                var remainingWarning = Warnings[removed];
                Warnings.Remove(removed);
                var itemKey = InventorySnapshots.PersistentItemKey(removed);
                foreach (var survivor in survivors
                    .Where(item => string.Equals(InventorySnapshots.PersistentItemKey(item), itemKey, StringComparison.Ordinal))
                    .OrderBy(item => item.m_gridPos.y)
                    .ThenBy(item => item.m_gridPos.x))
                {
                    int existing;
                    Warnings.TryGetValue(survivor, out existing);
                    var assigned = Math.Min(remainingWarning, Math.Max(0, survivor.m_stack - existing));
                    if (assigned <= 0) continue;
                    Warnings[survivor] = existing + assigned;
                    remainingWarning -= assigned;
                    if (remainingWarning == 0) break;
                }
            }
            RequestRefreshIfNeeded();
        }

        internal static void Reconcile()
        {
            // Vanilla raises Inventory.m_onChanged during each consolidation move. Keep warnings
            // attached to their original objects until SortExecutor can atomically map every
            // consumed donor to the final surviving stack after the complete sort succeeds.
            if (_playerSortDepth > 0) return;
            var inventory = Player.m_localPlayer?.GetInventory();
            foreach (var item in Warnings.Keys
                .Where(item => item == null || inventory == null || !inventory.ContainsItem(item) || item.m_stack <= 0)
                .ToArray())
            {
                Warnings.Remove(item);
            }
        }

        internal static void AcknowledgeHover(InventoryGrid grid)
        {
            var player = Player.m_localPlayer;
            var gui = InventoryGui.instance;
            if (grid == null || player == null || gui == null ||
                !ReferenceEquals(grid, gui.m_playerGrid) ||
                !ReferenceEquals(grid.GetInventory(), player.GetInventory()) ||
                GetHoveredElementMethod == null)
            {
                return;
            }

            var element = GetHoveredElementMethod.Invoke(grid, null) as InventoryElement;
            if (element == null) return;
            var item = player.GetInventory().GetItemAt(element.Position.x, element.Position.y);
            if (item != null && Warnings.Remove(item))
            {
                InventoryIntegration.HideFailedDepositOverlay(element);
            }
        }

        private static void RequestRefreshIfNeeded()
        {
            InventoryIntegration.RequestProtectionOverlayRefresh();
        }

        internal static void Clear()
        {
            if (Warnings.Count == 0) return;
            Warnings.Clear();
            InventoryIntegration.RequestProtectionOverlayRefresh();
        }

        private sealed class ReferenceComparer<T> : IEqualityComparer<T> where T : class
        {
            internal static readonly ReferenceComparer<T> Instance = new ReferenceComparer<T>();
            public bool Equals(T x, T y) => ReferenceEquals(x, y);
            public int GetHashCode(T obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }
    }

    internal static class InventoryGridPointerEnterPatch
    {
        private static void Postfix(InventoryGrid __instance)
        {
            if (!RuntimeContext.Compatibility.IsCompatible) return;
            try
            {
                FailedDepositWarnings.AcknowledgeHover(__instance);
            }
            catch (Exception exception)
            {
                RuntimeContext.Plugin?.Log.LogWarning("Failed-deposit hover acknowledgment was skipped safely: " + exception);
            }
        }
    }
}
