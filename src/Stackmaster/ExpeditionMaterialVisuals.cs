#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using Stackmaster.Core;

namespace Stackmaster
{
    /// <summary>
    /// Assigns the aggregate expedition requirement to concrete ordinary carried stacks for
    /// display. Explicitly protected, equipped, and quick-bar quantities stay personal; they do
    /// not silently satisfy or visually absorb the separate additive expedition target.
    /// </summary>
    internal static class ExpeditionMaterialVisuals
    {
        internal static bool TryAllocate(
            Player player,
            ProtectionState protection,
            out IReadOnlyDictionary<ItemDrop.ItemData, int> allocations)
        {
            allocations = new Dictionary<ItemDrop.ItemData, int>();
            if (player == null || protection == null) return false;

            IReadOnlyList<ResourceRequirement> requirements;
            if (!ExpeditionReservations.TryGetAggregateRequirements(player, out requirements))
            {
                return false;
            }

            var inventory = player.GetInventory();
            var resolution = InventorySnapshots.ResolveProtection(player, protection);
            var protectedSlots = new HashSet<Slot>(resolution.Assignments.Keys);
            var result = new Dictionary<ItemDrop.ItemData, int>();
            foreach (var requirement in requirements)
            {
                var remaining = requirement.Quantity;
                var candidates = inventory.GetAllItems()
                    .Where(item => item != null && item.m_shared != null &&
                        string.Equals(item.m_shared.m_name, requirement.ItemName, StringComparison.Ordinal) &&
                        (requirement.Quality < 0 || item.m_quality == requirement.Quality) &&
                        item.m_gridPos.y != 0 &&
                        !item.m_equipped && !player.IsItemEquiped(item) &&
                        !protectedSlots.Contains(new Slot(item.m_gridPos.x, item.m_gridPos.y)))
                    // The sort policy puts reservation-served stacks at the final available slots.
                    // Allocate from that end so partial quantities and orange labels agree with it.
                    .OrderByDescending(item => item.m_gridPos.y)
                    .ThenByDescending(item => item.m_gridPos.x)
                    .ToArray();

                foreach (var item in candidates)
                {
                    if (remaining <= 0) break;
                    var quantity = Math.Min(remaining, item.m_stack);
                    if (quantity <= 0) continue;
                    result[item] = result.TryGetValue(item, out var prior)
                        ? checked(prior + quantity)
                        : quantity;
                    remaining -= quantity;
                }
            }

            allocations = result;
            return true;
        }
    }
}
