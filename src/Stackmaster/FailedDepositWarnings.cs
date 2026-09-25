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
        internal FailedDepositCandidate(
            ItemDrop.ItemData item,
            ItemStackSnapshot snapshot,
            int reservationRetainedQuantity)
        {
            Item = item;
            Snapshot = snapshot;
            ReservationRetainedQuantity = Math.Max(0, reservationRetainedQuantity);
        }

        internal ItemDrop.ItemData Item { get; }
        internal ItemStackSnapshot Snapshot { get; }
        internal int ReservationRetainedQuantity { get; }
    }

    /// <summary>
    /// Ephemeral UI-only state keyed by exact ItemData object identity. It never touches item
    /// custom data, so warnings cannot change stacking, serialization, or network semantics.
    /// </summary>
    internal static class FailedDepositWarnings
    {
        private static readonly MethodInfo GetHoveredElementMethod = AccessTools.Method(typeof(InventoryGrid), "GetHoveredElement", Type.EmptyTypes);
        private static Dictionary<ItemDrop.ItemData, int> Warnings =
            new Dictionary<ItemDrop.ItemData, int>(ReferenceComparer<ItemDrop.ItemData>.Instance);
        private static int _playerSortDepth;

        internal static IReadOnlyList<FailedDepositCandidate> CaptureCandidates(
            Player player,
            InventorySnapshot snapshot,
            IEnumerable<ReservationSlotAllocation> reservationAllocations = null)
        {
            var result = new List<FailedDepositCandidate>();
            var inventory = player?.GetInventory();
            if (inventory == null || snapshot == null) return result;
            var retainedBySlot = (reservationAllocations ?? Array.Empty<ReservationSlotAllocation>())
                .GroupBy(item => item.PlayerSlot)
                .ToDictionary(group => group.Key, group => group.Sum(item => item.Quantity));
            foreach (var itemSnapshot in snapshot.Items)
            {
                int reservationRetained;
                retainedBySlot.TryGetValue(itemSnapshot.Slot, out reservationRetained);
                var attempted = Math.Max(
                    0,
                    FailedDepositPolicy.AttemptedQuantity(itemSnapshot) - reservationRetained);
                if (attempted == 0) continue;
                var position = InventorySnapshots.PositionForSlot(inventory, itemSnapshot.Slot);
                var item = inventory.GetItemAt(position.x, position.y);
                if (item != null)
                {
                    result.Add(new FailedDepositCandidate(item, itemSnapshot, reservationRetained));
                }
            }
            return result;
        }

        internal static void Replace(Player player, IEnumerable<FailedDepositCandidate> candidates)
        {
            var replacement = new Dictionary<ItemDrop.ItemData, int>(ReferenceComparer<ItemDrop.ItemData>.Instance);
            var inventory = player?.GetInventory();
            if (inventory != null && candidates != null)
            {
                foreach (var candidate in candidates)
                {
                    if (candidate?.Item == null || !inventory.ContainsItem(candidate.Item)) continue;
                    var attempted = Math.Max(
                        0,
                        FailedDepositPolicy.AttemptedQuantity(candidate.Snapshot) -
                        candidate.ReservationRetainedQuantity);
                    var explicitlyRetained = candidate.Snapshot.IsProtected &&
                                             candidate.Snapshot.ReplenishmentTarget.HasValue
                        ? candidate.Snapshot.ReplenishmentTarget.Value
                        : 0;
                    var failed = Math.Min(
                        attempted,
                        Math.Max(
                            0,
                            candidate.Item.m_stack - explicitlyRetained -
                            candidate.ReservationRetainedQuantity));
                    if (failed > 0)
                    {
                        replacement[candidate.Item] = failed;
                    }
                }
            }
            Warnings = replacement;
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
            IEnumerable<ItemDrop.ItemData> survivingItems,
            IEnumerable<SortPlacement> placements)
        {
            if (originalItems == null || survivingItems == null || placements == null) return;
            var originals = originalItems.Where(item => item != null).ToArray();
            var survivors = survivingItems.Where(item => item != null).ToArray();
            var warningsByItemKey = originals
                .Where(Warnings.ContainsKey)
                .GroupBy(InventorySnapshots.PersistentItemKey, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Sum(item => Warnings[item]), StringComparer.Ordinal);
            foreach (var original in originals)
            {
                Warnings.Remove(original);
            }

            var width = Player.m_localPlayer?.GetInventory()?.GetWidth() ?? 0;
            var reservedBySlot = placements.ToDictionary(
                placement => placement.Slot,
                placement => placement.ReservationQuantity);
            foreach (var group in survivors
                .GroupBy(InventorySnapshots.PersistentItemKey, StringComparer.Ordinal))
            {
                int remainingWarning;
                if (!warningsByItemKey.TryGetValue(group.Key, out remainingWarning) || remainingWarning <= 0)
                {
                    continue;
                }
                foreach (var survivor in group
                    .OrderBy(item => item.m_gridPos.y)
                    .ThenBy(item => item.m_gridPos.x))
                {
                    var slot = width > 0 ? itemSlot(survivor, width) : -1;
                    int reservedQuantity;
                    reservedBySlot.TryGetValue(slot, out reservedQuantity);
                    var ordinaryCapacity = Math.Max(0, survivor.m_stack - reservedQuantity);
                    var assigned = Math.Min(remainingWarning, ordinaryCapacity);
                    if (assigned <= 0) continue;
                    Warnings[survivor] = assigned;
                    remainingWarning -= assigned;
                    if (remainingWarning == 0) break;
                }
            }
            RequestRefreshIfNeeded();
        }

        private static int itemSlot(ItemDrop.ItemData item, int width)
        {
            return checked(item.m_gridPos.y * width + item.m_gridPos.x);
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
