#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Stackmaster.Core;

namespace Stackmaster
{
    internal sealed class CompatibilityCatalog
    {
        private readonly List<ItemDrop.ItemData> _representatives = new List<ItemDrop.ItemData>();

        internal string KeyFor(ItemDrop.ItemData item)
        {
            for (var index = 0; index < _representatives.Count; index++)
            {
                var representative = _representatives[index];
                if (representative.IsSameType(item) && item.IsSameType(representative))
                {
                    return "g:" + index.ToString(CultureInfo.InvariantCulture);
                }
            }

            _representatives.Add(item);
            return "g:" + (_representatives.Count - 1).ToString(CultureInfo.InvariantCulture);
        }
    }

    internal static class InventorySnapshots
    {
        internal static InventorySnapshot CapturePlayer(
            Player player,
            ProtectionState protection,
            CompatibilityCatalog catalog,
            string inventoryId = "player",
            bool pruneUnresolvedProtection = false)
        {
            var inventory = player.GetInventory();
            return CaptureInventory(
                inventoryId,
                inventory,
                catalog,
                true,
                player,
                protection,
                pruneUnresolvedProtection);
        }

        internal static InventorySnapshot CaptureInventory(
            string inventoryId,
            Inventory inventory,
            CompatibilityCatalog catalog,
            bool isPlayer = false,
            Player player = null,
            ProtectionState protection = null,
            bool pruneUnresolvedProtection = false)
        {
            if (inventory == null) throw new ArgumentNullException(nameof(inventory));
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));

            var protectionResolution = isPlayer && player != null && protection != null
                ? ResolveProtection(player, protection, pruneUnresolvedProtection)
                : null;
            var width = inventory.GetWidth();
            var items = inventory.GetAllItems()
                .Select(item => ToSnapshot(inventoryId, item, width, catalog, isPlayer, player, protectionResolution))
                .ToArray();

            var height = inventory.GetHeight();
            IEnumerable<int> reservedSlots = Enumerable.Empty<int>();
            if (isPlayer)
            {
                // The entire quick-bar row is fixed, including empty slots, so sorting can
                // neither remove from it nor use it as a destination.
                reservedSlots = height > 0 ? Enumerable.Range(0, width) : Enumerable.Empty<int>();
                if (protectionResolution != null)
                {
                    reservedSlots = reservedSlots.Concat(protectionResolution.Assignments.Keys
                        .Where(slot => slot.Column < width && slot.Row < height)
                        .Select(slot => slot.Row * width + slot.Column));
                }
            }
            return new InventorySnapshot(inventoryId, width * height, items, reservedSlots);
        }

        internal static ProtectionResolution ResolveProtection(
            Player player,
            ProtectionState protection,
            bool pruneUnresolved = false)
        {
            if (player == null) throw new ArgumentNullException(nameof(player));
            if (protection == null) throw new ArgumentNullException(nameof(protection));

            var inventory = player.GetInventory();
            var candidates = inventory.GetAllItems()
                .Select(item => new ProtectionCandidate(
                    new Slot(item.m_gridPos.x, item.m_gridPos.y),
                    PersistentItemKey(item)))
                .ToArray();
            var resolution = protection.Reconcile(candidates, pruneUnresolved);
            if (resolution.Changed)
            {
                RuntimeContext.SaveProtection(player, protection);
            }
            return resolution;
        }

        internal static ItemStackSnapshot ToSnapshot(
            string inventoryId,
            ItemDrop.ItemData item,
            int width,
            CompatibilityCatalog catalog,
            bool isPlayer,
            Player player,
            ProtectionResolution protectionResolution)
        {
            var slot = item.m_gridPos.y * width + item.m_gridPos.x;
            var protectedSlot = new Slot(item.m_gridPos.x, item.m_gridPos.y);
            ProtectionRecord record = null;
            var isProtected = isPlayer && protectionResolution != null && protectionResolution.TryGet(protectedSlot, out record);
            int? target = null;
            if (isProtected && record.TargetQuantity.HasValue &&
                string.Equals(record.TargetItemKey, PersistentItemKey(item), StringComparison.Ordinal))
            {
                target = Math.Min(record.TargetQuantity.Value, item.m_shared.m_maxStackSize);
            }

            return new ItemStackSnapshot(
                inventoryId + ":" + item.m_gridPos.x.ToString(CultureInfo.InvariantCulture) + ":" + item.m_gridPos.y.ToString(CultureInfo.InvariantCulture),
                catalog.KeyFor(item),
                VisibleName(item),
                item.m_stack,
                item.m_shared.m_maxStackSize,
                slot,
                isQuickBar: isPlayer && item.m_gridPos.y == 0,
                isEquipped: item.m_equipped || (player != null && player.IsItemEquiped(item)),
                isProtected: isProtected,
                replenishmentTarget: target,
                persistentItemKey: PersistentItemKey(item));
        }

        internal static string PersistentItemKey(ItemDrop.ItemData item)
        {
            var builder = new StringBuilder();
            builder.Append(item.m_dropPrefab != null ? item.m_dropPrefab.name : item.m_shared.m_name)
                .Append('|').Append(item.m_quality.ToString(CultureInfo.InvariantCulture))
                .Append('|').Append(item.m_variant.ToString(CultureInfo.InvariantCulture))
                .Append('|').Append(item.m_worldLevel.ToString(CultureInfo.InvariantCulture));

            if (item.m_customData != null)
            {
                foreach (var pair in item.m_customData.OrderBy(entry => entry.Key, StringComparer.Ordinal))
                {
                    builder.Append('|').Append(pair.Key.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(pair.Key)
                        .Append('=').Append(pair.Value == null ? -1 : pair.Value.Length).Append(':').Append(pair.Value);
                }
            }

            return builder.ToString();
        }

        internal static string VisibleName(ItemDrop.ItemData item)
        {
            var token = item.m_shared.m_name ?? string.Empty;
            return Localization.instance != null ? Localization.instance.Localize(token) : token;
        }

        internal static Vector2i PositionForSlot(Inventory inventory, int slot)
        {
            var width = inventory.GetWidth();
            return new Vector2i(slot % width, slot / width);
        }
    }
}
