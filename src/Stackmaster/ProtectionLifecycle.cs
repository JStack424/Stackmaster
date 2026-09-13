#nullable disable
using System;
using System.Reflection;
using HarmonyLib;
using Stackmaster.Core;

namespace Stackmaster
{
    internal sealed class ProtectionExitAttempt
    {
        internal ProtectionExitAttempt(
            Player player,
            Inventory sourceInventory,
            ItemDrop.ItemData protectedItem,
            ProtectionState protection,
            ProtectionRecord record)
        {
            Player = player;
            SourceInventory = sourceInventory;
            ProtectedItem = protectedItem;
            Protection = protection;
            Record = record;
        }

        internal Player Player { get; }
        internal Inventory SourceInventory { get; }
        internal ItemDrop.ItemData ProtectedItem { get; }
        internal ProtectionState Protection { get; }
        internal ProtectionRecord Record { get; }
    }

    /// <summary>
    /// Watches only completed manual inventory-UI exits. Internal player-inventory drags are not
    /// candidates, and a partial or failed external move keeps the original ItemData in the player
    /// inventory. That exact postcondition is what distinguishes a confirmed full exit from a
    /// transient cursor drag without guessing from slots or compatible duplicate stacks.
    /// </summary>
    internal static class ProtectionLifecycle
    {
        private static readonly FieldInfo DragInventoryField = AccessTools.Field(typeof(InventoryGui), "m_dragInventory");
        private static readonly FieldInfo DragItemField = AccessTools.Field(typeof(InventoryGui), "m_dragItem");

        internal static ProtectionExitAttempt CaptureSelection(
            InventoryGui gui,
            InventoryGrid grid,
            ItemDrop.ItemData selectedItem,
            InventoryGrid.Modifier modifier)
        {
            if (!RuntimeContext.Compatibility.IsCompatible || gui == null || grid == null)
            {
                return null;
            }

            var player = Player.m_localPlayer;
            var playerInventory = player?.GetInventory();
            if (playerInventory == null)
            {
                return null;
            }

            var dragInventory = DragInventoryField?.GetValue(gui) as Inventory;
            var dragItem = DragItemField?.GetValue(gui) as ItemDrop.ItemData;
            if (dragInventory != null && dragItem != null)
            {
                var destination = grid.GetInventory();
                return ReferenceEquals(dragInventory, playerInventory) &&
                       destination != null &&
                       !ReferenceEquals(destination, playerInventory)
                    ? Capture(player, playerInventory, dragItem)
                    : null;
            }

            var source = grid.GetInventory();
            var exitsThroughModifier = modifier == InventoryGrid.Modifier.Move ||
                                       modifier == InventoryGrid.Modifier.Drop;
            return exitsThroughModifier &&
                   ReferenceEquals(source, playerInventory) &&
                   selectedItem != null
                ? Capture(player, playerInventory, selectedItem)
                : null;
        }

        internal static ProtectionExitAttempt CaptureDropOutside(InventoryGui gui)
        {
            if (!RuntimeContext.Compatibility.IsCompatible || gui == null)
            {
                return null;
            }

            var player = Player.m_localPlayer;
            var playerInventory = player?.GetInventory();
            var dragInventory = DragInventoryField?.GetValue(gui) as Inventory;
            var dragItem = DragItemField?.GetValue(gui) as ItemDrop.ItemData;
            return playerInventory != null &&
                   ReferenceEquals(dragInventory, playerInventory) &&
                   dragItem != null
                ? Capture(player, playerInventory, dragItem)
                : null;
        }

        internal static void Complete(ProtectionExitAttempt attempt)
        {
            if (attempt == null || !RuntimeContext.Compatibility.IsCompatible)
            {
                return;
            }

            var stillPresent = attempt.SourceInventory.ContainsItem(attempt.ProtectedItem);
            if (!ProtectionExitPolicy.ShouldClear(
                    wasProtected: true,
                    destinationWasExternal: true,
                    sourceStillContainsProtectedItem: stillPresent))
            {
                // A failed or partial external move leaves the original stack in the player
                // inventory. Reconcile in case vanilla changed its slot while completing a swap.
                InventorySnapshots.ResolveProtection(attempt.Player, attempt.Protection);
                return;
            }

            if (!attempt.Protection.Unprotect(attempt.Record))
            {
                return;
            }

            RuntimeContext.SaveProtection(attempt.Player, attempt.Protection);
            InventoryIntegration.RefreshProtectionOverlays();
        }

        private static ProtectionExitAttempt Capture(
            Player player,
            Inventory playerInventory,
            ItemDrop.ItemData item)
        {
            if (!playerInventory.ContainsItem(item))
            {
                return null;
            }

            ProtectionState protection;
            if (!RuntimeContext.TryLoadProtection(player, out protection))
            {
                return null;
            }

            var resolution = InventorySnapshots.ResolveProtection(player, protection);
            ProtectionRecord record;
            var slot = new Slot(item.m_gridPos.x, item.m_gridPos.y);
            if (!resolution.TryGet(slot, out record) ||
                !string.Equals(record.TargetItemKey, InventorySnapshots.PersistentItemKey(item), StringComparison.Ordinal))
            {
                return null;
            }

            return new ProtectionExitAttempt(player, playerInventory, item, protection, record);
        }
    }

    internal static class ManualProtectionExitSelectionPatch
    {
        private static void Prefix(
            InventoryGui __instance,
            [HarmonyArgument(0)] InventoryGrid grid,
            [HarmonyArgument(1)] ItemDrop.ItemData item,
            [HarmonyArgument(3)] InventoryGrid.Modifier mod,
            out ProtectionExitAttempt __state)
        {
            __state = null;
            try
            {
                __state = ProtectionLifecycle.CaptureSelection(__instance, grid, item, mod);
            }
            catch (Exception exception)
            {
                RuntimeContext.Plugin?.Log.LogError("Protected-item exit observation failed safely before selection: " + exception);
            }
        }

        private static void Postfix(ProtectionExitAttempt __state)
        {
            try
            {
                ProtectionLifecycle.Complete(__state);
            }
            catch (Exception exception)
            {
                RuntimeContext.Plugin?.Log.LogError("Protected-item exit cleanup failed safely after selection: " + exception);
            }
        }
    }

    internal static class ManualProtectionExitDropOutsidePatch
    {
        private static void Prefix(InventoryGui __instance, out ProtectionExitAttempt __state)
        {
            __state = null;
            try
            {
                __state = ProtectionLifecycle.CaptureDropOutside(__instance);
            }
            catch (Exception exception)
            {
                RuntimeContext.Plugin?.Log.LogError("Protected-item exit observation failed safely before world drop: " + exception);
            }
        }

        private static void Postfix(ProtectionExitAttempt __state)
        {
            try
            {
                ProtectionLifecycle.Complete(__state);
            }
            catch (Exception exception)
            {
                RuntimeContext.Plugin?.Log.LogError("Protected-item exit cleanup failed safely after world drop: " + exception);
            }
        }
    }
}
