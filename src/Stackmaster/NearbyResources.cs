#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Stackmaster.Core;
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

    internal static class ResourceTransactionContext
    {
        [ThreadStatic]
        private static IReadOnlyList<RemovedResource> _removed;
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

        internal static void Begin(IReadOnlyList<RemovedResource> removed, int expectedUnits)
        {
            if (_removed != null) throw new InvalidOperationException("A nearby-resource transaction is already active.");
            _removed = removed ?? Array.Empty<RemovedResource>();
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
            Clear();
            var restored = removed == null || NearbyResourceService.Rollback(removed);
            if (!restored) RuntimeContext.Disable("Nearby resource rollback could not restore every item.");
            return restored;
        }

        private static void Clear()
        {
            _removed = null;
            _selectedIngredient = null;
            _selectedAmount = 0;
            _selectedExtraAmount = 0;
            _expectedUnits = 0;
            _acknowledgedUnits = 0;
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
            var capture = Capture(player, matchWorldLevel, true);
            var plan = Planner.Plan(normalizedRequirements, capture.Stacks);
            if (!plan.IsSatisfiable || plan.PlannedUnits != plan.RequiredUnits)
            {
                failure = "fresh nearby stock no longer satisfies the exact complete cost";
                return false;
            }
            if (!RevalidateContainers(player, plan, capture, out failure)) return false;
            if (!RevalidateStacks(plan, capture, matchWorldLevel, out failure)) return false;

            IReadOnlyList<RemovedResource> removed;
            if (!ExecuteWithRollback(plan, capture, out removed, out failure)) return false;
            ResourceTransactionContext.Begin(removed, plan.RequiredUnits);
            return true;
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
            var discovery = ContainerDiscovery.Discover(player, null, catalog, radius, true);
            var containers = discovery.Containers
                .Where(handle => handle != null && handle.Snapshot.IsEligible &&
                                 handle.NetworkView != null && handle.NetworkView.IsValid() &&
                                 handle.NetworkView.IsOwner() && handle.Container.IsOwner())
                .ToList();
            var resourceStacks = new List<ResourceStack>();
            var runtimeStacks = new Dictionary<string, RuntimeResourceStack>(StringComparer.Ordinal);
            AddInventory(resourceStacks, runtimeStacks, PlayerInventoryId, player.GetInventory(), null, 0, matchWorldLevel);
            for (var index = 0; index < containers.Count; index++)
            {
                var handle = containers[index];
                AddInventory(resourceStacks, runtimeStacks, handle.Id, handle.Container.GetInventory(), handle, index + 1, matchWorldLevel);
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
            var width = inventory.GetWidth();
            foreach (var item in inventory.GetAllItems())
            {
                if (item == null || item.m_stack <= 0 || item.m_shared == null) continue;
                if (matchWorldLevel && item.m_worldLevel < Game.m_worldLevel) continue;
                var slot = item.m_gridPos.y * width + item.m_gridPos.x;
                var stackId = inventoryId + ":" + slot.ToString(CultureInfo.InvariantCulture);
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
                    failure = "nearby container became in use before consumption";
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
            ResourceWithdrawalPlan plan,
            NearbyResourceCapture capture,
            out IReadOnlyList<RemovedResource> completed,
            out string failure)
        {
            failure = null;
            completed = Array.Empty<RemovedResource>();
            var removed = new List<RemovedResource>();
            foreach (var step in plan.Steps)
            {
                var runtime = capture.RuntimeStacks[step.StackId];
                var clone = runtime.Item.Clone();
                clone.m_stack = step.Quantity;
                var position = runtime.Item.m_gridPos;
                var before = TotalUnits(runtime.Inventory);
                var success = runtime.Inventory.RemoveItem(runtime.Item, step.Quantity);
                var after = TotalUnits(runtime.Inventory);
                if (!success || after != before - step.Quantity)
                {
                    if (!Rollback(removed))
                    {
                        failure = "resource removal failed and rollback could not restore every item";
                        RuntimeContext.Disable(failure);
                    }
                    else
                    {
                        failure = "resource removal failed; all prior removals were rolled back";
                    }
                    return false;
                }
                removed.Add(new RemovedResource(runtime.Inventory, clone, position, step.Quantity));
            }
            completed = removed;
            return true;
        }

        internal static bool Rollback(IEnumerable<RemovedResource> removed)
        {
            var restoredAll = true;
            foreach (var entry in removed.Reverse())
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
            return restoredAll;
        }

        private static int TotalUnits(Inventory inventory)
        {
            return inventory.GetAllItems().Sum(item => item.m_stack);
        }
    }

    internal static class NearbyRequirementPatches
    {
        internal static void RecipePostfix(Player __instance, Recipe recipe, bool discover, int qualityLevel, int amount, ref bool __result)
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

        internal static void PiecePostfix(Player __instance, Piece piece, Player.RequirementMode mode, ref bool __result)
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
    }

    internal static class NearbyFirstRequiredItemPatch
    {
        internal static void Postfix(
            Player __instance,
            Inventory inventory,
            Recipe recipe,
            int qualityLevel,
            ref int amount,
            ref int extraAmount,
            int craftMultiplier,
            ref ItemDrop.ItemData __result)
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
            __result = NearbyResourceService.FindFirstRequiredItem(
                __instance,
                recipe,
                qualityLevel,
                craftMultiplier,
                false,
                out amount,
                out extraAmount);
        }
    }

    internal static class NearbyCraftingActionPatch
    {
        internal static bool Prefix(
            Player player,
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
            RuntimeContext.ShowCenter("Stackmaster: " + failure + "; crafting was cancelled without consuming anything.");
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
        internal static bool Prefix(Player __instance, Piece piece, bool ___m_noPlacementCost, ref bool __result)
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
            RuntimeContext.ShowCenter("Stackmaster: " + failure + "; building was cancelled without consuming anything.");
            return false;
        }

        internal static void Postfix(bool __result)
        {
            if (!__result) ResourceTransactionContext.Rollback();
        }
    }

    internal static class NearbyResourceRemovalPatch
    {
        internal static bool Prefix(Inventory __instance, string name, int amount, int itemQuality, bool worldLevelBased)
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
