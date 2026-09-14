#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using Stackmaster.Core;
using UnityEngine;

namespace Stackmaster
{
    internal static class ExpeditionKitClickPatch
    {
        internal static bool Prefix([HarmonyArgument(0)] Piece piece)
        {
            var intercepting = false;
            try
            {
                var plugin = RuntimeContext.Plugin;
                var player = Player.m_localPlayer;
                var modifiers = plugin != null
                    ? plugin.StorageActionShortcut.Value.Modifiers.ToArray()
                    : Array.Empty<KeyCode>();
                var modifierStates = modifiers.Select(Input.GetKey).ToArray();
                var hasMaterialRecipe = piece != null && (piece.m_resources ?? Array.Empty<Piece.Requirement>())
                    .Any(requirement => requirement != null && requirement.m_resItem != null && requirement.m_amount > 0);
                if (!ExpeditionClickPolicy.ShouldIntercept(
                        RuntimeContext.Compatibility.IsCompatible,
                        player != null,
                        hasMaterialRecipe,
                        modifierStates))
                {
                    return true;
                }

                // Suppress BuildUi.OnSelectPiece: no selection change, button sound, or
                // Hud.CloseBuildUi. Every modified click is one independent complete-kit request.
                intercepting = true;
                ExpeditionKitAction.Begin(player, piece);
                return false;
            }
            catch (Exception exception)
            {
                NearbyHudFailOpen.ReportOnce("expedition-kit click", exception);
                // Once the configured modified click is recognized, every outcome stays on
                // the expedition path so an internal failure cannot select the piece/close the menu.
                return !intercepting;
            }
        }
    }

    internal sealed class ExpeditionInventoryBackup
    {
        internal ExpeditionInventoryBackup(Inventory inventory)
        {
            Inventory = inventory;
            Items = inventory.GetAllItems()
                .Select(item => item.Clone())
                .OrderBy(item => item.m_gridPos.y)
                .ThenBy(item => item.m_gridPos.x)
                .ToArray();
        }

        internal Inventory Inventory { get; }
        internal ItemDrop.ItemData[] Items { get; }
    }

    internal sealed class CompletedExpeditionMove
    {
        internal CompletedExpeditionMove(
            RuntimeResourceStack source,
            ItemDrop.ItemData sourceClone,
            Vector2i sourcePosition,
            Vector2i destinationPosition,
            string compatibilityKey,
            int quantity,
            ContainerReservation reservation)
        {
            Source = source;
            SourceClone = sourceClone;
            SourcePosition = sourcePosition;
            DestinationPosition = destinationPosition;
            CompatibilityKey = compatibilityKey;
            Quantity = quantity;
            Reservation = reservation;
        }

        internal RuntimeResourceStack Source { get; }
        internal ItemDrop.ItemData SourceClone { get; }
        internal Vector2i SourcePosition { get; }
        internal Vector2i DestinationPosition { get; }
        internal string CompatibilityKey { get; }
        internal int Quantity { get; }
        internal ContainerReservation Reservation { get; }
    }

    internal static class ExpeditionKitAction
    {
        private const float OwnershipTimeoutSeconds = 2f;
        private static readonly ExpeditionKitWithdrawalPlanner WithdrawalPlanner = new ExpeditionKitWithdrawalPlanner();
        private static readonly ExpeditionCapacityPlanner CapacityPlanner = new ExpeditionCapacityPlanner();
        private static readonly MethodInfo AddItemAtMethod = AccessTools.DeclaredMethod(
            typeof(Inventory),
            "AddItem",
            new[] { typeof(ItemDrop.ItemData), typeof(int), typeof(int), typeof(int), typeof(bool) });
        private static readonly Queue<Piece> PendingPieces = new Queue<Piece>();
        private static bool _running;
        private static int _generation;

        internal static void Begin(Player player, Piece piece)
        {
            if (player == null || piece == null)
            {
                return;
            }

            // Every recognized click represents another complete kit. Serialize queued clicks so
            // each gets a fresh storage, capacity, ownership, and transaction decision.
            PendingPieces.Enqueue(piece);
            StartNext(player);
        }

        private static void StartNext(Player player)
        {
            if (_running || PendingPieces.Count == 0 || !RuntimeContext.Compatibility.IsCompatible)
            {
                return;
            }

            var piece = PendingPieces.Dequeue();
            var generation = _generation;
            _running = true;
            try
            {
                IReadOnlyList<ResourceRequirement> requirements;
                NearbyResourceCapture capture;
                ResourceWithdrawalPlan plan;
                ContainerHandle[] handles;
                string failure;
                if (!TryPrepare(player, piece, out requirements, out capture, out plan, out handles, out failure))
                {
                    ShowFailure(failure);
                    Complete(player, generation);
                    return;
                }

                ExpeditionCapacityPlan ignoredCapacity;
                if (!TryPlanCapacity(player, capture, plan, out ignoredCapacity, out failure))
                {
                    ShowFailure(failure);
                    Complete(player, generation);
                    return;
                }

                if (handles.Any(handle => OwnershipLeaseManager.HasPotentialAcquisition(handle.Id)))
                {
                    ShowFailure("a previous ownership transition is still pending");
                    Complete(player, generation);
                    return;
                }

                var unowned = handles
                    .Where(handle => !handle.NetworkView.IsOwner() || !handle.Container.IsOwner())
                    .ToArray();
                if (unowned.Length == 0)
                {
                    Finish(player, piece, plan, null);
                    Complete(player, generation);
                    return;
                }

                RuntimeContext.ShowTopLeft("Stackmaster: checking expedition-kit storage…");
                RuntimeContext.Plugin.StartCoroutine(FinishAfterOwnership(player, piece, plan, unowned, generation));
            }
            catch (Exception exception)
            {
                RuntimeContext.Plugin?.Log.LogError("Expedition-kit action stopped safely: " + exception);
                RuntimeContext.ShowCenter("Stackmaster stopped safely; no expedition kit was added.");
                Complete(player, generation);
            }
        }

        private static void Complete(Player player, int generation)
        {
            if (generation != _generation)
            {
                return;
            }

            _running = false;
            if (player == Player.m_localPlayer)
            {
                StartNext(player);
            }
        }

        internal static void Shutdown()
        {
            // Invalidate any yielded ownership action before it can mutate, and discard clicks that
            // have not started. OwnershipCoordinator shutdown remains responsible for exact cleanup.
            _generation++;
            PendingPieces.Clear();
            _running = false;
        }

        private static IEnumerator FinishAfterOwnership(
            Player player,
            Piece piece,
            ResourceWithdrawalPlan expectedPlan,
            ContainerHandle[] unownedHandles,
            int generation)
        {
            OwnershipBatch ownership;
            try
            {
                ownership = OwnershipCoordinator.Begin(unownedHandles);
            }
            catch (Exception exception)
            {
                RuntimeContext.Plugin?.Log.LogError("Expedition-kit ownership setup failed safely: " + exception);
                RuntimeContext.ShowCenter("Stackmaster could not safely acquire the expedition kit; nothing was moved.");
                Complete(player, generation);
                yield break;
            }

            try
            {
                var refreshFailed = false;
                var deadline = Time.realtimeSinceStartup + OwnershipTimeoutSeconds;
                while (generation == _generation &&
                       !ownership.IsComplete &&
                       Time.realtimeSinceStartup < deadline)
                {
                    try
                    {
                        ownership.Refresh();
                    }
                    catch (Exception exception)
                    {
                        refreshFailed = true;
                        RuntimeContext.Plugin?.Log.LogError("Expedition-kit ownership refresh failed safely: " + exception);
                        RuntimeContext.ShowCenter("Stackmaster could not safely acquire the expedition kit; nothing was moved.");
                    }
                    if (refreshFailed) yield break;
                    yield return null;
                }

                try
                {
                    if (generation != _generation || !RuntimeContext.Compatibility.IsCompatible)
                    {
                        yield break;
                    }

                    ownership.Refresh();
                    if (!ownership.IsComplete) ownership.Timeout();

                    if (ownership.FailedContainerIds.Count > 0)
                    {
                        var ownerRejectedAsBusy = ownership.OwnerRejectedContainerIds.Count > 0 &&
                            unownedHandles
                                .Where(handle => ownership.OwnerRejectedContainerIds.Contains(handle.Id))
                                .All(handle => handle.NetworkView != null && handle.NetworkView.IsValid() &&
                                               handle.NetworkView.HasOwner() &&
                                               ContainerDiscovery.CheckAccess(player, handle.Container));
                        ShowFailure(ownerRejectedAsBusy
                            ? NearbyResourceOwnership.InUseMessage
                            : "required storage ownership could not be acquired");
                    }
                    else
                    {
                        Finish(player, piece, expectedPlan, ownership);
                    }
                }
                catch (Exception exception)
                {
                    RuntimeContext.Plugin?.Log.LogError("Expedition-kit ownership failed safely: " + exception);
                    RuntimeContext.ShowCenter("Stackmaster could not safely acquire the expedition kit; nothing was moved.");
                }
            }
            finally
            {
                try
                {
                    if (!ownership.IsComplete) ownership.Timeout();
                }
                catch (Exception exception)
                {
                    RuntimeContext.Plugin?.Log.LogError("Expedition-kit ownership timeout cleanup failed: " + exception);
                }

                try
                {
                    OwnershipLeaseManager.ReleaseBatch(ownership, "expedition-kit action ended");
                }
                catch (Exception exception)
                {
                    RuntimeContext.Plugin?.Log.LogError("Expedition-kit ownership release failed safely: " + exception);
                }
                finally
                {
                    try
                    {
                        OwnershipCoordinator.End(ownership);
                    }
                    catch (Exception exception)
                    {
                        RuntimeContext.Plugin?.Log.LogError("Expedition-kit ownership teardown failed safely: " + exception);
                    }
                    finally
                    {
                        Complete(player, generation);
                    }
                }
            }
        }

        private static void Finish(
            Player player,
            Piece piece,
            ResourceWithdrawalPlan expectedPlan,
            OwnershipBatch ownership)
        {
            IReadOnlyList<ResourceRequirement> requirements;
            NearbyResourceCapture capture;
            ResourceWithdrawalPlan plan;
            ContainerHandle[] handles;
            string failure;
            if (!TryPrepare(player, piece, out requirements, out capture, out plan, out handles, out failure) ||
                !ExpeditionKitWithdrawalPlanner.PlansAreIdentical(expectedPlan, plan))
            {
                ShowFailure(failure ?? "nearby materials changed before transfer");
                return;
            }
            if (handles.Any(handle => !handle.NetworkView.IsOwner() || !handle.Container.IsOwner()))
            {
                ShowFailure("required storage ownership changed before transfer");
                return;
            }

            // Refresh can replace ItemData objects. Revalidate the complete source set, then make
            // one final capture/plan from those synchronized live inventories.
            if (!NearbyResourceService.RevalidateContainers(player, plan, capture, out failure))
            {
                ShowFailure(failure);
                return;
            }
            if (!TryPrepare(player, piece, out requirements, out capture, out plan, out handles, out failure) ||
                !ExpeditionKitWithdrawalPlanner.PlansAreIdentical(expectedPlan, plan) ||
                handles.Any(handle => !handle.NetworkView.IsOwner() || !handle.Container.IsOwner()))
            {
                ShowFailure(failure ?? "nearby materials changed during transfer preparation");
                return;
            }

            IReadOnlyList<ContainerReservation> reservations;
            if (!NearbyResourceService.TryReserveContainers(
                    player,
                    capture.Scope,
                    handles,
                    false,
                    out reservations,
                    out failure))
            {
                ShowFailure(failure);
                return;
            }

            try
            {
                ExpeditionCapacityPlan capacity;
                if (!NearbyResourceService.RevalidateReservedContainers(player, capture.Scope, reservations, out failure) ||
                    !NearbyResourceService.RevalidateStacks(plan, capture, true, out failure) ||
                    !TryPlanCapacity(player, capture, plan, out capacity, out failure))
                {
                    ShowFailure(failure);
                    return;
                }

                if (!ExecuteAtomic(player, capture, plan, capacity, reservations, out failure))
                {
                    ShowFailure(failure);
                    return;
                }

                NearbyResourceService.ResetCaches();
                NearbyBuildHudPatch.ResetCache();
                RuntimeContext.ShowTopLeft("Stackmaster: expedition kit added (" +
                    plan.RequiredUnits.ToString(CultureInfo.InvariantCulture) + " items).");
            }
            finally
            {
                // Reservation cleanup never touches a pre-existing successful-build lease. The
                // outer ownership batch releases only ownership acquired by this kit click.
                NearbyResourceService.ReleaseReservations(reservations, false);
            }
        }

        private static bool TryPrepare(
            Player player,
            Piece piece,
            out IReadOnlyList<ResourceRequirement> requirements,
            out NearbyResourceCapture capture,
            out ResourceWithdrawalPlan plan,
            out ContainerHandle[] handles,
            out string failure)
        {
            requirements = Array.Empty<ResourceRequirement>();
            capture = null;
            plan = null;
            handles = Array.Empty<ContainerHandle>();
            failure = null;
            if (player == null || piece == null)
            {
                failure = "invalid build piece";
                return false;
            }

            requirements = NearbyResourceService.PieceRequirements(piece);
            if (requirements.Count == 0)
            {
                failure = "this build piece has no material recipe";
                return false;
            }
            capture = NearbyResourceService.CaptureForExpedition(player, true, true);
            plan = WithdrawalPlanner.Plan(
                requirements,
                capture.Stacks.Where(stack => !string.Equals(stack.InventoryId, "player", StringComparison.Ordinal)));
            if (!plan.IsSatisfiable || plan.PlannedUnits != plan.RequiredUnits)
            {
                failure = "nearby storage does not contain one complete additional kit";
                return false;
            }
            if (!NearbyResourceService.TryResolveRequiredContainers(player, plan, capture, out handles, out failure))
            {
                return false;
            }
            return true;
        }

        private static bool TryPlanCapacity(
            Player player,
            NearbyResourceCapture capture,
            ResourceWithdrawalPlan withdrawal,
            out ExpeditionCapacityPlan plan,
            out string failure)
        {
            failure = null;
            var catalog = new CompatibilityCatalog();
            var playerInventory = player.GetInventory();
            var playerSnapshot = InventorySnapshots.CaptureInventory("player", playerInventory, catalog);
            var cargo = new List<ExpeditionCargoStack>(withdrawal.Steps.Count);
            foreach (var step in withdrawal.Steps)
            {
                RuntimeResourceStack runtime;
                if (!capture.RuntimeStacks.TryGetValue(step.StackId, out runtime) || runtime.Item == null || runtime.Item.m_shared == null)
                {
                    plan = null;
                    failure = "planned expedition material disappeared";
                    return false;
                }
                cargo.Add(new ExpeditionCargoStack(
                    step.StackId,
                    catalog.KeyFor(runtime.Item),
                    step.Quantity,
                    runtime.Item.m_shared.m_maxStackSize,
                    runtime.Item.GetWeight(step.Quantity)));
            }

            plan = CapacityPlanner.Plan(
                playerSnapshot,
                cargo,
                playerInventory.GetTotalWeight(),
                player.GetMaxCarryWeight());
            if (!plan.FitsWeight)
            {
                failure = "the complete expedition kit is too heavy";
                return false;
            }
            if (!plan.FitsSlots)
            {
                failure = "the complete expedition kit does not fit in inventory";
                return false;
            }
            return true;
        }

        private static bool ExecuteAtomic(
            Player player,
            NearbyResourceCapture capture,
            ResourceWithdrawalPlan withdrawal,
            ExpeditionCapacityPlan capacity,
            IReadOnlyList<ContainerReservation> reservations,
            out string failure)
        {
            failure = null;
            var playerInventory = player.GetInventory();
            var catalog = new CompatibilityCatalog();
            // Seed the compatibility classes in the same order used by capacity planning.
            foreach (var item in playerInventory.GetAllItems()) catalog.KeyFor(item);
            foreach (var source in withdrawal.Steps) catalog.KeyFor(capture.RuntimeStacks[source.StackId].Item);

            var reservationByContainer = reservations.ToDictionary(reservation => reservation.Container);
            var backups = new List<ExpeditionInventoryBackup> { new ExpeditionInventoryBackup(playerInventory) };
            backups.AddRange(reservations.Select(reservation => new ExpeditionInventoryBackup(reservation.Container.GetInventory())));
            var completed = new List<CompletedExpeditionMove>();
            var sourceMoved = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (var step in capacity.Steps)
            {
                RuntimeResourceStack runtime;
                if (!capture.RuntimeStacks.TryGetValue(step.SourceStackId, out runtime) || runtime.Container == null)
                {
                    failure = "planned expedition source disappeared";
                    return RollbackOrDisable(player, capture.Scope, reservations, completed, backups, failure, out failure);
                }
                ContainerReservation reservation;
                if (!reservationByContainer.TryGetValue(runtime.Container.Container, out reservation) ||
                    !NearbyResourceService.ReservationMatches(player, capture.Scope, reservation))
                {
                    failure = "required storage reservation changed before transfer";
                    return RollbackOrDisable(player, capture.Scope, reservations, completed, backups, failure, out failure);
                }

                var movedFromSource = sourceMoved.TryGetValue(step.SourceStackId, out var previous) ? previous : 0;
                var sourceItem = runtime.Inventory.GetItemAt(runtime.Item.m_gridPos.x, runtime.Item.m_gridPos.y);
                if (!ReferenceEquals(sourceItem, runtime.Item) ||
                    sourceItem.m_stack != runtime.Item.m_stack ||
                    sourceItem.m_stack < step.Quantity ||
                    !string.Equals(catalog.KeyFor(sourceItem), step.CompatibilityKey, StringComparison.Ordinal))
                {
                    failure = "planned expedition source stack changed before transfer";
                    return RollbackOrDisable(player, capture.Scope, reservations, completed, backups, failure, out failure);
                }

                var destinationPosition = InventorySnapshots.PositionForSlot(playerInventory, step.DestinationSlot);
                var destination = playerInventory.GetItemAt(destinationPosition.x, destinationPosition.y);
                if ((destination == null && step.ExpectedDestinationQuantity != 0) ||
                    (destination != null &&
                     (destination.m_stack != step.ExpectedDestinationQuantity ||
                      !string.Equals(catalog.KeyFor(destination), step.CompatibilityKey, StringComparison.Ordinal))))
                {
                    failure = "player inventory changed before expedition transfer";
                    return RollbackOrDisable(player, capture.Scope, reservations, completed, backups, failure, out failure);
                }

                var sourceClone = sourceItem.Clone();
                sourceClone.m_stack = step.Quantity;
                var sourcePosition = sourceItem.m_gridPos;
                var sourceBefore = TotalUnits(runtime.Inventory);
                var playerBefore = TotalUnits(playerInventory);
                bool moved;
                try
                {
                    moved = playerInventory.MoveItemToThis(
                        runtime.Inventory,
                        sourceItem,
                        step.Quantity,
                        destinationPosition.x,
                        destinationPosition.y);
                }
                catch (Exception exception)
                {
                    moved = false;
                    RuntimeContext.Plugin?.Log.LogError("Expedition-kit transfer primitive threw: " + exception);
                }
                var sourceAfter = TotalUnits(runtime.Inventory);
                var playerAfter = TotalUnits(playerInventory);
                var destinationAfter = playerInventory.GetItemAt(destinationPosition.x, destinationPosition.y);
                var exact = sourceAfter == sourceBefore - step.Quantity &&
                    playerAfter == playerBefore + step.Quantity &&
                    destinationAfter != null &&
                    destinationAfter.m_stack == step.ExpectedDestinationQuantity + step.Quantity &&
                    string.Equals(catalog.KeyFor(destinationAfter), step.CompatibilityKey, StringComparison.Ordinal);

                if (exact)
                {
                    completed.Add(new CompletedExpeditionMove(
                        runtime,
                        sourceClone,
                        sourcePosition,
                        destinationPosition,
                        step.CompatibilityKey,
                        step.Quantity,
                        reservation));
                    sourceMoved[step.SourceStackId] = movedFromSource + step.Quantity;
                    if (!reservation.AdvanceDataRevisionAfterMutation() ||
                        !NearbyResourceService.ReservationMatches(player, capture.Scope, reservation))
                    {
                        failure = "required storage reservation changed after transfer";
                        return RollbackOrDisable(player, capture.Scope, reservations, completed, backups, failure, out failure);
                    }
                    continue;
                }

                if (!moved && sourceAfter == sourceBefore && playerAfter == playerBefore)
                {
                    failure = "game transfer primitive declined the expedition move";
                    return RollbackOrDisable(player, capture.Scope, reservations, completed, backups, failure, out failure);
                }

                failure = "an unexpected expedition transfer result required full rollback";
                var restored = RestoreBackups(backups, reservations);
                if (!restored)
                {
                    RuntimeContext.Disable("Expedition-kit rollback could not restore every inventory.");
                    failure += "; rollback could not restore every item";
                }
                else
                {
                    RuntimeContext.Disable("Unexpected expedition-kit transfer result; inventories were restored.");
                    failure += "; inventories were restored and Stackmaster was disabled";
                }
                return false;
            }

            var expectedUnits = withdrawal.RequiredUnits;
            if (completed.Sum(move => move.Quantity) != expectedUnits)
            {
                failure = "expedition transfer did not complete the exact kit";
                return RollbackOrDisable(player, capture.Scope, reservations, completed, backups, failure, out failure);
            }
            return true;
        }

        private static bool RollbackOrDisable(
            Player player,
            StorageScope scope,
            IReadOnlyList<ContainerReservation> reservations,
            IList<CompletedExpeditionMove> completed,
            IReadOnlyList<ExpeditionInventoryBackup> backups,
            string reason,
            out string failure)
        {
            var restored = RollbackCompleted(player, scope, completed);
            if (!restored) restored = RestoreBackups(backups, reservations);
            failure = restored
                ? reason + "; all transfers were rolled back"
                : reason + "; rollback could not restore every item";
            if (!restored)
            {
                RuntimeContext.Disable("Expedition-kit rollback could not restore every inventory.");
            }
            NearbyResourceService.ResetCaches();
            NearbyBuildHudPatch.ResetCache();
            return false;
        }

        private static bool RollbackCompleted(
            Player player,
            StorageScope scope,
            IEnumerable<CompletedExpeditionMove> completed)
        {
            var playerInventory = player.GetInventory();
            var catalog = new CompatibilityCatalog();
            foreach (var item in playerInventory.GetAllItems()) catalog.KeyFor(item);
            foreach (var move in completed) catalog.KeyFor(move.SourceClone);

            foreach (var move in completed.Reverse())
            {
                if (!NearbyResourceService.ReservationMatches(player, scope, move.Reservation)) return false;
                var destination = playerInventory.GetItemAt(move.DestinationPosition.x, move.DestinationPosition.y);
                if (destination == null || destination.m_stack < move.Quantity ||
                    !string.Equals(catalog.KeyFor(destination), move.CompatibilityKey, StringComparison.Ordinal))
                {
                    return false;
                }
                var playerBefore = TotalUnits(playerInventory);
                if (!playerInventory.RemoveItem(destination, move.Quantity) ||
                    TotalUnits(playerInventory) != playerBefore - move.Quantity)
                {
                    return false;
                }

                var sourceBefore = TotalUnits(move.Source.Inventory);
                move.SourceClone.m_stack = move.Quantity;
                move.SourceClone.m_gridPos = move.SourcePosition;
                var restored = AddItemAtMethod != null && (bool)AddItemAtMethod.Invoke(
                    move.Source.Inventory,
                    new object[]
                    {
                        move.SourceClone,
                        move.Quantity,
                        move.SourcePosition.x,
                        move.SourcePosition.y,
                        true
                    });
                if (!restored || TotalUnits(move.Source.Inventory) != sourceBefore + move.Quantity ||
                    !move.Reservation.AdvanceDataRevisionAfterMutation() ||
                    !NearbyResourceService.ReservationMatches(player, scope, move.Reservation))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool RestoreBackups(
            IEnumerable<ExpeditionInventoryBackup> backups,
            IEnumerable<ContainerReservation> reservations)
        {
            if (AddItemAtMethod == null) return false;
            try
            {
                foreach (var backup in backups)
                {
                    backup.Inventory.RemoveAll();
                    foreach (var original in backup.Items)
                    {
                        var clone = original.Clone();
                        if (!(bool)AddItemAtMethod.Invoke(
                                backup.Inventory,
                                new object[]
                                {
                                    clone,
                                    clone.m_stack,
                                    clone.m_gridPos.x,
                                    clone.m_gridPos.y,
                                    true
                                }))
                        {
                            return false;
                        }
                    }
                    if (TotalUnits(backup.Inventory) != backup.Items.Sum(item => item.m_stack)) return false;
                }
                foreach (var reservation in reservations)
                {
                    if (!reservation.AdvanceDataRevisionAfterMutation()) return false;
                }
                return true;
            }
            catch (Exception exception)
            {
                RuntimeContext.Plugin?.Log.LogError("Expedition-kit full rollback failed: " + exception);
                return false;
            }
        }

        private static int TotalUnits(Inventory inventory)
            => inventory.GetAllItems().Sum(item => item.m_stack);

        private static void ShowFailure(string failure)
        {
            var detail = string.IsNullOrWhiteSpace(failure) ? "the complete kit could not be transferred" : failure;
            RuntimeContext.Plugin?.Log.LogWarning("Expedition-kit action rejected safely: " + detail + ".");
            RuntimeContext.ShowCenter("Stackmaster: " + detail + ". Nothing was moved.");
        }
    }
}
