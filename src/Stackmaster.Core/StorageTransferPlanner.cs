using System;
using System.Collections.Generic;
using System.Linq;

namespace Stackmaster.Core
{
    /// <summary>
    /// Produces an ordered transfer plan over private working copies. It never mutates the
    /// supplied snapshots (or any game object represented by them).
    /// </summary>
    public sealed class StorageTransferPlanner
    {
        public TransferPlan Plan(
            InventorySnapshot player,
            IEnumerable<ContainerSnapshot> containers,
            IPlanningBudget? budget = null)
        {
            if (player == null) throw new ArgumentNullException(nameof(player));
            if (containers == null) throw new ArgumentNullException(nameof(containers));
            budget = budget ?? UnlimitedPlanningBudget.Instance;

            var orderedSnapshots = containers
                .OrderBy(container => container.IsTarget ? 0 : 1)
                .ThenBy(container => container.IsTarget ? 0d : container.Distance)
                .ThenBy(container => container.ContainerId, StringComparer.Ordinal)
                .ToList();
            ValidateContainers(orderedSnapshots);

            var inspectedIds = new List<string>();
            var skipped = new List<SkippedContainer>();
            var routedContainers = new List<WorkingContainer>();
            var searchTruncated = false;

            for (var index = 0; index < orderedSnapshots.Count; index++)
            {
                var snapshot = orderedSnapshots[index];
                if (!budget.TryConsume(new PlanningWork(PlanningWorkKind.ContainerInspection, index, snapshot.ContainerId)))
                {
                    searchTruncated = true;
                    break;
                }

                inspectedIds.Add(snapshot.ContainerId);
                if (!snapshot.IsEligible)
                {
                    skipped.Add(new SkippedContainer(snapshot.ContainerId, IneligibleReason(snapshot)));
                    continue;
                }

                routedContainers.Add(new WorkingContainer(snapshot));
            }

            var playerStacks = player.Items.ToDictionary(item => item.Slot, item => new WorkingStack(item));
            var steps = new List<TransferStep>();
            var shortages = new List<ReplenishmentShortage>();
            var replenishedUnits = PlanReplenishment(player, playerStacks, routedContainers, steps, shortages);
            var depositResult = PlanDeposits(player, playerStacks, routedContainers, steps);

            return new TransferPlan(
                steps,
                shortages,
                skipped,
                inspectedIds,
                depositResult.Deposited,
                replenishedUnits,
                depositResult.LeftBehind,
                searchTruncated);
        }

        private static int PlanReplenishment(
            InventorySnapshot player,
            IDictionary<int, WorkingStack> playerStacks,
            IList<WorkingContainer> containers,
            IList<TransferStep> steps,
            IList<ReplenishmentShortage> shortages)
        {
            var replenished = 0;
            foreach (var protectedItem in player.Items
                .Where(item => item.IsProtected && item.ReplenishmentTarget.HasValue)
                .OrderBy(item => item.Slot))
            {
                var playerStack = playerStacks[protectedItem.Slot];
                var target = protectedItem.ReplenishmentTarget!.Value;
                var needed = Math.Max(0, target - playerStack.Quantity);
                if (needed == 0) continue;

                foreach (var container in containers)
                {
                    foreach (var source in container.Stacks
                        .Where(pair => pair.Value.CompatibilityKey == protectedItem.CompatibilityKey && pair.Value.Quantity > 0)
                        .OrderBy(pair => pair.Key)
                        .ToList())
                    {
                        if (needed == 0) break;
                        var moved = Math.Min(needed, source.Value.Quantity);
                        source.Value.Quantity -= moved;
                        playerStack.Quantity += moved;
                        needed -= moved;
                        replenished += moved;
                        steps.Add(new TransferStep(
                            TransferKind.Replenishment,
                            new InventoryLocation(InventoryLocationKind.Container, container.Id, source.Key),
                            new InventoryLocation(InventoryLocationKind.Player, player.InventoryId, protectedItem.Slot),
                            protectedItem.CompatibilityKey,
                            protectedItem.VisibleName,
                            moved,
                            protectedItem.MaxStack));
                        if (source.Value.Quantity == 0) container.Stacks.Remove(source.Key);
                    }
                    if (needed == 0) break;
                }

                if (needed > 0)
                {
                    shortages.Add(new ReplenishmentShortage(
                        protectedItem.Slot,
                        protectedItem.CompatibilityKey,
                        protectedItem.VisibleName,
                        needed));
                }
            }
            return replenished;
        }

        private static DepositResult PlanDeposits(
            InventorySnapshot player,
            IDictionary<int, WorkingStack> playerStacks,
            IList<WorkingContainer> containers,
            IList<TransferStep> steps)
        {
            var deposited = 0;
            var leftBehind = 0;

            foreach (var original in player.Items.OrderBy(item => item.Slot))
            {
                var source = playerStacks[original.Slot];
                var excess = original.IsProtected && original.ReplenishmentTarget.HasValue;
                var available = excess
                    ? Math.Max(0, source.Quantity - original.ReplenishmentTarget!.Value)
                    : original.IsFixed ? 0 : source.Quantity;
                if (available == 0) continue;

                var matchingContainers = containers
                    .Where(container => container.InitialCompatibilityKeys.Contains(source.CompatibilityKey))
                    .ToList();
                if (matchingContainers.Count == 0)
                {
                    leftBehind += available;
                    continue;
                }

                var remaining = available;
                // Global pass one: every existing compatible partial stack, in routing and slot order.
                foreach (var container in matchingContainers)
                {
                    foreach (var destination in container.Stacks
                        .Where(pair => pair.Value.CompatibilityKey == source.CompatibilityKey && pair.Value.Quantity < pair.Value.MaxStack)
                        .OrderBy(pair => pair.Key)
                        .ToList())
                    {
                        if (remaining == 0) break;
                        var moved = Math.Min(remaining, destination.Value.MaxStack - destination.Value.Quantity);
                        MoveFromPlayer(player, original, source, container, destination.Key, moved, excess, steps);
                        destination.Value.Quantity += moved;
                        remaining -= moved;
                        deposited += moved;
                    }
                    if (remaining == 0) break;
                }

                // Global pass two: only after all compatible partials are full, create stacks.
                if (remaining > 0)
                {
                    foreach (var container in matchingContainers)
                    {
                        foreach (var destinationSlot in container.EmptySlots().ToList())
                        {
                            if (remaining == 0) break;
                            var moved = Math.Min(remaining, source.MaxStack);
                            MoveFromPlayer(player, original, source, container, destinationSlot, moved, excess, steps);
                            container.Stacks.Add(destinationSlot, new WorkingStack(
                                source.CompatibilityKey,
                                source.VisibleName,
                                moved,
                                source.MaxStack));
                            remaining -= moved;
                            deposited += moved;
                        }
                        if (remaining == 0) break;
                    }
                }

                leftBehind += remaining;
            }

            return new DepositResult(deposited, leftBehind);
        }

        private static void MoveFromPlayer(
            InventorySnapshot player,
            ItemStackSnapshot original,
            WorkingStack source,
            WorkingContainer destination,
            int destinationSlot,
            int quantity,
            bool excess,
            IList<TransferStep> steps)
        {
            source.Quantity -= quantity;
            steps.Add(new TransferStep(
                excess ? TransferKind.ExcessDeposit : TransferKind.Deposit,
                new InventoryLocation(InventoryLocationKind.Player, player.InventoryId, original.Slot),
                new InventoryLocation(InventoryLocationKind.Container, destination.Id, destinationSlot),
                source.CompatibilityKey,
                source.VisibleName,
                quantity,
                source.MaxStack));
        }

        private static void ValidateContainers(IList<ContainerSnapshot> containers)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var targetCount = 0;
            foreach (var container in containers)
            {
                if (!ids.Add(container.ContainerId)) throw new ArgumentException("Container ids must be unique.", nameof(containers));
                if (container.IsTarget) targetCount++;
            }
            if (targetCount > 1) throw new ArgumentException("At most one targeted container is allowed.", nameof(containers));
        }

        private static string IneligibleReason(ContainerSnapshot container)
        {
            if (container.IsInUse) return "in use";
            if (!container.IsKnown) return "unknown container type";
            if (!container.IsVanilla) return "non-vanilla container";
            if (!container.IsAccessible) return "inaccessible";
            return "ineligible";
        }

        private sealed class WorkingContainer
        {
            public WorkingContainer(ContainerSnapshot snapshot)
            {
                Id = snapshot.ContainerId;
                Capacity = snapshot.Capacity;
                Stacks = snapshot.Items.ToDictionary(item => item.Slot, item => new WorkingStack(item));
                InitialCompatibilityKeys = new HashSet<string>(snapshot.Items.Select(item => item.CompatibilityKey), StringComparer.Ordinal);
            }

            public string Id { get; }
            public int Capacity { get; }
            public Dictionary<int, WorkingStack> Stacks { get; }
            public HashSet<string> InitialCompatibilityKeys { get; }

            public IEnumerable<int> EmptySlots()
            {
                for (var slot = 0; slot < Capacity; slot++)
                    if (!Stacks.ContainsKey(slot)) yield return slot;
            }
        }

        private sealed class WorkingStack
        {
            public WorkingStack(ItemStackSnapshot snapshot)
                : this(snapshot.CompatibilityKey, snapshot.VisibleName, snapshot.Quantity, snapshot.MaxStack)
            {
            }

            public WorkingStack(string compatibilityKey, string visibleName, int quantity, int maxStack)
            {
                CompatibilityKey = compatibilityKey;
                VisibleName = visibleName;
                Quantity = quantity;
                MaxStack = maxStack;
            }

            public string CompatibilityKey { get; }
            public string VisibleName { get; }
            public int Quantity { get; set; }
            public int MaxStack { get; }
        }

        private sealed class DepositResult
        {
            public DepositResult(int deposited, int leftBehind)
            {
                Deposited = deposited;
                LeftBehind = leftBehind;
            }

            public int Deposited { get; }
            public int LeftBehind { get; }
        }
    }
}
