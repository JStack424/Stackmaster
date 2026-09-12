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
        internal uint DataRevision { get; private set; }
        internal ushort OwnerRevision { get; private set; }

        internal bool CaptureRevisionBaseline()
        {
            var view = Handle.NetworkView;
            var zdo = view != null && view.IsValid() ? view.GetZDO() : null;
            if (zdo == null) return false;
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

        internal static bool Complete()
        {
            if (_removed == null) return true;
            if (_acknowledgedUnits == _expectedUnits)
            {
                Clear();
                return true;
            }
            if (_acknowledgedUnits == 0)
            {
                Rollback();
            }
            else
            {
                // Some vanilla cost calls ran, so the craft/placement output already exists.
                // Keep the exact preplanned charge rather than restoring it and duplicating value.
                Clear();
            }
            RuntimeContext.Disable("Vanilla did not confirm the complete nearby-resource cost.");
            return false;
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

        private static void Clear()
        {
            var reservations = _reservations;
            ResetState();
            ReleaseReservations(reservations);
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

        private static void ReleaseReservations(IEnumerable<ContainerReservation> reservations)
        {
            if (reservations == null) return;
            foreach (var reservation in reservations.Reverse())
            {
                try
                {
                    var container = reservation.Container;
                    if (container != null && container.IsOwner()) container.SetInUse(false);
                }
                catch (Exception exception)
                {
                    if (RuntimeContext.Plugin != null)
                    {
                        RuntimeContext.Plugin.Log.LogError("Failed to release a nearby-resource container reservation: " + exception);
                    }
                }
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
            IReadOnlyList<ContainerHandle> containers,
            IReadOnlyList<ResourceStack> stacks,
            IReadOnlyDictionary<string, RuntimeResourceStack> runtimeStacks)
        {
            Containers = containers;
            Stacks = stacks;
            RuntimeStacks = runtimeStacks;
        }

        internal IReadOnlyList<ContainerHandle> Containers { get; }
        internal IReadOnlyList<ResourceStack> Stacks { get; }
        internal IReadOnlyDictionary<string, RuntimeResourceStack> RuntimeStacks { get; }
    }

    internal sealed class RuntimeRequirementAvailability
    {
        internal RuntimeRequirementAvailability(int required, int available, bool isSatisfied)
        {
            Required = required;
            Available = available;
            IsSatisfied = isSatisfied;
        }

        internal int Required { get; }
        internal int Available { get; }
        internal bool IsSatisfied { get; }
    }

    internal static class NearbyResourceService
    {
        private const string PlayerInventoryId = "player";
        private static readonly ResourceWithdrawalPlanner Planner = new ResourceWithdrawalPlanner();
        private static readonly MethodInfo AddItemAtMethod = AccessTools.DeclaredMethod(
            typeof(Inventory),
            "AddItem",
            new[] { typeof(ItemDrop.ItemData), typeof(int), typeof(int), typeof(int), typeof(bool) });
        private static int _cachedFrame = -1;
        private static Player _cachedPlayer;
        private static float _cachedRadius;
        private static bool _cachedMatchWorldLevel;
        private static NearbyResourceCapture _cachedCapture;

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
            var next = 0;
            var result = new List<RuntimeRequirementAvailability>(requirements.Length);
            foreach (var requirement in requirements)
            {
                if (requirement == null || requirement.m_resItem == null || requirement.m_amount <= 0)
                {
                    result.Add(new RuntimeRequirementAvailability(0, 0, true));
                    continue;
                }

                var entry = evaluated[next++];
                result.Add(new RuntimeRequirementAvailability(entry.Required, entry.Available, entry.IsSatisfied));
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
            var next = 0;
            var result = new List<RuntimeRequirementAvailability>(requirements.Count);
            foreach (var requirement in requirements)
            {
                var required = requirement == null || requirement.m_resItem == null
                    ? 0
                    : checked(requirement.GetAmount(qualityLevel) * craftMultiplier);
                if (required <= 0)
                {
                    result.Add(new RuntimeRequirementAvailability(0, 0, true));
                    continue;
                }

                var entry = evaluated[next++];
                result.Add(new RuntimeRequirementAvailability(entry.Required, entry.Available, entry.IsSatisfied));
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
            return TryBeginTransaction(player, PieceRequirements(piece), true, out failure);
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
                return TryBeginTransaction(player, RecipeRequirements(player, recipe, qualityLevel, craftMultiplier), true, out failure);
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

            var unowned = requiredHandles
                .Where(handle => !handle.NetworkView.IsOwner() || !handle.Container.IsOwner())
                .ToArray();
            if (unowned.Length > 0)
            {
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
            if (!TryReserveContainers(player, requiredHandles, out reservations, out failure)) return false;
            if (!RevalidateReservedContainers(player, reservations, out failure) ||
                !RevalidateStacks(mutablePlan, mutableCapture, matchWorldLevel, out failure))
            {
                ReleaseReservations(reservations);
                return false;
            }

            var removed = new List<RemovedResource>();
            ResourceTransactionContext.Begin(removed, mutablePlan.RequiredUnits, reservations);
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

        private static List<ResourceRequirement> PieceRequirements(Piece piece)
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

        private static NearbyResourceCapture Capture(Player player, bool matchWorldLevel, bool fresh)
        {
            var radius = RuntimeContext.Plugin.NearbyStorageRadius.Value;
            if (!fresh && _cachedCapture != null && _cachedFrame == Time.frameCount &&
                ReferenceEquals(_cachedPlayer, player) && _cachedMatchWorldLevel == matchWorldLevel &&
                Math.Abs(_cachedRadius - radius) < 0.001f)
            {
                return _cachedCapture;
            }

            var catalog = new CompatibilityCatalog();
            // Resource accounting must inspect the complete radius: a responsiveness cutoff may
            // safely omit deposit destinations, but it must never make a build/craft total partial.
            var discovery = ContainerDiscovery.Discover(player, null, catalog, radius, true, true);
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

            var capture = new NearbyResourceCapture(containers, resourceStacks, runtimeStacks);
            _cachedFrame = Time.frameCount;
            _cachedPlayer = player;
            _cachedRadius = radius;
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

        private static bool TryResolveRequiredContainers(
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
                if (!handles.TryGetValue(id, out handle) || !ValidateReadOnlyHandle(player, handle, out failure))
                {
                    requiredHandles = Array.Empty<ContainerHandle>();
                    return false;
                }
                result.Add(handle);
            }
            requiredHandles = result.ToArray();
            return true;
        }

        private static bool ValidateReadOnlyHandle(Player player, ContainerHandle handle, out string failure)
        {
            failure = null;
            if (handle == null || handle.Container == null || handle.Container.GetType() != typeof(Container) ||
                Vector3.Distance(player.transform.position, handle.Container.transform.position) > RuntimeContext.Plugin.NearbyStorageRadius.Value ||
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
            AddInventory(resourceStacks, runtimeStacks, PlayerInventoryId, player.GetInventory(), null, 0, matchWorldLevel);

            var inventoryOrder = 1;
            foreach (var handle in readOnlyCapture.Containers.Where(handle => requiredIds.Contains(handle.Id)))
            {
                if (!ValidateReadOnlyHandle(player, handle, out failure) ||
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
                AddInventory(
                    resourceStacks,
                    runtimeStacks,
                    handle.Id,
                    handle.Container.GetInventory(),
                    handle,
                    inventoryOrder++,
                    matchWorldLevel);
            }

            mutableCapture = new NearbyResourceCapture(requiredHandles, resourceStacks, runtimeStacks);
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
            IEnumerable<ContainerHandle> requiredHandles,
            out IReadOnlyList<ContainerReservation> reservations,
            out string failure)
        {
            var held = new List<ContainerReservation>();
            failure = null;
            foreach (var handle in requiredHandles)
            {
                if (!ValidateReadOnlyHandle(player, handle, out failure))
                {
                    ReleaseReservations(held);
                    reservations = Array.Empty<ContainerReservation>();
                    return false;
                }
                if (!handle.NetworkView.IsOwner() || !handle.Container.IsOwner() ||
                    handle.NetworkView.GetZDO() == null ||
                    handle.NetworkView.GetZDO().OwnerRevision != handle.ResourceOwnerRevision)
                {
                    failure = "required container ownership changed before reservation";
                    ReleaseReservations(held);
                    reservations = Array.Empty<ContainerReservation>();
                    return false;
                }
                if (handle.Container.IsInUse() || (handle.Container.m_wagon != null && handle.Container.m_wagon.InUse()))
                {
                    failure = NearbyResourceOwnership.InUseMessage;
                    ReleaseReservations(held);
                    reservations = Array.Empty<ContainerReservation>();
                    return false;
                }
                var reservation = new ContainerReservation(handle);
                // Add it before SetInUse so cleanup still releases a reservation if a patched
                // effects path throws after setting the ZDO flag.
                held.Add(reservation);
                try
                {
                    handle.Container.SetInUse(true);
                    if (!handle.Container.IsInUse() || !reservation.CaptureRevisionBaseline())
                    {
                        failure = "required container could not be reserved";
                        ReleaseReservations(held);
                        reservations = Array.Empty<ContainerReservation>();
                        return false;
                    }
                }
                catch (Exception exception)
                {
                    failure = "required container reservation failed: " + exception.GetType().Name;
                    ReleaseReservations(held);
                    reservations = Array.Empty<ContainerReservation>();
                    return false;
                }
            }
            reservations = held;
            return true;
        }

        private static bool RevalidateReservedContainers(
            Player player,
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
                    Vector3.Distance(player.transform.position, handle.Container.transform.position) > RuntimeContext.Plugin.NearbyStorageRadius.Value ||
                    !ContainerDiscovery.CheckAccess(player, handle.Container))
                {
                    failure = "required container reservation or state changed before consumption";
                    return false;
                }
            }
            return true;
        }

        private static void ReleaseReservations(IEnumerable<ContainerReservation> reservations)
        {
            if (reservations == null) return;
            foreach (var reservation in reservations.Reverse())
            {
                try
                {
                    if (reservation.Container != null && reservation.Container.IsOwner())
                    {
                        reservation.Container.SetInUse(false);
                    }
                }
                catch (Exception exception)
                {
                    if (RuntimeContext.Plugin != null)
                    {
                        RuntimeContext.Plugin.Log.LogError("Failed to release a pre-transaction container reservation: " + exception);
                    }
                }
            }
        }

        private static bool RevalidateContainers(
            Player player,
            ResourceWithdrawalPlan plan,
            NearbyResourceCapture capture,
            out string failure)
        {
            failure = null;
            foreach (var containerId in plan.Steps
                .Where(step => !string.Equals(step.InventoryId, PlayerInventoryId, StringComparison.Ordinal))
                .Select(step => step.InventoryId)
                .Distinct(StringComparer.Ordinal))
            {
                var handle = capture.Containers.FirstOrDefault(item => string.Equals(item.Id, containerId, StringComparison.Ordinal));
                if (handle == null || handle.Container == null || handle.Container.GetType() != typeof(Container) ||
                    Vector3.Distance(player.transform.position, handle.Container.transform.position) > RuntimeContext.Plugin.NearbyStorageRadius.Value ||
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

        private static bool RevalidateStacks(
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
            var reservationByContainer = reservations.ToDictionary(item => item.Container);
            foreach (var step in plan.Steps)
            {
                var runtime = capture.RuntimeStacks[step.StackId];
                ContainerReservation reservation = null;
                if (runtime.Container != null)
                {
                    if (!reservationByContainer.TryGetValue(runtime.Container.Container, out reservation) ||
                        !ReservationMatches(player, reservation))
                    {
                        failure = "required container reservation changed before resource removal";
                        return false;
                    }
                }

                var clone = runtime.Item.Clone();
                clone.m_stack = step.Quantity;
                var position = runtime.Item.m_gridPos;
                var before = TotalUnits(runtime.Inventory);
                bool success;
                Exception removalException = null;
                try
                {
                    success = runtime.Inventory.RemoveItem(runtime.Item, step.Quantity);
                }
                catch (Exception exception)
                {
                    success = false;
                    removalException = exception;
                }
                var after = TotalUnits(runtime.Inventory);
                var actualRemoved = before - after;
                if (actualRemoved > 0 && actualRemoved <= before)
                {
                    clone.m_stack = actualRemoved;
                    removed.Add(new RemovedResource(runtime.Inventory, clone, position, actualRemoved));
                }
                if (removalException != null)
                {
                    throw new InvalidOperationException("inventory removal threw after possible mutation", removalException);
                }
                if (!success || actualRemoved != step.Quantity)
                {
                    failure = actualRemoved < 0
                        ? "resource removal produced an invalid inventory delta"
                        : "resource removal failed; rollback is required";
                    return false;
                }
                if (reservation != null)
                {
                    if (!reservation.AdvanceDataRevisionAfterMutation() || !ReservationMatches(player, reservation))
                    {
                        failure = "required container reservation changed after resource removal";
                        return false;
                    }
                }
            }
            return true;
        }

        private static bool ReservationMatches(Player player, ContainerReservation reservation)
        {
            if (reservation == null || reservation.Handle == null || reservation.Container == null) return false;
            var handle = reservation.Handle;
            var view = handle.NetworkView;
            var zdo = view != null && view.IsValid() ? view.GetZDO() : null;
            return zdo != null && view.IsOwner() && handle.Container.IsOwner() && handle.Container.IsInUse() &&
                   string.Equals(zdo.m_uid.ToString(), handle.Id, StringComparison.Ordinal) &&
                   zdo.DataRevision == reservation.DataRevision &&
                   zdo.OwnerRevision == reservation.OwnerRevision &&
                   Vector3.Distance(player.transform.position, handle.Container.transform.position) <= RuntimeContext.Plugin.NearbyStorageRadius.Value &&
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
                    unownedHandles));
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
            ContainerHandle[] unownedHandles)
        {
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

            try
            {
                var refreshFailed = false;
                var deadline = Time.realtimeSinceStartup + OwnershipTimeoutSeconds;
                while (!ownership.IsComplete && Time.realtimeSinceStartup < deadline)
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
                        OwnershipCoordinator.End(ownership);
                    }
                    finally
                    {
                        _running = false;
                    }
                }
            }
        }
    }

    internal static class NearbyHudFailOpen
    {
        private static readonly HashSet<string> ReportedSurfaces = new HashSet<string>(StringComparer.Ordinal);

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
        private static float _cachedRadius;
        private static float _nextRefreshTime;
        private static IReadOnlyList<RuntimeRequirementAvailability> _cachedAvailability = Array.Empty<RuntimeRequirementAvailability>();

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
            if (!RuntimeContext.Compatibility.IsCompatible || RuntimeContext.Plugin == null ||
                !RuntimeContext.Plugin.BuildingFromNearbyChestsEnabled.Value ||
                __instance == null || piece == null || Player.m_localPlayer == null)
            {
                return;
            }

            var requirements = piece.m_resources ?? Array.Empty<Piece.Requirement>();
            var requirementItems = __instance.m_requirementItems ?? Array.Empty<GameObject>();
            var player = Player.m_localPlayer;
            var radius = RuntimeContext.Plugin.NearbyStorageRadius.Value;
            if (!ReferenceEquals(_cachedPlayer, player) || !ReferenceEquals(_cachedPiece, piece) ||
                Math.Abs(_cachedRadius - radius) >= 0.001f || Time.time >= _nextRefreshTime)
            {
                _cachedAvailability = NearbyResourceService.GetPieceRequirementAvailability(player, piece, false);
                _cachedPlayer = player;
                _cachedPiece = piece;
                _cachedRadius = radius;
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

                amountLabel.text = ResourceRequirementPresentation.Format(entry.Required, entry.Available);
                amountLabel.color = ResourceRequirementPresentation.ShouldUseShortageColor(
                    noBuildCost,
                    entry.IsSatisfied,
                    Mathf.Sin(Time.time * 10f))
                    ? Color.red
                    : Color.white;
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
        private static float _cachedRadius;
        private static float _nextRefreshTime;
        private static IReadOnlyList<RuntimeRequirementAvailability> _cachedAvailability = Array.Empty<RuntimeRequirementAvailability>();

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
            if (!vanillaResult || !craft || !RuntimeContext.Compatibility.IsCompatible || RuntimeContext.Plugin == null ||
                !RuntimeContext.Plugin.CraftingFromNearbyChestsEnabled.Value || inventoryGui == null ||
                player == null || !ReferenceEquals(player, Player.m_localPlayer) || elementRoot == null ||
                requirement == null || requirement.m_resItem == null || craftMultiplier <= 0 || requirements == null)
            {
                return;
            }

            var recipe = GetSelectedRecipe(inventoryGui);
            if (recipe == null) return;
            var requirementIndex = requirements.FindIndex(item => ReferenceEquals(item, requirement));
            if (requirementIndex < 0) return;

            var radius = RuntimeContext.Plugin.NearbyStorageRadius.Value;
            var signature = RequirementSignature(requirements, quality, craftMultiplier);
            if (!ReferenceEquals(_cachedPlayer, player) || !ReferenceEquals(_cachedRecipe, recipe) ||
                _cachedQuality != quality || _cachedCraftMultiplier != craftMultiplier ||
                _cachedRequirementSignature != signature || Math.Abs(_cachedRadius - radius) >= 0.001f ||
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
                _cachedRadius = radius;
                _nextRefreshTime = Time.time + RefreshIntervalSeconds;
            }

            if (requirementIndex >= _cachedAvailability.Count) return;
            var entry = _cachedAvailability[requirementIndex];
            if (entry.Required <= 0) return;
            var amountTransform = elementRoot.Find("res_amount");
            var amountLabel = amountTransform == null ? null : amountTransform.GetComponent<TMP_Text>();
            if (amountLabel == null) return;

            amountLabel.text = ResourceRequirementPresentation.Format(entry.Required, entry.Available);
            var noCraftCost = player.NoCostCheat() ||
                              (ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoCraftCost));
            amountLabel.color = ResourceRequirementPresentation.ShouldUseShortageColor(
                noCraftCost,
                entry.IsSatisfied,
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

    internal static class NearbyCraftingActionPatch
    {
        internal static bool Prefix(
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
            if (NearbyResourceService.TryBeginRecipeTransaction(player, ___m_craftRecipe, qualityLevel, multiplier, out failure))
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
            ResourceTransactionContext.Complete();
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
            ResourceTransactionContext.Complete();
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
