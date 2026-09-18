#nullable disable
using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Stackmaster.Core;
using TMPro;
using UnityEngine;

namespace Stackmaster
{
    internal static class ProtectionInteraction
    {
        private static readonly FieldInfo TextInputField = AccessTools.Field(typeof(TextInput), "m_inputField");

        internal static bool ShouldIntercept(InventoryGrid grid, ItemDrop.ItemData item)
        {
            var player = Player.m_localPlayer;
            return player != null &&
                   grid != null &&
                   item != null &&
                   ReferenceEquals(grid.GetInventory(), player.GetInventory()) &&
                   ConfiguredModifiersHeld();
        }

        internal static void HandleLeftClick(InventoryGrid grid, ItemDrop.ItemData item, Vector2i position)
        {
            var player = Player.m_localPlayer;
            var slot = new Slot(position.x, position.y);
            ProtectionState state;
            if (!RuntimeContext.TryLoadProtection(player, out state))
            {
                return;
            }

            var resolution = InventorySnapshots.ResolveProtection(player, state);
            ProtectionRecord protectedRecord;
            resolution.TryGet(slot, out protectedRecord);
            var route = ProtectionInteractionPolicy.Route(
                compatibilityEnabled: true,
                isPlayerInventory: true,
                hasItem: true,
                isStackable: item.m_shared.m_maxStackSize > 1,
                isProtected: protectedRecord != null,
                button: ProtectionPointerButton.Left,
                configuredModifierStates: new[] { true });
            if (route != ProtectionClickRoute.ProtectOnly && route != ProtectionClickRoute.Unprotect)
            {
                return;
            }

            ProtectionInteractionPolicy.ApplyLeftClick(
                state,
                protectedRecord,
                slot,
                InventorySnapshots.PersistentItemKey(item));
            RuntimeContext.SaveProtection(player, state);
            InventoryIntegration.RefreshProtectionOverlays();
            RuntimeContext.ShowTopLeft(protectedRecord == null
                ? "Stackmaster: item protected."
                : "Stackmaster: item unprotected.");
        }

        internal static void HandleRightClick(InventoryGrid grid, ItemDrop.ItemData item, Vector2i position)
        {
            var player = Player.m_localPlayer;
            var slot = new Slot(position.x, position.y);
            ProtectionState state;
            if (!RuntimeContext.TryLoadProtection(player, out state))
            {
                return;
            }

            var resolution = InventorySnapshots.ResolveProtection(player, state);
            ProtectionRecord protectedRecord;
            resolution.TryGet(slot, out protectedRecord);
            var route = ProtectionInteractionPolicy.Route(
                compatibilityEnabled: true,
                isPlayerInventory: true,
                hasItem: true,
                isStackable: item.m_shared.m_maxStackSize > 1,
                isProtected: protectedRecord != null,
                button: ProtectionPointerButton.Right,
                configuredModifierStates: new[] { true });
            if (route == ProtectionClickRoute.SuppressWithoutChange)
            {
                RuntimeContext.ShowCenter("Stackmaster: non-stackable items cannot have restocking targets.");
                return;
            }

            if (route != ProtectionClickRoute.OpenTargetDialog)
            {
                return;
            }

            if (TextInput.instance == null)
            {
                RuntimeContext.ShowCenter("Stackmaster could not open the target prompt; item unchanged.");
                return;
            }

            var receiver = new TargetPromptReceiver(player, item, protectedRecord);
            var defaultTargetText = receiver.GetText();
            TextInput.instance.RequestText(
                receiver,
                "Stackmaster: restock target 1-" + item.m_shared.m_maxStackSize.ToString(CultureInfo.InvariantCulture),
                Math.Max(4, defaultTargetText.Length));
            SelectPrefilledTarget(TextInput.instance, defaultTargetText.Length);
        }

        private static bool ConfiguredModifiersHeld()
        {
            var plugin = RuntimeContext.Plugin;
            if (plugin == null)
            {
                return false;
            }

            var modifiers = plugin.StorageActionShortcut.Value.Modifiers.ToArray();
            return modifiers.Length > 0 && modifiers.All(Input.GetKey);
        }

        private static void SelectPrefilledTarget(TextInput textInput, int textLength)
        {
            var inputField = TextInputField?.GetValue(textInput) as TMP_InputField;
            if (inputField == null)
            {
                RuntimeContext.Plugin.Log.LogWarning("Stackmaster target prompt opened with its current legal target selected, but the text selection field was unavailable.");
                return;
            }

            inputField.selectionAnchorPosition = 0;
            inputField.selectionFocusPosition = textLength;
        }

        private sealed class TargetPromptReceiver : TextReceiver
        {
            private readonly Player _player;
            private readonly ItemDrop.ItemData _item;
            private readonly string _itemKey;
            private readonly int _maxStack;
            private string _text;

            internal TargetPromptReceiver(Player player, ItemDrop.ItemData item, ProtectionRecord protectedRecord)
            {
                _player = player;
                _item = item;
                _itemKey = InventorySnapshots.PersistentItemKey(item);
                _maxStack = item.m_shared.m_maxStackSize;
                _text = (protectedRecord?.TargetQuantity ?? _maxStack).ToString(CultureInfo.InvariantCulture);
            }

            public string GetText() => _text;

            public void SetText(string text)
            {
                _text = text ?? string.Empty;
                int ignoredTarget;
                if (!TryValidateTarget(_text, out ignoredTarget))
                {
                    RuntimeContext.ShowCenter("Stackmaster: enter a target from 1 to " + _maxStack.ToString(CultureInfo.InvariantCulture) + ". Item unchanged.");
                    return;
                }

                var inventory = _player?.GetInventory();
                if (!ReferenceEquals(Player.m_localPlayer, _player) ||
                    inventory == null ||
                    !inventory.ContainsItem(_item) ||
                    !string.Equals(InventorySnapshots.PersistentItemKey(_item), _itemKey, StringComparison.Ordinal))
                {
                    RuntimeContext.ShowCenter("Stackmaster: that item changed while the target prompt was open; item unchanged.");
                    return;
                }

                ProtectionState state;
                if (!RuntimeContext.TryLoadProtection(_player, out state))
                {
                    return;
                }
                InventorySnapshots.ResolveProtection(_player, state);
                var currentSlot = new Slot(_item.m_gridPos.x, _item.m_gridPos.y);
                int target;
                if (!ProtectionInteractionPolicy.TryApplyTarget(
                        state,
                        currentSlot,
                        _itemKey,
                        _maxStack,
                        _text,
                        out target))
                {
                    RuntimeContext.ShowCenter("Stackmaster: enter a target from 1 to " + _maxStack.ToString(CultureInfo.InvariantCulture) + ". Item unchanged.");
                    return;
                }

                RuntimeContext.SaveProtection(_player, state);
                InventoryIntegration.RefreshProtectionOverlays();
                RuntimeContext.ShowTopLeft("Stackmaster: protected with target " + target.ToString(CultureInfo.InvariantCulture) + ".");
            }

            private bool TryValidateTarget(string text, out int target)
            {
                target = 0;
                return int.TryParse((text ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out target) &&
                       target >= 1 &&
                       target <= _maxStack;
            }
        }
    }

    // Pinned Valheim build 25364309 routes InventoryGrid.OnLeftDown through m_onSelected
    // to InventoryGui.OnSelectedItem, while OnRightDown uses the separate m_onRightClick path.
    [HarmonyPatch(typeof(InventoryGui), "OnSelectedItem", typeof(InventoryGrid), typeof(ItemDrop.ItemData), typeof(Vector2i), typeof(InventoryGrid.Modifier))]
    internal static class InventoryProtectionClickPatch
    {
        private static bool Prefix(InventoryGrid grid, ItemDrop.ItemData item, Vector2i pos, InventoryGrid.Modifier mod)
        {
            if (!RuntimeContext.Compatibility.IsCompatible)
            {
                return true;
            }

            var intercepting = false;
            try
            {
                intercepting = ProtectionInteraction.ShouldIntercept(grid, item);
                if (!intercepting)
                {
                    return true;
                }

                ProtectionInteraction.HandleLeftClick(grid, item, pos);
                return false;
            }
            catch (Exception exception)
            {
                RuntimeContext.Plugin?.Log.LogError("Protected-item left-click handling failed safely: " + exception);
                return !intercepting;
            }
        }
    }

    [HarmonyPatch(typeof(InventoryGui), "OnRightClickItem", typeof(InventoryGrid), typeof(ItemDrop.ItemData), typeof(Vector2i))]
    internal static class InventoryProtectionRightClickPatch
    {
        private static bool Prefix(InventoryGrid grid, ItemDrop.ItemData item, Vector2i pos)
        {
            if (!RuntimeContext.Compatibility.IsCompatible)
            {
                return true;
            }

            var intercepting = false;
            try
            {
                intercepting = ProtectionInteraction.ShouldIntercept(grid, item);
                if (!intercepting)
                {
                    return true;
                }

                ProtectionInteraction.HandleRightClick(grid, item, pos);
                return false;
            }
            catch (Exception exception)
            {
                RuntimeContext.Plugin?.Log.LogError("Protected-item right-click handling failed safely: " + exception);
                return !intercepting;
            }
        }
    }
}
