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

                // Opening an already-sorted inventory must not synthesize an Inventory.m_onChanged
                // notification. With a hammer equipped, Valheim treats that notification as a
                // real inventory mutation and rebuilds the placement ghost even though no item moved.
                if (PlanAlreadyApplied(source, plan)) return true;

                if (!isPlayer)
                {
                    return Execute(inventory, source, plan, false, out failure);
                }

                FailedDepositWarnings.BeginPlayerSort();
                try
                {
                    return Execute(inventory, source, plan, true, out failure);
                }
                finally
                {
                    FailedDepositWarnings.EndPlayerSort();
                }
            }
            catch (Exception exception)
            {
                failure = exception.GetType().Name + ": " + exception.Message;
                return false;
            }
        }

        private static bool PlanAlreadyApplied(InventorySnapshot source, SortPlan plan)
        {
            if (source == null || plan == null || source.Items.Count != plan.Placements.Count) return false;

            var placementBySlot = plan.Placements.ToDictionary(placement => placement.Slot);
            foreach (var item in source.Items)
            {
                SortPlacement placement;
                if (!placementBySlot.TryGetValue(item.Slot, out placement) ||
                    !string.Equals(placement.CompatibilityKey, item.CompatibilityKey, StringComparison.Ordinal) ||
                    placement.Quantity != item.Quantity ||
                    placement.IsFixed != item.IsFixed)
                {
                    return false;
                }
            }
            return true;
        }

        private static bool Execute(Inventory inventory, InventorySnapshot source, SortPlan plan, bool isPlayer, out string failure)
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
                    var placements = group.OrderBy(item => item.Slot).ToArray();
                    var sourceIds = group.SelectMany(item => item.SourceStackIds).Distinct(StringComparer.Ordinal).ToArray();
                    var available = sourceIds.Select(id => byStackId[id]).ToList();
                    if (available.Count < placements.Length)
                    {
                        throw new InvalidOperationException("The sort plan requires more source stacks than are available.");
                    }

                    foreach (var placement in placements)
                    {
                        var destination = available
                            .Where(backing.Contains)
                            .OrderByDescending(item => item.m_stack)
                            .FirstOrDefault();
                        if (destination == null || destination.m_stack > placement.Quantity)
                        {
                            throw new InvalidOperationException("A movable stack changed before consolidation.");
                        }
                        available.Remove(destination);

                        var needed = placement.Quantity - destination.m_stack;
                        foreach (var donor in available.ToArray())
                        {
                            if (needed == 0) break;
                            if (!backing.Contains(donor) || donor.m_stack <= 0)
                            {
                                available.Remove(donor);
                                continue;
                            }
                            if (!destination.IsSameType(donor) || !donor.IsSameType(destination))
                            {
                                throw new InvalidOperationException("A planned stack group is no longer compatible.");
                            }

                            var moved = Math.Min(needed, donor.m_stack);
                            var beforeDestination = destination.m_stack;
                            var beforeDonor = donor.m_stack;
                            inventory.MoveItemToThis(
                                inventory,
                                donor,
                                moved,
                                destination.m_gridPos.x,
                                destination.m_gridPos.y);
                            if (destination.m_stack != beforeDestination + moved ||
                                (backing.Contains(donor) ? donor.m_stack : 0) != beforeDonor - moved)
                            {
                                throw new InvalidOperationException("Vanilla stack consolidation returned an unexpected result.");
                            }
                            if (!backing.Contains(donor)) available.Remove(donor);
                            needed -= moved;
                        }

                        if (needed != 0 || destination.m_stack != placement.Quantity)
                        {
                            throw new InvalidOperationException("The planned stack quantity could not be produced safely.");
                        }
                        destination.m_gridPos = InventorySnapshots.PositionForSlot(inventory, placement.Slot);
                        survivors.Add(destination);
                    }
                }

                if (backing.Any(item => !survivors.Contains(item)))
                {
                    throw new InvalidOperationException("Vanilla consolidation left an unexpected source stack behind.");
                }

                var afterCatalog = new CompatibilityCatalog();
                var after = InventorySnapshots.CaptureInventory(source.InventoryId, inventory, afterCatalog, false, null, null);
                var beforeTotals = source.Items.GroupBy(item => item.CompatibilityKey).Select(group => group.Sum(item => item.Quantity)).OrderBy(value => value).ToArray();
                var afterTotals = after.Items.GroupBy(item => item.CompatibilityKey).Select(group => group.Sum(item => item.Quantity)).OrderBy(value => value).ToArray();
                if (!beforeTotals.SequenceEqual(afterTotals) || backing.Count != plan.Placements.Count)
                {
                    throw new InvalidOperationException("Sort conservation check failed.");
                }

                if (isPlayer)
                {
                    try
                    {
                        FailedDepositWarnings.ReconcileAfterSuccessfulSort(originalOrder, backing);
                    }
                    catch (Exception warningException)
                    {
                        FailedDepositWarnings.Clear();
                        RuntimeContext.Plugin?.Log.LogWarning("Failed-deposit warning reconciliation was cleared safely after sorting: " + warningException);
                    }
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
