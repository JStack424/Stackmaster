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
                Postfix(typeof(InventoryGui), "Hide", Type.EmptyTypes, typeof(InventoryGuiHidePatch)),
                Postfix(typeof(InventoryGui), "Update", Type.EmptyTypes, typeof(InventoryGuiUpdatePatch)),
                Postfix(typeof(InventoryGui), "Show", new[] { typeof(Container), typeof(int) }, typeof(InventoryGuiShowPatch)),
                Prefix(typeof(InventoryGui), "OnSelectedItem", new[]
                {
                    typeof(InventoryGrid), typeof(ItemDrop.ItemData), typeof(Vector2i), typeof(InventoryGrid.Modifier)
                }, typeof(InventoryProtectionClickPatch)),
                Prefix(typeof(Container), "Interact", new[] { typeof(Humanoid), typeof(bool), typeof(bool) }, typeof(ContainerInteractPatch)),
                Postfix(typeof(Container), "GetHoverText", Type.EmptyTypes, typeof(ContainerHoverTextPatch)),
                Prefix(typeof(Container), "RPC_StackResponse", new[] { typeof(long), typeof(bool) }, typeof(ContainerStackResponsePatch))
            };

            foreach (var patch in patches)
            {
                harmony.Patch(patch.Original, patch.Prefix, patch.Postfix);
            }
        }

        private static PatchSpec Prefix(Type targetType, string targetName, Type[] parameters, Type patchType)
            => new PatchSpec(ResolveTarget(targetType, targetName, parameters), ResolvePatch(patchType, "Prefix"), null);

        private static PatchSpec Postfix(Type targetType, string targetName, Type[] parameters, Type patchType)
            => new PatchSpec(ResolveTarget(targetType, targetName, parameters), null, ResolvePatch(patchType, "Postfix"));

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
            internal PatchSpec(MethodInfo original, HarmonyMethod prefix, HarmonyMethod postfix)
            {
                Original = original;
                Prefix = prefix;
                Postfix = postfix;
            }

            internal MethodInfo Original { get; }
            internal HarmonyMethod Prefix { get; }
            internal HarmonyMethod Postfix { get; }
        }
    }
}
