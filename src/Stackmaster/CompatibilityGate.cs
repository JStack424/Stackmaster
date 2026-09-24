#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Stackmaster
{
    internal sealed class CompatibilityResult
    {
        internal CompatibilityResult(bool isCompatible, string reason, string diagnostics = "")
        {
            IsCompatible = isCompatible;
            Reason = reason;
            Diagnostics = diagnostics;
        }

        internal bool IsCompatible { get; }
        internal string Reason { get; }
        internal string Diagnostics { get; }
    }

    internal static class CompatibilityGate
    {
        internal static CompatibilityResult Evaluate()
        {
            var failures = new List<string>();

            // Runtime identity is deliberately diagnostic only. Valheim's client and
            // dedicated-server assemblies can expose the same contract with different
            // labels, versions, file hashes, and MVIDs.
            var diagnostics = DescribeRuntimeIdentity();

            var requiredTypes = new[]
            {
                typeof(InventoryGui), typeof(Container), typeof(InventoryGrid),
                typeof(ItemDrop.ItemData), typeof(Vector2i), typeof(InventoryGrid.Modifier),
                typeof(GameObject), typeof(Transform), typeof(Piece.Requirement), typeof(Player),
                typeof(Hud), typeof(BuildUi), typeof(ZDOID), typeof(ZDO), typeof(Inventory),
                typeof(ZPackage), typeof(ZNetView), typeof(Humanoid), typeof(Game), typeof(ZNet),
                typeof(ZDOMan), typeof(Character), typeof(TextViewer), typeof(GameCamera),
                typeof(PlayerPrefs), typeof(Recipe), typeof(Player.RequirementMode), typeof(ZInput),
                typeof(SplitDialog), typeof(CraftingStation), typeof(ZNetScene), typeof(PrivateArea),
                typeof(Vector3), typeof(ZDOVars), typeof(TextInput), typeof(KeyHints),
                typeof(Localization), typeof(UnityEngine.UI.Text)
            };
            foreach (var type in requiredTypes) RuntimeContractValidator.RequireType(failures, type);

            HarmonyTargetManifest.Validate(failures);

            RequireMethod(failures, typeof(InventoryGui), "IsContainerOpen");
            RequireMethod(failures, typeof(InventoryGui), "UpdateRecipe", typeof(Player), typeof(float));
            RequireStaticMethod(failures, typeof(InventoryGui), "get_instance");
            RequireMethod(failures, typeof(Container), "CheckAccess", typeof(long));
            RequireMethod(failures, typeof(Container), "CheckForChanges");
            RequireMethod(failures, typeof(Container), "GetInventory");
            RequireMethod(failures, typeof(ZDOID), "IsNone");
            RequireMethod(failures, typeof(ZDO), "GetByteArray", typeof(int), typeof(byte[]));
            RequireMethod(failures, typeof(ZDO), "get_DataRevision");
            RequireMethod(failures, typeof(ZDO), "get_OwnerRevision");
            RequireMethod(failures, typeof(Inventory), "Load", typeof(ZPackage));
            RequireConstructor(failures, typeof(Inventory), typeof(bool));
            RequireConstructor(failures, typeof(ZPackage), typeof(byte[]));
            RequireMethod(failures, typeof(ZNetView), "HasOwner");
            RequireMethod(failures, typeof(Container), "IsOwner");
            RequireMethod(failures, typeof(Container), "IsInUse");
            RequireMethod(failures, typeof(Container), "SetInUse", typeof(bool));
            RequireMethod(failures, typeof(Container), "StackAll");
            RequireMethod(failures, typeof(Container), "RPC_RequestStack", typeof(long), typeof(long));
            RequireMethod(failures, typeof(ZDO), "GetOwner");
            RequireMethod(failures, typeof(ZDO), "SetOwner", typeof(long));
            RequireStaticMethod(failures, typeof(ZDOMan), "GetSessionID");
            RequireMethod(failures, typeof(ZDOMan), "GetZDO", typeof(ZDOID));
            RequireMethod(failures, typeof(ZDOMan), "ForceSendZDO", typeof(ZDOID));
            RequireMethod(failures, typeof(Character), "IsDead");
            RequireMethod(failures, typeof(Character), "InCutscene");
            RequireMethod(failures, typeof(Character), "IsTeleporting");
            RequireStaticMethod(failures, typeof(TextViewer), "get_instance");
            RequireMethod(failures, typeof(TextViewer), "IsVisible");
            RequireStaticMethod(failures, typeof(GameCamera), "InFreeFly");
            RequireMethod(failures, typeof(Inventory), "MoveItemToThis", typeof(Inventory), typeof(ItemDrop.ItemData), typeof(int), typeof(int), typeof(int));
            RequireMethod(failures, typeof(Inventory), "GetWidth");
            RequireMethod(failures, typeof(Inventory), "GetHeight");
            RequireMethod(failures, typeof(Inventory), "GetTotalWeight");
            RequireMethod(failures, typeof(Inventory), "ContainsItem", typeof(ItemDrop.ItemData));
            RequireMethod(failures, typeof(Inventory), "GetAllItems");
            RequireMethod(failures, typeof(Inventory), "GetItemAt", typeof(int), typeof(int));
            RequireMethod(failures, typeof(Inventory), "RemoveAll");
            RequireMethod(failures, typeof(Inventory), "CountItems", typeof(string), typeof(int), typeof(bool));
            RequireMethod(failures, typeof(Inventory), "RemoveItem", typeof(ItemDrop.ItemData), typeof(int));
            RequireMethod(failures, typeof(Inventory), "RemoveItem", typeof(string), typeof(int), typeof(int), typeof(bool));
            RequireMethod(failures, typeof(Inventory), "AddItem", typeof(ItemDrop.ItemData), typeof(int), typeof(int), typeof(int), typeof(bool));
            RequireMethod(failures, typeof(Player), "ConsumeResources", typeof(Piece.Requirement[]), typeof(int), typeof(int), typeof(int));
            RequireMethod(failures, typeof(ItemDrop.ItemData), "Clone");
            RequireMethod(failures, typeof(ItemDrop.ItemData), "GetWeight", typeof(int));
            RequireMethod(failures, typeof(Piece.Requirement), "GetAmount", typeof(int));
            RequireMethod(failures, typeof(Recipe), "GetAmount", typeof(int), typeof(int).MakeByRefType(), typeof(ItemDrop.ItemData).MakeByRefType(), typeof(int));
            RequireMethod(failures, typeof(Player), "GetHoverObject");
            RequireMethod(failures, typeof(Player), "GetMaxCarryWeight");
            RequireMethod(failures, typeof(Player), "GetPlayerID");
            RequireMethod(failures, typeof(ZNet), "GetWorldUID");
            RequireStaticMethod(failures, typeof(PlayerPrefs), "HasKey", typeof(string));
            RequireStaticMethod(failures, typeof(PlayerPrefs), "GetInt", typeof(string), typeof(int));
            RequireStaticMethod(failures, typeof(PlayerPrefs), "SetInt", typeof(string), typeof(int));
            RequireStaticMethod(failures, typeof(PlayerPrefs), "DeleteKey", typeof(string));
            RequireStaticMethod(failures, typeof(PlayerPrefs), "Save");
            RequireStaticMethod(failures, typeof(ZInput), "ResetButtonStatus", typeof(string));
            RequireStaticMethod(failures, typeof(ZInput), "IsGamepadActive");
            RequireMethod(failures, typeof(Localization), "RemoveTextFromCache", typeof(UnityEngine.UI.Text));
            RequireMethod(failures, typeof(SplitDialog), "get_IsActive");
            RequireMethod(failures, typeof(ZNetView), "IsOwner");
            RequireMethod(failures, typeof(ZNetView), "IsValid");
            RequireMethod(failures, typeof(ZNetView), "GetZDO");
            RequireStaticMethod(failures, typeof(CraftingStation), "get_Instances");
            RequireMethod(failures, typeof(CraftingStation), "GetStationBuildRange");
            RequireMethod(failures, typeof(CraftingStation), "GetLevel", typeof(bool));
            RequireMethod(failures, typeof(CraftingStation), "CheckUsable", typeof(Player), typeof(bool));
            RequireStaticMethod(failures, typeof(ZNetScene), "get_instance");
            RequireMethod(failures, typeof(ZNetScene), "GetPrefab", typeof(string));
            RequireMethod(failures, typeof(ZNetScene), "GetPrefabHash", typeof(GameObject));
            RequireMethod(failures, typeof(ZDO), "GetPrefab");
            RequireMethod(failures, typeof(ZNetView), "InvokeRPC", typeof(string), typeof(object[]));
            RequireStaticMethod(failures, typeof(PrivateArea), "CheckAccess", typeof(Vector3), typeof(float), typeof(bool), typeof(bool));
            RequireField(failures, typeof(InventoryGui), "m_pvp");
            RequireField(failures, typeof(InventoryGui), "m_container");
            RequireField(failures, typeof(InventoryGui), "m_currentContainer");
            RequireField(failures, typeof(InventoryGui), "m_craftTimer");
            RequireField(failures, typeof(InventoryGui), "m_craftRecipe");
            RequireField(failures, typeof(InventoryGui), "m_selectedRecipe");
            RequireField(failures, typeof(InventoryGui), "m_reqList");
            RequireField(failures, typeof(InventoryGui), "m_craftUpgradeItem");
            RequireField(failures, typeof(InventoryGui), "m_selectedVariant");
            RequireField(failures, typeof(InventoryGui), "m_craftVariant");
            RequireField(failures, typeof(InventoryGui), "m_touchMultiCrafting");
            var recipeDataPairType = AccessTools.Inner(typeof(InventoryGui), "RecipeDataPair");
            if (recipeDataPairType == null)
            {
                failures.Add("InventoryGui.RecipeDataPair missing");
            }
            else
            {
                RequireProperty(failures, recipeDataPairType, "Recipe");
                RequireProperty(failures, recipeDataPairType, "ItemData");
            }
            RequireField(failures, typeof(InventoryGui), "m_multiCrafting");
            RequireField(failures, typeof(InventoryGui), "m_multiCraftAmount");
            RequireField(failures, typeof(InventoryGui), "m_dragItem");
            RequireField(failures, typeof(InventoryGui), "m_dragInventory");
            RequireField(failures, typeof(InventoryGui), "m_trophiesPanel");
            RequireField(failures, typeof(InventoryGui), "m_achievementsPanel");
            RequireField(failures, typeof(InventoryGui), "m_skillsDialog");
            RequireField(failures, typeof(InventoryGui), "m_textsDialog");
            RequireField(failures, typeof(InventoryGui), "m_splitDialog");
            RequireField(failures, typeof(InventoryGui), "m_variantDialog");
            RequireField(failures, typeof(Hud), "m_requirementItems");
            RequireField(failures, typeof(KeyHints), "m_buildMenuHintsKB");
            RequireField(failures, typeof(Container), "m_nview");
            RequireField(failures, typeof(Container), "m_wagon");
            RequireField(failures, typeof(ZDO), "m_uid");
            RequireField(failures, typeof(ZDOVars), "s_items");
            RequireField(failures, typeof(Inventory), "m_onChanged");
            RequireField(failures, typeof(Inventory), "m_inventory");
            RequireField(failures, typeof(Player), "m_customData");
            RequireField(failures, typeof(Player), "m_noPlacementCost");
            RequireField(failures, typeof(TextInput), "m_inputField");
            RequireField(failures, typeof(ItemDrop.ItemData), "m_gridPos");
            RequireField(failures, typeof(ItemDrop.ItemData), "m_stack");
            RequireField(failures, typeof(ItemDrop.ItemData), "m_quality");
            RequireField(failures, typeof(ItemDrop.ItemData), "m_worldLevel");
            RequireField(failures, typeof(ItemDrop.ItemData), "m_equipped");
            RequireField(failures, typeof(Piece), "m_resources");
            RequireField(failures, typeof(Piece), "m_craftingStation");
            RequireField(failures, typeof(Recipe), "m_resources");
            RequireField(failures, typeof(Recipe), "m_requireOnlyOneIngredient");

            return failures.Count == 0
                ? new CompatibilityResult(true, "verified runtime contract", diagnostics)
                : new CompatibilityResult(false, string.Join("; ", failures), diagnostics);
        }

        private static string DescribeRuntimeIdentity()
        {
            var valheimAssembly = typeof(Player).Assembly;
            var valheimVersion = global::Version.CurrentVersion.ToString();
            var unityVersion = Application.unityVersion;
            var bepinexVersion = typeof(BepInEx.BaseUnityPlugin).Assembly.GetName().Version?.ToString() ?? "unknown";
            var harmonyVersion = typeof(Harmony).Assembly.GetName().Version?.ToString() ?? "unknown";
            var mvid = valheimAssembly.ManifestModule.ModuleVersionId;
            return "Valheim label " + Application.version + ", API " + valheimVersion +
                   ", Unity " + unityVersion + ", BepInEx " + bepinexVersion +
                   ", Harmony " + harmonyVersion + ", assembly_valheim MVID " + mvid;
        }

        private static void RequireMethod(ICollection<string> failures, Type type, string name, params Type[] parameters)
        {
            RuntimeContractValidator.RequireMethod(failures, type, name, false, false, parameters);
        }

        private static void RequireStaticMethod(ICollection<string> failures, Type type, string name, params Type[] parameters)
        {
            RuntimeContractValidator.RequireMethod(failures, type, name, true, false, parameters);
        }

        private static void RequireConstructor(ICollection<string> failures, Type type, params Type[] parameters)
        {
            RuntimeContractValidator.RequireConstructor(failures, type, parameters);
        }

        private static void RequireProperty(ICollection<string> failures, Type type, string name)
        {
            RuntimeContractValidator.RequireProperty(failures, type, name);
        }

        private static void RequireField(ICollection<string> failures, Type type, string name)
        {
            RuntimeContractValidator.RequireField(failures, type, name);
        }
    }
}
