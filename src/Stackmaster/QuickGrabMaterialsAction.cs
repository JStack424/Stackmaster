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
    internal static class QuickGrabMaterialsClickPatch
    {
        internal static bool Prefix([HarmonyArgument(0)] Piece piece)
        {
            if (QuickGrabMaterialsAction.IsReservationCardSelection) return true;
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
                if (!QuickGrabClickPolicy.ShouldIntercept(
                        RuntimeContext.Compatibility.IsCompatible,
                        player != null,
                        hasMaterialRecipe,
                        modifierStates))
                {
                    return true;
                }

                // Suppress BuildUi.OnSelectPiece: no selection change, button sound, or
                // Hud.CloseBuildUi. Every modified click is one independent reservation request
                // with an optional all-or-nothing complete-material transfer.
                intercepting = true;
                QuickGrabMaterialsAction.Begin(player, piece);
                return false;
            }
            catch (Exception exception)
            {
                NearbyHudFailOpen.ReportOnce("quick-grab click", exception);
                // Once the configured modified click is recognized, every outcome stays on
                // the quick-grab path so an internal failure cannot select the piece/close the menu.
                return !intercepting;
            }
        }
    }

    internal sealed class QuickGrabInventoryBackup
    {
        private readonly IdentityPreservingInventoryBackup<ItemDrop.ItemData, ItemDrop.ItemData> _items;

        internal QuickGrabInventoryBackup(Inventory inventory, bool preserveItemIdentity)
        {
            Inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            var ordered = inventory.GetAllItems()
                .OrderBy(item => item.m_gridPos.y)
                .ThenBy(item => item.m_gridPos.x)
                .ToArray();
            // Player backups retain the live instances because Humanoid equipment fields point to
            // them directly. Chest backups deliberately retain detached clones instead.
            var retained = preserveItemIdentity ? ordered : ordered.Select(item => item.Clone()).ToArray();
            _items = IdentityPreservingInventoryBackup<ItemDrop.ItemData, ItemDrop.ItemData>.Capture(
                retained,
                item => item.Clone());
            TotalUnits = ordered.Sum(item => item.m_stack);
        }

        internal Inventory Inventory { get; }
        internal int TotalUnits { get; }

        internal QuickGrabSnapshotRestoreTarget PrepareRestore()
            => new QuickGrabSnapshotRestoreTarget(
                Inventory,
                _items.PrepareRestore(snapshot => snapshot.Clone()),
                TotalUnits);
    }

    internal sealed class QuickGrabSnapshotRestoreTarget
    {
        internal QuickGrabSnapshotRestoreTarget(
            Inventory inventory,
            IdentityPreservingInventoryRestorePlan<ItemDrop.ItemData, ItemDrop.ItemData> items,
            int totalUnits)
        {
            Inventory = inventory;
            Items = items;
            TotalUnits = totalUnits;
        }

        internal Inventory Inventory { get; }
        internal IdentityPreservingInventoryRestorePlan<ItemDrop.ItemData, ItemDrop.ItemData> Items { get; }
        internal int TotalUnits { get; }
    }

    internal sealed class CompletedQuickGrabMove
    {
        internal CompletedQuickGrabMove(
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

    internal sealed class PendingQuickGrabRequest
    {
        internal PendingQuickGrabRequest(
            Player player,
            Piece piece,
            string pieceKey,
            IReadOnlyList<ResourceRequirement> reservationRequirements,
            string displayName,
            int committedCount)
        {
            Player = player;
            Piece = piece;
            PieceKey = pieceKey;
            ReservationRequirements = reservationRequirements;
            DisplayName = displayName;
            CommittedCount = committedCount;
        }

        internal Player Player { get; }
        internal Piece Piece { get; }
        internal string PieceKey { get; }
        internal IReadOnlyList<ResourceRequirement> ReservationRequirements { get; }
        internal string DisplayName { get; }
        internal int CommittedCount { get; }
    }

    internal static class QuickGrabMaterialsAction
    {
        private const float OwnershipTimeoutSeconds = 2f;
        private static readonly QuickGrabMaterialsWithdrawalPlanner WithdrawalPlanner = new QuickGrabMaterialsWithdrawalPlanner();
        private static readonly QuickGrabCapacityPlanner CapacityPlanner = new QuickGrabCapacityPlanner();
        private static readonly MethodInfo AddItemAtMethod = AccessTools.DeclaredMethod(
            typeof(Inventory),
            "AddItem",
            new[] { typeof(ItemDrop.ItemData), typeof(int), typeof(int), typeof(int), typeof(bool) });
        private static readonly FieldInfo InventoryItemsField = AccessTools.Field(typeof(Inventory), "m_inventory");
        private static readonly Queue<PendingQuickGrabRequest> PendingRequests = new Queue<PendingQuickGrabRequest>();
        private static bool _running;
        private static int _generation;
        private static string _activeDisplayName;
        private static int _activeCommittedCount;
        private static string _activePieceKey;
        private static bool _cancelActiveTransfer;
        private static int _reservationCardSelectionDepth;

        internal static bool IsReservationCardSelection => _reservationCardSelectionDepth > 0;

        internal static void SelectReservationCard(BuildUi owner, Piece piece)
        {
            if (owner == null || piece == null) return;
            _reservationCardSelectionDepth++;
            try
            {
                // Preserve Valheim's full normal BuildUi selection path while preventing our own
                // modifier-click reservation prefix from intercepting this separate card control.
                owner.OnSelectPiece(piece);
            }
            finally
            {
                _reservationCardSelectionDepth--;
            }
        }

        internal static void Begin(Player player, Piece piece)
        {
            if (player == null || piece == null)
            {
                return;
            }

            // Every recognized click represents one durable piece reservation and, when safely
            // available, one complete material set. Capture immutable prefab identities before
            // queuing while retaining the live Piece for fresh recipe and transaction checks.
            string pieceKey;
            string displayName;
            IReadOnlyList<ResourceRequirement> reservationRequirements;
            if (!ExpeditionReservations.TryGetStablePieceIdentity(piece, out pieceKey, out displayName) ||
                !ExpeditionReservations.TryGetStableRequirements(piece, out reservationRequirements))
            {
                RuntimeContext.ShowCenter("Stackmaster could not identify that piece safely; no reservation was added and no materials were moved.");
                return;
            }
            int committedCount;
            string reservationFailure;
            if (!ExpeditionReservations.TryAddQuickGrabReservation(
                    player,
                    pieceKey,
                    displayName,
                    reservationRequirements,
                    out committedCount,
                    out reservationFailure))
            {
                ShowFailure(reservationFailure);
                return;
            }

            // Persist each click synchronously before it can wait behind another transfer. A
            // disconnect or shutdown may cancel the optional queued grab, but never its intent.
            PendingRequests.Enqueue(new PendingQuickGrabRequest(
                player,
                piece,
                pieceKey,
                reservationRequirements,
                displayName,
                committedCount));
            StartNext();
        }

        internal static void CancelOneMaterialTransfer(string pieceKey)
        {
            if (string.IsNullOrEmpty(pieceKey)) return;
            if (_running && !_cancelActiveTransfer &&
                string.Equals(_activePieceKey, pieceKey, StringComparison.Ordinal))
            {
                _cancelActiveTransfer = true;
                return;
            }

            var retained = new Queue<PendingQuickGrabRequest>();
            var canceled = false;
            while (PendingRequests.Count > 0)
            {
                var request = PendingRequests.Dequeue();
                if (!canceled && string.Equals(request.PieceKey, pieceKey, StringComparison.Ordinal))
                {
                    canceled = true;
                    continue;
                }
                retained.Enqueue(request);
            }
            while (retained.Count > 0) PendingRequests.Enqueue(retained.Dequeue());
        }

        private static void StartNext()
        {
            if (_running || !RuntimeContext.Compatibility.IsCompatible)
            {
                return;
            }

            PendingQuickGrabRequest request = null;
            while (PendingRequests.Count > 0)
            {
                var candidate = PendingRequests.Dequeue();
                if (ReferenceEquals(candidate.Player, Player.m_localPlayer))
                {
                    request = candidate;
                    break;
                }
            }
            if (request == null) return;

            var player = request.Player;
            var piece = request.Piece;
            _activeDisplayName = request.DisplayName;
            _activeCommittedCount = request.CommittedCount;
            _activePieceKey = request.PieceKey;
            _cancelActiveTransfer = false;
            var generation = _generation;
            _running = true;
            try
            {
                // Intent was durably recorded synchronously at click time. Recipe drift after
                // that point preserves the captured reservation but cancels material movement.
                if (!ReservationRequirementsMatch(piece, request.ReservationRequirements))
                {
                    ShowReservationOnly("the build-piece recipe changed");
                    Complete(generation);
                    return;
                }

                IReadOnlyList<ResourceRequirement> requirements;
                NearbyResourceCapture capture;
                ResourceWithdrawalPlan plan;
                ContainerHandle[] handles;
                string failure;
                if (!TryPrepare(player, piece, request.ReservationRequirements, out requirements, out capture, out plan, out handles, out failure))
                {
                    ShowReservationOnly(failure);
                    Complete(generation);
                    return;
                }

                QuickGrabCapacityPlan ignoredCapacity;
                if (!TryPlanCapacity(player, capture, plan, out ignoredCapacity, out failure))
                {
                    ShowReservationOnly(failure);
                    Complete(generation);
                    return;
                }

                if (handles.Any(handle => OwnershipLeaseManager.HasPotentialAcquisition(handle.Id)))
                {
                    ShowReservationOnly("a previous ownership transition is still pending");
                    Complete(generation);
                    return;
                }

                var unowned = handles
                    .Where(handle => !handle.NetworkView.IsOwner() || !handle.Container.IsOwner())
                    .ToArray();
                if (unowned.Length == 0)
                {
                    Finish(
                        player,
                        piece,
                        request.ReservationRequirements,
                        plan,
                        null);
                    Complete(generation);
                    return;
                }

                RuntimeContext.ShowTopLeft("Stackmaster: reservation added; checking storage for materials…");
                RuntimeContext.Plugin.StartCoroutine(FinishAfterOwnership(
                    player,
                    piece,
                    request.ReservationRequirements,
                    plan,
                    unowned,
                    generation));
            }
            catch (Exception exception)
            {
                RuntimeContext.Plugin?.Log.LogError("Quick-grab action stopped safely: " + exception);
                ShowReservationOnly("the material transfer stopped safely");
                Complete(generation);
            }
        }

        private static void Complete(int generation)
        {
            if (generation != _generation)
            {
                return;
            }

            _running = false;
            _activeDisplayName = null;
            _activeCommittedCount = 0;
            _activePieceKey = null;
            _cancelActiveTransfer = false;
            StartNext();
        }

        internal static void Shutdown()
        {
            // Invalidate any yielded ownership action before it can mutate, and discard clicks that
            // have not started. OwnershipCoordinator shutdown remains responsible for exact cleanup.
            _generation++;
            PendingRequests.Clear();
            _running = false;
            _activeDisplayName = null;
            _activeCommittedCount = 0;
            _activePieceKey = null;
            _cancelActiveTransfer = false;
        }

        internal static void RearmSession()
        {
            PendingRequests.Clear();
            _running = false;
            _activeDisplayName = null;
            _activeCommittedCount = 0;
            _activePieceKey = null;
            _cancelActiveTransfer = false;
        }

        private static IEnumerator FinishAfterOwnership(
            Player player,
            Piece piece,
            IReadOnlyList<ResourceRequirement> reservationRequirements,
            ResourceWithdrawalPlan expectedPlan,
            ContainerHandle[] unownedHandles,
            int generation)
        {
            if (generation != _generation || !RuntimeContext.Compatibility.IsCompatible)
            {
                yield break;
            }
            if (_cancelActiveTransfer)
            {
                Complete(generation);
                yield break;
            }

            OwnershipBatch ownership;
            try
            {
                ownership = OwnershipCoordinator.Begin(unownedHandles);
            }
            catch (Exception exception)
            {
                RuntimeContext.Plugin?.Log.LogError("Quick-grab ownership setup failed safely: " + exception);
                ShowReservationOnly("the required materials could not be transferred safely");
                Complete(generation);
                yield break;
            }

            try
            {
                var refreshFailed = false;
                var deadline = Time.realtimeSinceStartup + OwnershipTimeoutSeconds;
                while (generation == _generation &&
                       !_cancelActiveTransfer &&
                       ReferenceEquals(player, Player.m_localPlayer) &&
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
                        RuntimeContext.Plugin?.Log.LogError("Quick-grab ownership refresh failed safely: " + exception);
                        ShowReservationOnly("the required materials could not be transferred safely");
                    }
                    if (refreshFailed) yield break;
                    yield return null;
                }

                try
                {
                    if (generation != _generation ||
                        _cancelActiveTransfer ||
                        !RuntimeContext.Compatibility.IsCompatible ||
                        !ReferenceEquals(player, Player.m_localPlayer))
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
                        ShowReservationOnly(ownerRejectedAsBusy
                            ? NearbyResourceOwnership.InUseMessage
                            : "required storage ownership could not be acquired");
                    }
                    else
                    {
                        Finish(
                            player,
                            piece,
                            reservationRequirements,
                            expectedPlan,
                            ownership);
                    }
                }
                catch (Exception exception)
                {
                    RuntimeContext.Plugin?.Log.LogError("Quick-grab ownership failed safely: " + exception);
                    ShowReservationOnly("the required materials could not be transferred safely");
                }
            }
            finally
            {
                if (generation == _generation)
                {
                try
                {
                    if (!ownership.IsComplete) ownership.Timeout();
                }
                catch (Exception exception)
                {
                    RuntimeContext.Plugin?.Log.LogError("Quick-grab ownership timeout cleanup failed: " + exception);
                    DisableAfterFatalFailure("Stackmaster could not finish quick-grab ownership timeout cleanup.");
                }

                try
                {
                    OwnershipLeaseManager.ReleaseBatch(ownership, "quick-grab action ended");
                }
                catch (Exception exception)
                {
                    RuntimeContext.Plugin?.Log.LogError("Quick-grab ownership release failed safely: " + exception);
                    DisableAfterFatalFailure("Stackmaster could not hand quick-grab ownership to cleanup.");
                }
                finally
                {
                    try
                    {
                        OwnershipCoordinator.End(ownership);
                    }
                    catch (Exception exception)
                    {
                        RuntimeContext.Plugin?.Log.LogError("Quick-grab ownership teardown failed safely: " + exception);
                        DisableAfterFatalFailure("Stackmaster could not finish quick-grab ownership teardown.");
                    }
                    finally
                    {
                        Complete(generation);
                    }
                }
                }
                // RuntimeContext already cleaned an invalidated generation; it must never touch
                // ownership records created by a later server session.
            }
        }

        private static void Finish(
            Player player,
            Piece piece,
            IReadOnlyList<ResourceRequirement> reservationRequirements,
            ResourceWithdrawalPlan expectedPlan,
            OwnershipBatch ownership)
        {
            if (!ReservationRequirementsMatch(piece, reservationRequirements))
            {
                ShowReservationOnly("the build-piece recipe changed before material transfer");
                return;
            }
            if (!ExpeditionReservations.CanCommitQuickGrab(player))
            {
                ShowReservationOnly("reservation storage became unavailable before material transfer");
                return;
            }

            IReadOnlyList<ResourceRequirement> requirements;
            NearbyResourceCapture capture;
            ResourceWithdrawalPlan plan;
            ContainerHandle[] handles;
            string failure;
            if (!TryPrepare(player, piece, reservationRequirements, out requirements, out capture, out plan, out handles, out failure) ||
                !QuickGrabMaterialsWithdrawalPlanner.PlansAreIdentical(expectedPlan, plan))
            {
                ShowReservationOnly(failure ?? "nearby materials changed before transfer");
                return;
            }
            if (handles.Any(handle => !handle.NetworkView.IsOwner() || !handle.Container.IsOwner()))
            {
                ShowReservationOnly("required storage ownership changed before transfer");
                return;
            }

            // Refresh can replace ItemData objects. Revalidate the complete source set, then make
            // one final capture/plan from those synchronized live inventories.
            if (!NearbyResourceService.RevalidateContainers(player, plan, capture, out failure))
            {
                ShowReservationOnly(failure);
                return;
            }
            if (!TryPrepare(player, piece, reservationRequirements, out requirements, out capture, out plan, out handles, out failure) ||
                !QuickGrabMaterialsWithdrawalPlanner.PlansAreIdentical(expectedPlan, plan) ||
                handles.Any(handle => !handle.NetworkView.IsOwner() || !handle.Container.IsOwner()))
            {
                ShowReservationOnly(failure ?? "nearby materials changed during transfer preparation");
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
                ShowReservationOnly(failure);
                return;
            }

            try
            {
                QuickGrabCapacityPlan capacity;
                if (!NearbyResourceService.RevalidateReservedContainers(player, capture.Scope, reservations, out failure) ||
                    !NearbyResourceService.RevalidateStacks(plan, capture, true, out failure) ||
                    !TryPlanCapacity(player, capture, plan, out capacity, out failure))
                {
                    ShowReservationOnly(failure);
                    return;
                }

                if (!ExecuteAtomic(player, capture, plan, capacity, reservations, out failure))
                {
                    ShowReservationOnly(failure);
                    return;
                }

                try
                {
                    NearbyResourceService.ResetCaches();
                    NearbyBuildHudPatch.ResetCache();
                }
                catch (Exception exception)
                {
                    RuntimeContext.Plugin?.Log.LogError("Quick-grab cache refresh failed after commit: " + exception);
                }
                try
                {
                    RuntimeContext.ShowTopLeft("Stackmaster: grabbed materials and added reservation (" +
                        plan.RequiredUnits.ToString(CultureInfo.InvariantCulture) + " items).");
                }
                catch (Exception exception)
                {
                    RuntimeContext.Plugin?.Log.LogError("Quick-grab success notification failed after commit: " + exception);
                }
            }
            finally
            {
                // Reservation cleanup never touches a pre-existing successful-build lease. The
                // outer ownership batch releases only ownership acquired by this quick-grab click.
                var reservationsReleased = false;
                try
                {
                    reservationsReleased = NearbyResourceService.ReleaseReservations(reservations, false);
                }
                catch (Exception exception)
                {
                    RuntimeContext.Plugin?.Log.LogError("Quick-grab reservation cleanup threw: " + exception);
                }
                if (!reservationsReleased)
                {
                    DisableAfterFatalFailure("Stackmaster could not fully release a quick-grab reservation.");
                }
            }
        }

        private static bool TryPrepare(
            Player player,
            Piece piece,
            IReadOnlyList<ResourceRequirement> reservationRequirements,
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
            capture = NearbyResourceService.CaptureForQuickGrab(player, true, true);
            var allowedIdentities = new HashSet<string>(
                (reservationRequirements ?? Array.Empty<ResourceRequirement>())
                    .Select(requirement => StableIdentityKey(requirement.ItemName, requirement.Quality)),
                StringComparer.Ordinal);
            var preparedCapture = capture;
            var eligibleStacks = preparedCapture.Stacks.Where(stack =>
            {
                RuntimeResourceStack runtime;
                string prefabName;
                int quality;
                return !string.Equals(stack.InventoryId, "player", StringComparison.Ordinal) &&
                       preparedCapture.RuntimeStacks.TryGetValue(stack.StackId, out runtime) &&
                       ExpeditionReservations.TryGetStableItemIdentity(runtime.Item, out prefabName, out quality) &&
                       allowedIdentities.Contains(StableIdentityKey(prefabName, quality));
            });
            plan = WithdrawalPlanner.Plan(requirements, eligibleStacks);
            if (!plan.IsSatisfiable || plan.PlannedUnits != plan.RequiredUnits)
            {
                failure = "nearby storage does not contain the exact recipe materials required for this build piece";
                return false;
            }
            if (!PlanMatchesReservationRequirements(plan, capture, reservationRequirements))
            {
                failure = "nearby materials did not match the build-piece recipe identity exactly";
                return false;
            }
            if (!NearbyResourceService.TryResolveRequiredContainers(player, plan, capture, out handles, out failure))
            {
                return false;
            }
            return true;
        }

        private static string StableIdentityKey(string prefabName, int quality)
        {
            return (prefabName ?? string.Empty) + "\u001f" + quality.ToString(CultureInfo.InvariantCulture);
        }

        private static bool PlanMatchesReservationRequirements(
            ResourceWithdrawalPlan plan,
            NearbyResourceCapture capture,
            IReadOnlyList<ResourceRequirement> reservationRequirements)
        {
            if (plan == null || capture == null || reservationRequirements == null) return false;
            var expected = reservationRequirements
                .GroupBy(requirement => StableIdentityKey(requirement.ItemName, requirement.Quality), StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Sum(requirement => requirement.Quantity), StringComparer.Ordinal);
            var actual = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var step in plan.Steps)
            {
                RuntimeResourceStack runtime;
                string prefabName;
                int quality;
                if (!capture.RuntimeStacks.TryGetValue(step.StackId, out runtime) ||
                    !ExpeditionReservations.TryGetStableItemIdentity(runtime.Item, out prefabName, out quality))
                {
                    return false;
                }
                var key = StableIdentityKey(prefabName, quality);
                int current;
                actual.TryGetValue(key, out current);
                actual[key] = checked(current + step.Quantity);
            }
            return expected.Count == actual.Count && expected.All(pair =>
            {
                int quantity;
                return actual.TryGetValue(pair.Key, out quantity) && quantity == pair.Value;
            });
        }

        private static bool TryPlanCapacity(
            Player player,
            NearbyResourceCapture capture,
            ResourceWithdrawalPlan withdrawal,
            out QuickGrabCapacityPlan plan,
            out string failure)
        {
            failure = null;
            var catalog = new CompatibilityCatalog();
            var playerInventory = player.GetInventory();
            var playerSnapshot = InventorySnapshots.CaptureInventory("player", playerInventory, catalog);
            var cargo = new List<QuickGrabCargoStack>(withdrawal.Steps.Count);
            foreach (var step in withdrawal.Steps)
            {
                RuntimeResourceStack runtime;
                if (!capture.RuntimeStacks.TryGetValue(step.StackId, out runtime) || runtime.Item == null || runtime.Item.m_shared == null)
                {
                    plan = null;
                    failure = "planned quick-grab material disappeared";
                    return false;
                }
                cargo.Add(new QuickGrabCargoStack(
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
                failure = "the required materials are too heavy";
                return false;
            }
            if (!plan.FitsSlots)
            {
                failure = "the required materials do not fit in inventory";
                return false;
            }
            return true;
        }

        private static bool ExecuteAtomic(
            Player player,
            NearbyResourceCapture capture,
            ResourceWithdrawalPlan withdrawal,
            QuickGrabCapacityPlan capacity,
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
            var backups = new List<QuickGrabInventoryBackup>
            {
                new QuickGrabInventoryBackup(playerInventory, preserveItemIdentity: true)
            };
            backups.AddRange(reservations.Select(reservation => new QuickGrabInventoryBackup(
                reservation.Container.GetInventory(),
                preserveItemIdentity: false)));
            var completed = new List<CompletedQuickGrabMove>();
            var sourceMoved = new Dictionary<string, int>(StringComparer.Ordinal);

            try
            {
                foreach (var step in capacity.Steps)
                {
                RuntimeResourceStack runtime;
                if (!capture.RuntimeStacks.TryGetValue(step.SourceStackId, out runtime) || runtime.Container == null)
                {
                    failure = "planned quick-grab source disappeared";
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
                    failure = "planned quick-grab source stack changed before transfer";
                    return RollbackOrDisable(player, capture.Scope, reservations, completed, backups, failure, out failure);
                }

                var destinationPosition = InventorySnapshots.PositionForSlot(playerInventory, step.DestinationSlot);
                var destination = playerInventory.GetItemAt(destinationPosition.x, destinationPosition.y);
                if ((destination == null && step.ExpectedDestinationQuantity != 0) ||
                    (destination != null &&
                     (destination.m_stack != step.ExpectedDestinationQuantity ||
                      !string.Equals(catalog.KeyFor(destination), step.CompatibilityKey, StringComparison.Ordinal))))
                {
                    failure = "player inventory changed before quick-grab transfer";
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
                    RuntimeContext.Plugin?.Log.LogError("Quick-grab transfer primitive threw: " + exception);
                    // A throwing game primitive may still have mutated either inventory. Let the
                    // transaction-wide handler restore every pre-transaction snapshot regardless
                    // of whether the observed counts happen to look exact.
                    throw;
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
                    completed.Add(new CompletedQuickGrabMove(
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
                    failure = "game transfer primitive declined the quick-grab move";
                    return RollbackOrDisable(player, capture.Scope, reservations, completed, backups, failure, out failure);
                }

                failure = "an unexpected quick-grab transfer result required full rollback";
                var restored = RestoreBackups(backups, reservations);
                if (!restored)
                {
                    RuntimeContext.Disable("Quick-grab rollback could not restore every inventory.");
                    failure += "; rollback could not restore every item";
                }
                else
                {
                    RuntimeContext.Disable("Unexpected quick-grab transfer result; inventories were restored.");
                    failure += "; inventories were restored and Stackmaster was disabled";
                }
                return false;
            }

                var expectedUnits = withdrawal.RequiredUnits;
                if (completed.Sum(move => move.Quantity) != expectedUnits)
                {
                    failure = "quick-grab transfer did not complete the exact material set";
                    return RollbackOrDisable(player, capture.Scope, reservations, completed, backups, failure, out failure);
                }
                return true;
            }
            catch (Exception exception)
            {
                RuntimeContext.Plugin?.Log.LogError("Quick-grab transaction threw after mutation began: " + exception);
                // The current move may have mutated before throwing and therefore may not yet be
                // represented in completed. Restore every inventory from its pre-transaction image.
                var restored = false;
                try
                {
                    restored = RestoreBackups(backups, reservations);
                }
                catch (Exception rollbackException)
                {
                    RuntimeContext.Plugin?.Log.LogError("Quick-grab emergency snapshot rollback threw: " + rollbackException);
                }
                if (restored)
                {
                    failure = "a quick-grab transfer exception required full rollback; inventories were restored and Stackmaster was disabled";
                    RuntimeContext.Disable("Quick-grab transfer exception; inventories were restored.");
                }
                else
                {
                    failure = "a quick-grab transfer exception required full rollback; rollback could not restore every item";
                    RuntimeContext.Disable("Quick-grab rollback could not restore every inventory.");
                }
                NearbyResourceService.ResetCaches();
                NearbyBuildHudPatch.ResetCache();
                return false;
            }
        }

        private static bool RollbackOrDisable(
            Player player,
            StorageScope scope,
            IReadOnlyList<ContainerReservation> reservations,
            IList<CompletedQuickGrabMove> completed,
            IReadOnlyList<QuickGrabInventoryBackup> backups,
            string reason,
            out string failure)
        {
            var restored = false;
            try
            {
                restored = RollbackCompleted(player, scope, completed);
            }
            catch (Exception exception)
            {
                RuntimeContext.Plugin?.Log.LogError("Quick-grab reverse rollback threw; restoring snapshots: " + exception);
            }
            if (!restored)
            {
                try
                {
                    restored = RestoreBackups(backups, reservations);
                }
                catch (Exception exception)
                {
                    RuntimeContext.Plugin?.Log.LogError("Quick-grab snapshot rollback threw: " + exception);
                }
            }
            failure = restored
                ? reason + "; all transfers were rolled back"
                : reason + "; rollback could not restore every item";
            if (!restored)
            {
                RuntimeContext.Disable("Quick-grab rollback could not restore every inventory.");
            }
            NearbyResourceService.ResetCaches();
            NearbyBuildHudPatch.ResetCache();
            return false;
        }

        private static bool RollbackCompleted(
            Player player,
            StorageScope scope,
            IEnumerable<CompletedQuickGrabMove> completed)
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
                // Restore the chest first. If any later step fails, the full snapshots still
                // contain every unit and can replace both inventories without a loss window.
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
                if (!restored || TotalUnits(move.Source.Inventory) != sourceBefore + move.Quantity)
                {
                    return false;
                }

                var playerBefore = TotalUnits(playerInventory);
                if (!playerInventory.RemoveItem(destination, move.Quantity) ||
                    TotalUnits(playerInventory) != playerBefore - move.Quantity ||
                    !move.Reservation.AdvanceDataRevisionAfterMutation() ||
                    !NearbyResourceService.ReservationMatches(player, scope, move.Reservation))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool RestoreBackups(
            IEnumerable<QuickGrabInventoryBackup> backups,
            IEnumerable<ContainerReservation> reservations)
        {
            if (InventoryItemsField == null) return false;
            QuickGrabSnapshotRestoreTarget[] prepared;
            List<ItemDrop.ItemData>[] restoredLists;
            try
            {
                // Clone every detached state and reconstruct every target list before touching any
                // live inventory. The target lists contain the original ItemData objects, not clones,
                // so Humanoid equipment fields stay attached through emergency fallback rollback.
                prepared = backups.Select(backup => backup.PrepareRestore()).ToArray();
                restoredLists = prepared.Select(target => target.Items.RebuildInventoryList()).ToArray();
                if (restoredLists.Any(items => items.Any(item => item == null))) return false;
            }
            catch (Exception exception)
            {
                RuntimeContext.Plugin?.Log.LogError("Quick-grab snapshot preparation failed: " + exception);
                return false;
            }

            try
            {
                // Prove the reflected inventory list is writable for every participant before any
                // ItemData state is restored. The exact-runtime gate and compile-time field copy
                // surface make state-copy compatibility fail closed before rollback mutation begins.
                foreach (var target in prepared)
                {
                    var current = InventoryItemsField.GetValue(target.Inventory);
                    InventoryItemsField.SetValue(target.Inventory, current);
                    if (current == null || !ReferenceEquals(InventoryItemsField.GetValue(target.Inventory), current))
                    {
                        return false;
                    }
                }
            }
            catch (Exception exception)
            {
                RuntimeContext.Plugin?.Log.LogError("Quick-grab snapshot field preflight failed: " + exception);
                return false;
            }

            var allRestored = true;
            for (var index = 0; index < prepared.Length; index++)
            {
                var target = prepared[index];
                try
                {
                    target.Items.RestoreStates(RestoreItemDataState);
                    InventoryItemsField.SetValue(target.Inventory, restoredLists[index]);
                }
                catch (Exception exception)
                {
                    allRestored = false;
                    RuntimeContext.Plugin?.Log.LogError("Quick-grab identity-preserving restore failed: " + exception);
                }
            }
            foreach (var target in prepared)
            {
                try
                {
                    allRestored &= target.Items.HasExactOriginalReferences(target.Inventory.GetAllItems()) &&
                        target.Items.StatesMatch(ItemDataStateMatches) &&
                        TotalUnits(target.Inventory) == target.TotalUnits;
                    target.Inventory.m_onChanged?.Invoke();
                }
                catch (Exception exception)
                {
                    allRestored = false;
                    RuntimeContext.Plugin?.Log.LogError("Quick-grab restored-inventory notification failed: " + exception);
                }
            }

            foreach (var reservation in reservations)
            {
                try
                {
                    allRestored &= reservation.AdvanceDataRevisionAfterMutation();
                }
                catch (Exception exception)
                {
                    allRestored = false;
                    RuntimeContext.Plugin?.Log.LogError("Quick-grab rollback revision update failed: " + exception);
                }
            }
            return allRestored;
        }

        private static void RestoreItemDataState(ItemDrop.ItemData original, ItemDrop.ItemData snapshot)
        {
            // ItemData is a field-only DTO in the pinned runtime. Copy every declared instance field
            // explicitly; the exact-assembly compatibility gate prevents silent layout drift.
            original.m_shared = snapshot.m_shared;
            original.m_stack = snapshot.m_stack;
            original.m_durability = snapshot.m_durability;
            original.m_equipped = snapshot.m_equipped;
            original.m_quality = snapshot.m_quality;
            original.m_variant = snapshot.m_variant;
            original.m_crafterID = snapshot.m_crafterID;
            original.m_crafterName = snapshot.m_crafterName;
            original.m_worldLevel = snapshot.m_worldLevel;
            original.m_pickedUp = snapshot.m_pickedUp;
            original.m_cheated = snapshot.m_cheated;
            original.m_gridPos = snapshot.m_gridPos;
            original.m_dropPrefab = snapshot.m_dropPrefab;
            original.m_lastAttackTime = snapshot.m_lastAttackTime;
            original.m_lastProjectile = snapshot.m_lastProjectile;
            original.m_customData = snapshot.m_customData;
        }

        private static bool ItemDataStateMatches(ItemDrop.ItemData original, ItemDrop.ItemData snapshot)
        {
            if (!ReferenceEquals(original.m_shared, snapshot.m_shared) ||
                original.m_stack != snapshot.m_stack ||
                !original.m_durability.Equals(snapshot.m_durability) ||
                original.m_equipped != snapshot.m_equipped ||
                original.m_quality != snapshot.m_quality ||
                original.m_variant != snapshot.m_variant ||
                original.m_crafterID != snapshot.m_crafterID ||
                !string.Equals(original.m_crafterName, snapshot.m_crafterName, StringComparison.Ordinal) ||
                original.m_worldLevel != snapshot.m_worldLevel ||
                original.m_pickedUp != snapshot.m_pickedUp ||
                original.m_cheated != snapshot.m_cheated ||
                !original.m_gridPos.Equals(snapshot.m_gridPos) ||
                !ReferenceEquals(original.m_dropPrefab, snapshot.m_dropPrefab) ||
                !original.m_lastAttackTime.Equals(snapshot.m_lastAttackTime) ||
                !ReferenceEquals(original.m_lastProjectile, snapshot.m_lastProjectile))
            {
                return false;
            }

            if (original.m_customData == null || snapshot.m_customData == null)
            {
                return original.m_customData == null && snapshot.m_customData == null;
            }
            return original.m_customData.Count == snapshot.m_customData.Count &&
                snapshot.m_customData.All(pair =>
                    original.m_customData.TryGetValue(pair.Key, out var value) &&
                    string.Equals(value, pair.Value, StringComparison.Ordinal));
        }

        private static int TotalUnits(Inventory inventory)
            => inventory.GetAllItems().Sum(item => item.m_stack);

        private static void DisableAfterFatalFailure(string reason)
        {
            try
            {
                RuntimeContext.Disable(reason);
            }
            catch (Exception exception)
            {
                RuntimeContext.Plugin?.Log.LogError("Stackmaster fatal-disable fallback failed: " + exception);
            }
        }

        private static bool ReservationRequirementsMatch(
            Piece piece,
            IReadOnlyList<ResourceRequirement> captured)
        {
            IReadOnlyList<ResourceRequirement> current;
            if (captured == null ||
                !ExpeditionReservations.TryGetStableRequirements(piece, out current))
            {
                return false;
            }

            Func<IEnumerable<ResourceRequirement>, ResourceRequirement[]> normalize = values => values
                .GroupBy(value => new { value.ItemName, value.Quality })
                .Select(group => new ResourceRequirement(
                    group.Key.ItemName,
                    checked(group.Sum(value => value.Quantity)),
                    group.Key.Quality))
                .OrderBy(value => value.ItemName, StringComparer.Ordinal)
                .ThenBy(value => value.Quality)
                .ToArray();
            var left = normalize(captured);
            var right = normalize(current);
            return left.Length == right.Length && left.Zip(right, (a, b) =>
                string.Equals(a.ItemName, b.ItemName, StringComparison.Ordinal) &&
                a.Quality == b.Quality &&
                a.Quantity == b.Quantity).All(equal => equal);
        }

        private static void ShowFailure(string failure)
        {
            var detail = string.IsNullOrWhiteSpace(failure) ? "the request could not be completed safely" : failure;
            RuntimeContext.Plugin?.Log.LogWarning("Quick-grab action rejected safely: " + detail + ".");
            RuntimeContext.ShowCenter("Stackmaster: " + detail + ". No reservation was added and nothing was moved.");
        }

        private static void ShowReservationOnly(string failure)
        {
            var detail = string.IsNullOrWhiteSpace(failure) ? "the complete material set was not available" : failure;
            RuntimeContext.Plugin?.Log.LogInfo("Quick Grab reservation saved without materials: " + detail + ".");
            var visibleName = Localization.instance != null
                ? Localization.instance.Localize(_activeDisplayName ?? string.Empty)
                : _activeDisplayName ?? string.Empty;
            RuntimeContext.ShowTopLeft(QuickStackSummaryFormatter.FormatReservationWithoutMaterials(
                string.IsNullOrWhiteSpace(visibleName) ? "build piece" : visibleName,
                _activeCommittedCount));
        }
    }
}
