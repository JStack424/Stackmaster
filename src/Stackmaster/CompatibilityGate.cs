#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Stackmaster
{
    internal sealed class CompatibilityResult
    {
        internal CompatibilityResult(bool isCompatible, string reason)
        {
            IsCompatible = isCompatible;
            Reason = reason;
        }

        internal bool IsCompatible { get; }
        internal string Reason { get; }
    }

    internal static class CompatibilityGate
    {
        // Exact assembly supplied by Joe's verified Steam build 25253764 reference bundle.
        private static readonly Guid SupportedValheimMvid = new Guid("b8a6fd30-3061-43b3-99f2-11c2e315bc54");
        private const string SupportedUnityVersion = "6000.0.75f1";

        internal static CompatibilityResult Evaluate()
        {
            var failures = new List<string>();

            if (!string.Equals(Application.unityVersion, SupportedUnityVersion, StringComparison.Ordinal))
            {
                failures.Add("Unity version " + Application.unityVersion + " != " + SupportedUnityVersion);
            }

            var observedMvid = typeof(Player).Assembly.ManifestModule.ModuleVersionId;
            if (observedMvid != SupportedValheimMvid)
            {
                failures.Add("assembly_valheim MVID " + observedMvid + " is not the verified build");
            }

            RequireMethod(failures, typeof(InventoryGui), "Show", typeof(Container), typeof(int));
            RequireMethod(failures, typeof(InventoryGui), "OnSelectedItem", typeof(InventoryGrid), typeof(ItemDrop.ItemData), typeof(Vector2i), typeof(InventoryGrid.Modifier));
            RequireMethod(failures, typeof(Container), "CheckAccess", typeof(long));
            RequireMethod(failures, typeof(Container), "GetInventory");
            RequireMethod(failures, typeof(Container), "IsInUse");
            RequireMethod(failures, typeof(Container), "Interact", typeof(Humanoid), typeof(bool), typeof(bool));
            RequireMethod(failures, typeof(Container), "GetHoverText");
            RequireMethod(failures, typeof(Container), "RPC_RequestOpen", typeof(long), typeof(long));
            RequireMethod(failures, typeof(Container), "RPC_OpenResponse", typeof(long), typeof(bool));
            RequireMethod(failures, typeof(Inventory), "MoveItemToThis", typeof(Inventory), typeof(ItemDrop.ItemData), typeof(int), typeof(int), typeof(int));
            RequireMethod(failures, typeof(Inventory), "GetAllItems");
            RequireMethod(failures, typeof(Inventory), "GetItemAt", typeof(int), typeof(int));
            RequireMethod(failures, typeof(Player), "GetHoverObject");
            RequireMethod(failures, typeof(ZNetView), "IsOwner");
            RequireMethod(failures, typeof(ZNetView), "InvokeRPC", typeof(string), typeof(object[]));
            RequireMethod(failures, typeof(PrivateArea), "CheckAccess", typeof(Vector3), typeof(float), typeof(bool), typeof(bool));
            RequireField(failures, typeof(Container), "m_nview");
            RequireField(failures, typeof(Inventory), "m_onChanged");
            RequireField(failures, typeof(Inventory), "m_inventory");
            RequireField(failures, typeof(Player), "m_customData");
            RequireField(failures, typeof(ItemDrop.ItemData), "m_gridPos");
            RequireField(failures, typeof(ItemDrop.ItemData), "m_stack");
            RequireField(failures, typeof(ItemDrop.ItemData), "m_equipped");

            return failures.Count == 0
                ? new CompatibilityResult(true, "verified runtime surface")
                : new CompatibilityResult(false, string.Join("; ", failures));
        }

        private static void RequireMethod(ICollection<string> failures, Type type, string name, params Type[] parameters)
        {
            if (AccessTools.DeclaredMethod(type, name, parameters) == null && AccessTools.Method(type, name, parameters) == null)
            {
                failures.Add(type.Name + "." + name + "(" + string.Join(",", parameters.Select(parameter => parameter.Name).ToArray()) + ") missing");
            }
        }

        private static void RequireField(ICollection<string> failures, Type type, string name)
        {
            if (AccessTools.Field(type, name) == null)
            {
                failures.Add(type.Name + "." + name + " missing");
            }
        }
    }
}
