#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using Stackmaster.Core;

namespace Stackmaster
{
    internal sealed class TransferExecutionResult
    {
        internal int DepositedUnits { get; set; }
        internal int ReplenishedUnits { get; set; }
        internal bool FatalPostconditionFailure { get; set; }
        internal Dictionary<string, string> FailedContainers { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
    }

    internal static class TransferExecutor
    {
        internal static TransferExecutionResult Execute(
            Player player,
            IReadOnlyDictionary<string, ContainerHandle> handles,
            TransferPlan plan,
            CompatibilityCatalog catalog,
            IReadOnlyDictionary<string, string> initialFailures)
        {
            var result = new TransferExecutionResult();
            foreach (var failure in initialFailures)
            {
                result.FailedContainers[failure.Key] = failure.Value;
            }
            foreach (var step in plan.Steps)
            {
                var containerId = step.Source.Kind == InventoryLocationKind.Container
                    ? step.Source.InventoryId
                    : step.Destination.InventoryId;
                if (result.FailedContainers.ContainsKey(containerId))
                {
                    continue;
                }

                string failure;
                var outcome = TryExecuteStep(player, handles, step, catalog, out failure);
                if (outcome == StepOutcome.FailedSafely)
                {
                    result.FailedContainers[containerId] = failure;
                    continue;
                }

                if (outcome == StepOutcome.FatalPostconditionFailure)
                {
                    result.FatalPostconditionFailure = true;
                    result.FailedContainers[containerId] = failure;
                    RuntimeContext.Disable("Unexpected transfer result; restart required before any further Stackmaster item changes.");
                    RuntimeContext.Plugin.Log.LogError("Transfer postcondition failed; Stackmaster disabled for this session and the remaining action aborted: " + failure);
                    break;
                }

                if (step.Kind == TransferKind.Replenishment)
                {
                    result.ReplenishedUnits += step.Quantity;
                }
                else
                {
                    result.DepositedUnits += step.Quantity;
                }
            }

            return result;
        }

        private static StepOutcome TryExecuteStep(
            Player player,
            IReadOnlyDictionary<string, ContainerHandle> handles,
            TransferStep step,
            CompatibilityCatalog catalog,
            out string failure)
        {
            failure = null;
            ContainerHandle handle;
            var containerId = step.Source.Kind == InventoryLocationKind.Container
                ? step.Source.InventoryId
                : step.Destination.InventoryId;
            if (!handles.TryGetValue(containerId, out handle))
            {
                failure = "container disappeared from the action snapshot";
                return StepOutcome.FailedSafely;
            }

            if (!RevalidateAndOwn(player, handle, out failure))
            {
                return StepOutcome.FailedSafely;
            }

            var playerInventory = player.GetInventory();
            var containerInventory = handle.Container.GetInventory();
            var sourceInventory = step.Source.Kind == InventoryLocationKind.Player ? playerInventory : containerInventory;
            var destinationInventory = step.Destination.Kind == InventoryLocationKind.Player ? playerInventory : containerInventory;
            var sourcePosition = InventorySnapshots.PositionForSlot(sourceInventory, step.Source.Slot);
            var destinationPosition = InventorySnapshots.PositionForSlot(destinationInventory, step.Destination.Slot);
            var source = sourceInventory.GetItemAt(sourcePosition.x, sourcePosition.y);
            if (source == null || source.m_stack < step.Quantity ||
                !string.Equals(catalog.KeyFor(source), step.CompatibilityKey, StringComparison.Ordinal))
            {
                failure = "source stack changed before transfer";
                return StepOutcome.FailedSafely;
            }

            var destination = destinationInventory.GetItemAt(destinationPosition.x, destinationPosition.y);
            if (destination != null)
            {
                if (!source.IsSameType(destination) || !destination.IsSameType(source) ||
                    destination.m_stack + step.Quantity > destination.m_shared.m_maxStackSize)
                {
                    failure = "destination stack changed before transfer";
                    return StepOutcome.FailedSafely;
                }
            }
            else if (step.Quantity > source.m_shared.m_maxStackSize ||
                     !destinationInventory.CanAddItem(source, step.Quantity))
            {
                failure = "destination no longer has legal capacity";
                return StepOutcome.FailedSafely;
            }

            var sourceBefore = TotalUnits(sourceInventory);
            var destinationBefore = TotalUnits(destinationInventory);
            var moved = destinationInventory.MoveItemToThis(
                sourceInventory,
                source,
                step.Quantity,
                destinationPosition.x,
                destinationPosition.y);
            var sourceAfter = TotalUnits(sourceInventory);
            var destinationAfter = TotalUnits(destinationInventory);

            var exactPostcondition = sourceAfter == sourceBefore - step.Quantity &&
                                     destinationAfter == destinationBefore + step.Quantity &&
                                     sourceAfter + destinationAfter == sourceBefore + destinationBefore;
            if (exactPostcondition)
            {
                return StepOutcome.Succeeded;
            }

            var unchanged = sourceAfter == sourceBefore && destinationAfter == destinationBefore;
            if (!moved && unchanged)
            {
                failure = "game transfer primitive declined the move";
                return StepOutcome.FailedSafely;
            }

            failure = "transfer returned " + moved + " with source " + sourceBefore + "→" + sourceAfter +
                      " and destination " + destinationBefore + "→" + destinationAfter;
            return StepOutcome.FatalPostconditionFailure;
        }

        private static bool RevalidateAndOwn(Player player, ContainerHandle handle, out string failure)
        {
            failure = null;
            var container = handle.Container;
            var view = handle.NetworkView;
            if (container == null || container.GetType() != typeof(Container) || view == null || !view.IsValid() || view.GetZDO() == null)
            {
                failure = "container is no longer a known vanilla container";
                return false;
            }
            var locallyOpenTarget = handle.Snapshot.IsTarget && StorageAction.IsLocalOpenTarget(container);
            if ((!locallyOpenTarget && container.IsInUse()) || (container.m_wagon != null && container.m_wagon.InUse()))
            {
                failure = "container became in use";
                return false;
            }
            if (!ContainerDiscovery.CheckAccess(player, container))
            {
                failure = "container is no longer accessible";
                return false;
            }

            if (!view.IsOwner() || !container.IsOwner())
            {
                failure = "container ownership could not be acquired";
                return false;
            }
            return true;
        }

        private static int TotalUnits(Inventory inventory) => inventory.GetAllItems().Sum(item => item.m_stack);

        private enum StepOutcome
        {
            Succeeded,
            FailedSafely,
            FatalPostconditionFailure
        }
    }
}
