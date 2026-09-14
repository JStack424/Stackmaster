#nullable disable
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace Stackmaster
{
    internal static class PatchInstaller
    {
        internal static void Install(Harmony harmony)
        {
            if (harmony == null) throw new ArgumentNullException(nameof(harmony));

            // Resolve the complete exact target set before installing the first patch.
            // A missing or ambiguous method therefore fails without leaving partial hooks.
            var patches = new List<PatchSpec>
            {
                Postfix(typeof(InventoryGui), "Awake", Type.EmptyTypes, typeof(InventoryGuiAwakePatch)),
                Both(typeof(InventoryGui), "Hide", Type.EmptyTypes, typeof(InventoryGuiHidePatch)),
                Postfix(typeof(InventoryGui), "Show", new[] { typeof(Container), typeof(int) }, typeof(InventoryGuiShowPatch)),
                Prefix(typeof(InventoryGui), "Update", Type.EmptyTypes, typeof(InventoryGuiStorageActionPatch)),
                Postfix(typeof(InventoryGrid), "UpdateInventory", new[] { typeof(Inventory), typeof(Player), typeof(ItemDrop.ItemData) }, typeof(InventoryGridUpdateInventoryPatch)),
                Prefix(typeof(InventoryGui), "OnSelectedItem", new[]
                {
                    typeof(InventoryGrid), typeof(ItemDrop.ItemData), typeof(Vector2i), typeof(InventoryGrid.Modifier)
                }, typeof(InventoryProtectionClickPatch)),
                Prefix(typeof(InventoryGui), "OnRightClickItem", new[]
                {
                    typeof(InventoryGrid), typeof(ItemDrop.ItemData), typeof(Vector2i)
                }, typeof(InventoryProtectionRightClickPatch)),
                Both(typeof(InventoryGui), "OnSelectedItem", new[]
                {
                    typeof(InventoryGrid), typeof(ItemDrop.ItemData), typeof(Vector2i), typeof(InventoryGrid.Modifier)
                }, typeof(ManualProtectionExitSelectionPatch)),
                Both(typeof(InventoryGui), "OnDropOutside", Type.EmptyTypes, typeof(ManualProtectionExitDropOutsidePatch)),
                Prefix(typeof(Container), "Interact", new[] { typeof(Humanoid), typeof(bool), typeof(bool) }, typeof(ContainerInteractPatch)),
                Postfix(typeof(Container), "GetHoverText", Type.EmptyTypes, typeof(ContainerHoverTextPatch)),
                Postfix(typeof(Container), "RPC_RequestOpen", new[] { typeof(long), typeof(long) }, typeof(ContainerOpenRequestLeasePatch)),
                Prefix(typeof(Container), "RPC_StackResponse", new[] { typeof(long), typeof(bool) }, typeof(ContainerStackResponsePatch)),
                Prefix(typeof(Game), "Shutdown", new[] { typeof(bool) }, typeof(OwnershipLifecyclePatch)),
                Prefix(typeof(ZNet), "Shutdown", new[] { typeof(bool) }, typeof(OwnershipLifecyclePatch)),
                Prefix(typeof(ZNet), "ShutdownWithoutSave", new[] { typeof(bool) }, typeof(OwnershipLifecyclePatch)),
                Postfix(typeof(ZNet), "Update", Type.EmptyTypes, typeof(OwnershipSafetyUpdatePatch)),
                Postfix(typeof(Player), "HaveRequirementItems", new[] { typeof(Recipe), typeof(bool), typeof(int), typeof(int) }, typeof(NearbyRequirementPatches), "RecipePostfix"),
                Postfix(typeof(Player), "HaveRequirements", new[] { typeof(Piece), typeof(Player.RequirementMode) }, typeof(NearbyRequirementPatches), "PiecePostfix"),
                Postfix(typeof(Hud), "SetupPieceInfo", new[] { typeof(Piece) }, typeof(NearbyBuildHudPatch)),
                Prefix(typeof(BuildUi), "OnSelectPiece", new[] { typeof(Piece) }, typeof(ExpeditionKitClickPatch)),
                Both(typeof(InventoryGui), "SetupRequirement", new[]
                {
                    typeof(UnityEngine.Transform), typeof(Piece.Requirement), typeof(Player), typeof(bool), typeof(int), typeof(int)
                }, typeof(NearbyCraftingHudPatch)),
                Postfix(typeof(Player), "GetFirstRequiredItem", new[]
                {
                    typeof(Inventory), typeof(Recipe), typeof(int), typeof(int).MakeByRefType(), typeof(int).MakeByRefType(), typeof(int)
                }, typeof(NearbyFirstRequiredItemPatch)),
                Transactional(typeof(InventoryGui), "DoCrafting", new[] { typeof(Player) }, typeof(NearbyCraftingActionPatch)),
                Transactional(typeof(Player), "UpdatePlacement", new[] { typeof(bool), typeof(float) }, typeof(NearbyBuildingActionPatch)),
                Both(typeof(Player), "TryPlacePiece", new[] { typeof(Piece) }, typeof(NearbyTryPlacePiecePatch)),
                Prefix(typeof(Inventory), "RemoveItem", new[] { typeof(string), typeof(int), typeof(int), typeof(bool) }, typeof(NearbyResourceRemovalPatch))
            };

            foreach (var patch in patches)
            {
                harmony.Patch(
                    patch.Original,
                    prefix: patch.Prefix,
                    postfix: patch.Postfix,
                    transpiler: null,
                    finalizer: patch.Finalizer,
                    ilmanipulator: null);
            }
        }

        private static PatchSpec Prefix(Type targetType, string targetName, Type[] parameters, Type patchType)
            => new PatchSpec(ResolveTarget(targetType, targetName, parameters), ResolvePatch(patchType, "Prefix"), null, null);

        private static PatchSpec Postfix(Type targetType, string targetName, Type[] parameters, Type patchType, string patchName = "Postfix")
            => new PatchSpec(ResolveTarget(targetType, targetName, parameters), null, ResolvePatch(patchType, patchName), null);

        private static PatchSpec Both(Type targetType, string targetName, Type[] parameters, Type patchType)
            => new PatchSpec(
                ResolveTarget(targetType, targetName, parameters),
                ResolvePatch(patchType, "Prefix"),
                ResolvePatch(patchType, "Postfix"),
                null);

        private static PatchSpec Transactional(Type targetType, string targetName, Type[] parameters, Type patchType)
            => new PatchSpec(
                ResolveTarget(targetType, targetName, parameters),
                ResolvePatch(patchType, "Prefix"),
                ResolvePatch(patchType, "Postfix"),
                ResolvePatch(patchType, "Finalizer"));

        private static MethodInfo ResolveTarget(Type type, string name, Type[] parameters)
        {
            var method = AccessTools.DeclaredMethod(type, name, parameters);
            if (method == null)
            {
                throw new MissingMethodException(type.FullName, name);
            }
            return method;
        }

        private static HarmonyMethod ResolvePatch(Type type, string name)
        {
            var method = AccessTools.DeclaredMethod(type, name);
            if (method == null)
            {
                throw new MissingMethodException(type.FullName, name);
            }
            return new HarmonyMethod(method);
        }

        private sealed class PatchSpec
        {
            internal PatchSpec(MethodInfo original, HarmonyMethod prefix, HarmonyMethod postfix, HarmonyMethod finalizer)
            {
                Original = original;
                Prefix = prefix;
                Postfix = postfix;
                Finalizer = finalizer;
            }

            internal MethodInfo Original { get; }
            internal HarmonyMethod Prefix { get; }
            internal HarmonyMethod Postfix { get; }
            internal HarmonyMethod Finalizer { get; }
        }
    }
}
