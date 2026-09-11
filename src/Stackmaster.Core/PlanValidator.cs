using System;
using System.Collections.Generic;
using System.Linq;

namespace Stackmaster.Core
{
    public static class PlanValidator
    {
        public static ValidationResult ValidateSortConservation(InventorySnapshot source, SortPlan plan)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            var errors = new List<string>();
            if (!string.Equals(source.InventoryId, plan.InventoryId, StringComparison.Ordinal))
                errors.Add("The sort plan targets a different inventory.");

            var duplicateSlots = plan.Placements.GroupBy(item => item.Slot).Where(group => group.Count() > 1).Select(group => group.Key);
            foreach (var slot in duplicateSlots) errors.Add("The sort plan places more than one stack in slot " + slot + ".");
            if (plan.Placements.Any(item => item.Slot >= source.Capacity)) errors.Add("The sort plan uses a slot outside the inventory capacity.");

            foreach (var fixedItem in source.Items.Where(item => item.IsFixed))
            {
                var placement = plan.Placements.SingleOrDefault(item => item.Slot == fixedItem.Slot);
                if (placement == null || !placement.IsFixed || placement.CompatibilityKey != fixedItem.CompatibilityKey || placement.Quantity != fixedItem.Quantity)
                    errors.Add("Fixed slot " + fixedItem.Slot + " was changed.");
            }

            CompareCounts(
                source.Items.GroupBy(item => item.CompatibilityKey, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.Sum(item => item.Quantity), StringComparer.Ordinal),
                plan.Placements.GroupBy(item => item.CompatibilityKey, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.Sum(item => item.Quantity), StringComparer.Ordinal),
                errors);
            return new ValidationResult(errors);
        }

        public static ValidationResult ValidateTransferConservation(
            InventorySnapshot player,
            IEnumerable<ContainerSnapshot> containers,
            TransferPlan plan)
        {
            if (player == null) throw new ArgumentNullException(nameof(player));
            if (containers == null) throw new ArgumentNullException(nameof(containers));
            if (plan == null) throw new ArgumentNullException(nameof(plan));

            var containerList = containers.ToList();
            var containerById = containerList.ToDictionary(container => container.ContainerId, StringComparer.Ordinal);
            var state = new Dictionary<string, Cell>(StringComparer.Ordinal);
            foreach (var item in player.Items)
                state.Add(Key(InventoryLocationKind.Player, player.InventoryId, item.Slot), new Cell(item.CompatibilityKey, item.Quantity, item.MaxStack));
            foreach (var container in containerList)
                foreach (var item in container.Items)
                    state.Add(Key(InventoryLocationKind.Container, container.ContainerId, item.Slot), new Cell(item.CompatibilityKey, item.Quantity, item.MaxStack));

            var before = Count(state.Values);
            var errors = new List<string>();
            foreach (var step in plan.Steps)
            {
                ValidateAndApplyStep(player, containerById, state, step, errors);
            }
            var after = Count(state.Values);
            CompareCounts(before, after, errors);
            return new ValidationResult(errors);
        }

        private static void ValidateAndApplyStep(
            InventorySnapshot player,
            IDictionary<string, ContainerSnapshot> containers,
            IDictionary<string, Cell> state,
            TransferStep step,
            IList<string> errors)
        {
            if (step.Kind == TransferKind.Replenishment)
            {
                if (step.Source.Kind != InventoryLocationKind.Container || step.Destination.Kind != InventoryLocationKind.Player)
                    errors.Add("A replenishment step has invalid endpoint kinds.");
            }
            else if (step.Source.Kind != InventoryLocationKind.Player || step.Destination.Kind != InventoryLocationKind.Container)
            {
                errors.Add("A deposit step has invalid endpoint kinds.");
            }

            if (step.Destination.Kind == InventoryLocationKind.Player && !string.Equals(step.Destination.InventoryId, player.InventoryId, StringComparison.Ordinal))
                errors.Add("A step targets an unknown player inventory.");

            ContainerSnapshot? destinationContainer = null;
            if (step.Destination.Kind == InventoryLocationKind.Container)
            {
                if (!containers.TryGetValue(step.Destination.InventoryId, out destinationContainer))
                    errors.Add("A step targets an unknown container.");
                else
                {
                    if (step.Destination.Slot >= destinationContainer.Capacity) errors.Add("A step targets a slot outside the container capacity.");
                    if (!destinationContainer.IsEligible) errors.Add("A step targets an ineligible container.");
                    if ((step.Kind == TransferKind.Deposit || step.Kind == TransferKind.ExcessDeposit) &&
                        !destinationContainer.Items.Any(item => item.CompatibilityKey == step.CompatibilityKey))
                        errors.Add("A deposit targets a container that did not initially contain a compatible item.");
                }
            }

            if (step.Source.Kind == InventoryLocationKind.Container)
            {
                ContainerSnapshot? sourceContainer;
                if (!containers.TryGetValue(step.Source.InventoryId, out sourceContainer))
                    errors.Add("A step reads from an unknown container.");
                else if (!sourceContainer.IsEligible)
                    errors.Add("A step reads from an ineligible container.");
            }

            var sourceKey = Key(step.Source.Kind, step.Source.InventoryId, step.Source.Slot);
            Cell? source;
            if (!state.TryGetValue(sourceKey, out source))
            {
                errors.Add("A transfer source slot is empty.");
                return;
            }
            if (source.CompatibilityKey != step.CompatibilityKey) errors.Add("A transfer source is not stack-compatible with its declared item.");
            if (source.Quantity < step.Quantity)
            {
                errors.Add("A transfer withdraws more units than its source contains.");
                return;
            }

            var destinationKey = Key(step.Destination.Kind, step.Destination.InventoryId, step.Destination.Slot);
            Cell? destination;
            if (state.TryGetValue(destinationKey, out destination))
            {
                if (destination.CompatibilityKey != step.CompatibilityKey)
                {
                    errors.Add("A transfer combines incompatible stacks.");
                    return;
                }
                if (destination.Quantity + step.Quantity > destination.MaxStack)
                {
                    errors.Add("A transfer overfills a destination stack.");
                    return;
                }
                destination.Quantity += step.Quantity;
            }
            else
            {
                if (step.Quantity > step.MaxStack)
                {
                    errors.Add("A transfer creates an overfilled stack.");
                    return;
                }
                state.Add(destinationKey, new Cell(step.CompatibilityKey, step.Quantity, step.MaxStack));
            }

            source.Quantity -= step.Quantity;
            if (source.Quantity == 0) state.Remove(sourceKey);
        }

        private static Dictionary<string, int> Count(IEnumerable<Cell> cells)
        {
            return cells.GroupBy(cell => cell.CompatibilityKey, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Sum(cell => cell.Quantity), StringComparer.Ordinal);
        }

        private static void CompareCounts(IDictionary<string, int> before, IDictionary<string, int> after, IList<string> errors)
        {
            foreach (var key in before.Keys.Concat(after.Keys).Distinct(StringComparer.Ordinal).OrderBy(key => key, StringComparer.Ordinal))
            {
                int beforeCount;
                int afterCount;
                before.TryGetValue(key, out beforeCount);
                after.TryGetValue(key, out afterCount);
                if (beforeCount != afterCount)
                    errors.Add("Count drift for " + key + ": " + beforeCount + " became " + afterCount + ".");
            }
        }

        private static string Key(InventoryLocationKind kind, string inventoryId, int slot)
        {
            return ((int)kind).ToString() + "\u001f" + inventoryId + "\u001f" + slot.ToString();
        }

        private sealed class Cell
        {
            public Cell(string compatibilityKey, int quantity, int maxStack)
            {
                CompatibilityKey = compatibilityKey;
                Quantity = quantity;
                MaxStack = maxStack;
            }

            public string CompatibilityKey { get; }
            public int Quantity { get; set; }
            public int MaxStack { get; }
        }
    }
}
