#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Stackmaster.Core;
using UnityEngine;

namespace Stackmaster
{
    internal static class StorageAction
    {
        private const float OwnershipTimeoutSeconds = 2f;
        private static readonly FieldInfo CurrentContainerField = AccessTools.Field(typeof(InventoryGui), "m_currentContainer");
        private static readonly FieldInfo CraftTimerField = AccessTools.Field(typeof(InventoryGui), "m_craftTimer");
        private static readonly FieldInfo DragItemField = AccessTools.Field(typeof(InventoryGui), "m_dragItem");
        private static int _lastActionFrame = -1;
        private static bool _actionRunning;
        private static int _generation;

        internal static void Shutdown()
        {
            _generation++;
            _lastActionFrame = -1;
            _actionRunning = false;
        }

        internal static void RearmSession()
        {
            _lastActionFrame = -1;
            _actionRunning = false;
        }

        internal static void Update()
        {
            var plugin = RuntimeContext.Plugin;
            if (plugin == null || !plugin.StorageActionShortcut.Value.IsDown() || _lastActionFrame == Time.frameCount)
            {
                return;
            }

            var player = Player.m_localPlayer;
            var gui = InventoryGui.instance;
            var openContainer = CurrentOpenContainer(gui);
            if (openContainer != null)
            {
                if (InputIsBlocked(true))
                {
                    return;
                }

                // Valheim normally treats Use while an inventory is visible as a close command.
                // Consume only that named action for this frame; the matching narrow UI prefix
                // also suppresses the one vanilla update that could close a configured shortcut.
                ZInput.ResetButtonStatus("Use");
                Begin(player, openContainer);
                return;
            }

            if (InputIsBlocked(false))
            {
                return;
            }

            var hover = player != null ? player.GetHoverObject() : null;
            var container = hover != null ? hover.GetComponentInParent<Container>() : null;
            if (container == null)
            {
                RuntimeContext.Plugin.Log.LogWarning("Storage action rejected: targetHovered=false reason=no targeted container.");
                RuntimeContext.ShowTopLeft("Stackmaster: target a valid container.");
                _lastActionFrame = Time.frameCount;
                return;
            }

            Begin(player, container);
        }

        internal static bool HandleContainerInteraction(Container container, Humanoid character)
        {
            var plugin = RuntimeContext.Plugin;
            if (plugin == null || InputIsBlocked(false) || character != Player.m_localPlayer || !plugin.StorageActionShortcut.Value.IsPressed())
            {
                return false;
            }

            if (plugin.StorageActionShortcut.Value.IsDown() && _lastActionFrame != Time.frameCount)
            {
                Begin(Player.m_localPlayer, container);
            }
            return true;
        }

        internal static bool HandleOpenContainerShortcut(InventoryGui gui)
        {
            var plugin = RuntimeContext.Plugin;
            if (plugin == null || !plugin.StorageActionShortcut.Value.IsDown() || InputIsBlocked(true))
            {
                return true;
            }

            var openContainer = CurrentOpenContainer(gui);
            if (openContainer == null)
            {
                return true;
            }

            // Consume Valheim's named Use action and skip only this one InventoryGui.Update frame.
            // That preserves the open chest for any configured shortcut, including a binding that
            // Valheim itself would otherwise interpret as an inventory-close command.
            ZInput.ResetButtonStatus("Use");
            if (_lastActionFrame != Time.frameCount)
            {
                Begin(Player.m_localPlayer, openContainer);
            }
            return false;
        }

        internal static bool IsLocalOpenTarget(Container container)
        {
            return container != null && CurrentOpenContainer(InventoryGui.instance) == container;
        }

        private static Container CurrentOpenContainer(InventoryGui gui)
        {
            if (gui == null || !InventoryGui.IsVisible() || !gui.IsContainerOpen() || InventoryUiHasBlockingState(gui))
            {
                return null;
            }

            return CurrentContainerField?.GetValue(gui) as Container;
        }

        private static bool InventoryUiHasBlockingState(InventoryGui gui)
        {
            if (CraftTimerField == null || DragItemField == null || (float)CraftTimerField.GetValue(gui) >= 0f ||
                DragItemField.GetValue(gui) != null)
            {
                return true;
            }

            return (gui.m_trophiesPanel != null && gui.m_trophiesPanel.activeSelf)
                || (gui.m_achievementsPanel != null && gui.m_achievementsPanel.gameObject.activeSelf)
                || (gui.m_skillsDialog != null && gui.m_skillsDialog.gameObject.activeSelf)
                || (gui.m_textsDialog != null && gui.m_textsDialog.gameObject.activeSelf)
                || (gui.m_splitDialog != null && gui.m_splitDialog.IsActive)
                || (gui.m_variantDialog != null && gui.m_variantDialog.gameObject.activeSelf);
        }

        private static bool InputIsBlocked(bool allowOpenContainerUi)
        {
            var player = Player.m_localPlayer;
            var textViewer = TextViewer.instance;
            return player == null
                || player.IsDead()
                || player.InCutscene()
                || player.IsTeleporting()
                || (textViewer != null && textViewer.IsVisible())
                || GameCamera.InFreeFly()
                || !ZInput.IsKeyboardAvailable()
                || (!allowOpenContainerUi && InventoryGui.IsVisible())
                || TextInput.IsVisible()
                || UnifiedPopup.IsVisible()
                || Menu.IsVisible()
                || Console.IsVisible()
                || Minimap.IsOpen()
                || Hud.InRadial()
                || (Chat.instance != null && Chat.instance.HasFocus());
        }

        private static void Begin(Player player, Container target)
        {
            _lastActionFrame = Time.frameCount;
            if (_actionRunning)
            {
                RuntimeContext.Plugin.Log.LogWarning("Storage action rejected: actionRunning=true reason=another storage action is already running.");
                RuntimeContext.ShowTopLeft("Stackmaster: storage action already running.");
                return;
            }
            _actionRunning = true;

            try
            {
                ProtectionState protection;
                if (!RuntimeContext.TryLoadProtection(player, out protection))
                {
                    _actionRunning = false;
                    return;
                }
                var catalog = new CompatibilityCatalog();
                var playerSnapshot = InventorySnapshots.CapturePlayer(
                    player,
                    protection,
                    catalog,
                    pruneUnresolvedProtection: true);
                var scope = StorageScopeProvider.Resolve(player);
                var discovery = ContainerDiscovery.Discover(
                    player,
                    target,
                    catalog,
                    scope);

                var targetHandle = discovery.Containers.FirstOrDefault(handle => handle.Container == target);
                if (targetHandle == null || !targetHandle.Snapshot.IsEligible)
                {
                    LogTargetRejection("initial discovery", discovery);
                    RuntimeContext.ShowTopLeft("Stackmaster: targeted container is inaccessible, in use, unknown, or outside the active storage scope.");
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
                var generation = _generation;
                RuntimeContext.Plugin.StartCoroutine(FinishAfterOwnership(
                    player,
                    target,
                    neededHandles,
                    generation));
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
            Container target,
            ContainerHandle[] neededHandles,
            int generation)
        {
            if (generation != _generation || !RuntimeContext.Compatibility.IsCompatible)
            {
                yield break;
            }

            OwnershipBatch ownership = null;
            try
            {
                ownership = OwnershipCoordinator.Begin(neededHandles);
            }
            catch (Exception exception)
            {
                _actionRunning = false;
                RuntimeContext.Plugin.Log.LogError("Storage ownership setup failed safely: " + exception);
                RuntimeContext.ShowCenter("Stackmaster stopped safely while requesting container ownership.");
                yield break;
            }

            try
            {
                var refreshFailed = false;
                var deadline = Time.realtimeSinceStartup + OwnershipTimeoutSeconds;
                while (generation == _generation && RuntimeContext.Compatibility.IsCompatible &&
                       !ownership.IsComplete && Time.realtimeSinceStartup < deadline)
                {
                    try
                    {
                        ownership.Refresh();
                    }
                    catch (Exception exception)
                    {
                        refreshFailed = true;
                        RuntimeContext.Plugin.Log.LogError("Storage ownership refresh failed safely: " + exception);
                        RuntimeContext.ShowCenter("Stackmaster stopped safely while requesting container ownership.");
                    }
                    if (refreshFailed) break;
                    yield return null;
                }
                if (refreshFailed || generation != _generation || !RuntimeContext.Compatibility.IsCompatible)
                {
                    yield break;
                }

                try
                {
                    ownership.Refresh();
                if (!ownership.IsComplete)
                {
                    ownership.Timeout();
                }

                ProtectionState freshProtection;
                if (!RuntimeContext.TryLoadProtection(player, out freshProtection))
                {
                    yield break;
                }

                // Ownership transfer can cause Container.Load to replace every ItemData instance.
                // Re-capture and re-plan from the synchronized inventories so no pre-RPC object
                // reference is ever used for mutation.
                var freshCatalog = new CompatibilityCatalog();
                var freshPlayer = InventorySnapshots.CapturePlayer(
                    player,
                    freshProtection,
                    freshCatalog,
                    pruneUnresolvedProtection: true);
                var freshScope = StorageScopeProvider.Resolve(player);
                var freshDiscovery = ContainerDiscovery.Discover(player, target, freshCatalog, freshScope);
                var freshTarget = freshDiscovery.Containers.FirstOrDefault(handle => handle.Container == target);
                if (freshTarget == null || !freshTarget.Snapshot.IsEligible)
                {
                    LogTargetRejection("post-ownership refresh", freshDiscovery);
                    RuntimeContext.ShowTopLeft("Stackmaster: targeted container is inaccessible, in use, unknown, or outside the active storage scope.");
                    yield break;
                }
                var freshHandles = freshDiscovery.Containers.ToDictionary(handle => handle.Id, StringComparer.Ordinal);
                var freshPlan = new StorageTransferPlanner().Plan(
                    freshPlayer,
                    freshDiscovery.Containers.Select(handle => handle.Snapshot));
                if (freshDiscovery.Truncated && !freshPlan.SearchTruncated)
                {
                    freshPlan = new TransferPlan(
                        freshPlan.Steps,
                        freshPlan.Shortages,
                        freshPlan.SkippedContainers,
                        freshPlan.InspectedContainerIds,
                        freshPlan.DepositedUnits,
                        freshPlan.ReplenishedUnits,
                        freshPlan.LeftBehindUnits,
                        true);
                }

                var validation = PlanValidator.ValidateTransferConservation(
                    freshPlayer,
                    freshDiscovery.Containers.Select(handle => handle.Snapshot),
                    freshPlan);
                if (!validation.IsValid)
                {
                    RuntimeContext.Plugin.Log.LogError("Refreshed storage action plan rejected before mutation: " + string.Join("; ", validation.Errors));
                    RuntimeContext.ShowCenter("Stackmaster stopped safely: refreshed transfer plan validation failed.");
                }
                else
                {
                    var execution = TransferExecutor.Execute(
                        player,
                        freshScope,
                        freshHandles,
                        freshPlan,
                        freshCatalog,
                        ownership.FailedContainerIds);
                    if (execution.FatalPostconditionFailure)
                    {
                        RuntimeContext.ShowCenter("Stackmaster disabled after an unexpected transfer result. Restart Valheim before using it again.");
                    }
                    else
                    {
                        ShowSummary(player, freshProtection, freshPlan, execution);
                    }
                }
            }
                catch (Exception exception)
                {
                    RuntimeContext.Plugin.Log.LogError("Storage action stopped safely during execution: " + exception);
                    RuntimeContext.ShowCenter("Stackmaster stopped safely during transfer execution.");
                }
            }
            finally
            {
                if (generation == _generation)
                {
                    // Alt+E never needs a cross-attempt lease. Return only exact ownership newly
                    // acquired by this action after transfer rollback/validation has finished.
                    OwnershipLeaseManager.ReleaseBatch(ownership, "storage action ended");
                    OwnershipCoordinator.End(ownership);
                    _actionRunning = false;
                }
                // A disconnect invalidates the generation and RuntimeContext performs the same
                // ordered cleanup centrally. The stale coroutine must not touch a later session.
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
            var resolution = InventorySnapshots.ResolveProtection(player, protection, pruneUnresolved: true);
            foreach (var assignment in resolution.Assignments.Where(value => value.Value.TargetQuantity.HasValue)
                .OrderBy(value => value.Key.Row).ThenBy(value => value.Key.Column))
            {
                var record = assignment.Value;
                var item = inventory.GetItemAt(assignment.Key.Column, assignment.Key.Row);
                if (item == null || !string.Equals(InventorySnapshots.PersistentItemKey(item), record.TargetItemKey, StringComparison.Ordinal))
                {
                    shortageNames.Add("target item missing");
                    continue;
                }

                var missing = Math.Max(0, record.TargetQuantity.Value - item.m_stack);
                if (missing > 0)
                {
                    shortageNames.Add(InventorySnapshots.VisibleName(item) + " " + missing);
                }
            }
            var assignedRecords = new HashSet<ProtectionRecord>(resolution.Assignments.Values);
            foreach (var dormant in protection.Records.Where(record => record.TargetQuantity.HasValue && !assignedRecords.Contains(record)))
            {
                shortageNames.Add("target item missing");
            }
            if (shortageNames.Count > 0)
            {
                lines.Add("Short: " + string.Join(", ", shortageNames));
            }

            var skipReasons = plan.SkippedContainers.Select(skip => skip.Reason)
                .Concat(execution.FailedContainers.Values.Select(MeaningfulFailureReason))
                .GroupBy(reason => reason, StringComparer.Ordinal)
                .Select(group => group.Count() + " " + group.Key)
                .ToArray();
            if (skipReasons.Length > 0)
            {
                lines.Add("Skipped: " + string.Join(", ", skipReasons));
            }
            if (execution.FailedContainers.Count > 0)
            {
                RuntimeContext.Plugin.Log.LogWarning(
                    "Storage action completed with safe container rejections: " + string.Join(", ", skipReasons));
            }
            if (plan.SearchTruncated)
            {
                lines.Add("Partial search: responsiveness limit reached.");
            }
            RuntimeContext.ShowTopLeft(string.Join("\n", lines));
        }

        private static void LogTargetRejection(string stage, DiscoveryResult discovery)
        {
            RuntimeContext.Plugin.Log.LogWarning(
                "Storage action rejected at " + stage + ": " +
                discovery.TargetDiagnostic.Format(discovery));
        }

        private static string MeaningfulFailureReason(string reason)
        {
            if (reason.IndexOf("timed out", StringComparison.OrdinalIgnoreCase) >= 0) return "ownership timeout";
            if (reason.IndexOf("still pending", StringComparison.OrdinalIgnoreCase) >= 0) return "ownership pending";
            if (reason.IndexOf("busy", StringComparison.OrdinalIgnoreCase) >= 0 ||
                reason.IndexOf("unavailable", StringComparison.OrdinalIgnoreCase) >= 0) return "busy/unavailable";
            if (reason.IndexOf("access", StringComparison.OrdinalIgnoreCase) >= 0) return "inaccessible";
            if (reason.IndexOf("in use", StringComparison.OrdinalIgnoreCase) >= 0) return "in use";
            return "changed/failed";
        }
    }

    [HarmonyPatch(typeof(InventoryGui), "Update")]
    internal static class InventoryGuiStorageActionPatch
    {
        private static bool Prefix(InventoryGui __instance)
        {
            return !RuntimeContext.Compatibility.IsCompatible || StorageAction.HandleOpenContainerShortcut(__instance);
        }

        private static void Postfix(InventoryGui __instance)
        {
            CraftingPreflightAction.Update(__instance);
        }
    }

    [HarmonyPatch(typeof(Container), "Interact", typeof(Humanoid), typeof(bool), typeof(bool))]
    internal static class ContainerInteractPatch
    {
        private static bool Prefix(Container __instance, Humanoid character, ref bool __result)
        {
            if (!RuntimeContext.Compatibility.IsCompatible || !StorageAction.HandleContainerInteraction(__instance, character))
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
            if (RuntimeContext.Compatibility.IsCompatible && __instance.GetType() == typeof(Container) && RuntimeContext.Plugin != null)
            {
                var shortcut = RuntimeContext.Plugin.StorageActionShortcut.Value;
                var shortcutText = string.Join(" + ", shortcut.Modifiers.Select(key => key.ToString())
                    .Concat(new[] { shortcut.MainKey.ToString() })
                    .ToArray());
                __result += "\n[<color=yellow>" + shortcutText + "</color>] Auto-Stack All";
            }
        }
    }
}
