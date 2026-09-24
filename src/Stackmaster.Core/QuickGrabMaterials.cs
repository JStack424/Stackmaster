using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Stackmaster.Core
{
    /// <summary>
    /// Pure input policy for the build-menu quick-grab shortcut. An ordinary click is never
    /// intercepted unless the configured shortcut has at least one modifier and every configured
    /// modifier is currently held.
    /// </summary>
    public static class QuickGrabClickPolicy
    {
        public static bool ShouldIntercept(
            bool runtimeCompatible,
            bool hasLocalPlayer,
            bool hasClickedPiece,
            IEnumerable<bool>? configuredModifierStates)
        {
            if (!runtimeCompatible || !hasLocalPlayer || !hasClickedPiece || configuredModifierStates == null)
            {
                return false;
            }

            var states = configuredModifierStates.ToArray();
            return states.Length > 0 && states.All(state => state);
        }
    }

    /// <summary>
    /// Plans one complete recipe entirely from storage. For each ingredient, containers are
    /// visited by their total stock of that ingredient (largest first), with stable identity and
    /// slot tie-breakers. Player-held stock is intentionally absent from this policy.
    /// </summary>
    public sealed class QuickGrabMaterialsWithdrawalPlanner
    {
        public ResourceWithdrawalPlan Plan(
            IEnumerable<ResourceRequirement> requirements,
            IEnumerable<ResourceStack> chestStacks)
        {
            if (requirements == null) throw new ArgumentNullException(nameof(requirements));
            if (chestStacks == null) throw new ArgumentNullException(nameof(chestStacks));

            var normalized = requirements
                .Where(requirement => requirement != null && requirement.Quantity > 0)
                .GroupBy(requirement => new { requirement.ItemName, requirement.Quality })
                .Select(group => new ResourceRequirement(
                    group.Key.ItemName,
                    checked(group.Sum(requirement => requirement.Quantity)),
                    group.Key.Quality))
                .OrderBy(requirement => requirement.Quality < 0 ? 1 : 0)
                .ThenBy(requirement => requirement.ItemName, StringComparer.Ordinal)
                .ThenBy(requirement => requirement.Quality)
                .ToArray();
            var stacks = chestStacks
                .Where(stack => stack != null && stack.Quantity > 0)
                .ToArray();
            var remainingByStack = stacks.ToDictionary(stack => stack.StackId, stack => stack.Quantity, StringComparer.Ordinal);
            var steps = new List<ResourceWithdrawalStep>();
            var shortages = new List<ResourceShortage>();

            foreach (var requirement in normalized)
            {
                var remaining = requirement.Quantity;
                var candidates = stacks
                    .Where(stack => string.Equals(stack.ItemName, requirement.ItemName, StringComparison.Ordinal) &&
                                    (requirement.Quality < 0 || stack.Quality == requirement.Quality))
                    .GroupBy(stack => stack.InventoryId, StringComparer.Ordinal)
                    .Select(group => new
                    {
                        InventoryId = group.Key,
                        Total = group.Sum(stack => remainingByStack[stack.StackId]),
                        Stacks = group.OrderBy(stack => stack.Slot)
                            .ThenBy(stack => stack.StackId, StringComparer.Ordinal)
                            .ToArray()
                    })
                    .Where(group => group.Total > 0)
                    .OrderByDescending(group => group.Total)
                    .ThenBy(group => group.InventoryId, StringComparer.Ordinal)
                    .ToArray();

                foreach (var container in candidates)
                {
                    foreach (var stack in container.Stacks)
                    {
                        if (remaining == 0) break;
                        var available = remainingByStack[stack.StackId];
                        if (available <= 0) continue;
                        var take = Math.Min(available, remaining);
                        steps.Add(new ResourceWithdrawalStep(
                            stack.InventoryId,
                            stack.StackId,
                            stack.ItemName,
                            stack.Quality,
                            take));
                        remainingByStack[stack.StackId] -= take;
                        remaining -= take;
                    }
                    if (remaining == 0) break;
                }

                if (remaining > 0)
                {
                    shortages.Add(new ResourceShortage(
                        requirement.ItemName,
                        requirement.Quality,
                        requirement.Quantity,
                        requirement.Quantity - remaining));
                }
            }

            if (shortages.Count > 0)
            {
                // A piece-specific material grab is indivisible. Keep diagnostic shortages but expose no executable steps.
                return new ResourceWithdrawalPlan(normalized, Array.Empty<ResourceWithdrawalStep>(), shortages);
            }
            return new ResourceWithdrawalPlan(normalized, steps, shortages);
        }

        public static bool PlansAreIdentical(ResourceWithdrawalPlan expected, ResourceWithdrawalPlan actual)
        {
            if (expected == null || actual == null ||
                expected.RequiredUnits != actual.RequiredUnits ||
                expected.Steps.Count != actual.Steps.Count)
            {
                return false;
            }

            for (var index = 0; index < expected.Steps.Count; index++)
            {
                var left = expected.Steps[index];
                var right = actual.Steps[index];
                if (!string.Equals(left.InventoryId, right.InventoryId, StringComparison.Ordinal) ||
                    !string.Equals(left.StackId, right.StackId, StringComparison.Ordinal) ||
                    !string.Equals(left.ItemName, right.ItemName, StringComparison.Ordinal) ||
                    left.Quality != right.Quality ||
                    left.Quantity != right.Quantity)
                {
                    return false;
                }
            }
            return true;
        }
    }

    public sealed class QuickGrabCargoStack
    {
        public QuickGrabCargoStack(
            string sourceStackId,
            string compatibilityKey,
            int quantity,
            int maxStack,
            double addedWeight)
        {
            if (string.IsNullOrWhiteSpace(sourceStackId)) throw new ArgumentException("A source stack id is required.", nameof(sourceStackId));
            if (string.IsNullOrWhiteSpace(compatibilityKey)) throw new ArgumentException("A compatibility key is required.", nameof(compatibilityKey));
            if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
            if (maxStack <= 0) throw new ArgumentOutOfRangeException(nameof(maxStack));
            if (double.IsNaN(addedWeight) || double.IsInfinity(addedWeight) || addedWeight < 0) throw new ArgumentOutOfRangeException(nameof(addedWeight));
            SourceStackId = sourceStackId;
            CompatibilityKey = compatibilityKey;
            Quantity = quantity;
            MaxStack = maxStack;
            AddedWeight = addedWeight;
        }

        public string SourceStackId { get; }
        public string CompatibilityKey { get; }
        public int Quantity { get; }
        public int MaxStack { get; }
        public double AddedWeight { get; }
    }

    public sealed class QuickGrabDestinationStep
    {
        public QuickGrabDestinationStep(
            string sourceStackId,
            string compatibilityKey,
            int destinationSlot,
            int expectedDestinationQuantity,
            int quantity)
        {
            SourceStackId = sourceStackId;
            CompatibilityKey = compatibilityKey;
            DestinationSlot = destinationSlot;
            ExpectedDestinationQuantity = expectedDestinationQuantity;
            Quantity = quantity;
        }

        public string SourceStackId { get; }
        public string CompatibilityKey { get; }
        public int DestinationSlot { get; }
        public int ExpectedDestinationQuantity { get; }
        public int Quantity { get; }
    }

    public sealed class QuickGrabCapacityPlan
    {
        public QuickGrabCapacityPlan(
            bool fitsSlots,
            bool fitsWeight,
            double addedWeight,
            IEnumerable<QuickGrabDestinationStep> steps)
        {
            FitsSlots = fitsSlots;
            FitsWeight = fitsWeight;
            AddedWeight = addedWeight;
            Steps = new ReadOnlyCollection<QuickGrabDestinationStep>((steps ?? Array.Empty<QuickGrabDestinationStep>()).ToList());
        }

        public bool FitsSlots { get; }
        public bool FitsWeight { get; }
        public bool IsFeasible => FitsSlots && FitsWeight;
        public double AddedWeight { get; }
        public IReadOnlyList<QuickGrabDestinationStep> Steps { get; }
    }

    /// <summary>
    /// Simulates exact stacking and slot use before any chest is changed. Cargo order is retained
    /// so the runtime can execute and compensate the same deterministic transfer sequence.
    /// </summary>
    public sealed class QuickGrabCapacityPlanner
    {
        public QuickGrabCapacityPlan Plan(
            InventorySnapshot player,
            IEnumerable<QuickGrabCargoStack> cargo,
            double currentWeight,
            double maxCarryWeight)
        {
            if (player == null) throw new ArgumentNullException(nameof(player));
            if (cargo == null) throw new ArgumentNullException(nameof(cargo));
            if (double.IsNaN(currentWeight) || double.IsInfinity(currentWeight) || currentWeight < 0) throw new ArgumentOutOfRangeException(nameof(currentWeight));
            if (double.IsNaN(maxCarryWeight) || double.IsInfinity(maxCarryWeight) || maxCarryWeight < 0) throw new ArgumentOutOfRangeException(nameof(maxCarryWeight));

            var incoming = cargo.ToArray();
            var addedWeight = incoming.Sum(stack => stack.AddedWeight);
            var fitsWeight = currentWeight + addedWeight <= maxCarryWeight + 0.0001d;
            var slots = new VirtualSlot[player.Capacity];
            foreach (var item in player.Items)
            {
                slots[item.Slot] = new VirtualSlot(item.CompatibilityKey, item.Quantity, item.MaxStack);
            }
            var steps = new List<QuickGrabDestinationStep>();
            var fitsSlots = true;

            foreach (var source in incoming)
            {
                var remaining = source.Quantity;
                for (var slot = 0; slot < slots.Length && remaining > 0; slot++)
                {
                    var destination = slots[slot];
                    if (destination == null || !string.Equals(destination.CompatibilityKey, source.CompatibilityKey, StringComparison.Ordinal)) continue;
                    var available = destination.MaxStack - destination.Quantity;
                    if (available <= 0) continue;
                    var moved = Math.Min(available, remaining);
                    steps.Add(new QuickGrabDestinationStep(
                        source.SourceStackId,
                        source.CompatibilityKey,
                        slot,
                        destination.Quantity,
                        moved));
                    destination.Quantity += moved;
                    remaining -= moved;
                }

                while (remaining > 0)
                {
                    var emptySlot = Array.FindIndex(slots, slot => slot == null);
                    if (emptySlot < 0)
                    {
                        fitsSlots = false;
                        break;
                    }
                    var moved = Math.Min(source.MaxStack, remaining);
                    steps.Add(new QuickGrabDestinationStep(
                        source.SourceStackId,
                        source.CompatibilityKey,
                        emptySlot,
                        0,
                        moved));
                    slots[emptySlot] = new VirtualSlot(source.CompatibilityKey, moved, source.MaxStack);
                    remaining -= moved;
                }

                if (!fitsSlots) break;
            }

            if (!fitsSlots || !fitsWeight)
            {
                return new QuickGrabCapacityPlan(fitsSlots, fitsWeight, addedWeight, Array.Empty<QuickGrabDestinationStep>());
            }
            return new QuickGrabCapacityPlan(true, true, addedWeight, steps);
        }

        private sealed class VirtualSlot
        {
            internal VirtualSlot(string compatibilityKey, int quantity, int maxStack)
            {
                CompatibilityKey = compatibilityKey;
                Quantity = quantity;
                MaxStack = maxStack;
            }

            internal string CompatibilityKey { get; }
            internal int Quantity { get; set; }
            internal int MaxStack { get; }
        }
    }
}
