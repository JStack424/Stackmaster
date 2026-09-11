#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Stackmaster.Core;
using UnityEngine;

namespace Stackmaster
{
    internal static class StorageAction
    {
        private const float OwnershipTimeoutSeconds = 2f;
        private static int _lastActionFrame = -1;
        private static bool _actionRunning;

        internal static void Update()
        {
            var plugin = RuntimeContext.Plugin;
            if (plugin == null || !plugin.StorageActionShortcut.Value.IsDown() || _lastActionFrame == Time.frameCount)
            {
                return;
            }

            var player = Player.m_localPlayer;
            var hover = player != null ? player.GetHoverObject() : null;
            var container = hover != null ? hover.GetComponentInParent<Container>() : null;
            if (container == null)
            {
                RuntimeContext.ShowTopLeft("Stackmaster: target a valid container.");
                _lastActionFrame = Time.frameCount;
                return;
            }

            Begin(player, container);
        }

        internal static bool HandleContainerInteraction(Container container, Humanoid character)
        {
            var plugin = RuntimeContext.Plugin;
            if (plugin == null || character != Player.m_localPlayer || !plugin.StorageActionShortcut.Value.IsPressed())
            {
                return false;
            }

            if (plugin.StorageActionShortcut.Value.IsDown() && _lastActionFrame != Time.frameCount)
            {
                Begin(Player.m_localPlayer, container);
            }
            return true;
        }

        private static void Begin(Player player, Container target)
        {
            _lastActionFrame = Time.frameCount;
            if (_actionRunning)
            {
                RuntimeContext.ShowTopLeft("Stackmaster: storage action already running.");
                return;
            }
            _actionRunning = true;

            try
            {
                var protection = RuntimeContext.LoadProtection(player);
                var catalog = new CompatibilityCatalog();
                var playerSnapshot = InventorySnapshots.CapturePlayer(player, protection, catalog);
                var discovery = ContainerDiscovery.Discover(
                    player,
                    target,
                    catalog,
                    RuntimeContext.Plugin.NearbyStorageRadius.Value);

                var targetHandle = discovery.Containers.FirstOrDefault(handle => handle.Container == target);
                if (targetHandle == null || !targetHandle.Snapshot.IsEligible)
                {
                    RuntimeContext.ShowTopLeft("Stackmaster: targeted container is inaccessible, in use, unknown, or outside the configured radius.");
                    _actionRunning = false;
                    return;
                }

                var planner = new StorageTransferPlanner();
                var plan = planner.Plan(playerSnapshot, discovery.Containers.Select(handle => handle.Snapshot));
                if (discovery.Truncated && !plan.SearchTruncated)
                {
                    plan = new TransferPlan(
                        plan.Steps,
                        plan.Shortages,
                        plan.SkippedContainers,
                        plan.InspectedContainerIds,
                        plan.DepositedUnits,
                        plan.ReplenishedUnits,
                        plan.LeftBehindUnits,
                        true);
                }

                var validation = PlanValidator.ValidateTransferConservation(
                    playerSnapshot,
                    discovery.Containers.Select(handle => handle.Snapshot),
                    plan);
                if (!validation.IsValid)
                {
                    RuntimeContext.Plugin.Log.LogError("Storage action plan rejected before mutation: " + string.Join("; ", validation.Errors));
                    RuntimeContext.ShowCenter("Stackmaster stopped safely: transfer plan validation failed.");
                    _actionRunning = false;
                    return;
                }

                var handles = discovery.Containers.ToDictionary(handle => handle.Id, StringComparer.Ordinal);
                var neededContainerIds = plan.Steps.Select(step => step.Source.Kind == InventoryLocationKind.Container
                        ? step.Source.InventoryId
                        : step.Destination.InventoryId)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                var neededHandles = neededContainerIds.Select(id => handles[id]).ToArray();

                if (neededHandles.Length == 0)
                {
                    ShowSummary(player, protection, plan, new TransferExecutionResult());
                    _actionRunning = false;
                    return;
                }

                RuntimeContext.ShowTopLeft("Stackmaster: checking nearby storage…");
                RuntimeContext.Plugin.StartCoroutine(FinishAfterOwnership(
                    player, protection, catalog, handles, neededHandles, plan));
            }
            catch (Exception exception)
            {
                _actionRunning = false;
                RuntimeContext.Plugin.Log.LogError("Storage action stopped safely: " + exception);
                RuntimeContext.ShowCenter("Stackmaster stopped safely: " + exception.GetType().Name + ".");
            }
        }

        private static IEnumerator FinishAfterOwnership(
            Player player,
            ProtectionState protection,
            CompatibilityCatalog catalog,
            IReadOnlyDictionary<string, ContainerHandle> handles,
            ContainerHandle[] neededHandles,
            TransferPlan plan)
        {
            OwnershipBatch ownership = null;
            try
            {
                ownership = OwnershipCoordinator.Begin(neededHandles, player);
            }
            catch (Exception exception)
            {
                _actionRunning = false;
                RuntimeContext.Plugin.Log.LogError("Storage ownership setup failed safely: " + exception);
                RuntimeContext.ShowCenter("Stackmaster stopped safely while requesting container ownership.");
                yield break;
            }

            var deadline = Time.realtimeSinceStartup + OwnershipTimeoutSeconds;
            while (!ownership.IsComplete && Time.realtimeSinceStartup < deadline)
            {
                ownership.Refresh();
                yield return null;
            }
            ownership.Refresh();
            if (!ownership.IsComplete)
            {
                ownership.Timeout();
            }

            foreach (var handle in neededHandles.Where(handle => ownership.SuccessfulContainerIds.Contains(handle.Id)))
            {
                if (!ContainerDiscovery.RefreshFromNetwork(handle.Container))
                {
                    ownership.FailedContainerIds[handle.Id] = "latest network state could not be loaded";
                }
            }

            try
            {
                var execution = TransferExecutor.Execute(player, handles, plan, catalog, ownership.FailedContainerIds);
                ShowSummary(player, protection, plan, execution);
            }
            catch (Exception exception)
            {
                RuntimeContext.Plugin.Log.LogError("Storage action stopped safely during execution: " + exception);
                RuntimeContext.ShowCenter("Stackmaster stopped safely during transfer execution.");
            }
            finally
            {
                OwnershipCoordinator.End(ownership);
                _actionRunning = false;
            }
        }

        private static void ShowSummary(Player player, ProtectionState protection, TransferPlan plan, TransferExecutionResult execution)
        {
            var depositable = plan.DepositedUnits + plan.LeftBehindUnits;
            var leftBehind = Math.Max(0, depositable - execution.DepositedUnits);
            var lines = new List<string>
            {
                "Stackmaster: " + execution.DepositedUnits + " deposited • " +
                execution.ReplenishedUnits + " replenished • " + leftBehind + " left behind"
            };

            var shortageNames = new List<string>();
            var inventory = player.GetInventory();
            foreach (var record in protection.Records.Where(value => value.TargetQuantity.HasValue).OrderBy(value => value.Slot.Row).ThenBy(value => value.Slot.Column))
            {
                var item = inventory.GetItemAt(record.Slot.Column, record.Slot.Row);
                if (item == null || !string.Equals(InventorySnapshots.PersistentItemKey(item), record.TargetItemKey, StringComparison.Ordinal))
                {
                    shortageNames.Add("slot " + (record.Slot.Row * inventory.GetWidth() + record.Slot.Column + 1) + " target missing");
                    continue;
                }

                var missing = Math.Max(0, record.TargetQuantity.Value - item.m_stack);
                if (missing > 0)
                {
                    shortageNames.Add(InventorySnapshots.VisibleName(item) + " " + missing);
                }
            }
            if (shortageNames.Count > 0)
            {
                lines.Add("Short: " + string.Join(", ", shortageNames));
            }

            var skipReasons = plan.SkippedContainers.Select(skip => skip.Reason)
                .Concat(execution.FailedContainers.Values.Select(reason => "changed/failed"))
                .GroupBy(reason => reason, StringComparer.Ordinal)
                .Select(group => group.Count() + " " + group.Key)
                .ToArray();
            if (skipReasons.Length > 0)
            {
                lines.Add("Skipped: " + string.Join(", ", skipReasons));
            }
            if (plan.SearchTruncated)
            {
                lines.Add("Partial search: time budget reached.");
            }
            if (execution.FatalPostconditionFailure)
            {
                lines.Add("Stopped after an unexpected transfer result; check the log.");
            }

            RuntimeContext.ShowTopLeft(string.Join("\n", lines));
        }
    }

    [HarmonyPatch(typeof(Container), "Interact", typeof(Humanoid), typeof(bool), typeof(bool))]
    internal static class ContainerInteractPatch
    {
        private static bool Prefix(Container __instance, Humanoid character, ref bool __result)
        {
            if (!StorageAction.HandleContainerInteraction(__instance, character))
            {
                return true;
            }

            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(Container), "GetHoverText")]
    internal static class ContainerHoverTextPatch
    {
        private static void Postfix(Container __instance, ref string __result)
        {
            if (__instance.GetType() == typeof(Container) && RuntimeContext.Plugin != null)
            {
                __result += "\n[<color=yellow>" + RuntimeContext.Plugin.StorageActionShortcut.Value + "</color>] Stackmaster: deposit + replenish";
            }
        }
    }
}
