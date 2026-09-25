using System;
using System.Collections.Generic;
using System.Linq;

namespace Stackmaster.Core
{
    public sealed class InventorySortPlanner
    {
        public SortPlan Plan(InventorySnapshot inventory)
            => Plan(inventory, Array.Empty<ReservationSlotAllocation>());

        /// <summary>
        /// Sorts an inventory while carrying expedition-reservation attribution through the move.
        /// Within each compatibility group ordinary movable units fill first and reservation-served
        /// units occupy the tail of the group's final sortable placements. This preserves the
        /// minimum legal stack count while giving the runtime exact orange-highlight metadata.
        /// </summary>
        public SortPlan Plan(
            InventorySnapshot inventory,
            IEnumerable<ReservationSlotAllocation> reservationAllocations)
        {
            if (inventory == null) throw new ArgumentNullException(nameof(inventory));
            if (reservationAllocations == null) throw new ArgumentNullException(nameof(reservationAllocations));

            var reservationBySlot = NormalizeAllocations(inventory, reservationAllocations);
            var fixedItems = inventory.Items.Where(item => item.IsFixed).OrderBy(item => item.Slot).ToList();
            var occupiedFixedSlots = new HashSet<int>(inventory.ReservedSlots);
            occupiedFixedSlots.UnionWith(fixedItems.Select(item => item.Slot));
            var freeSlots = Enumerable.Range(0, inventory.Capacity).Where(slot => !occupiedFixedSlots.Contains(slot)).ToList();
            var placements = new List<SortPlacement>();

            foreach (var item in fixedItems)
            {
                placements.Add(new SortPlacement(
                    item.Slot,
                    item.CompatibilityKey,
                    item.VisibleName,
                    item.Quantity,
                    item.MaxStack,
                    true,
                    new[] { item.StackId },
                    ReservationQuantity(reservationBySlot, item.Slot)));
            }

            var groups = inventory.Items
                .Where(item => !item.IsFixed)
                .GroupBy(item => item.CompatibilityKey, StringComparer.Ordinal)
                .Select(group => new SortGroup(
                    group.OrderBy(item => item.Slot).ToList(),
                    group.Sum(item => ReservationQuantity(reservationBySlot, item.Slot))))
                .OrderBy(group => group.VisibleName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(group => group.VisibleName, StringComparer.Ordinal)
                .ThenBy(group => group.CompatibilityKey, StringComparer.Ordinal)
                .ThenBy(group => group.FirstSlot)
                .ToList();

            var ordinaryPlacements = new List<UnslottedPlacement>();
            var reservationPlacements = new List<UnslottedPlacement>();
            foreach (var group in groups)
            {
                var groupPlacements = new List<UnslottedPlacement>();
                var remaining = group.TotalQuantity;
                while (remaining > 0)
                {
                    var quantity = Math.Min(group.MaxStack, remaining);
                    groupPlacements.Add(new UnslottedPlacement(group, quantity));
                    remaining -= quantity;
                }

                // Attribute reservation units from the last stack backwards. A boundary stack may
                // contain ordinary and reserved units together so sorting never increases the
                // minimum legal stack count. Every stack with any reserved units is then placed
                // after every ordinary-only stack across the whole sortable inventory.
                var reservationRemaining = group.ReservationQuantity;
                for (var index = groupPlacements.Count - 1; index >= 0 && reservationRemaining > 0; index--)
                {
                    var draft = groupPlacements[index];
                    draft.ReservationQuantity = Math.Min(draft.Quantity, reservationRemaining);
                    reservationRemaining -= draft.ReservationQuantity;
                }
                if (reservationRemaining != 0)
                    throw new InvalidOperationException("Reservation attribution exceeds its sortable item quantity.");

                ordinaryPlacements.AddRange(groupPlacements.Where(item => item.ReservationQuantity == 0));
                reservationPlacements.AddRange(groupPlacements.Where(item => item.ReservationQuantity > 0));
            }

            var freeSlotIndex = 0;
            foreach (var draft in ordinaryPlacements.Concat(reservationPlacements))
            {
                if (freeSlotIndex >= freeSlots.Count)
                    throw new InvalidOperationException("The sorted inventory would require more slots than the source inventory.");
                placements.Add(new SortPlacement(
                    freeSlots[freeSlotIndex++],
                    draft.Group.CompatibilityKey,
                    draft.Group.VisibleName,
                    draft.Quantity,
                    draft.Group.MaxStack,
                    false,
                    draft.Group.SourceStackIds,
                    draft.ReservationQuantity));
            }

            return new SortPlan(inventory.InventoryId, placements.OrderBy(placement => placement.Slot));
        }

        private static Dictionary<int, int> NormalizeAllocations(
            InventorySnapshot inventory,
            IEnumerable<ReservationSlotAllocation> allocations)
        {
            var itemsBySlot = inventory.Items.ToDictionary(item => item.Slot);
            var result = new Dictionary<int, int>();
            foreach (var allocation in allocations)
            {
                if (allocation == null) throw new ArgumentException("Reservation allocations cannot contain null.", nameof(allocations));
                if (!itemsBySlot.TryGetValue(allocation.PlayerSlot, out var item))
                    throw new ArgumentException("A reservation allocation targets an empty player slot.", nameof(allocations));
                if (item.IsFixed)
                    throw new ArgumentException("A reservation allocation cannot target a fixed player stack.", nameof(allocations));
                if (!string.Equals(item.CompatibilityKey, allocation.CompatibilityKey, StringComparison.Ordinal) ||
                    !string.Equals(item.ResourceItemName, allocation.ItemName, StringComparison.Ordinal) ||
                    item.ResourceQuality != allocation.Quality)
                    throw new ArgumentException("A reservation allocation does not match its player stack.", nameof(allocations));
                var current = result.TryGetValue(allocation.PlayerSlot, out var quantity) ? quantity : 0;
                var total = checked(current + allocation.Quantity);
                if (total > item.Quantity)
                    throw new ArgumentException("A reservation allocation exceeds its player stack quantity.", nameof(allocations));
                result[allocation.PlayerSlot] = total;
            }
            return result;
        }

        private static int ReservationQuantity(IReadOnlyDictionary<int, int> allocations, int slot)
            => allocations.TryGetValue(slot, out var quantity) ? quantity : 0;

        private sealed class UnslottedPlacement
        {
            public UnslottedPlacement(SortGroup group, int quantity)
            {
                Group = group;
                Quantity = quantity;
            }

            public SortGroup Group { get; }
            public int Quantity { get; }
            public int ReservationQuantity { get; set; }
        }

        private sealed class SortGroup
        {
            public SortGroup(IList<ItemStackSnapshot> items, int reservationQuantity)
            {
                if (items.Count == 0) throw new ArgumentException("A sort group cannot be empty.", nameof(items));
                var maxStack = items[0].MaxStack;
                var visibleName = items[0].VisibleName;
                foreach (var item in items)
                {
                    if (item.MaxStack != maxStack)
                        throw new InvalidOperationException("Stack-compatible snapshots must have the same maximum stack size.");
                }

                CompatibilityKey = items[0].CompatibilityKey;
                VisibleName = visibleName;
                MaxStack = maxStack;
                FirstSlot = items[0].Slot;
                SourceStackIds = items.Select(item => item.StackId).ToArray();
                TotalQuantity = items.Sum(item => item.Quantity);
                if (reservationQuantity < 0 || reservationQuantity > TotalQuantity)
                    throw new ArgumentOutOfRangeException(nameof(reservationQuantity));
                ReservationQuantity = reservationQuantity;
            }

            public string CompatibilityKey { get; }
            public string VisibleName { get; }
            public int MaxStack { get; }
            public int FirstSlot { get; }
            public string[] SourceStackIds { get; }
            public int TotalQuantity { get; }
            public int ReservationQuantity { get; }
        }
    }
}
