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
            string inventoryId = "player")
        {
            var inventory = player.GetInventory();
            return CaptureInventory(inventoryId, inventory, catalog, true, player, protection);
        }

        internal static InventorySnapshot CaptureInventory(
            string inventoryId,
            Inventory inventory,
            CompatibilityCatalog catalog,
            bool isPlayer = false,
            Player player = null,
            ProtectionState protection = null)
        {
            if (inventory == null) throw new ArgumentNullException(nameof(inventory));
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));

            var width = inventory.GetWidth();
            var items = inventory.GetAllItems()
                .Select(item => ToSnapshot(inventoryId, item, width, catalog, isPlayer, player, protection))
                .ToArray();

            var height = inventory.GetHeight();
            var reservedSlots = isPlayer && protection != null
                ? protection.Records
                    .Where(record => record.Slot.Column < width && record.Slot.Row < height)
                    .Select(record => record.Slot.Row * width + record.Slot.Column)
                : Enumerable.Empty<int>();
            return new InventorySnapshot(inventoryId, width * inventory.GetHeight(), items, reservedSlots);
        }

        internal static ItemStackSnapshot ToSnapshot(
            string inventoryId,
            ItemDrop.ItemData item,
            int width,
            CompatibilityCatalog catalog,
            bool isPlayer,
            Player player,
            ProtectionState protection)
        {
            var slot = item.m_gridPos.y * width + item.m_gridPos.x;
            var protectedSlot = new Slot(item.m_gridPos.x, item.m_gridPos.y);
            ProtectionRecord record = null;
            var isProtected = isPlayer && protection != null && protection.TryGet(protectedSlot, out record);
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
                replenishmentTarget: target);
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
