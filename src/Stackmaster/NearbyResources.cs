#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Stackmaster.Core;
using TMPro;
using UnityEngine;

namespace Stackmaster
{
    internal enum ResourceActionKind
    {
        None,
        Crafting,
        Building
    }

    internal static class ResourceActionContext
    {
        [ThreadStatic]
        private static ResourceActionKind _current;

        internal static ResourceActionKind Current => _current;

        internal static ResourceActionKind Enter(ResourceActionKind next)
        {
            var previous = _current;
            _current = next;
            return previous;
        }

        internal static void Restore(ResourceActionKind previous)
        {
            _current = previous;
        }

        internal static void Reset()
        {
            _current = ResourceActionKind.None;
        }
    }

    internal sealed class RemovedResource
    {
        internal RemovedResource(Inventory inventory, ItemDrop.ItemData item, Vector2i position, int quantity)
        {
            Inventory = inventory;
            Item = item;
            Position = position;
            Quantity = quantity;
        }

        internal Inventory Inventory { get; }
        internal ItemDrop.ItemData Item { get; }
        internal Vector2i Position { get; }
        internal int Quantity { get; }
    }

    internal sealed class ContainerReservation
    {
        internal ContainerReservation(ContainerHandle handle)
        {
            Handle = handle;
        }

        internal ContainerHandle Handle { get; }
        internal Container Container => Handle.Container;
        internal long LocalSession { get; private set; }
        internal uint DataRevision { get; private set; }
        internal ushort OwnerRevision { get; private set; }

        internal bool CaptureRevisionBaseline()
        {
            var view = Handle.NetworkView;
            var zdo = view != null && view.IsValid() ? view.GetZDO() : null;
            if (zdo == null || ZDOMan.instance == null) return false;
            LocalSession = ZDOMan.GetSessionID();
            DataRevision = zdo.DataRevision;
            OwnerRevision = zdo.OwnerRevision;
            return true;
        }

        internal bool AdvanceDataRevisionAfterMutation()
        {
            var view = Handle.NetworkView;
            var zdo = view != null && view.IsValid() ? view.GetZDO() : null;
            if (zdo == null || zdo.OwnerRevision != OwnerRevision) return false;
            DataRevision = zdo.DataRevision;
            return true;
        }
    }

    internal sealed class PendingReservationRelease
    {
        internal PendingReservationRelease(ContainerReservation reservation, bool releaseMatchingOwnership)
        {
            Reservation = reservation;
            ReleaseMatchingOwnership = releaseMatchingOwnership;
        }

        internal ContainerReservation Reservation { get; }
        internal bool ReleaseMatchingOwnership { get; set; }
    }

    internal static class ResourceTransactionContext
    {
        [ThreadStatic]
        private static IReadOnlyList<RemovedResource> _removed;
        [ThreadStatic]
        private static IReadOnlyList<ContainerReservation> _reservations;
        [ThreadStatic]
        private static ItemDrop.ItemData _selectedIngredient;
        [ThreadStatic]
        private static int _selectedAmount;
        [ThreadStatic]
        private static int _selectedExtraAmount;
        [ThreadStatic]
        private static int _expectedUnits;
        [ThreadStatic]
        private static int _acknowledgedUnits;

        internal static bool Active => _removed != null;
        internal static ItemDrop.ItemData SelectedIngredient => _selectedIngredient;
        internal static int SelectedAmount => _selectedAmount;
        internal static int SelectedExtraAmount => _selectedExtraAmount;

        internal static void Begin(
            IReadOnlyList<RemovedResource> removed,
            int expectedUnits,
            IReadOnlyList<ContainerReservation> reservations)
        {
            if (_removed != null) throw new InvalidOperationException("A nearby-resource transaction is already active.");
            _removed = removed ?? Array.Empty<RemovedResource>();
            _reservations = reservations ?? Array.Empty<ContainerReservation>();
            _expectedUnits = expectedUnits;
            _acknowledgedUnits = 0;
        }

        internal static bool AcknowledgeVanillaRemoval(int amount)
        {
            if (_removed == null || amount <= 0 || _acknowledgedUnits > _expectedUnits - amount) return false;
            _acknowledgedUnits += amount;
            return true;
        }

        internal static void SetSelectedIngredient(ItemDrop.ItemData item, int amount, int extraAmount)
        {
            _selectedIngredient = item;
            _selectedAmount = amount;
            _selectedExtraAmount = extraAmount;
        }

        internal static bool Complete(ResourceActionKind actionKind)
        {
            if (_removed == null) return true;
            if (_acknowledgedUnits == _expectedUnits)
            {
                Clear(actionKind == ResourceActionKind.Building);
                return true;
            }
            if (_acknowledgedUnits == 0)
            {
                Rollback();
            }
            else
            {
                // Some vanilla cost calls ran, so the craft/placement output already exists.
                // Keep the exact preplanned charge rather than restoring it and duplicating value,
                // but release ownership immediately because success was not fully confirmed.
                Clear(false);
            }
            RuntimeContext.Disable("Vanilla did not confirm the complete nearby-resource cost.");
            return false;
        }

        internal static void Shutdown()
        {
            if (_removed != null)
            {
                Rollback();
            }
        }

        internal static bool Rollback()
        {
            var removed = _removed;
            var reservations = _reservations;
            ResetState();
            var restored = true;
            try
            {
                // Keep every required chest reserved until compensation has finished.
                restored = removed == null || NearbyResourceService.Rollback(removed);
            }
            finally
            {
                ReleaseReservations(reservations);
            }
            if (!restored) RuntimeContext.Disable("Nearby resource rollback could not restore every item.");
            return restored;
        }

        private static void Clear(bool retainSuccessfulBuildOwnership)
        {
            var reservations = _reservations;
            ResetState();
            ReleaseReservations(reservations, retainSuccessfulBuildOwnership);
        }

        private static void ResetState()
        {
            _removed = null;
            _reservations = null;
            _selectedIngredient = null;
            _selectedAmount = 0;
            _selectedExtraAmount = 0;
            _expectedUnits = 0;
            _acknowledgedUnits = 0;
        }

        private static void ReleaseReservations(
            IEnumerable<ContainerReservation> reservations,
            bool retainSuccessfulBuildOwnership = false)
        {
            if (reservations == null) return;
            var held = reservations.ToArray();
            var reservationsReleased = NearbyResourceService.ReleaseReservations(
                held,
                releaseMatchingOwnership: false);

            // Only ownership that Stackmaster demonstrably acquired and this successful build
            // actually consumed can enter or renew the sliding build lease. A failed exact
            // in-use cleanup is retained and blocks even shutdown ownership release until the
            // session-lifetime safety update clears that reservation first.
            if (retainSuccessfulBuildOwnership)
            {
                OwnershipLeaseManager.RenewForSuccessfulBuild(held.Select(item => item.Handle));
            }
            else
            {
                OwnershipLeaseManager.ReleaseMatching(held.Select(item => item.Handle),
                    "nearby-resource transaction ended");
            }
            if (!reservationsReleased)
            {
                RuntimeContext.Disable("Stackmaster could not fully release a nearby-resource reservation.");
            }
        }
    }

    internal sealed class RuntimeResourceStack
    {
        internal RuntimeResourceStack(string stackId, Inventory inventory, ItemDrop.ItemData item, ContainerHandle container)
        {
            StackId = stackId;
            Inventory = inventory;
            Item = item;
            Container = container;
        }

        internal string StackId { get; }
        internal Inventory Inventory { get; }
        internal ItemDrop.ItemData Item { get; }
        internal ContainerHandle Container { get; }
    }

    internal sealed class NearbyResourceCapture
    {
        internal NearbyResourceCapture(
            StorageScope scope,
            IReadOnlyList<ContainerHandle> containers,
            IReadOnlyList<ResourceStack> stacks,
            IReadOnlyDictionary<string, RuntimeResourceStack> runtimeStacks)
        {
            Scope = scope;
            Containers = containers;
            Stacks = stacks;
            RuntimeStacks = runtimeStacks;
        }

        internal StorageScope Scope { get; }
        internal IReadOnlyList<ContainerHandle> Containers { get; }
        internal IReadOnlyList<ResourceStack> Stacks { get; }
        internal IReadOnlyDictionary<string, RuntimeResourceStack> RuntimeStacks { get; }
    }

    internal sealed class CraftingResourcePlan
    {
        internal CraftingResourcePlan(
            IReadOnlyList<ResourceRequirement> requirements,
            NearbyResourceCapture capture,
            ResourceWithdrawalPlan plan,
            ContainerHandle[] requiredHandles,
            bool requiresOwnershipTransfer,
            ItemDrop.ItemData selectedIngredient,
            int selectedAmount,
            int selectedExtraAmount)
        {
            Requirements = requirements;
            Capture = capture;
            Plan = plan;
            RequiredHandles = requiredHandles;
            RequiresOwnershipTransfer = requiresOwnershipTransfer;
            SelectedIngredient = selectedIngredient;
            SelectedAmount = selectedAmount;
            SelectedExtraAmount = selectedExtraAmount;
        }

        internal IReadOnlyList<ResourceRequirement> Requirements { get; }
        internal NearbyResourceCapture Capture { get; }
        internal ResourceWithdrawalPlan Plan { get; }
        internal ContainerHandle[] RequiredHandles { get; }
        internal bool RequiresOwnershipTransfer { get; }
        internal ItemDrop.ItemData SelectedIngredient { get; }
        internal int SelectedAmount { get; }
        internal int SelectedExtraAmount { get; }
    }

    internal sealed class PreparedCraftingResources
    {
        internal PreparedCraftingResources(
            CraftingResourcePlan source,
            NearbyResourceCapture capture,
            ResourceWithdrawalPlan plan,
            ContainerHandle[] requiredHandles,
            IReadOnlyList<ContainerReservation> reservations,
            string inventorySignature)
        {
            Source = source;
            Capture = capture;
            Plan = plan;
            RequiredHandles = requiredHandles;
            Reservations = reservations;
            InventorySignature = inventorySignature;
        }

        internal CraftingResourcePlan Source { get; }
        internal NearbyResourceCapture Capture { get; }
        internal ResourceWithdrawalPlan Plan { get; }
        internal ContainerHandle[] RequiredHandles { get; }
        internal IReadOnlyList<ContainerReservation> Reservations { get; private set; }
        internal string InventorySignature { get; }

        internal IReadOnlyList<ContainerReservation> TakeReservations()
        {
            var result = Reservations;
            Reservations = Array.Empty<ContainerReservation>();
            return result;
        }
    }

    internal sealed class RuntimeRequirementAvailability
    {
        internal RuntimeRequirementAvailability(
            int required,
            int totalAvailable,
            bool aggregateSatisfied,
            bool playerSatisfied)
        {
            Required = required;
            TotalAvailable = totalAvailable;
            AggregateSatisfied = aggregateSatisfied;
            PlayerSatisfied = playerSatisfied;
        }

        internal int Required { get; }
        internal int TotalAvailable { get; }
        internal bool AggregateSatisfied { get; }
        internal bool PlayerSatisfied { get; }
    }

    internal static class NearbyResourceService
    {
        private const string PlayerInventoryId = "player";
        private static readonly ResourceWithdrawalPlanner Planner = new ResourceWithdrawalPlanner();
        private static readonly Dictionary<string, PendingReservationRelease> PendingReservationReleases =
            new Dictionary<string, PendingReservationRelease>(StringComparer.Ordinal);
        private static float _nextReservationReleaseRetryAt;
        private static readonly MethodInfo AddItemAtMethod = AccessTools.DeclaredMethod(
            typeof(Inventory),
            "AddItem",
            new[] { typeof(ItemDrop.ItemData), typeof(int), typeof(int), typeof(int), typeof(bool) });
        private static int _cachedFrame = -1;
        private static Player _cachedPlayer;
        private static string _cachedScopeSignature;
        private static bool _cachedMatchWorldLevel;
        private static NearbyResourceCapture _cachedCapture;

        internal static bool HasPendingReservationReleases => PendingReservationReleases.Count > 0;

        internal static bool HasPendingReservationRelease(string containerId)
            => !string.IsNullOrEmpty(containerId) && PendingReservationReleases.ContainsKey(containerId);

        internal static void DiscardEndedSessionState()
        {
            // Called only after a different ZNet instance has started. Records from the ended
            // transport can no longer match a live reservation and must not cross into the new world.
            PendingReservationReleases.Clear();
            _nextReservationReleaseRetryAt = 0f;
            ResetCaches();
        }

        internal static void ResetCaches()
        {
            _cachedFrame = -1;
            _cachedPlayer = null;
            _cachedScopeSignature = null;
            _cachedMatchWorldLevel = false;
            _cachedCapture = null;
        }

        internal static bool HasPieceRequirements(Player player, Piece piece, bool fresh)
        {
            if (player == null || piece == null) return false;
            if (ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey(piece.FreeBuildKey())) return true;
            var requirements = PieceRequirements(piece);
            return requirements.Count == 0 || Plan(player, requirements, true, fresh).IsSatisfiable;
        }

        internal static IReadOnlyList<RuntimeRequirementAvailability> GetPieceRequirementAvailability(
            Player player,
            Piece piece,
            bool fresh)
        {
            if (player == null || piece == null) return Array.Empty<RuntimeRequirementAvailability>();

            var requirements = piece.m_resources ?? Array.Empty<Piece.Requirement>();
            var capture = Capture(player, true, fresh);
            var validRequirements = requirements
                .Where(requirement => requirement != null && requirement.m_resItem != null && requirement.m_amount > 0)
                .Select(requirement => new ResourceRequirement(
                    requirement.m_resItem.m_itemData.m_shared.m_name,
                    requirement.m_amount))
                .ToList();
            var evaluated = ResourceDisplayAvailability.Evaluate(validRequirements, capture.Stacks);
            var playerEvaluated = ResourceDisplayAvailability.Evaluate(
                validRequirements,
                capture.Stacks.Where(stack => string.Equals(stack.InventoryId, PlayerInventoryId, StringComparison.Ordinal)));
            var next = 0;
            var result = new List<RuntimeRequirementAvailability>(requirements.Length);
            foreach (var requirement in requirements)
            {
                if (requirement == null || requirement.m_resItem == null || requirement.m_amount <= 0)
                {
                    result.Add(new RuntimeRequirementAvailability(0, 0, true, true));
                    continue;
                }

                var entry = evaluated[next];
                var playerEntry = playerEvaluated[next++];
                result.Add(new RuntimeRequirementAvailability(
                    entry.Required,
                    entry.Available,
                    entry.IsSatisfied,
                    playerEntry.IsSatisfied));
            }
            return result;
        }

        internal static IReadOnlyList<RuntimeRequirementAvailability> GetRecipeRequirementAvailability(
            Player player,
            Recipe recipe,
            IEnumerable<Piece.Requirement> visibleRequirements,
            int qualityLevel,
            int craftMultiplier,
            bool fresh)
        {
            if (player == null || recipe == null || visibleRequirements == null || craftMultiplier <= 0)
            {
                return Array.Empty<RuntimeRequirementAvailability>();
            }

            var requirements = visibleRequirements.ToList();
            var capture = Capture(player, true, fresh);
            var validRequirements = requirements
                .Where(requirement => requirement != null && requirement.m_resItem != null && requirement.GetAmount(qualityLevel) > 0)
                .Select(requirement => new ResourceRequirement(
                    requirement.m_resItem.m_itemData.m_shared.m_name,
                    checked(requirement.GetAmount(qualityLevel) * craftMultiplier)))
                .ToList();
            var evaluated = ResourceDisplayAvailability.Evaluate(
                validRequirements,
                capture.Stacks,
                alternatives: recipe.m_requireOnlyOneIngredient,
                requireSingleQuality: recipe.m_requireOnlyOneIngredient);
            var playerEvaluated = ResourceDisplayAvailability.Evaluate(
                validRequirements,
                capture.Stacks.Where(stack => string.Equals(stack.InventoryId, PlayerInventoryId, StringComparison.Ordinal)),
                alternatives: recipe.m_requireOnlyOneIngredient,
                requireSingleQuality: recipe.m_requireOnlyOneIngredient);
            var next = 0;
            var result = new List<RuntimeRequirementAvailability>(requirements.Count);
            foreach (var requirement in requirements)
            {
                var required = requirement == null || requirement.m_resItem == null
                    ? 0
                    : checked(requirement.GetAmount(qualityLevel) * craftMultiplier);
                if (required <= 0)
                {
                    result.Add(new RuntimeRequirementAvailability(0, 0, true, true));
                    continue;
                }

                var entry = evaluated[next];
                var playerEntry = playerEvaluated[next++];
                result.Add(new RuntimeRequirementAvailability(
                    entry.Required,
                    entry.Available,
                    entry.IsSatisfied,
                    playerEntry.IsSatisfied));
            }
            return result;
        }

        internal static bool HasRecipeRequirements(Player player, Recipe recipe, int qualityLevel, int craftMultiplier, bool fresh)
        {
            if (player == null || recipe == null || craftMultiplier <= 0) return false;
            if (recipe.m_requireOnlyOneIngredient)
            {
                int amount;
                int extraAmount;
                return FindFirstRequiredItem(player, recipe, qualityLevel, craftMultiplier, fresh, out amount, out extraAmount) != null;
            }

            var requirements = RecipeRequirements(player, recipe, qualityLevel, craftMultiplier);
            return requirements.Count == 0 || Plan(player, requirements, true, fresh).IsSatisfiable;
        }

        internal static bool TryBeginPieceTransaction(Player player, Piece piece, out string failure)
        {
            failure = null;
            if (player == null || piece == null)
            {
                failure = "invalid building transaction";
                return false;
            }
            return TryBeginTransaction(player, PieceRequirements(piece), true, true, out failure);
        }

        internal static bool TryPlanCraftingResources(
            Player player,
            Recipe recipe,
            int qualityLevel,
            int craftMultiplier,
            out CraftingResourcePlan planned,
            out string failure)
        {
            planned = null;
            failure = null;
            if (player == null || recipe == null || craftMultiplier <= 0)
            {
                failure = "invalid crafting transaction";
                return false;
            }

            ItemDrop.ItemData selected = null;
            var amount = 0;
            var extraAmount = 0;
            IReadOnlyList<ResourceRequirement> requirements;
            if (!recipe.m_requireOnlyOneIngredient)
            {
                requirements = RecipeRequirements(player, recipe, qualityLevel, craftMultiplier);
            }
            else
            {
                selected = FindFirstRequiredItem(player, recipe, qualityLevel, craftMultiplier, true, out amount, out extraAmount);
                if (selected == null || selected.m_shared == null || amount <= 0)
                {
                    failure = "fresh nearby stock no longer satisfies the selected ingredient";
                    return false;
                }
                requirements = new[] { new ResourceRequirement(selected.m_shared.m_name, amount, selected.m_quality) };
            }

            var capture = Capture(player, true, true);
            ResourceWithdrawalPlan plan;
            if (!Planner.TryPlanWithMinimumContainers(requirements, capture.Stacks, PlayerInventoryId, out plan))
            {
                failure = "the exact minimum-container search exceeded its safe bound";
                return false;
            }
            if (!plan.IsSatisfiable || plan.PlannedUnits != plan.RequiredUnits)
            {
                failure = "fresh nearby stock no longer satisfies the exact complete cost";
                return false;
            }

            ContainerHandle[] handles;
            if (!TryResolveRequiredContainers(player, plan, capture, out handles, out failure)) return false;
            if (handles.Any(handle => OwnershipLeaseManager.HasPotentialAcquisition(handle.Id)))
            {
                failure = "a previous ownership transition is still pending";
                return false;
            }

            planned = new CraftingResourcePlan(
                requirements,
                capture,
                plan,
                handles,
                handles.Any(handle => !handle.NetworkView.IsOwner() || !handle.Container.IsOwner()),
                selected,
                amount,
                extraAmount);
            return true;
        }

        internal static bool TryPrepareCraftingResources(
            Player player,
            CraftingResourcePlan source,
            out PreparedCraftingResources prepared,
            out string failure)
        {
            prepared = null;
            failure = null;
            if (player == null || source == null)
            {
                failure = "invalid crafting reservation";
                return false;
            }

            // Planning and HUD reads intentionally use detached ZDO-decoded inventories. Once
            // ownership is ready, a craft must switch to the live Container inventories before
            // reserving or removing anything. Mutating the detached planning snapshots would make
            // the crafted output real while every chest debit disappeared with the snapshot.
            NearbyResourceCapture mutableCapture;
            ResourceWithdrawalPlan mutablePlan;
            if (!TryCaptureOwnedPlan(
                    player,
                    source.Requirements,
                    true,
                    source.Capture,
                    source.Plan,
                    source.RequiredHandles,
                    source.RequiresOwnershipTransfer,
                    out mutableCapture,
                    out mutablePlan,
                    out failure))
            {
                return false;
            }
            if (!SameWithdrawalPlan(source.Plan, mutablePlan))
            {
                failure = "nearby or carried materials changed before the craft could be reserved";
                return false;
            }

            var handles = mutableCapture.Containers.ToArray();
            IReadOnlyList<ContainerReservation> reservations;
            if (!TryReserveContainers(player, mutableCapture.Scope, handles, true, out reservations, out failure)) return false;
            if (!RevalidateReservedContainers(player, mutableCapture.Scope, reservations, out failure) ||
                !RevalidateStacks(mutablePlan, mutableCapture, true, out failure))
            {
                ReleaseReservations(reservations);
                return false;
            }

            prepared = new PreparedCraftingResources(
                source,
                mutableCapture,
                mutablePlan,
                handles,
                reservations,
                CraftingInventorySignature(player, handles));
            return true;
        }

        internal static bool TryBeginPreparedCraftingTransaction(
            Player player,
            PreparedCraftingResources prepared,
            out string failure)
        {
            failure = null;
            if (player == null || prepared == null || prepared.Reservations == null ||
                prepared.Reservations.Count != prepared.RequiredHandles.Length)
            {
                failure = "the prepared crafting reservation is unavailable";
                return false;
            }
            if (ResourceTransactionContext.Active)
            {
                failure = "another nearby-resource transaction is already active";
                return false;
            }
            if (!string.Equals(prepared.InventorySignature,
                    CraftingInventorySignature(player, prepared.RequiredHandles),
                    StringComparison.Ordinal))
            {
                failure = "carried or nearby inventory changed during crafting";
                return false;
            }
            if (!RevalidateReservedContainers(player, prepared.Capture.Scope, prepared.Reservations, out failure) ||
                !RevalidateStacks(prepared.Plan, prepared.Capture, true, out failure))
            {
                return false;
            }

            var reservations = prepared.TakeReservations();
            var removed = new List<RemovedResource>();
            try
            {
                ResourceTransactionContext.Begin(removed, prepared.Plan.RequiredUnits, reservations);
            }
            catch (Exception exception)
            {
                ReleaseReservations(reservations);
                failure = "prepared crafting transaction could not begin: " + exception.GetType().Name;
                return false;
            }
            ResourceTransactionContext.SetSelectedIngredient(
                prepared.Source.SelectedIngredient,
                prepared.Source.SelectedAmount,
                prepared.Source.SelectedExtraAmount);
            try
            {
                if (!ExecuteWithRollback(player, prepared.Plan, prepared.Capture, reservations, removed, out failure))
                {
                    ResourceTransactionContext.Rollback();
                    return false;
                }
                return true;
            }
            catch (Exception exception)
            {
                var restored = ResourceTransactionContext.Rollback();
                failure = "resource removal threw " + exception.GetType().Name +
                    (restored ? "; all mutations were rolled back" : "; rollback could not restore every item");
                if (!restored) RuntimeContext.Disable(failure);
                return false;
            }
        }

        internal static void ReleasePreparedCraftingResources(PreparedCraftingResources prepared)
        {
            if (prepared == null) return;
            var reservations = prepared.TakeReservations();
            if (!ReleaseReservations(reservations, releaseMatchingOwnership: true))
            {
                RuntimeContext.Disable("Stackmaster could not fully release a prepared crafting reservation.");
            }
        }

        private static bool SameWithdrawalPlan(ResourceWithdrawalPlan left, ResourceWithdrawalPlan right)
        {
            if (left == null || right == null || left.RequiredUnits != right.RequiredUnits ||
                left.PlannedUnits != right.PlannedUnits || left.Steps.Count != right.Steps.Count)
            {
                return false;
            }
            for (var index = 0; index < left.Steps.Count; index++)
            {
                var a = left.Steps[index];
                var b = right.Steps[index];
                if (!string.Equals(a.InventoryId, b.InventoryId, StringComparison.Ordinal) ||
                    !string.Equals(a.StackId, b.StackId, StringComparison.Ordinal) ||
                    !string.Equals(a.ItemName, b.ItemName, StringComparison.Ordinal) ||
                    a.Quality != b.Quality || a.Quantity != b.Quantity)
                {
                    return false;
                }
            }
            return true;
        }

        private static string CraftingInventorySignature(Player player, IEnumerable<ContainerHandle> handles)
        {
            var parts = new List<string>();
            AppendInventorySignature(parts, PlayerInventoryId, player.GetInventory());
            foreach (var handle in handles.OrderBy(item => item.Id, StringComparer.Ordinal))
            {
                AppendInventorySignature(parts, handle.Id, handle.Container.GetInventory());
            }
            return string.Join("|", parts);
        }

        private static void AppendInventorySignature(ICollection<string> parts, string id, Inventory inventory)
        {
            if (inventory == null)
            {
                parts.Add(id + ":missing");
                return;
            }
            foreach (var item in inventory.GetAllItems()
                .Where(item => item != null)
                .OrderBy(item => item.m_gridPos.y)
                .ThenBy(item => item.m_gridPos.x))
            {
                parts.Add(string.Concat(
                    id, ":", item.m_gridPos.x.ToString(CultureInfo.InvariantCulture), ",",
                    item.m_gridPos.y.ToString(CultureInfo.InvariantCulture), ":",
                    item.m_shared == null ? "<missing>" : item.m_shared.m_name, ":",
                    item.m_quality.ToString(CultureInfo.InvariantCulture), ":",
                    item.m_worldLevel.ToString(CultureInfo.InvariantCulture), ":",
                    item.m_stack.ToString(CultureInfo.InvariantCulture)));
            }
        }

        internal static bool TryBeginRecipeTransaction(
            Player player,
            Recipe recipe,
            int qualityLevel,
            int craftMultiplier,
            out string failure)
        {
            failure = null;
            if (player == null || recipe == null || craftMultiplier <= 0)
            {
                failure = "invalid crafting transaction";
                return false;
            }

            if (!recipe.m_requireOnlyOneIngredient)
            {
                return TryBeginTransaction(player, RecipeRequirements(player, recipe, qualityLevel, craftMultiplier), true, false, out failure);
            }

            int amount;
            int extraAmount;
            var selected = FindFirstRequiredItem(player, recipe, qualityLevel, craftMultiplier, true, out amount, out extraAmount);
            if (selected == null || selected.m_shared == null || amount <= 0)
            {
                failure = "fresh nearby stock no longer satisfies the selected ingredient";
                return false;
            }
            if (!TryBeginTransaction(
                    player,
                    new[] { new ResourceRequirement(selected.m_shared.m_name, amount, selected.m_quality) },
                    true,
                    false,
                    out failure))
            {
                return false;
            }
            ResourceTransactionContext.SetSelectedIngredient(selected, amount, extraAmount);
            return true;
        }

        internal static ItemDrop.ItemData FindFirstRequiredItem(
            Player player,
            Recipe recipe,
            int qualityLevel,
            int craftMultiplier,
            bool fresh,
            out int amount,
            out int extraAmount)
        {
            amount = 0;
            extraAmount = 0;
            if (player == null || recipe == null || craftMultiplier <= 0) return null;

            var capture = Capture(player, true, fresh);
            var station = player.GetCurrentCraftingStation();
            foreach (var requirement in recipe.m_resources ?? Array.Empty<Piece.Requirement>())
            {
                if (!AppliesAtStation(requirement, station) || requirement.m_resItem == null) continue;
                var required = checked(requirement.GetAmount(qualityLevel) * craftMultiplier);
                if (required <= 0) continue;
                var name = requirement.m_resItem.m_itemData.m_shared.m_name;
                var maxQuality = requirement.m_resItem.m_itemData.m_shared.m_maxQuality;
                for (var quality = 1; quality <= maxQuality; quality++)
                {
                    var plan = Planner.Plan(
                        new[] { new ResourceRequirement(name, required, quality) },
                        capture.Stacks);
                    if (!plan.IsSatisfiable) continue;
                    var first = plan.Steps.FirstOrDefault();
                    RuntimeResourceStack runtime;
                    if (first == null || !capture.RuntimeStacks.TryGetValue(first.StackId, out runtime)) continue;
                    amount = required;
                    extraAmount = requirement.m_extraAmountOnlyOneIngredient;
                    return runtime.Item;
                }
            }
            return null;
        }

        private static bool TryBeginTransaction(
            Player player,
            IEnumerable<ResourceRequirement> requirements,
            bool matchWorldLevel,
            bool allowDeferredOwnership,
            out string failure)
        {
            failure = null;
            if (ResourceTransactionContext.Active)
            {
                failure = "another nearby-resource transaction is already active";
                return false;
            }

            var normalizedRequirements = requirements == null ? new List<ResourceRequirement>() : requirements.ToList();
            var readOnlyCapture = Capture(player, matchWorldLevel, true);
            ResourceWithdrawalPlan readOnlyPlan;
            if (!Planner.TryPlanWithMinimumContainers(
                    normalizedRequirements,
                    readOnlyCapture.Stacks,
                    PlayerInventoryId,
                    out readOnlyPlan))
            {
                failure = "the exact minimum-container search exceeded its safe bound";
                return false;
            }
            if (!readOnlyPlan.IsSatisfiable || readOnlyPlan.PlannedUnits != readOnlyPlan.RequiredUnits)
            {
                failure = "fresh nearby stock no longer satisfies the exact complete cost";
                return false;
            }

            ContainerHandle[] requiredHandles;
            if (!TryResolveRequiredContainers(player, readOnlyPlan, readOnlyCapture, out requiredHandles, out failure))
            {
                return false;
            }
            if (requiredHandles.Any(handle => OwnershipLeaseManager.HasPotentialAcquisition(handle.Id)))
            {
                failure = "a previous ownership transition is still pending";
                return false;
            }

            var unowned = requiredHandles
                .Where(handle => !handle.NetworkView.IsOwner() || !handle.Container.IsOwner())
                .ToArray();
            if (unowned.Length > 0)
            {
                if (!allowDeferredOwnership)
                {
                    failure = "required container ownership was not prepared before crafting";
                    return false;
                }
                // Valheim's owner-authorized RPC is asynchronous. Never block the main thread or
                // bypass the current owner with ClaimOwnership. This attempt is cancelled with no
                // mutation while only the minimum chests selected by the player-first plan are
                // requested. A successful bounded handshake prepares a safe retry.
                if (!NearbyResourceOwnership.TryBegin(
                        player,
                        normalizedRequirements,
                        matchWorldLevel,
                        readOnlyCapture,
                        readOnlyPlan,
                        requiredHandles,
                        unowned,
                        out failure))
                {
                    return false;
                }
                failure = null;
                return false;
            }

            var transactionBegan = false;
            try
            {
                NearbyResourceCapture mutableCapture;
                ResourceWithdrawalPlan mutablePlan;
                if (!TryCaptureOwnedPlan(
                        player,
                        normalizedRequirements,
                        matchWorldLevel,
                        readOnlyCapture,
                        readOnlyPlan,
                        requiredHandles,
                        false,
                        out mutableCapture,
                        out mutablePlan,
                        out failure))
                {
                    return false;
                }
                if (!RevalidateContainers(player, mutablePlan, mutableCapture, out failure)) return false;
                if (!RevalidateStacks(mutablePlan, mutableCapture, matchWorldLevel, out failure)) return false;

                IReadOnlyList<ContainerReservation> reservations;
                if (!TryReserveContainers(player, mutableCapture.Scope, requiredHandles, out reservations, out failure)) return false;
                if (!RevalidateReservedContainers(player, mutableCapture.Scope, reservations, out failure) ||
                    !RevalidateStacks(mutablePlan, mutableCapture, matchWorldLevel, out failure))
                {
                    ReleaseReservations(reservations);
                    return false;
                }

                var removed = new List<RemovedResource>();
                ResourceTransactionContext.Begin(removed, mutablePlan.RequiredUnits, reservations);
                transactionBegan = true;
                try
                {
                    if (!ExecuteWithRollback(player, mutablePlan, mutableCapture, reservations, removed, out failure))
                    {
                        ResourceTransactionContext.Rollback();
                        return false;
                    }
                    return true;
                }
                catch (Exception exception)
                {
                    var restored = ResourceTransactionContext.Rollback();
                    failure = "resource removal threw " + exception.GetType().Name +
                        (restored ? "; all mutations were rolled back" : "; rollback could not restore every item");
                    if (!restored) RuntimeContext.Disable(failure);
                    return false;
                }
            }
            finally
            {
                // Before Begin, no rollback context owns cleanup. Release only exact leases
                // matching this action's player-first minimum plan on every validation/cancel path.
                if (!transactionBegan)
                {
                    OwnershipLeaseManager.ReleaseMatching(requiredHandles,
                        "nearby-resource action ended before mutation");
                }
            }
        }

        internal static ResourceWithdrawalPlan Plan(
            Player player,
            IEnumerable<ResourceRequirement> requirements,
            bool matchWorldLevel,
            bool fresh)
        {
            return Planner.Plan(requirements, Capture(player, matchWorldLevel, fresh).Stacks);
        }

        internal static bool PieceNonMaterialRequirementsPass(Player player, Piece piece)
        {
            if (player == null || piece == null) return false;
            if (piece.m_craftingStation != null &&
                CraftingStation.HaveBuildStationInRange(piece.m_craftingStation.m_name, player.transform.position) == null &&
                (ZoneSystem.instance == null || !ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoWorkbench)))
            {
                return false;
            }
            if (!string.IsNullOrEmpty(piece.m_dlc) && (DLCMan.instance == null || !DLCMan.instance.IsDLCInstalled(piece.m_dlc)))
            {
                return false;
            }
            return true;
        }

        internal static List<ResourceRequirement> PieceRequirements(Piece piece)
        {
            return (piece.m_resources ?? Array.Empty<Piece.Requirement>())
                .Where(requirement => requirement != null && requirement.m_resItem != null && requirement.m_amount > 0)
                .Select(requirement => new ResourceRequirement(
                    requirement.m_resItem.m_itemData.m_shared.m_name,
                    requirement.m_amount))
                .ToList();
        }

        private static List<ResourceRequirement> RecipeRequirements(Player player, Recipe recipe, int qualityLevel, int craftMultiplier)
        {
            var station = player.GetCurrentCraftingStation();
            return (recipe.m_resources ?? Array.Empty<Piece.Requirement>())
                .Where(requirement => requirement != null && requirement.m_resItem != null && AppliesAtStation(requirement, station))
                .Select(requirement => new
                {
                    Name = requirement.m_resItem.m_itemData.m_shared.m_name,
                    Quantity = checked(requirement.GetAmount(qualityLevel) * craftMultiplier)
                })
                .Where(requirement => requirement.Quantity > 0)
                .Select(requirement => new ResourceRequirement(requirement.Name, requirement.Quantity))
                .ToList();
        }

        private static bool AppliesAtStation(Piece.Requirement requirement, CraftingStation station)
        {
            if (station != null) return station.m_upgrader == requirement.m_upgraderResource;
            return !requirement.m_upgraderResource;
        }

        internal static NearbyResourceCapture CaptureForExpedition(Player player, bool matchWorldLevel, bool fresh)
        {
            return Capture(player, matchWorldLevel, fresh);
        }

        private static NearbyResourceCapture Capture(Player player, bool matchWorldLevel, bool fresh)
        {
            var scope = StorageScopeProvider.Resolve(player);
            if (!fresh && _cachedCapture != null && _cachedFrame == Time.frameCount &&
                ReferenceEquals(_cachedPlayer, player) && _cachedMatchWorldLevel == matchWorldLevel &&
                string.Equals(_cachedScopeSignature, scope.Signature, StringComparison.Ordinal))
            {
                return _cachedCapture;
            }

            var catalog = new CompatibilityCatalog();
            // Resource accounting must inspect the complete active scope. Returning a partial
            // workbench mesh would advertise materials that an exact transaction cannot honor.
            var discovery = ContainerDiscovery.Discover(player, null, catalog, scope, true, true);
            var containers = discovery.Containers
                .Where(handle => handle != null && handle.ResourceReadable &&
                                 handle.ResourceInventory != null &&
                                 handle.NetworkView != null && handle.NetworkView.IsValid())
                .ToList();
            var resourceStacks = new List<ResourceStack>();
            var runtimeStacks = new Dictionary<string, RuntimeResourceStack>(StringComparer.Ordinal);
            AddInventory(resourceStacks, runtimeStacks, PlayerInventoryId, player.GetInventory(), null, 0, matchWorldLevel);
            for (var index = 0; index < containers.Count; index++)
            {
                var handle = containers[index];
                AddInventory(resourceStacks, runtimeStacks, handle.Id, handle.ResourceInventory, handle, index + 1, matchWorldLevel);
            }

            var capture = new NearbyResourceCapture(scope, containers, resourceStacks, runtimeStacks);
            _cachedFrame = Time.frameCount;
            _cachedPlayer = player;
            _cachedScopeSignature = scope.Signature;
            _cachedMatchWorldLevel = matchWorldLevel;
            _cachedCapture = capture;
            return capture;
        }

        private static void AddInventory(
            ICollection<ResourceStack> snapshots,
            IDictionary<string, RuntimeResourceStack> runtime,
            string inventoryId,
            Inventory inventory,
            ContainerHandle container,
            int inventoryOrder,
            bool matchWorldLevel)
        {
            if (inventory == null) return;
            foreach (var item in inventory.GetAllItems())
            {
                if (item == null || item.m_stack <= 0 || item.m_shared == null) continue;
                if (matchWorldLevel && item.m_worldLevel < Game.m_worldLevel) continue;
                var slot = checked((item.m_gridPos.y << 16) + item.m_gridPos.x);
                var stackId = inventoryId + ":" +
                    item.m_gridPos.x.ToString(CultureInfo.InvariantCulture) + "," +
                    item.m_gridPos.y.ToString(CultureInfo.InvariantCulture);
                snapshots.Add(new ResourceStack(
                    inventoryId,
                    stackId,
                    item.m_shared.m_name,
                    item.m_quality,
                    item.m_stack,
                    inventoryOrder,
                    slot));
                runtime.Add(stackId, new RuntimeResourceStack(stackId, inventory, item, container));
            }
        }

        internal static bool TryResolveRequiredContainers(
            Player player,
            ResourceWithdrawalPlan plan,
            NearbyResourceCapture capture,
            out ContainerHandle[] requiredHandles,
            out string failure)
        {
            failure = null;
            var handles = capture.Containers.ToDictionary(handle => handle.Id, StringComparer.Ordinal);
            var requiredIds = ResourceOwnershipSelection.RequiredContainerIds(plan, PlayerInventoryId).ToArray();
            var result = new List<ContainerHandle>(requiredIds.Length);
            foreach (var id in requiredIds)
            {
                ContainerHandle handle;
                if (!handles.TryGetValue(id, out handle) || !ValidateReadOnlyHandle(player, capture.Scope, handle, out failure))
                {
                    requiredHandles = Array.Empty<ContainerHandle>();
                    return false;
                }
                result.Add(handle);
            }
            requiredHandles = result.ToArray();
            return true;
        }

        private static bool ValidateReadOnlyHandle(Player player, StorageScope scope, ContainerHandle handle, out string failure)
        {
            failure = null;
            if (handle == null || handle.Container == null || handle.Container.GetType() != typeof(Container) ||
                (scope == null || !scope.Contains(handle.Container.transform.position)) ||
                handle.NetworkView == null || !handle.NetworkView.IsValid() || handle.NetworkView.GetZDO() == null ||
                !string.Equals(handle.NetworkView.GetZDO().m_uid.ToString(), handle.Id, StringComparison.Ordinal) ||
                !handle.NetworkView.HasOwner() || !handle.ResourceReadable || handle.ResourceInventory == null)
            {
                failure = "nearby container identity or readable state changed before ownership";
                return false;
            }
            if (!ContainerDiscovery.CheckAccess(player, handle.Container))
            {
                failure = "nearby container access changed before ownership";
                return false;
            }
            if (handle.NetworkView.GetZDO().DataRevision != handle.ResourceDataRevision)
            {
                failure = "nearby container contents changed before ownership";
                return false;
            }
            return true;
        }

        internal static bool ValidateClaimedPlan(
            Player player,
            IReadOnlyList<ResourceRequirement> requirements,
            bool matchWorldLevel,
            NearbyResourceCapture readOnlyCapture,
            ResourceWithdrawalPlan readOnlyPlan,
            ContainerHandle[] requiredHandles,
            out string failure)
        {
            NearbyResourceCapture ignoredCapture;
            ResourceWithdrawalPlan ignoredPlan;
            return TryCaptureOwnedPlan(
                player,
                requirements,
                matchWorldLevel,
                readOnlyCapture,
                readOnlyPlan,
                requiredHandles,
                true,
                out ignoredCapture,
                out ignoredPlan,
                out failure);
        }

        private static bool TryCaptureOwnedPlan(
            Player player,
            IReadOnlyList<ResourceRequirement> requirements,
            bool matchWorldLevel,
            NearbyResourceCapture readOnlyCapture,
            ResourceWithdrawalPlan readOnlyPlan,
            ContainerHandle[] requiredHandles,
            bool allowExpectedOwnershipChange,
            out NearbyResourceCapture mutableCapture,
            out ResourceWithdrawalPlan mutablePlan,
            out string failure)
        {
            failure = null;
            mutableCapture = null;
            mutablePlan = null;
            var requiredIds = new HashSet<string>(requiredHandles.Select(handle => handle.Id), StringComparer.Ordinal);
            var expectedRevisions = requiredHandles.ToDictionary(
                handle => handle.Id,
                handle => handle.ResourceDataRevision,
                StringComparer.Ordinal);
            var currentRevisions = requiredHandles
                .Where(handle => handle.NetworkView != null && handle.NetworkView.IsValid() && handle.NetworkView.GetZDO() != null)
                .ToDictionary(handle => handle.Id, handle => handle.NetworkView.GetZDO().DataRevision, StringComparer.Ordinal);
            if (!ResourceOwnershipSelection.RequiredRevisionsMatch(requiredIds, expectedRevisions, currentRevisions))
            {
                failure = "required container contents changed before ownership could be used";
                return false;
            }
            var ownerBaselines = requiredHandles
                .Where(handle => handle.NetworkView != null && handle.NetworkView.IsValid() && handle.NetworkView.GetZDO() != null)
                .ToDictionary(handle => handle.Id, handle => handle.NetworkView.GetZDO().OwnerRevision, StringComparer.Ordinal);
            if (ownerBaselines.Count != requiredIds.Count ||
                (!allowExpectedOwnershipChange && requiredHandles.Any(
                    handle => ownerBaselines[handle.Id] != handle.ResourceOwnerRevision)))
            {
                failure = "required container ownership revision changed before ownership could be used";
                return false;
            }
            var resourceStacks = new List<ResourceStack>();
            var runtimeStacks = new Dictionary<string, RuntimeResourceStack>(StringComparer.Ordinal);
            var mutableHandles = new List<ContainerHandle>(requiredIds.Count);
            AddInventory(resourceStacks, runtimeStacks, PlayerInventoryId, player.GetInventory(), null, 0, matchWorldLevel);

            var inventoryOrder = 1;
            var freshScope = StorageScopeProvider.Resolve(player);
            foreach (var handle in readOnlyCapture.Containers.Where(handle => requiredIds.Contains(handle.Id)))
            {
                if (!ValidateReadOnlyHandle(player, freshScope, handle, out failure) ||
                    !handle.NetworkView.IsOwner() || !handle.Container.IsOwner())
                {
                    if (failure == null) failure = "required container ownership changed before consumption";
                    return false;
                }
                if (handle.Container.IsInUse() || (handle.Container.m_wagon != null && handle.Container.m_wagon.InUse()))
                {
                    failure = NearbyResourceOwnership.InUseMessage;
                    return false;
                }
                if (!ContainerDiscovery.RefreshFromNetwork(handle.Container) ||
                    !handle.NetworkView.IsValid() || !handle.NetworkView.IsOwner() || !handle.Container.IsOwner() ||
                    handle.NetworkView.GetZDO() == null ||
                    handle.NetworkView.GetZDO().DataRevision != handle.ResourceDataRevision ||
                    handle.NetworkView.GetZDO().OwnerRevision != ownerBaselines[handle.Id])
                {
                    failure = "required container state changed during ownership transfer";
                    return false;
                }
                var zdo = handle.NetworkView.GetZDO();
                var liveInventory = handle.Container.GetInventory();
                if (zdo == null || liveInventory == null)
                {
                    failure = "required live container inventory disappeared before consumption";
                    return false;
                }
                var mutableHandle = new ContainerHandle(
                    handle.Id,
                    handle.Container,
                    handle.NetworkView,
                    handle.Snapshot,
                    liveInventory,
                    zdo.DataRevision,
                    zdo.m_uid,
                    zdo.OwnerRevision,
                    zdo.GetOwner(),
                    true);
                mutableHandles.Add(mutableHandle);
                AddInventory(
                    resourceStacks,
                    runtimeStacks,
                    mutableHandle.Id,
                    liveInventory,
                    mutableHandle,
                    inventoryOrder++,
                    matchWorldLevel);
            }

            mutableCapture = new NearbyResourceCapture(freshScope, mutableHandles, resourceStacks, runtimeStacks);
            if (!Planner.TryPlanWithMinimumContainers(
                    requirements,
                    mutableCapture.Stacks,
                    PlayerInventoryId,
                    out mutablePlan))
            {
                failure = "the refreshed minimum-container search exceeded its safe bound";
                return false;
            }
            if (!mutablePlan.IsSatisfiable || mutablePlan.PlannedUnits != mutablePlan.RequiredUnits)
            {
                failure = "required container contents changed before consumption";
                return false;
            }

            var refreshedIds = new HashSet<string>(mutablePlan.Steps
                .Where(step => !string.Equals(step.InventoryId, PlayerInventoryId, StringComparison.Ordinal))
                .Select(step => step.InventoryId), StringComparer.Ordinal);
            if (!refreshedIds.SetEquals(requiredIds))
            {
                failure = "the exact minimum container plan changed before consumption";
                return false;
            }
            return true;
        }

        private static bool TryReserveContainers(
            Player player,
            StorageScope transactionScope,
            IEnumerable<ContainerHandle> requiredHandles,
            out IReadOnlyList<ContainerReservation> reservations,
            out string failure)
        {
            return TryReserveContainers(player, transactionScope, requiredHandles, true, out reservations, out failure);
        }

        internal static bool TryReserveContainers(
            Player player,
            StorageScope transactionScope,
            IEnumerable<ContainerHandle> requiredHandles,
            bool releaseMatchingOwnershipOnFailure,
            out IReadOnlyList<ContainerReservation> reservations,
            out string failure)
        {
            var held = new List<ContainerReservation>();
            failure = null;
            foreach (var handle in requiredHandles)
            {
                if (!ValidateReadOnlyHandle(player, transactionScope, handle, out failure))
                {
                    ReleaseReservations(held, releaseMatchingOwnershipOnFailure);
                    reservations = Array.Empty<ContainerReservation>();
                    return false;
                }
                if (!handle.NetworkView.IsOwner() || !handle.Container.IsOwner() ||
                    handle.NetworkView.GetZDO() == null ||
                    handle.NetworkView.GetZDO().OwnerRevision != handle.ResourceOwnerRevision)
                {
                    failure = "required container ownership changed before reservation";
                    ReleaseReservations(held, releaseMatchingOwnershipOnFailure);
                    reservations = Array.Empty<ContainerReservation>();
                    return false;
                }
                if (handle.Container.IsInUse() || (handle.Container.m_wagon != null && handle.Container.m_wagon.InUse()))
                {
                    failure = NearbyResourceOwnership.InUseMessage;
                    ReleaseReservations(held, releaseMatchingOwnershipOnFailure);
                    reservations = Array.Empty<ContainerReservation>();
                    return false;
                }
                var reservation = new ContainerReservation(handle);
                if (!reservation.CaptureRevisionBaseline())
                {
                    failure = "required container reservation baseline could not be captured";
                    ReleaseReservations(held, releaseMatchingOwnershipOnFailure);
                    reservations = Array.Empty<ContainerReservation>();
                    return false;
                }
                // Add it before SetInUse so cleanup still releases a reservation if a patched
                // effects path throws after setting the local in-use flag.
                held.Add(reservation);
                try
                {
                    handle.Container.SetInUse(true);
                    if (!handle.Container.IsInUse() || !reservation.AdvanceDataRevisionAfterMutation())
                    {
                        failure = "required container could not be reserved";
                        ReleaseReservations(held, releaseMatchingOwnershipOnFailure);
                        reservations = Array.Empty<ContainerReservation>();
                        return false;
                    }
                }
                catch (Exception exception)
                {
                    failure = "required container reservation failed: " + exception.GetType().Name;
                    ReleaseReservations(held, releaseMatchingOwnershipOnFailure);
                    reservations = Array.Empty<ContainerReservation>();
                    return false;
                }
            }
            reservations = held;
            return true;
        }

        internal static bool RevalidateReservedContainers(
            Player player,
            StorageScope transactionScope,
            IEnumerable<ContainerReservation> reservations,
            out string failure)
        {
            failure = null;
            foreach (var reservation in reservations)
            {
                var handle = reservation.Handle;
                var view = handle != null ? handle.NetworkView : null;
                var zdo = view != null && view.IsValid() ? view.GetZDO() : null;
                if (handle == null || handle.Container == null || zdo == null ||
                    !view.IsOwner() || !handle.Container.IsOwner() || !handle.Container.IsInUse() ||
                    !string.Equals(zdo.m_uid.ToString(), handle.Id, StringComparison.Ordinal) ||
                    zdo.DataRevision != reservation.DataRevision ||
                    zdo.OwnerRevision != reservation.OwnerRevision ||
                    (transactionScope == null || !transactionScope.Contains(handle.Container.transform.position)) ||
                    !ContainerDiscovery.CheckAccess(player, handle.Container))
                {
                    failure = "required container reservation or state changed before consumption";
                    return false;
                }
            }
            return true;
        }

        internal static bool ReleaseReservations(
            IEnumerable<ContainerReservation> reservations,
            bool releaseMatchingOwnership = true)
        {
            if (reservations == null) return true;
            var allReleased = true;
            var held = reservations.ToArray();
            foreach (var reservation in held.Reverse())
            {
                var released = false;
                for (var attempt = 0; attempt < 2 && !released; attempt++)
                {
                    released = TryClearReservation(reservation);
                }
                if (released)
                {
                    PendingReservationRelease pending;
                    if (PendingReservationReleases.TryGetValue(reservation.Handle.Id, out pending) &&
                        SameReservation(pending.Reservation, reservation))
                    {
                        PendingReservationReleases.Remove(reservation.Handle.Id);
                    }
                }
                else
                {
                    PendingReservationRelease pending;
                    if (PendingReservationReleases.TryGetValue(reservation.Handle.Id, out pending))
                    {
                        pending.ReleaseMatchingOwnership |= releaseMatchingOwnership;
                    }
                    else
                    {
                        PendingReservationReleases[reservation.Handle.Id] =
                            new PendingReservationRelease(reservation, releaseMatchingOwnership);
                    }
                    _nextReservationReleaseRetryAt = Time.realtimeSinceStartup + 1f;
                    allReleased = false;
                }
            }
            if (releaseMatchingOwnership)
            {
                try
                {
                    OwnershipLeaseManager.ReleaseMatching(held.Select(item => item.Handle),
                        "pre-transaction reservation ended");
                }
                catch (Exception exception)
                {
                    allReleased = false;
                    RuntimeContext.Plugin?.Log.LogError("Failed to hand reserved storage to ownership cleanup: " + exception);
                }
            }
            return allReleased;
        }

        internal static void UpdatePendingReservationReleases()
        {
            if (PendingReservationReleases.Count == 0 || Time.realtimeSinceStartup < _nextReservationReleaseRetryAt) return;
            RetryPendingReservationReleases("deferred reservation cleanup completed");
        }

        internal static void FlushPendingReservationReleasesBeforeOwnershipShutdown()
        {
            // Ignore the ordinary one-second retry delay during disable/unload. Any exact cleanup
            // that still fails remains recorded for the retained session-lifetime safety patch.
            RetryPendingReservationReleases("shutdown reservation cleanup completed");
        }

        private static void RetryPendingReservationReleases(string ownershipReason)
        {
            _nextReservationReleaseRetryAt = Time.realtimeSinceStartup + 1f;
            foreach (var pair in PendingReservationReleases.ToArray())
            {
                var pending = pair.Value;
                if (!TryClearReservation(pending.Reservation)) continue;
                PendingReservationReleases.Remove(pair.Key);
                if (pending.ReleaseMatchingOwnership)
                {
                    try
                    {
                        OwnershipLeaseManager.ReleaseMatching(
                            new[] { pending.Reservation.Handle },
                            ownershipReason);
                    }
                    catch (Exception exception)
                    {
                        RuntimeContext.Plugin?.Log.LogError("Failed to resume ownership cleanup after reservation release: " + exception);
                    }
                }
            }
        }

        private static bool SameReservation(ContainerReservation left, ContainerReservation right)
        {
            return left != null && right != null &&
                   string.Equals(left.Handle.Id, right.Handle.Id, StringComparison.Ordinal) &&
                   left.LocalSession == right.LocalSession &&
                   left.DataRevision == right.DataRevision &&
                   left.OwnerRevision == right.OwnerRevision;
        }

        private static bool TryClearReservation(ContainerReservation reservation)
        {
            try
            {
                var handle = reservation != null ? reservation.Handle : null;
                var container = handle != null ? handle.Container : null;
                if (container == null) return true;
                if (ZDOMan.instance == null || ZDOMan.GetSessionID() != reservation.LocalSession) return true;
                var view = handle.NetworkView;
                var zdo = view != null && view.IsValid() ? view.GetZDO() : null;
                if (zdo == null) return false;
                if (!string.Equals(zdo.m_uid.ToString(), handle.Id, StringComparison.Ordinal) ||
                    zdo.OwnerRevision != reservation.OwnerRevision)
                {
                    // The exact reservation identity/owner generation is gone. Never clear a
                    // later owner's legitimate in-use state.
                    return true;
                }
                if (!view.IsOwner() || !container.IsOwner()) return false;
                if (!container.IsInUse()) return true;
                container.SetInUse(false);
                return !container.IsInUse();
            }
            catch (Exception exception)
            {
                RuntimeContext.Plugin?.Log.LogError("Failed to release a pre-transaction container reservation: " + exception);
                return false;
            }
        }

        internal static bool RevalidateContainers(
            Player player,
            ResourceWithdrawalPlan plan,
            NearbyResourceCapture capture,
            out string failure)
        {
            failure = null;
            var transactionScope = capture != null ? capture.Scope : null;
            foreach (var containerId in plan.Steps
                .Where(step => !string.Equals(step.InventoryId, PlayerInventoryId, StringComparison.Ordinal))
                .Select(step => step.InventoryId)
                .Distinct(StringComparer.Ordinal))
            {
                var handle = capture.Containers.FirstOrDefault(item => string.Equals(item.Id, containerId, StringComparison.Ordinal));
                if (handle == null || handle.Container == null || handle.Container.GetType() != typeof(Container) ||
                    (transactionScope == null || !transactionScope.Contains(handle.Container.transform.position)) ||
                    handle.NetworkView == null || !handle.NetworkView.IsValid() || handle.NetworkView.GetZDO() == null ||
                    !string.Equals(handle.NetworkView.GetZDO().m_uid.ToString(), handle.Id, StringComparison.Ordinal) ||
                    !handle.NetworkView.IsOwner() || !handle.Container.IsOwner())
                {
                    failure = "nearby container ownership or identity changed before consumption";
                    return false;
                }
                if (handle.Container.IsInUse() || (handle.Container.m_wagon != null && handle.Container.m_wagon.InUse()))
                {
                    failure = NearbyResourceOwnership.InUseMessage;
                    return false;
                }
                if (!ContainerDiscovery.CheckAccess(player, handle.Container) || !ContainerDiscovery.RefreshFromNetwork(handle.Container) ||
                    !handle.NetworkView.IsValid() || !handle.NetworkView.IsOwner() || !handle.Container.IsOwner())
                {
                    failure = "nearby container access, ownership, or state changed before consumption";
                    return false;
                }
            }
            return true;
        }

        internal static bool RevalidateStacks(
            ResourceWithdrawalPlan plan,
            NearbyResourceCapture capture,
            bool matchWorldLevel,
            out string failure)
        {
            failure = null;
            foreach (var step in plan.Steps)
            {
                RuntimeResourceStack runtime;
                if (!capture.RuntimeStacks.TryGetValue(step.StackId, out runtime))
                {
                    failure = "planned resource stack disappeared";
                    return false;
                }
                var item = runtime.Inventory.GetItemAt(runtime.Item.m_gridPos.x, runtime.Item.m_gridPos.y);
                if (!ReferenceEquals(item, runtime.Item) || item.m_stack < step.Quantity ||
                    item.m_quality != step.Quality || item.m_shared == null ||
                    !string.Equals(item.m_shared.m_name, step.ItemName, StringComparison.Ordinal) ||
                    (matchWorldLevel && item.m_worldLevel < Game.m_worldLevel))
                {
                    failure = "planned resource stack changed before consumption";
                    return false;
                }
            }
            return true;
        }

        private static bool ExecuteWithRollback(
            Player player,
            ResourceWithdrawalPlan plan,
            NearbyResourceCapture capture,
            IReadOnlyList<ContainerReservation> reservations,
            IList<RemovedResource> removed,
            out string failure)
        {
            failure = null;
            var transactionScope = capture != null ? capture.Scope : null;
            var reservationByContainer = reservations.ToDictionary(item => item.Container);
            var exactDebit = new ExactResourceDebit<RemovedResource>(plan);
            string debitFailure;
            bool success;
            try
            {
                success = exactDebit.TryApply(step =>
                {
                    RuntimeResourceStack runtime;
                    if (!capture.RuntimeStacks.TryGetValue(step.StackId, out runtime))
                    {
                        return ResourceDebitAttempt<RemovedResource>.FailureWithoutMutation(
                            "planned resource stack disappeared before removal");
                    }

                    ContainerReservation reservation = null;
                    if (runtime.Container != null)
                    {
                        // Detached ZDO snapshots are valid only for discovery and planning. A
                        // prepared transaction must point at the owned Container's live inventory;
                        // otherwise removing from it changes only a throwaway copy and grants a
                        // free crafted item.
                        if (!ReferenceEquals(runtime.Inventory, runtime.Container.Container.GetInventory()))
                        {
                            return ResourceDebitAttempt<RemovedResource>.FailureWithoutMutation(
                                "prepared crafting source was not the live container inventory");
                        }
                        if (!reservationByContainer.TryGetValue(runtime.Container.Container, out reservation) ||
                            !ReservationMatches(player, transactionScope, reservation))
                        {
                            return ResourceDebitAttempt<RemovedResource>.FailureWithoutMutation(
                                "required container reservation changed before resource removal");
                        }
                    }

                    var clone = runtime.Item.Clone();
                    clone.m_stack = step.Quantity;
                    var position = runtime.Item.m_gridPos;
                    var before = TotalUnits(runtime.Inventory);
                    bool removedExactly;
                    Exception removalException = null;
                    try
                    {
                        removedExactly = runtime.Inventory.RemoveItem(runtime.Item, step.Quantity);
                    }
                    catch (Exception exception)
                    {
                        removedExactly = false;
                        removalException = exception;
                    }
                    var after = TotalUnits(runtime.Inventory);
                    var actualRemoved = before - after;
                    RemovedResource receipt = null;
                    if (actualRemoved > 0 && actualRemoved <= before)
                    {
                        clone.m_stack = actualRemoved;
                        receipt = new RemovedResource(runtime.Inventory, clone, position, actualRemoved);
                    }

                    string stepFailure = null;
                    if (removalException != null)
                    {
                        stepFailure = "inventory removal threw " + removalException.GetType().Name +
                            " after a possible mutation; rollback is required";
                    }
                    else if (!removedExactly || actualRemoved != step.Quantity)
                    {
                        stepFailure = actualRemoved < 0
                            ? "resource removal produced an invalid inventory delta"
                            : "resource removal failed; rollback is required";
                    }
                    else if (reservation != null)
                    {
                        try
                        {
                            if (!reservation.AdvanceDataRevisionAfterMutation() ||
                                !ReservationMatches(player, transactionScope, reservation))
                            {
                                stepFailure = "required container reservation changed after resource removal";
                            }
                        }
                        catch (Exception exception)
                        {
                            stepFailure = "container reservation validation threw " + exception.GetType().Name +
                                " after resource removal; rollback is required";
                        }
                    }

                    return stepFailure == null
                        ? ResourceDebitAttempt<RemovedResource>.Success(actualRemoved, receipt)
                        : receipt == null
                            ? ResourceDebitAttempt<RemovedResource>.FailureWithoutMutation(stepFailure)
                            : ResourceDebitAttempt<RemovedResource>.FailureAfterMutation(actualRemoved, receipt, stepFailure);
                }, out debitFailure);
            }
            finally
            {
                foreach (var receipt in exactDebit.Receipts)
                {
                    removed.Add(receipt);
                }
            }

            failure = debitFailure;
            return success;
        }

        internal static bool ReservationMatches(Player player, StorageScope transactionScope, ContainerReservation reservation)
        {
            if (reservation == null || reservation.Handle == null || reservation.Container == null) return false;
            var handle = reservation.Handle;
            var view = handle.NetworkView;
            var zdo = view != null && view.IsValid() ? view.GetZDO() : null;
            return zdo != null && view.IsOwner() && handle.Container.IsOwner() && handle.Container.IsInUse() &&
                   string.Equals(zdo.m_uid.ToString(), handle.Id, StringComparison.Ordinal) &&
                   zdo.DataRevision == reservation.DataRevision &&
                   zdo.OwnerRevision == reservation.OwnerRevision &&
                   transactionScope != null && transactionScope.Contains(handle.Container.transform.position) &&
                   ContainerDiscovery.CheckAccess(player, handle.Container);
        }

        internal static bool Rollback(IEnumerable<RemovedResource> removed)
        {
            var restoredAll = true;
            foreach (var entry in removed.Reverse())
            {
                try
                {
                    var before = TotalUnits(entry.Inventory);
                    entry.Item.m_stack = entry.Quantity;
                    entry.Item.m_gridPos = entry.Position;
                    var restored = AddItemAtMethod != null && (bool)AddItemAtMethod.Invoke(
                        entry.Inventory,
                        new object[] { entry.Item, entry.Quantity, entry.Position.x, entry.Position.y, true });
                    var after = TotalUnits(entry.Inventory);
                    restoredAll &= restored && after == before + entry.Quantity;
                }
                catch (Exception exception)
                {
                    restoredAll = false;
                    if (RuntimeContext.Plugin != null)
                    {
                        RuntimeContext.Plugin.Log.LogError("Failed to restore a removed nearby resource: " + exception);
                    }
                }
            }
            return restoredAll;
        }

        private static int TotalUnits(Inventory inventory)
        {
            return inventory.GetAllItems().Sum(item => item.m_stack);
        }
    }

    internal static class NearbyResourceOwnership
    {
        internal const string InUseMessage = "The required materials are currently in use";
        private const float OwnershipTimeoutSeconds = 2f;
        private static bool _running;
        private static int _generation;

        internal static void Shutdown()
        {
            _generation++;
            _running = false;
        }

        internal static void RearmSession()
        {
            _running = false;
        }

        internal static bool TryBegin(
            Player player,
            IReadOnlyList<ResourceRequirement> requirements,
            bool matchWorldLevel,
            NearbyResourceCapture readOnlyCapture,
            ResourceWithdrawalPlan readOnlyPlan,
            ContainerHandle[] requiredHandles,
            ContainerHandle[] unownedHandles,
            out string failure)
        {
            failure = null;
            if (_running)
            {
                RuntimeContext.ShowTopLeft("Stackmaster: checking required storage…");
                return true;
            }
            if (RuntimeContext.Plugin == null)
            {
                failure = "ownership coordinator is unavailable";
                return false;
            }

            var generation = _generation;
            _running = true;
            try
            {
                RuntimeContext.ShowTopLeft("Stackmaster: checking required storage…");
                RuntimeContext.Plugin.StartCoroutine(Run(
                    player,
                    requirements,
                    matchWorldLevel,
                    readOnlyCapture,
                    readOnlyPlan,
                    requiredHandles,
                    unownedHandles,
                    generation));
                return true;
            }
            catch (Exception exception)
            {
                _running = false;
                failure = "ownership request could not start: " + exception.GetType().Name;
                return false;
            }
        }

        private static IEnumerator Run(
            Player player,
            IReadOnlyList<ResourceRequirement> requirements,
            bool matchWorldLevel,
            NearbyResourceCapture readOnlyCapture,
            ResourceWithdrawalPlan readOnlyPlan,
            ContainerHandle[] requiredHandles,
            ContainerHandle[] unownedHandles,
            int generation)
        {
            if (generation != _generation || !RuntimeContext.Compatibility.IsCompatible)
            {
                yield break;
            }

            OwnershipBatch ownership;
            try
            {
                ownership = OwnershipCoordinator.Begin(unownedHandles);
            }
            catch (Exception exception)
            {
                _running = false;
                RuntimeContext.Plugin.Log.LogError("Nearby-resource ownership setup failed safely: " + exception);
                RuntimeContext.ShowCenter("Stackmaster could not safely acquire the required materials; nothing was consumed.");
                yield break;
            }

            var keepRetryLease = false;
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
                        RuntimeContext.Plugin.Log.LogError("Nearby-resource ownership refresh failed safely: " + exception);
                        RuntimeContext.ShowCenter("Stackmaster could not safely acquire the required materials; nothing was consumed.");
                    }
                    if (refreshFailed) yield break;
                    yield return null;
                }

                if (generation != _generation || !RuntimeContext.Compatibility.IsCompatible)
                {
                    yield break;
                }

                try
                {
                    ownership.Refresh();
                    if (!ownership.IsComplete) ownership.Timeout();

                    if (ownership.FailedContainerIds.Count > 0)
                    {
                        // RPC_StackResponse only exposes granted/not-granted. If access and identity
                        // still validate locally, an authoritative rejection is the only safe signal
                        // available for a remotely busy chest. A concurrent access/owner change uses
                        // the generic failure instead of misreporting it as in-use.
                        var ownerRejectedAsBusy = ownership.OwnerRejectedContainerIds.Count > 0 &&
                            unownedHandles
                                .Where(handle => ownership.OwnerRejectedContainerIds.Contains(handle.Id))
                                .All(handle =>
                                    handle.NetworkView != null && handle.NetworkView.IsValid() &&
                                    handle.NetworkView.HasOwner() &&
                                    ContainerDiscovery.CheckAccess(player, handle.Container));
                        RuntimeContext.ShowCenter(ownerRejectedAsBusy
                            ? InUseMessage
                            : "Stackmaster could not safely acquire the required materials; nothing was consumed.");
                    }
                    else
                    {
                        string failure;
                        if (!NearbyResourceService.ValidateClaimedPlan(
                                player,
                                requirements,
                                matchWorldLevel,
                                readOnlyCapture,
                                readOnlyPlan,
                                requiredHandles,
                                out failure))
                        {
                            RuntimeContext.Plugin.Log.LogWarning("Nearby-resource ownership revalidation failed safely: " + failure);
                            RuntimeContext.ShowCenter(string.Equals(failure, InUseMessage, StringComparison.Ordinal)
                                ? InUseMessage
                                : "Stackmaster: nearby materials changed; nothing was consumed. Try again.");
                        }
                        else
                        {
                            // A Harmony prefix cannot synchronously wait for remote RPCs. Replaying a build
                            // later would skip UpdatePlacement's vanilla stamina/stat/durability path, so the
                            // safe cross-action contract is explicit: ownership is prepared, then the user
                            // retries and the normal vanilla action runs atomically from a fresh plan.
                            // Only exact ZDOs newly acquired in this batch enter the short retry lease.
                            OwnershipLeaseManager.HoldForRetry(ownership);
                            keepRetryLease = true;
                            RuntimeContext.ShowTopLeft("Stackmaster: required materials ready — try the action again.");
                        }
                    }
                }
                catch (Exception exception)
                {
                    RuntimeContext.Plugin.Log.LogError("Nearby-resource ownership failed safely: " + exception);
                    RuntimeContext.ShowCenter("Stackmaster could not safely acquire the required materials; nothing was consumed.");
                }
            }
            finally
            {
                if (generation == _generation)
                {
                // Stopping/disposal of the coroutine must not strand the coordinator. If a vanilla
                // response can still arrive, Timeout records that container for permanent one-shot
                // suppression before End clears the active batch.
                try
                {
                    if (!ownership.IsComplete) ownership.Timeout();
                }
                catch (Exception exception)
                {
                    RuntimeContext.Plugin.Log.LogError("Nearby-resource ownership cleanup failed safely: " + exception);
                }
                finally
                {
                    try
                    {
                        if (!keepRetryLease)
                        {
                            OwnershipLeaseManager.ReleaseBatch(ownership,
                                "ownership acquisition did not reach the retry lease");
                        }
                        OwnershipCoordinator.End(ownership);
                    }
                    finally
                    {
                        if (generation == _generation)
                        {
                            _running = false;
                        }
                    }
                }
                }
                // RuntimeContext already cleaned an invalidated generation; it must never touch
                // ownership records created by a later server session.
            }
        }
    }

    internal static class NearbyHudFailOpen
    {
        private static readonly HashSet<string> ReportedSurfaces = new HashSet<string>(StringComparer.Ordinal);

        internal static void ResetSession()
        {
            lock (ReportedSurfaces)
            {
                ReportedSurfaces.Clear();
            }
        }

        internal static void ReportOnce(string surface, Exception exception)
        {
            // UI postfixes must never interfere with vanilla chest or build/craft UI. Even
            // diagnostics are best-effort so a logger failure cannot escape the safety net.
            try
            {
                lock (ReportedSurfaces)
                {
                    if (!ReportedSurfaces.Add(surface)) return;
                }
                var plugin = RuntimeContext.Plugin;
                if (plugin != null)
                {
                    plugin.Log.LogWarning("Nearby-resource " + surface +
                        " overlay failed open to vanilla UI: " + exception);
                }
            }
            catch
            {
                // Deliberately preserve vanilla UI under every reporting failure.
            }
        }
    }

    // HarmonyX otherwise binds ordinary patch parameters by the game's source-level
    // argument names. Use explicit indexes for every original-method argument so a
    // harmless metadata rename cannot prevent Stackmaster from loading.
    internal static class NearbyRequirementPatches
    {
        internal static void RecipePostfix(
            Player __instance,
            [HarmonyArgument(0)] Recipe recipe,
            [HarmonyArgument(1)] bool discover,
            [HarmonyArgument(2)] int qualityLevel,
            [HarmonyArgument(3)] int amount,
            ref bool __result)
        {
            var vanillaResult = __result;
            try
            {
                if (!RuntimeContext.Compatibility.IsCompatible || discover ||
                    RuntimeContext.Plugin == null || !RuntimeContext.Plugin.CraftingFromNearbyChestsEnabled.Value)
                {
                    return;
                }
                if (ResourceActionContext.Current == ResourceActionKind.Crafting && ResourceTransactionContext.Active)
                {
                    __result = true;
                    return;
                }
                __result = NearbyResourceService.HasRecipeRequirements(__instance, recipe, qualityLevel, amount, false);
            }
            catch (Exception exception)
            {
                __result = vanillaResult;
                NearbyHudFailOpen.ReportOnce("crafting requirements", exception);
            }
        }

        internal static void PiecePostfix(
            Player __instance,
            [HarmonyArgument(0)] Piece piece,
            [HarmonyArgument(1)] Player.RequirementMode mode,
            ref bool __result)
        {
            var vanillaResult = __result;
            try
            {
                if (!RuntimeContext.Compatibility.IsCompatible || mode != Player.RequirementMode.CanBuild ||
                    RuntimeContext.Plugin == null || !RuntimeContext.Plugin.BuildingFromNearbyChestsEnabled.Value)
                {
                    return;
                }
                if (!NearbyResourceService.PieceNonMaterialRequirementsPass(__instance, piece))
                {
                    __result = false;
                    return;
                }
                if (ResourceActionContext.Current == ResourceActionKind.Building && ResourceTransactionContext.Active)
                {
                    __result = true;
                    return;
                }
                __result = NearbyResourceService.HasPieceRequirements(__instance, piece, false);
            }
            catch (Exception exception)
            {
                __result = vanillaResult;
                NearbyHudFailOpen.ReportOnce("building requirements", exception);
            }
        }
    }

    internal static class NearbyBuildHudPatch
    {
        private const float RefreshIntervalSeconds = 0.25f;
        private static Player _cachedPlayer;
        private static Piece _cachedPiece;
        private static string _cachedScopeSignature;
        private static float _nextRefreshTime;
        private static IReadOnlyList<RuntimeRequirementAvailability> _cachedAvailability = Array.Empty<RuntimeRequirementAvailability>();

        internal static void ResetCache()
        {
            _cachedPlayer = null;
            _cachedPiece = null;
            _cachedScopeSignature = null;
            _nextRefreshTime = 0f;
            _cachedAvailability = Array.Empty<RuntimeRequirementAvailability>();
        }

        internal static void Postfix(Hud __instance, [HarmonyArgument(0)] Piece piece)
        {
            try
            {
                Apply(__instance, piece);
            }
            catch (Exception exception)
            {
                NearbyHudFailOpen.ReportOnce("building HUD", exception);
            }
        }

        private static void Apply(Hud __instance, Piece piece)
        {
            var requirementItems = __instance == null
                ? Array.Empty<GameObject>()
                : __instance.m_requirementItems ?? Array.Empty<GameObject>();
            foreach (var requirementItem in requirementItems)
            {
                RequirementAmountTextFitter.PrepareForVanilla(
                    requirementItem == null ? null : requirementItem.transform);
            }

            var plugin = RuntimeContext.Plugin;
            if (!RuntimeContext.Compatibility.IsCompatible || plugin == null ||
                (!plugin.ShowStorageAmountsInRequirementMenus.Value &&
                 !plugin.BuildingFromNearbyChestsEnabled.Value) ||
                __instance == null || piece == null || Player.m_localPlayer == null)
            {
                return;
            }

            var requirements = piece.m_resources ?? Array.Empty<Piece.Requirement>();
            var player = Player.m_localPlayer;
            var scopeSignature = StorageScopeProvider.Resolve(player).Signature;
            if (!ReferenceEquals(_cachedPlayer, player) || !ReferenceEquals(_cachedPiece, piece) ||
                !string.Equals(_cachedScopeSignature, scopeSignature, StringComparison.Ordinal) || Time.time >= _nextRefreshTime)
            {
                _cachedAvailability = NearbyResourceService.GetPieceRequirementAvailability(player, piece, false);
                _cachedPlayer = player;
                _cachedPiece = piece;
                _cachedScopeSignature = scopeSignature;
                _nextRefreshTime = Time.time + RefreshIntervalSeconds;
            }
            var itemCount = Math.Min(
                Math.Min(requirementItems.Length, requirements.Length),
                _cachedAvailability.Count);
            var noBuildCost = ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey(piece.FreeBuildKey());
            for (var index = 0; index < itemCount; index++)
            {
                var entry = _cachedAvailability[index];
                if (entry.Required <= 0) continue;

                var requirementRoot = requirementItems[index];
                var amountTransform = requirementRoot == null ? null : requirementRoot.transform.Find("res_amount");
                var amountLabel = amountTransform == null ? null : amountTransform.GetComponent<TMP_Text>();
                if (amountLabel == null) continue;

                var decision = RequirementUiPolicy.Resolve(
                    plugin.ShowStorageAmountsInRequirementMenus.Value,
                    plugin.BuildingFromNearbyChestsEnabled.Value,
                    entry.AggregateSatisfied,
                    entry.PlayerSatisfied);
                if (decision.ShouldOverrideText)
                {
                    RequirementAmountTextFitter.Apply(
                        amountLabel,
                        ResourceRequirementPresentation.Format(entry.Required, entry.TotalAvailable));
                }
                amountLabel.color = ResourceRequirementPresentation.ShouldUseShortageColor(
                    noBuildCost,
                    decision.IsSatisfied,
                    Mathf.Sin(Time.time * 10f))
                    ? Color.red
                    : Color.white;
            }
        }
    }

    internal static class RequirementAmountTextFitter
    {
        // The vanilla amount label is deliberately narrow. Keep its normal typography for
        // ordinary values, then let TextMeshPro fit longer exact totals inside the existing
        // rect instead of silently truncating the final digits. Ten points is still legible at
        // Valheim's supported UI scales and is low enough for realistic three-digit pairs.
        private const float MinimumReadableFontSize = 10f;
        private const float MinimumFontScale = 0.55f;
        private static readonly Dictionary<int, LabelState> States = new Dictionary<int, LabelState>();

        internal static void PrepareForVanilla(Transform elementRoot)
        {
            Restore(FindLabel(elementRoot));
        }

        internal static void Apply(TMP_Text label, string exactText)
        {
            if (label == null) return;

            var id = label.GetInstanceID();
            LabelState state;
            if (!States.TryGetValue(id, out state) || !ReferenceEquals(state.Label, label))
            {
                state = new LabelState(label);
                States[id] = state;
            }
            else
            {
                // SetupRequirement normally runs through our prefix first, but restoring here
                // also makes direct/re-entrant calls deterministic.
                state.Restore();
            }

            var normalSize = state.EnableAutoSizing && state.FontSizeMax > 0f
                ? state.FontSizeMax
                : state.FontSize;
            if (float.IsNaN(normalSize) || float.IsInfinity(normalSize) || normalSize <= 0f)
            {
                normalSize = 16f;
            }
            var minimumSize = Math.Min(normalSize,
                Math.Max(MinimumReadableFontSize, normalSize * MinimumFontScale));

            label.text = exactText;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.enableAutoSizing = true;
            label.fontSizeMin = minimumSize;
            label.fontSizeMax = normalSize;
            label.fontSize = normalSize;
        }

        internal static void Restore(TMP_Text label)
        {
            if (label == null) return;
            var id = label.GetInstanceID();
            LabelState state;
            if (!States.TryGetValue(id, out state) || !ReferenceEquals(state.Label, label)) return;
            state.Restore();
            States.Remove(id);
        }

        internal static void RestoreAll()
        {
            foreach (var state in States.Values.ToArray())
            {
                if (state.Label != null) state.Restore();
            }
            States.Clear();
        }

        private static TMP_Text FindLabel(Transform elementRoot)
        {
            var amountTransform = elementRoot == null ? null : elementRoot.Find("res_amount");
            return amountTransform == null ? null : amountTransform.GetComponent<TMP_Text>();
        }

        private sealed class LabelState
        {
            internal LabelState(TMP_Text label)
            {
                Label = label;
                FontSize = label.fontSize;
                EnableAutoSizing = label.enableAutoSizing;
                FontSizeMin = label.fontSizeMin;
                FontSizeMax = label.fontSizeMax;
                TextWrappingMode = label.textWrappingMode;
            }

            internal TMP_Text Label { get; }
            internal float FontSize { get; }
            internal bool EnableAutoSizing { get; }
            internal float FontSizeMin { get; }
            internal float FontSizeMax { get; }
            internal TextWrappingModes TextWrappingMode { get; }

            internal void Restore()
            {
                Label.enableAutoSizing = EnableAutoSizing;
                Label.fontSizeMin = FontSizeMin;
                Label.fontSizeMax = FontSizeMax;
                Label.fontSize = FontSize;
                Label.textWrappingMode = TextWrappingMode;
            }
        }
    }

    internal static class NearbyCraftingHudPatch
    {
        private const float RefreshIntervalSeconds = 0.25f;
        private static readonly FieldInfo SelectedRecipeField = AccessTools.Field(typeof(InventoryGui), "m_selectedRecipe");
        private static readonly MethodInfo SelectedRecipeGetter = SelectedRecipeField == null
            ? null
            : AccessTools.PropertyGetter(SelectedRecipeField.FieldType, "Recipe");
        private static readonly FieldInfo RequirementsField = AccessTools.Field(typeof(InventoryGui), "m_reqList");
        private static Player _cachedPlayer;
        private static Recipe _cachedRecipe;
        private static int _cachedQuality;
        private static int _cachedCraftMultiplier;
        private static int _cachedRequirementSignature;
        private static string _cachedScopeSignature;
        private static float _nextRefreshTime;
        private static IReadOnlyList<RuntimeRequirementAvailability> _cachedAvailability = Array.Empty<RuntimeRequirementAvailability>();

        internal static void ResetCache()
        {
            _cachedPlayer = null;
            _cachedRecipe = null;
            _cachedQuality = 0;
            _cachedCraftMultiplier = 0;
            _cachedRequirementSignature = 0;
            _cachedScopeSignature = null;
            _nextRefreshTime = 0f;
            _cachedAvailability = Array.Empty<RuntimeRequirementAvailability>();
            RequirementAmountTextFitter.RestoreAll();
        }

        // Restore the exact vanilla text settings before SetupRequirement reuses a pooled row.
        // This prevents a prior long count from shrinking a later short recipe and makes the
        // toggle-off/compatibility-failure path indistinguishable from vanilla.
        internal static void Prefix([HarmonyArgument(0)] Transform elementRoot)
        {
            RequirementAmountTextFitter.PrepareForVanilla(elementRoot);
        }

        // InventoryGui.SetupRequirement is static in the supported Valheim build. A Harmony
        // __instance argument is therefore always null, and instance-field injection cannot
        // supply m_reqList. Resolve the live InventoryGui explicitly after vanilla renders the
        // row, then replace only its amount text and shortage color.
        internal static void Postfix(
            [HarmonyArgument(0)] Transform elementRoot,
            [HarmonyArgument(1)] Piece.Requirement requirement,
            [HarmonyArgument(2)] Player player,
            [HarmonyArgument(3)] bool craft,
            [HarmonyArgument(4)] int quality,
            [HarmonyArgument(5)] int craftMultiplier,
            ref bool __result)
        {
            try
            {
                var inventoryGui = InventoryGui.instance;
                var requirements = GetRequirements(inventoryGui);
                Apply(inventoryGui, elementRoot, requirement, player, craft, quality, craftMultiplier, requirements, __result);
            }
            catch (Exception exception)
            {
                RequirementAmountTextFitter.PrepareForVanilla(elementRoot);
                NearbyHudFailOpen.ReportOnce("crafting HUD", exception);
            }
        }

        private static void Apply(
            InventoryGui inventoryGui,
            Transform elementRoot,
            Piece.Requirement requirement,
            Player player,
            bool craft,
            int quality,
            int craftMultiplier,
            List<Piece.Requirement> requirements,
            bool vanillaResult)
        {
            var plugin = RuntimeContext.Plugin;
            if (!vanillaResult || !craft || !RuntimeContext.Compatibility.IsCompatible || plugin == null ||
                (!plugin.ShowStorageAmountsInRequirementMenus.Value &&
                 !plugin.CraftingFromNearbyChestsEnabled.Value) || inventoryGui == null ||
                player == null || !ReferenceEquals(player, Player.m_localPlayer) || elementRoot == null ||
                requirement == null || requirement.m_resItem == null || craftMultiplier <= 0 || requirements == null)
            {
                return;
            }

            var recipe = GetSelectedRecipe(inventoryGui);
            if (recipe == null) return;
            var requirementIndex = requirements.FindIndex(item => ReferenceEquals(item, requirement));
            if (requirementIndex < 0) return;

            var scopeSignature = StorageScopeProvider.Resolve(player).Signature;
            var signature = RequirementSignature(requirements, quality, craftMultiplier);
            if (!ReferenceEquals(_cachedPlayer, player) || !ReferenceEquals(_cachedRecipe, recipe) ||
                _cachedQuality != quality || _cachedCraftMultiplier != craftMultiplier ||
                _cachedRequirementSignature != signature ||
                !string.Equals(_cachedScopeSignature, scopeSignature, StringComparison.Ordinal) ||
                Time.time >= _nextRefreshTime)
            {
                _cachedAvailability = NearbyResourceService.GetRecipeRequirementAvailability(
                    player,
                    recipe,
                    requirements,
                    quality,
                    craftMultiplier,
                    false);
                _cachedPlayer = player;
                _cachedRecipe = recipe;
                _cachedQuality = quality;
                _cachedCraftMultiplier = craftMultiplier;
                _cachedRequirementSignature = signature;
                _cachedScopeSignature = scopeSignature;
                _nextRefreshTime = Time.time + RefreshIntervalSeconds;
            }

            if (requirementIndex >= _cachedAvailability.Count) return;
            var entry = _cachedAvailability[requirementIndex];
            if (entry.Required <= 0) return;
            var amountTransform = elementRoot.Find("res_amount");
            var amountLabel = amountTransform == null ? null : amountTransform.GetComponent<TMP_Text>();
            if (amountLabel == null) return;

            var decision = RequirementUiPolicy.Resolve(
                plugin.ShowStorageAmountsInRequirementMenus.Value,
                plugin.CraftingFromNearbyChestsEnabled.Value,
                entry.AggregateSatisfied,
                entry.PlayerSatisfied);
            if (decision.ShouldOverrideText)
            {
                RequirementAmountTextFitter.Apply(
                    amountLabel,
                    ResourceRequirementPresentation.Format(entry.Required, entry.TotalAvailable));
            }
            var noCraftCost = player.NoCostCheat() ||
                              (ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoCraftCost));
            amountLabel.color = ResourceRequirementPresentation.ShouldUseShortageColor(
                noCraftCost,
                decision.IsSatisfied,
                Mathf.Sin(Time.time * 10f))
                ? Color.red
                : Color.white;
        }

        private static List<Piece.Requirement> GetRequirements(InventoryGui inventoryGui)
        {
            if (inventoryGui == null || RequirementsField == null) return null;
            try
            {
                return RequirementsField.GetValue(inventoryGui) as List<Piece.Requirement>;
            }
            catch
            {
                return null;
            }
        }

        private static Recipe GetSelectedRecipe(InventoryGui inventoryGui)
        {
            if (SelectedRecipeField == null || SelectedRecipeGetter == null) return null;
            try
            {
                var selected = SelectedRecipeField.GetValue(inventoryGui);
                return selected == null ? null : SelectedRecipeGetter.Invoke(selected, null) as Recipe;
            }
            catch
            {
                return null;
            }
        }

        private static int RequirementSignature(IEnumerable<Piece.Requirement> requirements, int quality, int craftMultiplier)
        {
            unchecked
            {
                var hash = (quality * 397) ^ craftMultiplier;
                foreach (var requirement in requirements)
                {
                    if (requirement == null || requirement.m_resItem == null)
                    {
                        hash = hash * 31;
                        continue;
                    }
                    var itemName = requirement.m_resItem.m_itemData.m_shared.m_name;
                    hash = hash * 31 + StringComparer.Ordinal.GetHashCode(itemName);
                    hash = hash * 31 + requirement.GetAmount(quality);
                    hash = hash * 31 + (requirement.m_upgraderResource ? 1 : 0);
                }
                return hash;
            }
        }
    }

    internal static class NearbyFirstRequiredItemPatch
    {
        internal static void Postfix(
            Player __instance,
            [HarmonyArgument(0)] Inventory inventory,
            [HarmonyArgument(1)] Recipe recipe,
            [HarmonyArgument(2)] int qualityLevel,
            [HarmonyArgument(3)] ref int amount,
            [HarmonyArgument(4)] ref int extraAmount,
            [HarmonyArgument(5)] int craftMultiplier,
            ref ItemDrop.ItemData __result)
        {
            var vanillaResult = __result;
            var vanillaAmount = amount;
            var vanillaExtraAmount = extraAmount;
            try
            {
                if (!RuntimeContext.Compatibility.IsCompatible || RuntimeContext.Plugin == null ||
                    !RuntimeContext.Plugin.CraftingFromNearbyChestsEnabled.Value ||
                    !ReferenceEquals(inventory, __instance.GetInventory()) || recipe == null || !recipe.m_requireOnlyOneIngredient)
                {
                    return;
                }
                if (ResourceActionContext.Current == ResourceActionKind.Crafting && ResourceTransactionContext.Active)
                {
                    __result = ResourceTransactionContext.SelectedIngredient;
                    amount = ResourceTransactionContext.SelectedAmount;
                    extraAmount = ResourceTransactionContext.SelectedExtraAmount;
                    return;
                }

                int nearbyAmount;
                int nearbyExtraAmount;
                var nearbyResult = NearbyResourceService.FindFirstRequiredItem(
                    __instance,
                    recipe,
                    qualityLevel,
                    craftMultiplier,
                    false,
                    out nearbyAmount,
                    out nearbyExtraAmount);
                __result = nearbyResult;
                amount = nearbyAmount;
                extraAmount = nearbyExtraAmount;
            }
            catch (Exception exception)
            {
                __result = vanillaResult;
                amount = vanillaAmount;
                extraAmount = vanillaExtraAmount;
                NearbyHudFailOpen.ReportOnce("first required crafting item", exception);
            }
        }
    }

    internal static class CraftingStartPatch
    {
        internal static bool Prefix(InventoryGui __instance)
            => CraftingPreflightAction.Prefix(__instance);

        internal static void Postfix(InventoryGui __instance)
            => CraftingPreflightAction.AfterVanillaStart(__instance);

        internal static Exception Finalizer(Exception __exception)
            => CraftingPreflightAction.Finalizer(__exception);
    }

    internal static class CraftingCancelPatch
    {
        internal static void Prefix()
            => CraftingPreflightAction.Cancel("the craft was canceled", false);
    }

    internal static class CraftingSelectionPatch
    {
        internal static void Prefix()
            => CraftingPreflightAction.Cancel("the crafting tab or recipe changed", false);
    }

    internal static class NearbyCraftingActionPatch
    {
        internal static bool Prefix(
            InventoryGui __instance,
            [HarmonyArgument(0)] Player player,
            Recipe ___m_craftRecipe,
            ItemDrop.ItemData ___m_craftUpgradeItem,
            bool ___m_multiCrafting,
            int ___m_multiCraftAmount,
            ref ResourceActionKind __state)
        {
            __state = ResourceActionContext.Enter(ResourceActionKind.Crafting);
            if (!RuntimeContext.Compatibility.IsCompatible || RuntimeContext.Plugin == null ||
                !RuntimeContext.Plugin.CraftingFromNearbyChestsEnabled.Value || player == null || ___m_craftRecipe == null ||
                player.NoCostCheat() || (ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoCraftCost)))
            {
                return true;
            }

            var qualityLevel = ___m_craftUpgradeItem == null ? 1 : ___m_craftUpgradeItem.m_quality + 1;
            var multiplier = ___m_multiCrafting ? ___m_multiCraftAmount : 1;
            string failure;
            bool handledPrepared;
            if (CraftingPreflightAction.TryBeginPreparedTransaction(
                    __instance,
                    player,
                    ___m_craftRecipe,
                    qualityLevel,
                    multiplier,
                    out handledPrepared,
                    out failure))
            {
                return true;
            }
            if (!handledPrepared && NearbyResourceService.TryBeginRecipeTransaction(player, ___m_craftRecipe, qualityLevel, multiplier, out failure))
            {
                return true;
            }

            ResourceActionContext.Restore(__state);
            if (!string.IsNullOrEmpty(failure))
            {
                RuntimeContext.ShowCenter(string.Equals(failure, NearbyResourceOwnership.InUseMessage, StringComparison.Ordinal)
                    ? NearbyResourceOwnership.InUseMessage
                    : "Stackmaster: " + failure + "; crafting was cancelled without consuming anything.");
            }
            return false;
        }

        internal static void Postfix(ResourceActionKind __state)
        {
            ResourceTransactionContext.Complete(ResourceActionKind.Crafting);
            ResourceActionContext.Restore(__state);
        }

        internal static Exception Finalizer(Exception __exception, ResourceActionKind __state)
        {
            if (__exception != null)
            {
                ResourceTransactionContext.Rollback();
                ResourceActionContext.Restore(__state);
            }
            return __exception;
        }
    }

    internal static class NearbyBuildingActionPatch
    {
        internal static void Prefix(ref ResourceActionKind __state)
        {
            __state = ResourceActionContext.Enter(ResourceActionKind.Building);
        }

        internal static void Postfix(ResourceActionKind __state)
        {
            ResourceTransactionContext.Complete(ResourceActionKind.Building);
            ResourceActionContext.Restore(__state);
        }

        internal static Exception Finalizer(Exception __exception, ResourceActionKind __state)
        {
            if (__exception != null)
            {
                ResourceTransactionContext.Rollback();
                ResourceActionContext.Restore(__state);
            }
            return __exception;
        }
    }

    internal static class NearbyTryPlacePiecePatch
    {
        internal static bool Prefix(
            Player __instance,
            [HarmonyArgument(0)] Piece piece,
            bool ___m_noPlacementCost,
            ref bool __result)
        {
            if (!RuntimeContext.Compatibility.IsCompatible || RuntimeContext.Plugin == null ||
                !RuntimeContext.Plugin.BuildingFromNearbyChestsEnabled.Value ||
                ResourceActionContext.Current != ResourceActionKind.Building || ___m_noPlacementCost ||
                (ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey(piece.FreeBuildKey())))
            {
                return true;
            }

            string failure;
            if (NearbyResourceService.TryBeginPieceTransaction(__instance, piece, out failure)) return true;
            __result = false;
            if (!string.IsNullOrEmpty(failure))
            {
                RuntimeContext.ShowCenter(string.Equals(failure, NearbyResourceOwnership.InUseMessage, StringComparison.Ordinal)
                    ? NearbyResourceOwnership.InUseMessage
                    : "Stackmaster: " + failure + "; building was cancelled without consuming anything.");
            }
            return false;
        }

        internal static void Postfix(bool __result)
        {
            if (!__result) ResourceTransactionContext.Rollback();
        }
    }

    internal static class NearbyResourceRemovalPatch
    {
        internal static bool Prefix(
            Inventory __instance,
            [HarmonyArgument(0)] string name,
            [HarmonyArgument(1)] int amount,
            [HarmonyArgument(2)] int itemQuality,
            [HarmonyArgument(3)] bool worldLevelBased)
        {
            var action = ResourceActionContext.Current;
            var plugin = RuntimeContext.Plugin;
            var enabled = action == ResourceActionKind.Building
                ? plugin != null && plugin.BuildingFromNearbyChestsEnabled.Value
                : action == ResourceActionKind.Crafting && plugin != null && plugin.CraftingFromNearbyChestsEnabled.Value;
            var player = Player.m_localPlayer;
            if (!RuntimeContext.Compatibility.IsCompatible || !enabled || player == null ||
                !ReferenceEquals(__instance, player.GetInventory()) || !ResourceTransactionContext.Active)
            {
                return true;
            }

            // The complete action cost was already removed atomically before vanilla created
            // the crafted item or placed the piece. Record vanilla's intended charge, then
            // suppress its per-requirement removal so split stacks cannot double-charge.
            ResourceTransactionContext.AcknowledgeVanillaRemoval(amount);
            return false;
        }
    }
}
