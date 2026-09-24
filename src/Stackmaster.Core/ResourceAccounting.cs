using System;
using System.Collections.Generic;
using System.Globalization;
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

    /// <summary>Shared text and flash policy for building and crafting requirement rows.</summary>
    public static class ResourceRequirementPresentation
    {
        public static string Format(int required, int available)
        {
            if (required < 0) throw new ArgumentOutOfRangeException(nameof(required));
            if (available < 0) throw new ArgumentOutOfRangeException(nameof(available));
            return required.ToString(CultureInfo.InvariantCulture) + " / " +
                   available.ToString(CultureInfo.InvariantCulture);
        }

        public static bool ShouldUseShortageColor(bool noCost, bool isSatisfied, float flashSignal)
            => !noCost && !isSatisfied && flashSignal > 0f;
    }

    /// <summary>Pure policy that keeps storage totals independent from storage-backed action permission.</summary>
    public sealed class RequirementUiDecision
    {
        internal RequirementUiDecision(bool shouldApply, bool shouldOverrideText, bool isSatisfied)
        {
            ShouldApply = shouldApply;
            ShouldOverrideText = shouldOverrideText;
            IsSatisfied = isSatisfied;
        }

        public bool ShouldApply { get; }
        public bool ShouldOverrideText { get; }
        public bool IsSatisfied { get; }
    }

    public static class RequirementUiPolicy
    {
        public static RequirementUiDecision Resolve(
            bool showStorageAmounts,
            bool allowStorageUse,
            bool aggregateSatisfied,
            bool playerSatisfied)
            => new RequirementUiDecision(
                showStorageAmounts || allowStorageUse,
                showStorageAmounts,
                allowStorageUse ? aggregateSatisfied : playerSatisfied);
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
    /// Immutable quantity index for repeated display-only requirement checks. Building the
    /// index is linear in the snapshot size; every later item/quality lookup is constant-time.
    /// </summary>
    public sealed class ResourceAvailabilityIndex
    {
        private readonly IReadOnlyDictionary<string, int> _allQualities;
        private readonly IReadOnlyDictionary<AvailabilityKey, int> _exactQualities;
        private readonly IReadOnlyDictionary<string, IReadOnlyList<int>> _quantitiesByQuality;

        private ResourceAvailabilityIndex(
            IReadOnlyDictionary<string, int> allQualities,
            IReadOnlyDictionary<AvailabilityKey, int> exactQualities,
            IReadOnlyDictionary<string, IReadOnlyList<int>> quantitiesByQuality)
        {
            _allQualities = allQualities;
            _exactQualities = exactQualities;
            _quantitiesByQuality = quantitiesByQuality;
        }

        public static ResourceAvailabilityIndex Create(IEnumerable<ResourceStack> stacks)
        {
            if (stacks == null) throw new ArgumentNullException(nameof(stacks));

            var allQualities = new Dictionary<string, int>(StringComparer.Ordinal);
            var exactQualities = new Dictionary<AvailabilityKey, int>();
            var quantitiesByQuality = new Dictionary<string, Dictionary<int, int>>(StringComparer.Ordinal);
            foreach (var stack in stacks.Where(stack => stack != null))
            {
                int quantity;
                allQualities.TryGetValue(stack.ItemName, out quantity);
                allQualities[stack.ItemName] = checked(quantity + stack.Quantity);

                var key = new AvailabilityKey(stack.ItemName, stack.Quality);
                exactQualities.TryGetValue(key, out quantity);
                exactQualities[key] = checked(quantity + stack.Quantity);

                Dictionary<int, int> qualities;
                if (!quantitiesByQuality.TryGetValue(stack.ItemName, out qualities))
                {
                    qualities = new Dictionary<int, int>();
                    quantitiesByQuality.Add(stack.ItemName, qualities);
                }
                qualities.TryGetValue(stack.Quality, out quantity);
                qualities[stack.Quality] = checked(quantity + stack.Quantity);
            }

            return new ResourceAvailabilityIndex(
                allQualities,
                exactQualities,
                quantitiesByQuality.ToDictionary(
                    pair => pair.Key,
                    pair => (IReadOnlyList<int>)pair.Value.Values.ToArray(),
                    StringComparer.Ordinal));
        }

        public int CountAvailable(string itemName, int quality = -1)
        {
            if (string.IsNullOrWhiteSpace(itemName)) throw new ArgumentException("An item name is required.", nameof(itemName));
            if (quality == 0 || quality < -1) throw new ArgumentOutOfRangeException(nameof(quality));
            int quantity;
            return quality < 0
                ? (_allQualities.TryGetValue(itemName, out quantity) ? quantity : 0)
                : (_exactQualities.TryGetValue(new AvailabilityKey(itemName, quality), out quantity) ? quantity : 0);
        }

        public bool HasSingleQualityAmount(string itemName, int required)
        {
            if (itemName == null) throw new ArgumentNullException(nameof(itemName));
            IReadOnlyList<int> quantities;
            return _quantitiesByQuality.TryGetValue(itemName, out quantities) &&
                   quantities.Any(quantity => quantity >= required);
        }

        private sealed class AvailabilityKey : IEquatable<AvailabilityKey>
        {
            internal AvailabilityKey(string itemName, int quality)
            {
                ItemName = itemName;
                Quality = quality;
            }

            private string ItemName { get; }
            private int Quality { get; }

            public bool Equals(AvailabilityKey? other)
                => other != null && Quality == other.Quality &&
                   string.Equals(ItemName, other.ItemName, StringComparison.Ordinal);

            public override bool Equals(object obj) => Equals(obj as AvailabilityKey);
            public override int GetHashCode()
                => unchecked((StringComparer.Ordinal.GetHashCode(ItemName) * 397) ^ Quality);
        }
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
            if (stacks == null) throw new ArgumentNullException(nameof(stacks));
            return Evaluate(requirements, ResourceAvailabilityIndex.Create(stacks), alternatives, requireSingleQuality);
        }

        public static IReadOnlyList<ResourceDisplayRequirement> Evaluate(
            IEnumerable<ResourceRequirement> requirements,
            ResourceAvailabilityIndex availability,
            bool alternatives = false,
            bool requireSingleQuality = false)
        {
            if (requirements == null) throw new ArgumentNullException(nameof(requirements));
            if (availability == null) throw new ArgumentNullException(nameof(availability));

            var requirementList = requirements.Where(requirement => requirement != null).ToList();
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
                    var available = availability.CountAvailable(requirement.ItemName, requirement.Quality);
                    var satisfied = requireSingleQuality && requirement.Quality < 0
                        ? availability.HasSingleQualityAmount(requirement.ItemName, required)
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
        private const int MinimumContainerSearchNodeLimit = 250000;

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

        /// <summary>
        /// Finds an exact all-or-nothing plan that uses player stock first and the smallest
        /// possible number of distinct non-player inventories. Equal-size solutions retain the
        /// caller's deterministic inventory order. The bounded exhaustive search fails closed
        /// rather than silently falling back to a non-minimal ownership set.
        /// </summary>
        public bool TryPlanWithMinimumContainers(
            IEnumerable<ResourceRequirement> requirements,
            IEnumerable<ResourceStack> stacks,
            string playerInventoryId,
            out ResourceWithdrawalPlan plan)
        {
            if (requirements == null) throw new ArgumentNullException(nameof(requirements));
            if (stacks == null) throw new ArgumentNullException(nameof(stacks));
            if (playerInventoryId == null) throw new ArgumentNullException(nameof(playerInventoryId));

            var requirementList = requirements.Where(requirement => requirement != null).ToList();
            var stackList = stacks.Where(stack => stack != null).ToList();
            var completePlan = Plan(requirementList, stackList);
            plan = completePlan;
            if (!completePlan.IsSatisfiable) return true;

            var candidateIds = stackList
                .Where(stack => !string.Equals(stack.InventoryId, playerInventoryId, StringComparison.Ordinal) &&
                                requirementList.Any(requirement => Matches(requirement, stack)))
                .GroupBy(stack => stack.InventoryId, StringComparer.Ordinal)
                .OrderBy(group => group.Min(stack => stack.InventoryOrder))
                .ThenBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => group.Key)
                .ToList();
            var playerStacks = stackList
                .Where(stack => string.Equals(stack.InventoryId, playerInventoryId, StringComparison.Ordinal))
                .ToList();
            var selected = new List<string>();
            var visited = 0;

            for (var count = 0; count <= candidateIds.Count; count++)
            {
                ResourceWithdrawalPlan candidatePlan;
                if (TryFindMinimumSubset(
                        requirementList,
                        stackList,
                        playerStacks,
                        candidateIds,
                        0,
                        count,
                        selected,
                        ref visited,
                        out candidatePlan))
                {
                    plan = candidatePlan;
                    return true;
                }
                if (visited >= MinimumContainerSearchNodeLimit) return false;
            }
            return false;
        }

        private bool TryFindMinimumSubset(
            IReadOnlyList<ResourceRequirement> requirements,
            IReadOnlyList<ResourceStack> allStacks,
            IReadOnlyList<ResourceStack> playerStacks,
            IReadOnlyList<string> candidateIds,
            int start,
            int remainingSlots,
            IList<string> selected,
            ref int visited,
            out ResourceWithdrawalPlan plan)
        {
            plan = null!;
            if (++visited > MinimumContainerSearchNodeLimit) return false;
            if (remainingSlots == 0)
            {
                var selectedIds = new HashSet<string>(selected, StringComparer.Ordinal);
                var eligible = playerStacks.Concat(allStacks.Where(stack => selectedIds.Contains(stack.InventoryId)));
                var candidate = Plan(requirements, eligible);
                if (!candidate.IsSatisfiable) return false;
                plan = candidate;
                return true;
            }
            if (candidateIds.Count - start < remainingSlots) return false;

            for (var index = start; index <= candidateIds.Count - remainingSlots; index++)
            {
                selected.Add(candidateIds[index]);
                if (TryFindMinimumSubset(
                        requirements,
                        allStacks,
                        playerStacks,
                        candidateIds,
                        index + 1,
                        remainingSlots - 1,
                        selected,
                        ref visited,
                        out plan))
                {
                    return true;
                }
                selected.RemoveAt(selected.Count - 1);
                if (visited >= MinimumContainerSearchNodeLimit) return false;
            }
            return false;
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

    /// <summary>
    /// Derives the exact chest set from an all-or-nothing withdrawal plan and validates only
    /// those chests' immutable read revisions. Display-only inventories never enter this API.
    /// </summary>
    public static class ResourceOwnershipSelection
    {
        public static IReadOnlyList<string> RequiredContainerIds(
            ResourceWithdrawalPlan plan,
            string playerInventoryId = "player")
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            if (playerInventoryId == null) throw new ArgumentNullException(nameof(playerInventoryId));
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var result = new List<string>();
            foreach (var step in plan.Steps)
            {
                if (string.Equals(step.InventoryId, playerInventoryId, StringComparison.Ordinal) ||
                    !seen.Add(step.InventoryId))
                {
                    continue;
                }
                result.Add(step.InventoryId);
            }
            return new ReadOnlyCollection<string>(result);
        }

        public static bool RequiredRevisionsMatch(
            IEnumerable<string> requiredContainerIds,
            IReadOnlyDictionary<string, uint> expected,
            IReadOnlyDictionary<string, uint> current)
        {
            if (requiredContainerIds == null) throw new ArgumentNullException(nameof(requiredContainerIds));
            if (expected == null) throw new ArgumentNullException(nameof(expected));
            if (current == null) throw new ArgumentNullException(nameof(current));
            foreach (var id in requiredContainerIds.Distinct(StringComparer.Ordinal))
            {
                uint expectedRevision;
                uint currentRevision;
                if (!expected.TryGetValue(id, out expectedRevision) ||
                    !current.TryGetValue(id, out currentRevision) ||
                    expectedRevision != currentRevision)
                {
                    return false;
                }
            }
            return true;
        }
    }

}
