#nullable disable
using System;
using System.Globalization;
using HarmonyLib;
using Stackmaster.Core;
using UnityEngine;

namespace Stackmaster
{
    internal static class ProtectionInteraction
    {
        internal static bool TryHandle(InventoryGrid grid, ItemDrop.ItemData item, Vector2i position, InventoryGrid.Modifier modifier)
        {
            if (modifier != InventoryGrid.Modifier.Select || !Input.GetKey(KeyCode.LeftAlt))
            {
                return false;
            }

            var player = Player.m_localPlayer;
            if (player == null || grid == null || grid.GetInventory() != player.GetInventory())
            {
                return false;
            }

            var slot = new Slot(position.x, position.y);
            ProtectionState state;
            if (!RuntimeContext.TryLoadProtection(player, out state))
            {
                return true;
            }
            var resolution = InventorySnapshots.ResolveProtection(player, state);
            ProtectionRecord protectedRecord;
            if (resolution.TryGet(slot, out protectedRecord))
            {
                state.Unprotect(protectedRecord);
                RuntimeContext.SaveProtection(player, state);
                InventoryIntegration.RefreshProtectionOverlays();
                RuntimeContext.ShowTopLeft("Stackmaster: item unprotected.");
                return true;
            }

            if (item == null)
            {
                return false;
            }

            if (item.m_shared.m_maxStackSize <= 1)
            {
                state.Protect(slot, null, InventorySnapshots.PersistentItemKey(item));
                RuntimeContext.SaveProtection(player, state);
                InventoryIntegration.RefreshProtectionOverlays();
                RuntimeContext.ShowTopLeft("Stackmaster: item protected.");
                return true;
            }

            if (TextInput.instance == null)
            {
                RuntimeContext.ShowCenter("Stackmaster could not open the target prompt; slot unchanged.");
                return true;
            }

            var receiver = new TargetPromptReceiver(player, slot, item);
            TextInput.instance.RequestText(
                receiver,
                "Stackmaster: 0 = protect only; 1-" + item.m_shared.m_maxStackSize.ToString(CultureInfo.InvariantCulture) + " = target",
                4);
            return true;
        }

        private sealed class TargetPromptReceiver : TextReceiver
        {
            private readonly Player _player;
            private readonly Slot _slot;
            private readonly string _itemKey;
            private readonly int _maxStack;
            private string _text = string.Empty;

            internal TargetPromptReceiver(Player player, Slot slot, ItemDrop.ItemData item)
            {
                _player = player;
                _slot = slot;
                _itemKey = InventorySnapshots.PersistentItemKey(item);
                _maxStack = item.m_shared.m_maxStackSize;
            }

            public string GetText() => _text;

            public void SetText(string text)
            {
                _text = text ?? string.Empty;
                int target;
                if (!int.TryParse(_text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out target) || target < 0 || target > _maxStack)
                {
                    RuntimeContext.ShowCenter("Stackmaster: enter 0 or a target from 1 to " + _maxStack.ToString(CultureInfo.InvariantCulture) + ". Slot unchanged.");
                    return;
                }

                ProtectionState state;
                if (!RuntimeContext.TryLoadProtection(_player, out state))
                {
                    return;
                }
                if (target == 0)
                {
                    state.Protect(_slot, null, _itemKey);
                    RuntimeContext.ShowTopLeft("Stackmaster: item protected.");
                }
                else
                {
                    state.Protect(_slot, target, _itemKey);
                    RuntimeContext.ShowTopLeft("Stackmaster: protected with target " + target.ToString(CultureInfo.InvariantCulture) + ".");
                }
                RuntimeContext.SaveProtection(_player, state);
                InventoryIntegration.RefreshProtectionOverlays();
            }
        }
    }

    [HarmonyPatch(typeof(InventoryGui), "OnSelectedItem", typeof(InventoryGrid), typeof(ItemDrop.ItemData), typeof(Vector2i), typeof(InventoryGrid.Modifier))]
    internal static class InventoryProtectionClickPatch
    {
        private static bool Prefix(InventoryGrid grid, ItemDrop.ItemData item, Vector2i pos, InventoryGrid.Modifier mod)
        {
            return !RuntimeContext.Compatibility.IsCompatible || !ProtectionInteraction.TryHandle(grid, item, pos, mod);
        }
    }
}
