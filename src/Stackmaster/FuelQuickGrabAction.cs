#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Stackmaster.Core;
using UnityEngine;

namespace Stackmaster
{
    internal sealed class FuelQuickGrabItem
    {
        internal FuelQuickGrabItem(string prefabName, string itemName, string displayName, int quality, int maximumStack)
        {
            PrefabName = prefabName;
            ItemName = itemName;
            DisplayName = displayName;
            Quality = quality;
            MaximumStack = maximumStack;
        }

        internal string PrefabName { get; }
        internal string ItemName { get; }
        internal string DisplayName { get; }
        internal int Quality { get; }
        internal int MaximumStack { get; }
    }

    internal sealed class FuelQuickGrabTarget
    {
        internal FuelQuickGrabTarget(string displayName, IEnumerable<FuelQuickGrabItem> priorities)
        {
            DisplayName = displayName;
            Priorities = (priorities ?? Array.Empty<FuelQuickGrabItem>()).Where(item => item != null).ToArray();
        }

        internal string DisplayName { get; }
        internal IReadOnlyList<FuelQuickGrabItem> Priorities { get; }
    }

    internal static class FuelQuickGrabTargetResolver
    {
        internal static bool TryResolve(GameObject hoveredObject, out FuelQuickGrabTarget target)
        {
            target = null;
            if (hoveredObject == null) return false;

            try
            {
                var piece = hoveredObject.GetComponentInParent<Piece>();
                if (piece == null || piece.gameObject == null || !piece.gameObject.activeInHierarchy ||
                    !piece.m_enabled || !piece.IsPlacedByPlayer() || hoveredObject.GetComponentInParent<Container>() != null)
                {
                    return false;
                }
                var piecePrefabName = Utils.GetPrefabName(piece.gameObject);
                if (string.IsNullOrEmpty(piecePrefabName) ||
                    string.Equals(piecePrefabName, "charred_shieldgenerator", StringComparison.Ordinal))
                {
                    return false;
                }
                var displayName = Localize(piece.m_name);

                var shieldGenerator = piece.GetComponentInChildren<ShieldGenerator>(true);
                if (shieldGenerator != null && string.Equals(piecePrefabName, "piece_shieldgenerator", StringComparison.Ordinal))
                {
                    return TryResolveList(displayName, shieldGenerator.m_fuelItems, out target);
                }

                var siegeMachine = piece.GetComponentInChildren<SiegeMachine>(true);
                if (siegeMachine != null && siegeMachine.m_engine != null &&
                    string.Equals(piecePrefabName, "BatteringRam", StringComparison.Ordinal))
                {
                    return TryResolveConversions(displayName, siegeMachine.m_engine.m_conversion, out target);
                }

                var fireplace = piece.GetComponentInChildren<Fireplace>(true);
                if (fireplace != null && fireplace.m_canRefill && !fireplace.m_infiniteFuel && fireplace.m_fuelItem != null &&
                    fireplace.m_maxFuel > 0f && !float.IsNaN(fireplace.m_maxFuel) && !float.IsInfinity(fireplace.m_maxFuel))
                {
                    return TryResolveDirect(displayName, fireplace.m_fuelItem, out target);
                }

                var cookingStation = piece.GetComponentInChildren<CookingStation>(true);
                if (cookingStation != null && cookingStation.m_useFuel && cookingStation.m_maxFuel > 0 &&
                    cookingStation.m_fuelItem != null)
                {
                    return TryResolveDirect(displayName, cookingStation.m_fuelItem, out target);
                }

                var smelter = piece.GetComponentInChildren<Smelter>(true);
                if (smelter != null && smelter.m_maxFuel > 0 && smelter.m_fuelItem != null)
                {
                    return TryResolveDirect(displayName, smelter.m_fuelItem, out target);
                }
                if (smelter != null && string.Equals(piecePrefabName, "charcoal_kiln", StringComparison.Ordinal))
                {
                    return TryResolveConversions(displayName, smelter.m_conversion, out target);
                }
            }
            catch (Exception exception)
            {
                RuntimeContext.Plugin?.Log.LogWarning("Fuel Quick Grab target resolution failed safely: " + exception.GetType().Name);
            }
            return false;
        }

        private static bool TryResolveDirect(string displayName, ItemDrop fuelItem, out FuelQuickGrabTarget target)
        {
            target = null;
            FuelQuickGrabItem item;
            if (!TryCreate(fuelItem, out item)) return false;
            target = new FuelQuickGrabTarget(displayName, new[] { item });
            return true;
        }

        private static bool TryResolveList(string displayName, IEnumerable<ItemDrop> fuelItems, out FuelQuickGrabTarget target)
        {
            target = null;
            if (fuelItems == null) return false;
            var items = new List<FuelQuickGrabItem>();
            foreach (var fuelItem in fuelItems)
            {
                FuelQuickGrabItem item;
                if (!TryCreate(fuelItem, out item)) return false;
                items.Add(item);
            }
            if (items.Count == 0) return false;
            target = new FuelQuickGrabTarget(displayName, items);
            return true;
        }

        private static bool TryResolveConversions(
            string displayName,
            IEnumerable<Smelter.ItemConversion> conversions,
            out FuelQuickGrabTarget target)
        {
            target = null;
            if (conversions == null) return false;
            var items = new List<FuelQuickGrabItem>();
            foreach (var conversion in conversions)
            {
                FuelQuickGrabItem item;
                if (conversion == null || !TryCreate(conversion.m_from, out item)) return false;
                if (items.Any(existing => string.Equals(existing.PrefabName, item.PrefabName, StringComparison.Ordinal))) continue;
                items.Add(item);
            }
            if (items.Count == 0) return false;
            target = new FuelQuickGrabTarget(displayName, items);
            return true;
        }

        private static bool TryCreate(ItemDrop itemDrop, out FuelQuickGrabItem item)
        {
            item = null;
            if (itemDrop == null || itemDrop.m_itemData == null || itemDrop.m_itemData.m_shared == null ||
                itemDrop.m_itemData.m_shared.m_maxStackSize <= 0) return false;
            string prefabName;
            int quality;
            if (!ExpeditionReservations.TryGetStableItemIdentity(itemDrop.m_itemData, out prefabName, out quality)) return false;
            item = new FuelQuickGrabItem(
                prefabName,
                itemDrop.m_itemData.m_shared.m_name,
                Localize(itemDrop.m_itemData.m_shared.m_name),
                quality,
                itemDrop.m_itemData.m_shared.m_maxStackSize);
            return true;
        }

        private static string Localize(string token)
        {
            try
            {
                return Localization.instance != null ? Localization.instance.Localize(token) : token;
            }
            catch
            {
                return token;
            }
        }
    }

    internal static class FuelQuickGrabAction
    {
        private const float OwnershipTimeoutSeconds = 2f;
        private static readonly QuickGrabMaterialsWithdrawalPlanner WithdrawalPlanner = new QuickGrabMaterialsWithdrawalPlanner();
        private static bool _running;
        private static int _generation;

        internal static void Begin(Player player, FuelQuickGrabTarget target)
        {
            if (_running)
            {
                RuntimeContext.ShowTopLeft("Stackmaster: Fuel Quick Grab already running.");
                return;
            }
            if (player == null || target == null || target.Priorities.Count == 0 ||
                !ReferenceEquals(player, Player.m_localPlayer) || !RuntimeContext.Compatibility.IsCompatible)
            {
                return;
            }

            _running = true;
            var generation = _generation;
            try
            {
                FuelTransferPlan prepared;
                string failure;
                bool expectedUnavailable;
                if (!TryPrepare(player, target, out prepared, out failure, out expectedUnavailable))
                {
                    ShowFailure(failure, expectedUnavailable);
                    Complete(generation);
                    return;
                }

                QuickGrabCapacityPlan ignored;
                if (!QuickGrabMaterialsAction.TryPlanCapacity(player, prepared.Capture, prepared.Withdrawal, out ignored, out failure, out expectedUnavailable))
                {
                    ShowFailure(failure, expectedUnavailable);
                    Complete(generation);
                    return;
                }
                if (prepared.Handles.Any(handle => OwnershipLeaseManager.HasPotentialAcquisition(handle.Id)))
                {
                    ShowFailure("a previous ownership transition is still pending", false);
                    Complete(generation);
                    return;
                }

                var unowned = prepared.Handles
                    .Where(handle => !handle.NetworkView.IsOwner() || !handle.Container.IsOwner())
                    .ToArray();
                if (unowned.Length == 0)
                {
                    Finish(player, target, prepared);
                    Complete(generation);
                    return;
                }

                RuntimeContext.ShowTopLeft("Stackmaster: checking home storage for " + prepared.Selected.DisplayName + "…");
                RuntimeContext.Plugin.StartCoroutine(FinishAfterOwnership(player, target, prepared, unowned, generation));
            }
            catch (Exception exception)
            {
                RuntimeContext.Plugin?.Log.LogError("Fuel Quick Grab stopped safely: " + exception);
                ShowFailure("the fuel transfer stopped safely", false);
                Complete(generation);
            }
        }

        internal static void Shutdown()
        {
            _generation++;
            _running = false;
        }

        internal static void RearmSession()
        {
            _running = false;
        }

        private static IEnumerator FinishAfterOwnership(
            Player player,
            FuelQuickGrabTarget target,
            FuelTransferPlan expected,
            ContainerHandle[] unownedHandles,
            int generation)
        {
            if (generation != _generation || !RuntimeContext.Compatibility.IsCompatible) yield break;

            OwnershipBatch ownership;
            try
            {
                ownership = OwnershipCoordinator.Begin(unownedHandles);
            }
            catch (Exception exception)
            {
                RuntimeContext.Plugin?.Log.LogError("Fuel Quick Grab ownership setup failed safely: " + exception);
                ShowFailure("home storage ownership could not be acquired", false);
                Complete(generation);
                yield break;
            }

            try
            {
                var refreshFailed = false;
                var deadline = Time.realtimeSinceStartup + OwnershipTimeoutSeconds;
                while (generation == _generation && ReferenceEquals(player, Player.m_localPlayer) &&
                       !ownership.IsComplete && Time.realtimeSinceStartup < deadline)
                {
                    try { ownership.Refresh(); }
                    catch (Exception exception)
                    {
                        refreshFailed = true;
                        RuntimeContext.Plugin?.Log.LogError("Fuel Quick Grab ownership refresh failed safely: " + exception);
                        ShowFailure("home storage ownership could not be acquired", false);
                    }
                    if (refreshFailed) yield break;
                    yield return null;
                }

                try
                {
                    if (generation != _generation || !RuntimeContext.Compatibility.IsCompatible ||
                        !ReferenceEquals(player, Player.m_localPlayer)) yield break;

                    ownership.Refresh();
                    if (!ownership.IsComplete) ownership.Timeout();
                    if (ownership.FailedContainerIds.Count > 0)
                    {
                        var busy = ownership.OwnerRejectedContainerIds.Count > 0 && unownedHandles
                            .Where(handle => ownership.OwnerRejectedContainerIds.Contains(handle.Id))
                            .All(handle => handle.NetworkView != null && handle.NetworkView.IsValid() &&
                                handle.NetworkView.HasOwner() && ContainerDiscovery.CheckAccess(player, handle.Container));
                        ShowFailure(busy ? NearbyResourceOwnership.InUseMessage : "home storage ownership could not be acquired", false);
                    }
                    else
                    {
                        Finish(player, target, expected);
                    }
                }
                catch (Exception exception)
                {
                    RuntimeContext.Plugin?.Log.LogError("Fuel Quick Grab ownership failed safely: " + exception);
                    ShowFailure("the fuel transfer stopped safely", false);
                }
            }
            finally
            {
                if (generation == _generation)
                {
                    try
                    {
                        if (!ownership.IsComplete) ownership.Timeout();
                        OwnershipLeaseManager.ReleaseBatch(ownership, "fuel quick-grab action ended");
                    }
                    catch (Exception exception)
                    {
                        RuntimeContext.Plugin?.Log.LogError("Fuel Quick Grab ownership cleanup failed: " + exception);
                        RuntimeContext.Disable("Stackmaster could not finish Fuel Quick Grab ownership cleanup.");
                    }
                    finally
                    {
                        try { OwnershipCoordinator.End(ownership); }
                        catch (Exception exception)
                        {
                            RuntimeContext.Plugin?.Log.LogError("Fuel Quick Grab ownership teardown failed: " + exception);
                            RuntimeContext.Disable("Stackmaster could not finish Fuel Quick Grab ownership teardown.");
                        }
                        Complete(generation);
                    }
                }
            }
        }

        private static void Finish(Player player, FuelQuickGrabTarget target, FuelTransferPlan expected)
        {
            FuelTransferPlan prepared;
            string failure;
            bool expectedUnavailable;
            if (!TryPrepare(player, target, out prepared, out failure, out expectedUnavailable) ||
                !SamePlan(expected, prepared) ||
                prepared.Handles.Any(handle => !handle.NetworkView.IsOwner() || !handle.Container.IsOwner()))
            {
                ShowFailure(failure ?? "home fuel stock changed before transfer", false);
                return;
            }

            NearbyResourceCapture liveCapture;
            ContainerHandle[] liveHandles;
            if (!NearbyResourceService.TryCaptureOwnedQuickGrabInventories(
                    player, prepared.Capture, prepared.Handles, true,
                    out liveCapture, out liveHandles, out failure))
            {
                ShowFailure(failure, false);
                return;
            }

            FuelTransferPlan live;
            if (!TryPlanFromCapture(player, target, liveCapture, out live, out failure, out expectedUnavailable) ||
                !SamePlan(expected, live) ||
                liveHandles.Any(handle => !handle.NetworkView.IsOwner() || !handle.Container.IsOwner()))
            {
                ShowFailure(failure ?? "home fuel stock changed during transfer preparation", false);
                return;
            }
            live = new FuelTransferPlan(live.Selected, liveCapture, live.Withdrawal, liveHandles);

            IReadOnlyList<ContainerReservation> reservations;
            if (!NearbyResourceService.TryReserveContainers(player, live.Capture.Scope, live.Handles, false, out reservations, out failure))
            {
                ShowFailure(failure, false);
                return;
            }

            try
            {
                QuickGrabCapacityPlan capacity;
                if (!NearbyResourceService.RevalidateReservedContainers(player, live.Capture.Scope, reservations, out failure) ||
                    !NearbyResourceService.RevalidateStacks(live.Withdrawal, live.Capture, true, out failure) ||
                    !QuickGrabMaterialsAction.TryPlanCapacity(player, live.Capture, live.Withdrawal, out capacity, out failure, out expectedUnavailable))
                {
                    ShowFailure(failure, expectedUnavailable);
                    return;
                }
                if (!QuickGrabMaterialsAction.ExecuteAtomic(player, live.Capture, live.Withdrawal, capacity, reservations, out failure))
                {
                    ShowFailure(failure, false);
                    return;
                }

                NearbyResourceService.ResetCaches();
                NearbyBuildHudPatch.ResetCache();
                RuntimeContext.ShowTopLeft("Stackmaster: grabbed " + live.Withdrawal.RequiredUnits.ToString(CultureInfo.InvariantCulture) +
                    " " + live.Selected.DisplayName + " from home storage.");
            }
            finally
            {
                var released = false;
                try { released = NearbyResourceService.ReleaseReservations(reservations, false); }
                catch (Exception exception) { RuntimeContext.Plugin?.Log.LogError("Fuel Quick Grab reservation cleanup threw: " + exception); }
                if (!released) RuntimeContext.Disable("Stackmaster could not fully release a Fuel Quick Grab reservation.");
            }
        }

        private static bool TryPrepare(
            Player player,
            FuelQuickGrabTarget target,
            out FuelTransferPlan prepared,
            out string failure,
            out bool expectedUnavailable)
        {
            prepared = null;
            failure = null;
            expectedUnavailable = false;
            var capture = NearbyResourceService.CaptureForQuickGrab(player, true, true);
            return TryPlanFromCapture(player, target, capture, out prepared, out failure, out expectedUnavailable);
        }

        private static bool TryPlanFromCapture(
            Player player,
            FuelQuickGrabTarget target,
            NearbyResourceCapture capture,
            out FuelTransferPlan prepared,
            out string failure,
            out bool expectedUnavailable)
        {
            prepared = null;
            failure = null;
            expectedUnavailable = false;
            if (capture == null || capture.Scope == null || capture.Scope.Kind != StorageScopeKind.WorkbenchMesh)
            {
                failure = "stand inside a connected Workbench home";
                expectedUnavailable = true;
                return false;
            }

            var eligibleByPrefab = target.Priorities.ToDictionary(
                candidate => candidate.PrefabName,
                candidate => capture.Stacks.Where(stack =>
                {
                    RuntimeResourceStack runtime;
                    string prefabName;
                    int quality;
                    return !string.Equals(stack.InventoryId, "player", StringComparison.Ordinal) &&
                        capture.RuntimeStacks.TryGetValue(stack.StackId, out runtime) &&
                        ExpeditionReservations.TryGetStableItemIdentity(runtime.Item, out prefabName, out quality) &&
                        string.Equals(prefabName, candidate.PrefabName, StringComparison.Ordinal) &&
                        quality == candidate.Quality;
                }).ToArray(),
                StringComparer.Ordinal);
            var selection = FuelQuickGrabPolicy.Select(target.Priorities.Select(candidate =>
                new FuelQuickGrabCandidate(
                    candidate.PrefabName,
                    eligibleByPrefab[candidate.PrefabName].Sum(stack => checked(stack.Quantity)),
                    candidate.MaximumStack)));
            if (selection != null)
            {
                var candidate = target.Priorities.First(item =>
                    string.Equals(item.PrefabName, selection.Identity, StringComparison.Ordinal));
                var eligible = eligibleByPrefab[candidate.PrefabName];
                var requirement = new ResourceRequirement(candidate.ItemName, selection.Quantity, candidate.Quality);
                var withdrawal = WithdrawalPlanner.Plan(new[] { requirement }, eligible);
                if (!withdrawal.IsSatisfiable || withdrawal.PlannedUnits != selection.Quantity)
                {
                    failure = "home fuel stock could not be planned exactly";
                    return false;
                }
                ContainerHandle[] handles;
                if (!NearbyResourceService.TryResolveRequiredContainers(player, withdrawal, capture, out handles, out failure))
                {
                    return false;
                }
                prepared = new FuelTransferPlan(candidate, capture, withdrawal, handles);
                return true;
            }

            failure = "no recognized fuel is available in this Workbench home";
            expectedUnavailable = true;
            return false;
        }

        private static bool SamePlan(FuelTransferPlan first, FuelTransferPlan second)
            => first != null && second != null &&
               string.Equals(first.Selected.PrefabName, second.Selected.PrefabName, StringComparison.Ordinal) &&
               QuickGrabMaterialsWithdrawalPlanner.PlansAreIdentical(first.Withdrawal, second.Withdrawal);

        private static void ShowFailure(string failure, bool expectedUnavailable)
        {
            var message = string.IsNullOrWhiteSpace(failure) ? "the fuel transfer could not be completed safely" : failure;
            if (!expectedUnavailable) RuntimeContext.Plugin?.Log.LogWarning("Fuel Quick Grab rejected: " + message);
            RuntimeContext.ShowTopLeft("Stackmaster: " + message + ".");
        }

        private static void Complete(int generation)
        {
            if (generation == _generation) _running = false;
        }

        private sealed class FuelTransferPlan
        {
            internal FuelTransferPlan(FuelQuickGrabItem selected, NearbyResourceCapture capture, ResourceWithdrawalPlan withdrawal, ContainerHandle[] handles)
            {
                Selected = selected;
                Capture = capture;
                Withdrawal = withdrawal;
                Handles = handles ?? Array.Empty<ContainerHandle>();
            }

            internal FuelQuickGrabItem Selected { get; }
            internal NearbyResourceCapture Capture { get; }
            internal ResourceWithdrawalPlan Withdrawal { get; }
            internal ContainerHandle[] Handles { get; }
        }
    }
}
