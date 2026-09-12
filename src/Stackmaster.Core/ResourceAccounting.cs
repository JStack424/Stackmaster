using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Stackmaster.Core
{
    /// <summary>One material cost after Valheim has applied quality and craft-count multipliers.</summary>
    public sealed class ResourceRequirement
    {
        public ResourceRequirement(string itemName, int quantity, int quality = -1)
        {
            if (string.IsNullOrWhiteSpace(itemName)) throw new ArgumentException("An item name is required.", nameof(itemName));
            if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
            if (quality == 0 || quality < -1) throw new ArgumentOutOfRangeException(nameof(quality));
            ItemName = itemName;
            Quantity = quantity;
            Quality = quality;
        }

        public string ItemName { get; }
        public int Quantity { get; }
        public int Quality { get; }
    }

    /// <summary>A removable material stack in deterministic player/container order.</summary>
    public sealed class ResourceStack
    {
        public ResourceStack(string inventoryId, string stackId, string itemName, int quality, int quantity, int inventoryOrder, int slot)
        {
            if (string.IsNullOrWhiteSpace(inventoryId)) throw new ArgumentException("An inventory id is required.", nameof(inventoryId));
            if (string.IsNullOrWhiteSpace(stackId)) throw new ArgumentException("A stack id is required.", nameof(stackId));
            if (string.IsNullOrWhiteSpace(itemName)) throw new ArgumentException("An item name is required.", nameof(itemName));
            if (quality <= 0) throw new ArgumentOutOfRangeException(nameof(quality));
            if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
            if (inventoryOrder < 0) throw new ArgumentOutOfRangeException(nameof(inventoryOrder));
            if (slot < 0) throw new ArgumentOutOfRangeException(nameof(slot));
            InventoryId = inventoryId;
            StackId = stackId;
            ItemName = itemName;
            Quality = quality;
            Quantity = quantity;
            InventoryOrder = inventoryOrder;
            Slot = slot;
        }

        public string InventoryId { get; }
        public string StackId { get; }
        public string ItemName { get; }
        public int Quality { get; }
        public int Quantity { get; }
        public int InventoryOrder { get; }
        public int Slot { get; }
    }

    /// <summary>Pure stock counting shared by availability UI and withdrawal planning.</summary>
    public static class ResourceAvailability
    {
        public static int CountAvailable(IEnumerable<ResourceStack> stacks, string itemName, int quality = -1)
        {
            if (stacks == null) throw new ArgumentNullException(nameof(stacks));
            if (string.IsNullOrWhiteSpace(itemName)) throw new ArgumentException("An item name is required.", nameof(itemName));
            if (quality == 0 || quality < -1) throw new ArgumentOutOfRangeException(nameof(quality));

            return stacks
                .Where(stack => stack != null &&
                                string.Equals(stack.ItemName, itemName, StringComparison.Ordinal) &&
                                (quality < 0 || stack.Quality == quality))
                .Sum(stack => checked(stack.Quantity));
        }
    }

    /// <summary>One rendered requirement line with stock from the complete eligible capture.</summary>
    public sealed class ResourceDisplayRequirement
    {
        internal ResourceDisplayRequirement(string itemName, int required, int totalRequired, int available, bool isSatisfied)
        {
            ItemName = itemName;
            Required = required;
            TotalRequired = totalRequired;
            Available = available;
            IsSatisfied = isSatisfied;
        }

        public string ItemName { get; }
        public int Required { get; }
        public int TotalRequired { get; }
        public int Available { get; }
        public bool IsSatisfied { get; }
    }

    /// <summary>
    /// Pure availability accounting for requirement UIs. Normal recipes combine duplicate
    /// costs before deciding whether any line is affordable. One-ingredient recipes treat
    /// their rows as alternatives and require one quality tier to satisfy the selected row.
    /// </summary>
    public static class ResourceDisplayAvailability
    {
        public static IReadOnlyList<ResourceDisplayRequirement> Evaluate(
            IEnumerable<ResourceRequirement> requirements,
            IEnumerable<ResourceStack> stacks,
            bool alternatives = false,
            bool requireSingleQuality = false)
        {
            if (requirements == null) throw new ArgumentNullException(nameof(requirements));
            if (stacks == null) throw new ArgumentNullException(nameof(stacks));

            var requirementList = requirements.Where(requirement => requirement != null).ToList();
            var stackList = stacks.Where(stack => stack != null).ToList();
            var totalRequired = alternatives
                ? null
                : requirementList
                    .GroupBy(requirement => new RequirementKey(requirement.ItemName, requirement.Quality))
                    .ToDictionary(
                        group => group.Key,
                        group => checked(group.Sum(requirement => requirement.Quantity)));

            return requirementList
                .Select(requirement =>
                {
                    var key = new RequirementKey(requirement.ItemName, requirement.Quality);
                    var required = alternatives ? requirement.Quantity : totalRequired![key];
                    var available = ResourceAvailability.CountAvailable(stackList, requirement.ItemName, requirement.Quality);
                    var satisfied = requireSingleQuality && requirement.Quality < 0
                        ? stackList
                            .Where(stack => string.Equals(stack.ItemName, requirement.ItemName, StringComparison.Ordinal))
                            .GroupBy(stack => stack.Quality)
                            .Any(group => group.Sum(stack => checked(stack.Quantity)) >= required)
                        : available >= required;
                    return new ResourceDisplayRequirement(
                        requirement.ItemName,
                        requirement.Quantity,
                        required,
                        available,
                        satisfied);
                })
                .ToList()
                .AsReadOnly();
        }

        private sealed class RequirementKey : IEquatable<RequirementKey>
        {
            internal RequirementKey(string itemName, int quality)
            {
                ItemName = itemName;
                Quality = quality;
            }

            internal string ItemName { get; }
            internal int Quality { get; }

            public bool Equals(RequirementKey? other)
                => other != null && Quality == other.Quality && string.Equals(ItemName, other.ItemName, StringComparison.Ordinal);

            public override bool Equals(object obj) => Equals(obj as RequirementKey);
            public override int GetHashCode() => unchecked((StringComparer.Ordinal.GetHashCode(ItemName) * 397) ^ Quality);
        }
    }

    public sealed class ResourceWithdrawalStep
    {
        public ResourceWithdrawalStep(string inventoryId, string stackId, string itemName, int quality, int quantity)
        {
            InventoryId = inventoryId;
            StackId = stackId;
            ItemName = itemName;
            Quality = quality;
            Quantity = quantity;
        }

        public string InventoryId { get; }
        public string StackId { get; }
        public string ItemName { get; }
        public int Quality { get; }
        public int Quantity { get; }
    }

    public sealed class ResourceShortage
    {
        public ResourceShortage(string itemName, int quality, int required, int available)
        {
            ItemName = itemName;
            Quality = quality;
            Required = required;
            Available = available;
        }

        public string ItemName { get; }
        public int Quality { get; }
        public int Required { get; }
        public int Available { get; }
        public int Missing => Required - Available;
    }

    public sealed class ResourceWithdrawalPlan
    {
        internal ResourceWithdrawalPlan(
            IEnumerable<ResourceRequirement> normalizedRequirements,
            IEnumerable<ResourceWithdrawalStep> steps,
            IEnumerable<ResourceShortage> shortages)
        {
            NormalizedRequirements = new ReadOnlyCollection<ResourceRequirement>(normalizedRequirements.ToList());
            Steps = new ReadOnlyCollection<ResourceWithdrawalStep>(steps.ToList());
            Shortages = new ReadOnlyCollection<ResourceShortage>(shortages.ToList());
        }

        public IReadOnlyList<ResourceRequirement> NormalizedRequirements { get; }
        public IReadOnlyList<ResourceWithdrawalStep> Steps { get; }
        public IReadOnlyList<ResourceShortage> Shortages { get; }
        public bool IsSatisfiable => Shortages.Count == 0;
        public int RequiredUnits => NormalizedRequirements.Sum(requirement => requirement.Quantity);
        public int PlannedUnits => Steps.Sum(step => step.Quantity);
    }

    /// <summary>
    /// Pure all-or-nothing accounting for player plus nearby-container materials. Duplicate
    /// requirements are combined before stock is considered, and a shortage returns no
    /// withdrawal steps so a caller cannot accidentally perform a partial charge.
    /// </summary>
    public sealed class ResourceWithdrawalPlanner
    {
        public ResourceWithdrawalPlan Plan(IEnumerable<ResourceRequirement> requirements, IEnumerable<ResourceStack> stacks)
        {
            if (requirements == null) throw new ArgumentNullException(nameof(requirements));
            if (stacks == null) throw new ArgumentNullException(nameof(stacks));

            var normalized = requirements
                .Where(requirement => requirement != null)
                .GroupBy(requirement => new RequirementKey(requirement.ItemName, requirement.Quality))
                .Select(group => new ResourceRequirement(group.Key.ItemName, checked(group.Sum(item => item.Quantity)), group.Key.Quality))
                // Exact-quality requirements reserve their stacks before any-quality requirements.
                .OrderBy(requirement => requirement.Quality < 0 ? 1 : 0)
                .ThenBy(requirement => requirement.ItemName, StringComparer.Ordinal)
                .ThenBy(requirement => requirement.Quality)
                .ToList();
            var orderedStacks = stacks
                .Where(stack => stack != null)
                .OrderBy(stack => stack.InventoryOrder)
                .ThenBy(stack => stack.Slot)
                .ThenBy(stack => stack.StackId, StringComparer.Ordinal)
                .ToList();
            var remaining = orderedStacks.ToDictionary(stack => stack.StackId, stack => stack.Quantity, StringComparer.Ordinal);
            var tentative = new List<ResourceWithdrawalStep>();
            var shortages = new List<ResourceShortage>();

            foreach (var requirement in normalized)
            {
                var needed = requirement.Quantity;
                var available = 0;
                foreach (var stack in orderedStacks)
                {
                    int quantity;
                    if (!remaining.TryGetValue(stack.StackId, out quantity) || quantity <= 0 || !Matches(requirement, stack)) continue;
                    available = checked(available + quantity);
                    var take = Math.Min(needed, quantity);
                    if (take > 0)
                    {
                        tentative.Add(new ResourceWithdrawalStep(stack.InventoryId, stack.StackId, stack.ItemName, stack.Quality, take));
                        remaining[stack.StackId] = quantity - take;
                        needed -= take;
                    }
                    if (needed == 0) break;
                }

                if (needed > 0)
                {
                    shortages.Add(new ResourceShortage(requirement.ItemName, requirement.Quality, requirement.Quantity, available));
                }
            }

            // Never expose a partial mutation plan. Availability and consumption callers use
            // the same invariant: every required unit is accounted for or nothing is removed.
            var steps = shortages.Count == 0 ? tentative : Enumerable.Empty<ResourceWithdrawalStep>();
            return new ResourceWithdrawalPlan(normalized, steps, shortages);
        }

        private static bool Matches(ResourceRequirement requirement, ResourceStack stack)
        {
            return string.Equals(requirement.ItemName, stack.ItemName, StringComparison.Ordinal) &&
                   (requirement.Quality < 0 || requirement.Quality == stack.Quality);
        }

        private sealed class RequirementKey : IEquatable<RequirementKey>
        {
            internal RequirementKey(string itemName, int quality)
            {
                ItemName = itemName;
                Quality = quality;
            }

            internal string ItemName { get; }
            internal int Quality { get; }

            public bool Equals(RequirementKey? other)
            {
                return other != null && Quality == other.Quality && string.Equals(ItemName, other.ItemName, StringComparison.Ordinal);
            }

            public override bool Equals(object obj) => Equals(obj as RequirementKey);
            public override int GetHashCode() => unchecked((StringComparer.Ordinal.GetHashCode(ItemName) * 397) ^ Quality);
        }
    }
}
