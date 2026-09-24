#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Stackmaster
{
    /// <summary>
    /// The single source of truth for every Harmony hook. Compatibility validation and
    /// patch installation both resolve these exact descriptors, so they cannot disagree
    /// about declaring type, overload, static/instance shape, return type, or patch methods.
    /// </summary>
    internal static class HarmonyTargetManifest
    {
        private static readonly IReadOnlyList<HarmonyTargetDescriptor> Targets = new[]
        {
            Postfix(typeof(InventoryGui), "Awake", false, typeof(void), Type.EmptyTypes, typeof(InventoryGuiAwakePatch)),
            Both(typeof(InventoryGui), "Hide", false, typeof(void), Type.EmptyTypes, typeof(InventoryGuiHidePatch)),
            Postfix(typeof(InventoryGui), "Show", false, typeof(void), new[] { typeof(Container), typeof(int) }, typeof(InventoryGuiShowPatch)),
            Both(typeof(InventoryGui), "Update", false, typeof(void), Type.EmptyTypes, typeof(InventoryGuiStorageActionPatch)),
            Transactional(typeof(InventoryGui), "OnCraftPressed", false, typeof(void), Type.EmptyTypes, typeof(CraftingStartPatch)),
            Prefix(typeof(InventoryGui), "OnCraftCancelPressed", false, typeof(void), Type.EmptyTypes, typeof(CraftingCancelPatch)),
            Prefix(typeof(InventoryGui), "OnTabCraftPressed", false, typeof(void), Type.EmptyTypes, typeof(CraftingSelectionPatch)),
            Prefix(typeof(InventoryGui), "OnTabUpgradePressed", false, typeof(void), Type.EmptyTypes, typeof(CraftingSelectionPatch)),
            Prefix(typeof(InventoryGui), "OnSelectedRecipe", false, typeof(void), new[] { typeof(GameObject) }, typeof(CraftingSelectionPatch)),
            Postfix(typeof(InventoryGrid), "UpdateInventory", false, typeof(void), new[] { typeof(Inventory), typeof(Player), typeof(ItemDrop.ItemData) }, typeof(InventoryGridUpdateInventoryPatch)),
            Prefix(typeof(InventoryGui), "OnSelectedItem", false, typeof(void), new[]
            {
                typeof(InventoryGrid), typeof(ItemDrop.ItemData), typeof(Vector2i), typeof(InventoryGrid.Modifier)
            }, typeof(InventoryProtectionClickPatch)),
            Prefix(typeof(InventoryGui), "OnRightClickItem", false, typeof(void), new[]
            {
                typeof(InventoryGrid), typeof(ItemDrop.ItemData), typeof(Vector2i)
            }, typeof(InventoryProtectionRightClickPatch)),
            Both(typeof(InventoryGui), "OnSelectedItem", false, typeof(void), new[]
            {
                typeof(InventoryGrid), typeof(ItemDrop.ItemData), typeof(Vector2i), typeof(InventoryGrid.Modifier)
            }, typeof(ManualProtectionExitSelectionPatch)),
            Both(typeof(InventoryGui), "OnDropOutside", false, typeof(void), Type.EmptyTypes, typeof(ManualProtectionExitDropOutsidePatch)),
            Prefix(typeof(Container), "Interact", false, typeof(bool), new[] { typeof(Humanoid), typeof(bool), typeof(bool) }, typeof(ContainerInteractPatch)),
            Postfix(typeof(Container), "GetHoverText", false, typeof(string), Type.EmptyTypes, typeof(ContainerHoverTextPatch)),
            Postfix(typeof(Container), "RPC_RequestOpen", false, typeof(void), new[] { typeof(long), typeof(long) }, typeof(ContainerOpenRequestLeasePatch)),
            Prefix(typeof(Container), "RPC_StackResponse", false, typeof(void), new[] { typeof(long), typeof(bool) }, typeof(ContainerStackResponsePatch), preserveDuringCleanup: true),
            Prefix(typeof(Game), "Shutdown", false, typeof(void), new[] { typeof(bool) }, typeof(OwnershipLifecyclePatch)),
            Prefix(typeof(ZNet), "Shutdown", false, typeof(void), new[] { typeof(bool) }, typeof(OwnershipLifecyclePatch)),
            Prefix(typeof(ZNet), "ShutdownWithoutSave", false, typeof(void), new[] { typeof(bool) }, typeof(OwnershipLifecyclePatch)),
            Postfix(typeof(ZNet), "Update", false, typeof(void), Type.EmptyTypes, typeof(OwnershipSafetyUpdatePatch), preserveDuringCleanup: true),
            Postfix(typeof(Player), "HaveRequirementItems", false, typeof(bool), new[] { typeof(Recipe), typeof(bool), typeof(int), typeof(int) }, typeof(NearbyRequirementPatches), "RecipePostfix"),
            Postfix(typeof(Player), "HaveRequirements", false, typeof(bool), new[] { typeof(Piece), typeof(Player.RequirementMode) }, typeof(NearbyRequirementPatches), "PiecePostfix"),
            Postfix(typeof(Hud), "SetupPieceInfo", false, typeof(void), new[] { typeof(Piece) }, typeof(NearbyBuildHudPatch)),
            Prefix(typeof(BuildUi), "OnSelectPiece", false, typeof(void), new[] { typeof(Piece) }, typeof(QuickGrabMaterialsClickPatch)),
            Postfix(typeof(KeyHints), "Update", false, typeof(void), Type.EmptyTypes, typeof(QuickGrabMaterialsBuildHintPatch)),
            Both(typeof(InventoryGui), "SetupRequirement", true, typeof(bool), new[]
            {
                typeof(Transform), typeof(Piece.Requirement), typeof(Player), typeof(bool), typeof(int), typeof(int)
            }, typeof(NearbyCraftingHudPatch)),
            Postfix(typeof(Player), "GetFirstRequiredItem", false, typeof(ItemDrop.ItemData), new[]
            {
                typeof(Inventory), typeof(Recipe), typeof(int), typeof(int).MakeByRefType(), typeof(int).MakeByRefType(), typeof(int)
            }, typeof(NearbyFirstRequiredItemPatch)),
            Transactional(typeof(InventoryGui), "DoCrafting", false, typeof(void), new[] { typeof(Player) }, typeof(NearbyCraftingActionPatch)),
            Transactional(typeof(Player), "UpdatePlacement", false, typeof(void), new[] { typeof(bool), typeof(float) }, typeof(NearbyBuildingActionPatch)),
            Both(typeof(Player), "TryPlacePiece", false, typeof(bool), new[] { typeof(Piece) }, typeof(NearbyTryPlacePiecePatch)),
            Prefix(typeof(Inventory), "RemoveItem", false, typeof(void), new[] { typeof(string), typeof(int), typeof(int), typeof(bool) }, typeof(NearbyResourceRemovalPatch))
        };

        internal static IReadOnlyList<HarmonyTargetDescriptor> Descriptors => Targets;

        internal static IReadOnlyList<PatchInstaller.PatchSpec> ResolveAll()
        {
            return Resolve(Targets);
        }

        internal static IReadOnlyList<PatchInstaller.PatchSpec> ResolveCleanupSafety()
        {
            return Resolve(Targets.Where(target => target.PreserveDuringCleanup).ToArray());
        }

        private static IReadOnlyList<PatchInstaller.PatchSpec> Resolve(IReadOnlyList<HarmonyTargetDescriptor> targets)
        {
            var resolved = new List<PatchInstaller.PatchSpec>(targets.Count);
            foreach (var target in targets) resolved.Add(target.Resolve());
            return resolved;
        }

        internal static void Validate(ICollection<string> failures)
        {
            try
            {
                ResolveAll();
            }
            catch (Exception exception)
            {
                failures.Add("Harmony target manifest failed: " + exception.Message);
            }
        }

        private static HarmonyTargetDescriptor Prefix(Type targetType, string targetName, bool isStatic, Type returnType, Type[] parameters, Type patchType, bool preserveDuringCleanup = false)
            => new HarmonyTargetDescriptor(targetType, targetName, isStatic, returnType, parameters, patchType, "Prefix", null, null, preserveDuringCleanup);

        private static HarmonyTargetDescriptor Postfix(Type targetType, string targetName, bool isStatic, Type returnType, Type[] parameters, Type patchType, string patchName = "Postfix", bool preserveDuringCleanup = false)
            => new HarmonyTargetDescriptor(targetType, targetName, isStatic, returnType, parameters, patchType, null, patchName, null, preserveDuringCleanup);

        private static HarmonyTargetDescriptor Both(Type targetType, string targetName, bool isStatic, Type returnType, Type[] parameters, Type patchType)
            => new HarmonyTargetDescriptor(targetType, targetName, isStatic, returnType, parameters, patchType, "Prefix", "Postfix", null, false);

        private static HarmonyTargetDescriptor Transactional(Type targetType, string targetName, bool isStatic, Type returnType, Type[] parameters, Type patchType)
            => new HarmonyTargetDescriptor(targetType, targetName, isStatic, returnType, parameters, patchType, "Prefix", "Postfix", "Finalizer", false);
    }

    internal sealed class HarmonyTargetDescriptor
    {
        internal HarmonyTargetDescriptor(
            Type targetType,
            string targetName,
            bool isStatic,
            Type returnType,
            Type[] parameters,
            Type patchType,
            string prefixName,
            string postfixName,
            string finalizerName,
            bool preserveDuringCleanup = false)
        {
            TargetType = targetType;
            TargetName = targetName;
            IsStatic = isStatic;
            ReturnType = returnType;
            Parameters = parameters;
            PatchType = patchType;
            PrefixName = prefixName;
            PostfixName = postfixName;
            FinalizerName = finalizerName;
            PreserveDuringCleanup = preserveDuringCleanup;
        }

        internal Type TargetType { get; }
        internal string TargetName { get; }
        internal bool IsStatic { get; }
        internal Type ReturnType { get; }
        internal Type[] Parameters { get; }
        internal Type PatchType { get; }
        internal string PrefixName { get; }
        internal string PostfixName { get; }
        internal string FinalizerName { get; }
        internal bool PreserveDuringCleanup { get; }

        internal string Identity => TargetType.FullName + "." + TargetName + "(" +
                                    string.Join(",", Parameters.Select(type => type.FullName).ToArray()) + ")";

        internal PatchInstaller.PatchSpec Resolve()
        {
            var original = RuntimeContractValidator.ResolveExactMethod(
                TargetType, TargetName, IsStatic, true, ReturnType, Parameters);
            var prefix = ResolvePatch(PrefixName);
            var postfix = ResolvePatch(PostfixName);
            var finalizer = ResolvePatch(FinalizerName);
            HarmonyPatchCompatibility.Validate(original, prefix, postfix, finalizer);
            return new PatchInstaller.PatchSpec(original, prefix, postfix, finalizer);
        }

        private MethodInfo ResolvePatch(string name)
        {
            return string.IsNullOrEmpty(name)
                ? null
                : RuntimeContractValidator.ResolveUniqueNamedMethod(PatchType, name, true);
        }
    }

    internal static class HarmonyPatchCompatibility
    {
        internal static void Validate(MethodInfo original, MethodInfo prefix, MethodInfo postfix, MethodInfo finalizer)
        {
            ValidatePatch(original, prefix, "prefix");
            ValidatePatch(original, postfix, "postfix");
            ValidatePatch(original, finalizer, "finalizer");
            ValidateReturns(prefix, postfix, finalizer);
            ValidateState(prefix, postfix, finalizer);
        }

        private static void ValidateReturns(MethodInfo prefix, MethodInfo postfix, MethodInfo finalizer)
        {
            if (prefix != null && prefix.ReturnType != typeof(void) && prefix.ReturnType != typeof(bool))
                throw new InvalidOperationException(prefix.DeclaringType.Name + "." + prefix.Name + " has an invalid Harmony prefix return type");
            if (postfix != null && postfix.ReturnType != typeof(void))
                throw new InvalidOperationException(postfix.DeclaringType.Name + "." + postfix.Name + " has an invalid Harmony postfix return type");
            if (finalizer != null && finalizer.ReturnType != typeof(void) && finalizer.ReturnType != typeof(Exception))
                throw new InvalidOperationException(finalizer.DeclaringType.Name + "." + finalizer.Name + " has an invalid Harmony finalizer return type");
        }

        private static void ValidateState(MethodInfo prefix, MethodInfo postfix, MethodInfo finalizer)
        {
            var prefixState = prefix?.GetParameters().FirstOrDefault(parameter => parameter.Name == "__state");
            var consumerStates = new[] { postfix, finalizer }
                .Where(value => value != null)
                .SelectMany(value => value.GetParameters())
                .Where(parameter => parameter.Name == "__state")
                .ToArray();
            if (prefixState == null)
            {
                if (consumerStates.Length != 0)
                    throw new InvalidOperationException("Harmony __state is consumed without a prefix producer");
                return;
            }
            if (!prefixState.ParameterType.IsByRef)
                throw new InvalidOperationException(prefix.DeclaringType.Name + "." + prefix.Name + " must produce Harmony __state by ref or out");

            var stateType = ElementType(prefixState.ParameterType);
            foreach (var state in consumerStates)
            {
                if (ElementType(state.ParameterType) != stateType)
                    throw new InvalidOperationException("Harmony __state types disagree for " + state.Member.DeclaringType.Name);
            }
        }

        private static void ValidatePatch(MethodInfo original, MethodInfo patch, string role)
        {
            if (patch == null) return;
            if (!patch.IsStatic)
                throw new InvalidOperationException(patch.DeclaringType.Name + "." + patch.Name + " " + role + " must be static");

            var originalParameters = original.GetParameters();
            foreach (var parameter in patch.GetParameters())
            {
                var name = parameter.Name ?? string.Empty;
                if (name == "__instance")
                {
                    if (original.IsStatic || ElementType(parameter.ParameterType) != original.DeclaringType)
                        throw new InvalidOperationException(patch.DeclaringType.Name + "." + patch.Name + " has incompatible __instance");
                    continue;
                }
                if (name == "__result")
                {
                    if (original.ReturnType == typeof(void) || ElementType(parameter.ParameterType) != original.ReturnType)
                        throw new InvalidOperationException(patch.DeclaringType.Name + "." + patch.Name + " has incompatible __result");
                    continue;
                }
                if (name == "__state") continue;
                if (name == "__runOriginal")
                {
                    if (ElementType(parameter.ParameterType) != typeof(bool))
                        throw new InvalidOperationException(patch.DeclaringType.Name + "." + patch.Name + " has incompatible __runOriginal");
                    continue;
                }
                if (name == "__exception")
                {
                    if (role != "finalizer" || ElementType(parameter.ParameterType) != typeof(Exception))
                        throw new InvalidOperationException(patch.DeclaringType.Name + "." + patch.Name + " has incompatible __exception");
                    continue;
                }
                if (name.StartsWith("___", StringComparison.Ordinal))
                {
                    if (original.IsStatic)
                        throw new InvalidOperationException(patch.DeclaringType.Name + "." + patch.Name + " cannot inject an instance field for a static original");
                    var fieldName = name.Substring(3);
                    var fields = original.DeclaringType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                        .Where(field => field.Name == fieldName).ToArray();
                    if (fields.Length != 1 || ElementType(parameter.ParameterType) != fields[0].FieldType)
                        throw new InvalidOperationException(patch.DeclaringType.Name + "." + patch.Name + " has incompatible field injection " + name);
                    continue;
                }

                var arguments = parameter.GetCustomAttributes(typeof(HarmonyArgument), false)
                    .Cast<HarmonyArgument>().ToArray();
                if (arguments.Length > 1)
                    throw new InvalidOperationException(patch.DeclaringType.Name + "." + patch.Name + " has multiple HarmonyArgument mappings for " + name);
                var argument = arguments.SingleOrDefault();
                ParameterInfo originalParameter;
                if (argument == null)
                {
                    originalParameter = originalParameters.SingleOrDefault(candidate => candidate.Name == name);
                }
                else if (argument.Index >= 0)
                {
                    if (argument.Index >= originalParameters.Length)
                        throw new InvalidOperationException(patch.DeclaringType.Name + "." + patch.Name + " has an out-of-range HarmonyArgument index for " + name);
                    originalParameter = originalParameters[argument.Index];
                }
                else if (!string.IsNullOrEmpty(argument.OriginalName))
                {
                    originalParameter = originalParameters.SingleOrDefault(candidate => candidate.Name == argument.OriginalName);
                }
                else
                {
                    throw new InvalidOperationException(patch.DeclaringType.Name + "." + patch.Name + " has an empty HarmonyArgument mapping for " + name);
                }

                if (originalParameter == null || ElementType(parameter.ParameterType) != ElementType(originalParameter.ParameterType) ||
                    (originalParameter.ParameterType.IsByRef && !parameter.ParameterType.IsByRef))
                    throw new InvalidOperationException(patch.DeclaringType.Name + "." + patch.Name + " cannot bind patch parameter " + name);
            }
        }

        private static Type ElementType(Type type) => type.IsByRef ? type.GetElementType() : type;
    }
}
