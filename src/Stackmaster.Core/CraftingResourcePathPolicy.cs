using System;
using System.Collections.Generic;
using System.Linq;

namespace Stackmaster.Core
{
    public enum CraftingResourcePath
    {
        GuardedNearbyStorage,
        VanillaPlayerInventory
    }

    /// <summary>
    /// Chooses whether a craft must enter Stackmaster's guarded nearby-storage transaction.
    /// The vanilla path is intentionally narrow: it is available only in the player-centered
    /// fallback scope and only after a fresh player-inventory-only check satisfies the full cost.
    /// </summary>
    public static class CraftingResourcePathPolicy
    {
        private static readonly ResourceWithdrawalPlanner Planner = new ResourceWithdrawalPlanner();

        public static CraftingResourcePath Select(
            StorageScopeKind scopeKind,
            bool playerInventorySatisfiesFullCost)
        {
            return scopeKind == StorageScopeKind.NearbyRadius && playerInventorySatisfiesFullCost
                ? CraftingResourcePath.VanillaPlayerInventory
                : CraftingResourcePath.GuardedNearbyStorage;
        }

        public static CraftingResourcePath SelectForPlayerInventory(
            StorageScopeKind scopeKind,
            IEnumerable<ResourceRequirement> requirements,
            IEnumerable<ResourceStack> playerInventory,
            bool requireOnlyOneIngredient)
        {
            var required = (requirements ?? Array.Empty<ResourceRequirement>()).ToArray();
            var stacks = (playerInventory ?? Array.Empty<ResourceStack>()).ToArray();
            var satisfied = requireOnlyOneIngredient
                ? required.Any(alternative =>
                    Planner.Plan(new[] { alternative }, stacks).IsSatisfiable)
                : required.Length == 0 || Planner.Plan(required, stacks).IsSatisfiable;
            return Select(scopeKind, satisfied);
        }
    }
}
