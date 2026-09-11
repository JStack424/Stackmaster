using System;
using System.Collections.Generic;
using System.Linq;

namespace Stackmaster.Core
{
    public sealed class InventorySortPlanner
    {
        public SortPlan Plan(InventorySnapshot inventory)
        {
            if (inventory == null) throw new ArgumentNullException(nameof(inventory));

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
                    new[] { item.StackId }));
            }

            var groups = inventory.Items
                .Where(item => !item.IsFixed)
                .GroupBy(item => item.CompatibilityKey, StringComparer.Ordinal)
                .Select(group => new SortGroup(group.OrderBy(item => item.Slot).ToList()))
                .OrderBy(group => group.VisibleName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(group => group.VisibleName, StringComparer.Ordinal)
                .ThenBy(group => group.CompatibilityKey, StringComparer.Ordinal)
                .ThenBy(group => group.FirstSlot)
                .ToList();

            var freeSlotIndex = 0;
            foreach (var group in groups)
            {
                var remaining = group.TotalQuantity;
                while (remaining > 0)
                {
                    if (freeSlotIndex >= freeSlots.Count)
                        throw new InvalidOperationException("The sorted inventory would require more slots than the source inventory.");
                    var quantity = Math.Min(group.MaxStack, remaining);
                    placements.Add(new SortPlacement(
                        freeSlots[freeSlotIndex++],
                        group.CompatibilityKey,
                        group.VisibleName,
                        quantity,
                        group.MaxStack,
                        false,
                        group.SourceStackIds));
                    remaining -= quantity;
                }
            }

            return new SortPlan(inventory.InventoryId, placements.OrderBy(placement => placement.Slot));
        }

        private sealed class SortGroup
        {
            public SortGroup(IList<ItemStackSnapshot> items)
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
            }

            public string CompatibilityKey { get; }
            public string VisibleName { get; }
            public int MaxStack { get; }
            public int FirstSlot { get; }
            public string[] SourceStackIds { get; }
            public int TotalQuantity { get; }
        }
    }
}
