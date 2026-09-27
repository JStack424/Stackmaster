#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using Stackmaster.Core;

namespace Stackmaster
{
    /// <summary>
    /// Assigns aggregate expedition requirements to concrete movable player stacks. Fixed
    /// quick-bar, equipped, and explicitly protected quantities remain personal and do not
    /// silently satisfy the separate additive expedition target.
    /// </summary>
    internal static class ExpeditionMaterialVisuals
    {
        internal static bool TryAllocate(
            Player player,
            ProtectionState protection,
            out IReadOnlyDictionary<ItemDrop.ItemData, int> allocations)
        {
            allocations = new Dictionary<ItemDrop.ItemData, int>();
            try
            {
                if (player == null || protection == null)
                {
                    return false;
                }

                var inventory = player.GetInventory();
                var snapshot = InventorySnapshots.CapturePlayer(
                    player,
                    protection,
                    new CompatibilityCatalog());
                IReadOnlyList<ReservationSlotAllocation> slotAllocations;
                if (!TryAllocateSlots(player, snapshot, out slotAllocations))
                {
                    return false;
                }

                var result = new Dictionary<ItemDrop.ItemData, int>();
                foreach (var allocation in slotAllocations)
                {
                    var position = InventorySnapshots.PositionForSlot(inventory, allocation.PlayerSlot);
                    var item = inventory.GetItemAt(position.x, position.y);
                    if (item != null)
                    {
                        result[item] = allocation.Quantity;
                    }
                }
                allocations = result;
                return true;
            }
            catch (Exception exception)
            {
                RuntimeContext.Plugin?.Log.LogWarning(
                    "Expedition material highlights were skipped safely: " + exception.GetType().Name);
                allocations = new Dictionary<ItemDrop.ItemData, int>();
                return false;
            }
        }

        internal static bool TryAllocateSlots(
            Player player,
            InventorySnapshot snapshot,
            out IReadOnlyList<ReservationSlotAllocation> allocations)
        {
            allocations = Array.Empty<ReservationSlotAllocation>();
            IReadOnlyList<ResourceRequirement> requirements;
            if (player == null || snapshot == null ||
                !ExpeditionReservations.TryGetAggregateRequirements(player, out requirements))
            {
                return false;
            }

            var result = new List<ReservationSlotAllocation>();
            var remainingBySlot = snapshot.Items.ToDictionary(item => item.Slot, item => item.Quantity);
            foreach (var requirement in requirements
                .OrderBy(item => item.ItemName, StringComparer.Ordinal)
                .ThenBy(item => item.Quality))
            {
                var remaining = requirement.Quantity;
                foreach (var item in snapshot.Items
                    .Where(item => !item.IsFixed &&
                                   string.Equals(item.ResourceItemName, requirement.ItemName, StringComparison.Ordinal) &&
                                   (requirement.Quality < 0 || item.ResourceQuality == requirement.Quality))
                    // Attribute from the front so the same minimum number of stacks the sorter
                    // places first receive the exact orange quantity before and after sorting.
                    .OrderBy(item => item.Slot))
                {
                    if (remaining == 0) break;
                    var available = remainingBySlot[item.Slot];
                    var assigned = Math.Min(available, remaining);
                    if (assigned <= 0) continue;
                    result.Add(new ReservationSlotAllocation(
                        item.Slot,
                        item.CompatibilityKey,
                        item.ResourceItemName,
                        item.ResourceQuality,
                        assigned));
                    remainingBySlot[item.Slot] -= assigned;
                    remaining -= assigned;
                }
            }

            allocations = result;
            return true;
        }
    }
}
