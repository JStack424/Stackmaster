using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Stackmaster.Core
{
    public sealed class SortPlacement
    {
        public SortPlacement(
            int slot,
            string compatibilityKey,
            string visibleName,
            int quantity,
            int maxStack,
            bool isFixed,
            IEnumerable<string> sourceStackIds)
        {
            if (slot < 0) throw new ArgumentOutOfRangeException(nameof(slot));
            if (string.IsNullOrWhiteSpace(compatibilityKey)) throw new ArgumentException("A compatibility key is required.", nameof(compatibilityKey));
            if (visibleName == null) throw new ArgumentNullException(nameof(visibleName));
            if (quantity <= 0 || maxStack <= 0 || quantity > maxStack) throw new ArgumentOutOfRangeException(nameof(quantity));
            if (sourceStackIds == null) throw new ArgumentNullException(nameof(sourceStackIds));

            Slot = slot;
            CompatibilityKey = compatibilityKey;
            VisibleName = visibleName;
            Quantity = quantity;
            MaxStack = maxStack;
            IsFixed = isFixed;
            SourceStackIds = new ReadOnlyCollection<string>(new List<string>(sourceStackIds));
        }

        public int Slot { get; }
        public string CompatibilityKey { get; }
        public string VisibleName { get; }
        public int Quantity { get; }
        public int MaxStack { get; }
        public bool IsFixed { get; }
        public IReadOnlyList<string> SourceStackIds { get; }
    }

    public sealed class SortPlan
    {
        public SortPlan(string inventoryId, IEnumerable<SortPlacement> placements)
        {
            InventoryId = inventoryId ?? throw new ArgumentNullException(nameof(inventoryId));
            Placements = new ReadOnlyCollection<SortPlacement>(new List<SortPlacement>(placements ?? throw new ArgumentNullException(nameof(placements))));
        }

        public string InventoryId { get; }
        public IReadOnlyList<SortPlacement> Placements { get; }
    }

    public enum InventoryLocationKind
    {
        Player,
        Container
    }

    public sealed class InventoryLocation
    {
        public InventoryLocation(InventoryLocationKind kind, string inventoryId, int slot)
        {
            if (string.IsNullOrWhiteSpace(inventoryId)) throw new ArgumentException("An inventory id is required.", nameof(inventoryId));
            if (slot < 0) throw new ArgumentOutOfRangeException(nameof(slot));
            Kind = kind;
            InventoryId = inventoryId;
            Slot = slot;
        }

        public InventoryLocationKind Kind { get; }
        public string InventoryId { get; }
        public int Slot { get; }
    }

    public enum TransferKind
    {
        Deposit,
        ExcessDeposit,
        Replenishment
    }

    public sealed class TransferStep
    {
        public TransferStep(
            TransferKind kind,
            InventoryLocation source,
            InventoryLocation destination,
            string compatibilityKey,
            string visibleName,
            int quantity,
            int maxStack)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            if (string.IsNullOrWhiteSpace(compatibilityKey)) throw new ArgumentException("A compatibility key is required.", nameof(compatibilityKey));
            if (visibleName == null) throw new ArgumentNullException(nameof(visibleName));
            if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
            if (maxStack <= 0) throw new ArgumentOutOfRangeException(nameof(maxStack));
            Kind = kind;
            Source = source;
            Destination = destination;
            CompatibilityKey = compatibilityKey;
            VisibleName = visibleName;
            Quantity = quantity;
            MaxStack = maxStack;
        }

        public TransferKind Kind { get; }
        public InventoryLocation Source { get; }
        public InventoryLocation Destination { get; }
        public string CompatibilityKey { get; }
        public string VisibleName { get; }
        public int Quantity { get; }
        public int MaxStack { get; }
    }

    public sealed class ReplenishmentShortage
    {
        public ReplenishmentShortage(int playerSlot, string compatibilityKey, string visibleName, int missingQuantity)
        {
            if (playerSlot < 0) throw new ArgumentOutOfRangeException(nameof(playerSlot));
            if (missingQuantity <= 0) throw new ArgumentOutOfRangeException(nameof(missingQuantity));
            PlayerSlot = playerSlot;
            CompatibilityKey = compatibilityKey ?? throw new ArgumentNullException(nameof(compatibilityKey));
            VisibleName = visibleName ?? throw new ArgumentNullException(nameof(visibleName));
            MissingQuantity = missingQuantity;
        }

        public int PlayerSlot { get; }
        public string CompatibilityKey { get; }
        public string VisibleName { get; }
        public int MissingQuantity { get; }
    }

    public sealed class SkippedContainer
    {
        public SkippedContainer(string containerId, string reason)
        {
            ContainerId = containerId ?? throw new ArgumentNullException(nameof(containerId));
            Reason = reason ?? throw new ArgumentNullException(nameof(reason));
        }

        public string ContainerId { get; }
        public string Reason { get; }
    }

    public sealed class TransferPlan
    {
        public const string PartialSearchNotice = "The storage action stopped before every nearby container was checked.";

        public TransferPlan(
            IEnumerable<TransferStep> steps,
            IEnumerable<ReplenishmentShortage> shortages,
            IEnumerable<SkippedContainer> skippedContainers,
            IEnumerable<string> inspectedContainerIds,
            int depositedUnits,
            int replenishedUnits,
            int leftBehindUnits,
            bool searchTruncated)
        {
            if (depositedUnits < 0) throw new ArgumentOutOfRangeException(nameof(depositedUnits));
            if (replenishedUnits < 0) throw new ArgumentOutOfRangeException(nameof(replenishedUnits));
            if (leftBehindUnits < 0) throw new ArgumentOutOfRangeException(nameof(leftBehindUnits));
            Steps = new ReadOnlyCollection<TransferStep>(new List<TransferStep>(steps ?? throw new ArgumentNullException(nameof(steps))));
            Shortages = new ReadOnlyCollection<ReplenishmentShortage>(new List<ReplenishmentShortage>(shortages ?? throw new ArgumentNullException(nameof(shortages))));
            SkippedContainers = new ReadOnlyCollection<SkippedContainer>(new List<SkippedContainer>(skippedContainers ?? throw new ArgumentNullException(nameof(skippedContainers))));
            InspectedContainerIds = new ReadOnlyCollection<string>(new List<string>(inspectedContainerIds ?? throw new ArgumentNullException(nameof(inspectedContainerIds))));
            DepositedUnits = depositedUnits;
            ReplenishedUnits = replenishedUnits;
            LeftBehindUnits = leftBehindUnits;
            SearchTruncated = searchTruncated;
        }

        public IReadOnlyList<TransferStep> Steps { get; }
        public IReadOnlyList<ReplenishmentShortage> Shortages { get; }
        public IReadOnlyList<SkippedContainer> SkippedContainers { get; }
        public IReadOnlyList<string> InspectedContainerIds { get; }
        public int DepositedUnits { get; }
        public int ReplenishedUnits { get; }
        public int LeftBehindUnits { get; }
        public bool SearchTruncated { get; }
        public string? Notice => SearchTruncated ? PartialSearchNotice : null;
    }

    public sealed class ValidationResult
    {
        public ValidationResult(IEnumerable<string> errors)
        {
            Errors = new ReadOnlyCollection<string>(new List<string>(errors ?? throw new ArgumentNullException(nameof(errors))));
        }

        public IReadOnlyList<string> Errors { get; }
        public bool IsValid => Errors.Count == 0;
    }
}
