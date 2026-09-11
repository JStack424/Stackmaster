#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Stackmaster.Core;

namespace Stackmaster
{
    internal static class SortExecutor
    {
        private static readonly FieldInfo InventoryItemsField = AccessTools.Field(typeof(Inventory), "m_inventory");

        internal static bool Sort(Inventory inventory, bool isPlayer, Player player, ProtectionState protection, out string failure)
        {
            failure = null;
            try
            {
                var catalog = new CompatibilityCatalog();
                var inventoryId = isPlayer ? "player" : "opened-container";
                var source = InventorySnapshots.CaptureInventory(inventoryId, inventory, catalog, isPlayer, player, protection);
                var plan = new InventorySortPlanner().Plan(source);
                var validation = PlanValidator.ValidateSortConservation(source, plan);
                if (!validation.IsValid)
                {
                    failure = string.Join("; ", validation.Errors);
                    return false;
                }

                return Execute(inventory, source, plan, out failure);
            }
            catch (Exception exception)
            {
                failure = exception.GetType().Name + ": " + exception.Message;
                return false;
            }
        }

        private static bool Execute(Inventory inventory, InventorySnapshot source, SortPlan plan, out string failure)
        {
            failure = null;
            var backing = (List<ItemDrop.ItemData>)InventoryItemsField.GetValue(inventory);
            var originalOrder = backing.ToArray();
            var originalState = originalOrder.ToDictionary(item => item, item => new ItemState(item.m_stack, item.m_gridPos));
            var byStackId = source.Items.ToDictionary(
                snapshot => snapshot.StackId,
                snapshot => inventory.GetItemAt(snapshot.Slot % inventory.GetWidth(), snapshot.Slot / inventory.GetWidth()),
                StringComparer.Ordinal);

            if (byStackId.Values.Any(item => item == null))
            {
                failure = "Inventory changed after the sort snapshot.";
                return false;
            }

            var survivors = new HashSet<ItemDrop.ItemData>();
            try
            {
                foreach (var placement in plan.Placements.Where(item => item.IsFixed))
                {
                    var fixedItem = byStackId[placement.SourceStackIds[0]];
                    if (fixedItem.m_stack != placement.Quantity ||
                        fixedItem.m_gridPos.x != placement.Slot % inventory.GetWidth() ||
                        fixedItem.m_gridPos.y != placement.Slot / inventory.GetWidth())
                    {
                        throw new InvalidOperationException("A protected, quick-bar, or equipped stack changed before sorting.");
                    }
                    survivors.Add(fixedItem);
                }

                foreach (var group in plan.Placements.Where(item => !item.IsFixed).GroupBy(item => item.CompatibilityKey))
                {
                    var sourceIds = group.SelectMany(item => item.SourceStackIds).Distinct(StringComparer.Ordinal).ToArray();
                    var available = new Queue<ItemDrop.ItemData>(sourceIds.Select(id => byStackId[id]));
                    foreach (var placement in group.OrderBy(item => item.Slot))
                    {
                        if (available.Count == 0)
                        {
                            throw new InvalidOperationException("The sort plan requires more source stacks than are available.");
                        }

                        var item = available.Dequeue();
                        item.m_stack = placement.Quantity;
                        item.m_gridPos = InventorySnapshots.PositionForSlot(inventory, placement.Slot);
                        survivors.Add(item);
                    }
                }

                backing.Clear();
                foreach (var item in originalOrder.Where(survivors.Contains))
                {
                    backing.Add(item);
                }

                var afterCatalog = new CompatibilityCatalog();
                var after = InventorySnapshots.CaptureInventory(source.InventoryId, inventory, afterCatalog, false, null, null);
                var beforeTotals = source.Items.GroupBy(item => item.CompatibilityKey).Select(group => group.Sum(item => item.Quantity)).OrderBy(value => value).ToArray();
                var afterTotals = after.Items.GroupBy(item => item.CompatibilityKey).Select(group => group.Sum(item => item.Quantity)).OrderBy(value => value).ToArray();
                if (!beforeTotals.SequenceEqual(afterTotals) || backing.Count != plan.Placements.Count)
                {
                    throw new InvalidOperationException("Sort conservation check failed.");
                }

                inventory.m_onChanged?.Invoke();
                return true;
            }
            catch (Exception exception)
            {
                backing.Clear();
                backing.AddRange(originalOrder);
                foreach (var pair in originalState)
                {
                    pair.Key.m_stack = pair.Value.Quantity;
                    pair.Key.m_gridPos = pair.Value.Position;
                }

                inventory.m_onChanged?.Invoke();
                failure = exception.GetType().Name + ": " + exception.Message;
                return false;
            }
        }

        private sealed class ItemState
        {
            internal ItemState(int quantity, Vector2i position)
            {
                Quantity = quantity;
                Position = position;
            }

            internal int Quantity { get; }
            internal Vector2i Position { get; }
        }
    }
}
