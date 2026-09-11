#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using BepInEx;
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
        private const string SupportedValheimSha256 = "27a766a8d23a7bd8b6a54fb9ad0452a96c305fb3629b39c40527c09a1c393a84";
        private const string SupportedGameVersion = "1.0.12";
        private const string SupportedUnityVersion = "6000.0.75f1";
        private const string SupportedBepInExVersion = "5.4.23.5";
        private const string SupportedHarmonyVersion = "2.9.0.0";

        internal static CompatibilityResult Evaluate()
        {
            var failures = new List<string>();

            var observedGameVersion = global::Version.CurrentVersion.ToString();
            if (!string.Equals(observedGameVersion, SupportedGameVersion, StringComparison.Ordinal))
            {
                failures.Add("Valheim API version " + observedGameVersion + " != " + SupportedGameVersion);
            }
            if (!string.Equals(Application.unityVersion, SupportedUnityVersion, StringComparison.Ordinal))
            {
                failures.Add("Unity version " + Application.unityVersion + " != " + SupportedUnityVersion);
            }
            RequireAssemblyVersion(failures, typeof(BaseUnityPlugin).Assembly, SupportedBepInExVersion, "BepInEx");
            RequireAssemblyVersion(failures, typeof(Harmony).Assembly, SupportedHarmonyVersion, "Harmony");

            var valheimAssembly = typeof(Player).Assembly;
            var observedMvid = valheimAssembly.ManifestModule.ModuleVersionId;
            if (observedMvid != SupportedValheimMvid)
            {
                failures.Add("assembly_valheim MVID " + observedMvid + " is not the verified build");
            }
            try
            {
                var observedHash = Sha256(valheimAssembly.Location);
                if (!string.Equals(observedHash, SupportedValheimSha256, StringComparison.Ordinal))
                {
                    failures.Add("assembly_valheim SHA-256 does not match the verified build");
                }
            }
            catch (Exception exception)
            {
                failures.Add("assembly_valheim SHA-256 could not be verified: " + exception.GetType().Name);
            }

            RequireMethod(failures, typeof(InventoryGui), "Awake");
            RequireMethod(failures, typeof(InventoryGui), "Hide");
            RequireMethod(failures, typeof(InventoryGui), "Show", typeof(Container), typeof(int));
            RequireMethod(failures, typeof(InventoryGui), "OnSelectedItem", typeof(InventoryGrid), typeof(ItemDrop.ItemData), typeof(Vector2i), typeof(InventoryGrid.Modifier));
            RequireMethod(failures, typeof(Container), "CheckAccess", typeof(long));
            RequireMethod(failures, typeof(Container), "CheckForChanges");
            RequireMethod(failures, typeof(Container), "GetInventory");
            RequireMethod(failures, typeof(Container), "IsInUse");
            RequireMethod(failures, typeof(Container), "Interact", typeof(Humanoid), typeof(bool), typeof(bool));
            RequireMethod(failures, typeof(Container), "GetHoverText");
            RequireMethod(failures, typeof(Container), "StackAll");
            RequireMethod(failures, typeof(Container), "RPC_RequestStack", typeof(long), typeof(long));
            RequireMethod(failures, typeof(Container), "RPC_StackResponse", typeof(long), typeof(bool));
            RequireMethod(failures, typeof(Inventory), "MoveItemToThis", typeof(Inventory), typeof(ItemDrop.ItemData), typeof(int), typeof(int), typeof(int));
            RequireMethod(failures, typeof(Inventory), "GetAllItems");
            RequireMethod(failures, typeof(Inventory), "GetItemAt", typeof(int), typeof(int));
            RequireMethod(failures, typeof(Player), "GetHoverObject");
            RequireMethod(failures, typeof(ZNetView), "IsOwner");
            RequireMethod(failures, typeof(ZNetView), "InvokeRPC", typeof(string), typeof(object[]));
            RequireMethod(failures, typeof(PrivateArea), "CheckAccess", typeof(Vector3), typeof(float), typeof(bool), typeof(bool));
            RequireField(failures, typeof(InventoryGui), "m_pvp");
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

        private static void RequireAssemblyVersion(ICollection<string> failures, Assembly assembly, string expected, string name)
        {
            var observed = assembly.GetName().Version?.ToString() ?? "unknown";
            if (!string.Equals(observed, expected, StringComparison.Ordinal))
            {
                failures.Add(name + " version " + observed + " != " + expected);
            }
        }

        private static string Sha256(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var algorithm = SHA256.Create())
            {
                var hash = algorithm.ComputeHash(stream);
                var text = new StringBuilder(hash.Length * 2);
                foreach (var value in hash) text.Append(value.ToString("x2"));
                return text.ToString();
            }
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
