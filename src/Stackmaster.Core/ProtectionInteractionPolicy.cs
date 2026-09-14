using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Stackmaster.Core
{
    public enum ProtectionPointerButton
    {
        Left,
        Right
    }

    public enum ProtectionClickRoute
    {
        Vanilla,
        ProtectOnly,
        Unprotect,
        OpenTargetDialog,
        SuppressWithoutChange
    }

    /// <summary>
    /// Pure routing and state transitions for the player-inventory protection controls.
    /// Runtime input adapters supply the configured shortcut-modifier states and keep mouse
    /// button routing separate from Valheim's own inventory Modifier enum.
    /// </summary>
    public static class ProtectionInteractionPolicy
    {
        public static ProtectionClickRoute Route(
            bool compatibilityEnabled,
            bool isPlayerInventory,
            bool hasItem,
            bool isStackable,
            bool isProtected,
            ProtectionPointerButton button,
            IEnumerable<bool> configuredModifierStates)
        {
            if (!compatibilityEnabled || !isPlayerInventory || !hasItem || configuredModifierStates == null)
            {
                return ProtectionClickRoute.Vanilla;
            }

            var modifiers = configuredModifierStates.ToArray();
            if (modifiers.Length == 0 || modifiers.Any(pressed => !pressed))
            {
                return ProtectionClickRoute.Vanilla;
            }

            if (button == ProtectionPointerButton.Left)
            {
                return isProtected ? ProtectionClickRoute.Unprotect : ProtectionClickRoute.ProtectOnly;
            }

            return isStackable
                ? ProtectionClickRoute.OpenTargetDialog
                : ProtectionClickRoute.SuppressWithoutChange;
        }

        public static void ApplyLeftClick(
            ProtectionState state,
            ProtectionRecord existingRecord,
            Slot slot,
            string itemKey)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (existingRecord != null)
            {
                state.Unprotect(existingRecord);
                return;
            }

            state.Protect(slot, null, itemKey);
        }

        public static bool TryApplyTarget(
            ProtectionState state,
            Slot slot,
            string itemKey,
            int maximumStackSize,
            string text,
            out int target)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            target = 0;
            if (maximumStackSize <= 1 ||
                !int.TryParse((text ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out target) ||
                target < 1 ||
                target > maximumStackSize)
            {
                return false;
            }

            state.Protect(slot, target, itemKey);
            return true;
        }
    }
}
