using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Stackmaster.Core
{
    /// <summary>
    /// A game-agnostic, read-only description of one item stack. The integration layer is
    /// responsible for producing a CompatibilityKey that captures every game rule and
    /// metadata field relevant to stack compatibility.
    /// </summary>
    public sealed class ItemStackSnapshot
    {
        public ItemStackSnapshot(
            string stackId,
            string compatibilityKey,
            string visibleName,
            int quantity,
            int maxStack,
            int slot,
            bool isQuickBar = false,
            bool isEquipped = false,
            bool isProtected = false,
            int? replenishmentTarget = null)
        {
            if (string.IsNullOrWhiteSpace(stackId)) throw new ArgumentException("A stack id is required.", nameof(stackId));
            if (string.IsNullOrWhiteSpace(compatibilityKey)) throw new ArgumentException("A compatibility key is required.", nameof(compatibilityKey));
            if (visibleName == null) throw new ArgumentNullException(nameof(visibleName));
            if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
            if (maxStack <= 0 || quantity > maxStack) throw new ArgumentOutOfRangeException(nameof(maxStack));
            if (slot < 0) throw new ArgumentOutOfRangeException(nameof(slot));
            if (replenishmentTarget.HasValue && !isProtected)
                throw new ArgumentException("Only a protected slot can have a replenishment target.", nameof(replenishmentTarget));
            if (replenishmentTarget.HasValue && (replenishmentTarget.Value < 0 || replenishmentTarget.Value > maxStack))
                throw new ArgumentOutOfRangeException(nameof(replenishmentTarget));

            StackId = stackId;
            CompatibilityKey = compatibilityKey;
            VisibleName = visibleName;
            Quantity = quantity;
            MaxStack = maxStack;
            Slot = slot;
            IsQuickBar = isQuickBar;
            IsEquipped = isEquipped;
            IsProtected = isProtected;
            ReplenishmentTarget = replenishmentTarget;
        }

        public string StackId { get; }
        public string CompatibilityKey { get; }
        public string VisibleName { get; }
        public int Quantity { get; }
        public int MaxStack { get; }
        public int Slot { get; }
        public bool IsQuickBar { get; }
        public bool IsEquipped { get; }
        public bool IsProtected { get; }
        public int? ReplenishmentTarget { get; }

        public bool IsFixed => IsQuickBar || IsEquipped || IsProtected;
    }

    public sealed class InventorySnapshot
    {
        public InventorySnapshot(string inventoryId, int capacity, IEnumerable<ItemStackSnapshot> items)
        {
            if (string.IsNullOrWhiteSpace(inventoryId)) throw new ArgumentException("An inventory id is required.", nameof(inventoryId));
            if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            if (items == null) throw new ArgumentNullException(nameof(items));

            var copy = new List<ItemStackSnapshot>(items);
            ValidateSlots(copy, capacity, nameof(items));
            InventoryId = inventoryId;
            Capacity = capacity;
            Items = new ReadOnlyCollection<ItemStackSnapshot>(copy);
        }

        public string InventoryId { get; }
        public int Capacity { get; }
        public IReadOnlyList<ItemStackSnapshot> Items { get; }

        internal static void ValidateSlots(IList<ItemStackSnapshot> items, int capacity, string parameterName)
        {
            var occupied = new HashSet<int>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in items)
            {
                if (item == null) throw new ArgumentException("Snapshots cannot contain null items.", parameterName);
                if (item.Slot >= capacity) throw new ArgumentException("An item slot is outside the inventory capacity.", parameterName);
                if (!occupied.Add(item.Slot)) throw new ArgumentException("Two items occupy the same slot.", parameterName);
                if (!ids.Add(item.StackId)) throw new ArgumentException("Stack ids must be unique inside an inventory.", parameterName);
            }
        }
    }

    public sealed class ContainerSnapshot
    {
        public ContainerSnapshot(
            string containerId,
            double distance,
            bool isTarget,
            bool isKnown,
            bool isAccessible,
            bool isVanilla,
            bool isInUse,
            int capacity,
            IEnumerable<ItemStackSnapshot> items)
        {
            if (string.IsNullOrWhiteSpace(containerId)) throw new ArgumentException("A container id is required.", nameof(containerId));
            if (double.IsNaN(distance) || double.IsInfinity(distance) || distance < 0) throw new ArgumentOutOfRangeException(nameof(distance));
            if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            if (items == null) throw new ArgumentNullException(nameof(items));

            var copy = new List<ItemStackSnapshot>(items);
            InventorySnapshot.ValidateSlots(copy, capacity, nameof(items));
            ContainerId = containerId;
            Distance = distance;
            IsTarget = isTarget;
            IsKnown = isKnown;
            IsAccessible = isAccessible;
            IsVanilla = isVanilla;
            IsInUse = isInUse;
            Capacity = capacity;
            Items = new ReadOnlyCollection<ItemStackSnapshot>(copy);
        }

        public string ContainerId { get; }
        public double Distance { get; }
        public bool IsTarget { get; }
        public bool IsKnown { get; }
        public bool IsAccessible { get; }
        public bool IsVanilla { get; }
        public bool IsInUse { get; }
        public int Capacity { get; }
        public IReadOnlyList<ItemStackSnapshot> Items { get; }

        public bool IsEligible => IsKnown && IsAccessible && IsVanilla && !IsInUse;
    }
}
