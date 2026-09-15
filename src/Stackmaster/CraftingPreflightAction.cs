#nullable disable
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Stackmaster.Core;
using UnityEngine;

namespace Stackmaster
{
    internal sealed class SessionToken
    {
        internal SessionToken(ZNet network, long localSession)
        {
            Network = network;
            LocalSession = localSession;
        }

        internal ZNet Network { get; }
        internal long LocalSession { get; }

        internal static bool TryCapture(out SessionToken token)
        {
            token = null;
            if (!RuntimeContext.Compatibility.IsCompatible || ZNet.instance == null || ZDOMan.instance == null) return false;
            token = new SessionToken(ZNet.instance, ZDOMan.GetSessionID());
            return true;
        }

        internal bool IsCurrent()
            => RuntimeContext.Compatibility.IsCompatible && ReferenceEquals(Network, ZNet.instance) &&
               ZDOMan.instance != null && ZDOMan.GetSessionID() == LocalSession;
    }

    internal sealed class CraftingIntent
    {
        internal CraftingIntent(
            InventoryGui gui,
            Player player,
            Recipe recipe,
            ItemDrop.ItemData upgradeItem,
            int quality,
            int variant,
            bool multiCrafting,
            int multiplier,
            CraftingStation station,
            StorageScope scope,
            SessionToken session)
        {
            Gui = gui;
            Player = player;
            Recipe = recipe;
            UpgradeItem = upgradeItem;
            Quality = quality;
            Variant = variant;
            MultiCrafting = multiCrafting;
            Multiplier = multiplier;
            Station = station;
            StationLevel = station == null ? 0 : station.GetLevel(true);
            PlayerPosition = player.transform.position;
            ScopeSignature = scope == null ? null : scope.Signature;
            Session = session;
        }

        internal InventoryGui Gui { get; }
        internal Player Player { get; }
        internal Recipe Recipe { get; }
        internal ItemDrop.ItemData UpgradeItem { get; }
        internal int Quality { get; }
        internal int Variant { get; }
        internal bool MultiCrafting { get; }
        internal int Multiplier { get; }
        internal CraftingStation Station { get; }
        internal int StationLevel { get; }
        internal Vector3 PlayerPosition { get; }
        internal string ScopeSignature { get; }
        internal SessionToken Session { get; }
    }

    internal static class CraftingPreflightAction
    {
        private const float OwnershipTimeoutSeconds = 2f;

        private static readonly CraftingActionLifecycle Lifecycle = new CraftingActionLifecycle();
        private static readonly MethodInfo OnCraftPressedMethod = AccessTools.Method(typeof(InventoryGui), "OnCraftPressed", Type.EmptyTypes);
        private static readonly FieldInfo SelectedRecipeField = AccessTools.Field(typeof(InventoryGui), "m_selectedRecipe");
        private static readonly Type RecipeDataPairType = AccessTools.Inner(typeof(InventoryGui), "RecipeDataPair");
        private static readonly PropertyInfo SelectedRecipeProperty = AccessTools.Property(RecipeDataPairType, "Recipe");
        private static readonly PropertyInfo SelectedUpgradeItemProperty = AccessTools.Property(RecipeDataPairType, "ItemData");
        private static readonly FieldInfo SelectedVariantField = AccessTools.Field(typeof(InventoryGui), "m_selectedVariant");
        private static readonly FieldInfo CraftRecipeField = AccessTools.Field(typeof(InventoryGui), "m_craftRecipe");
        private static readonly FieldInfo CraftUpgradeItemField = AccessTools.Field(typeof(InventoryGui), "m_craftUpgradeItem");
        private static readonly FieldInfo CraftVariantField = AccessTools.Field(typeof(InventoryGui), "m_craftVariant");
        private static readonly FieldInfo MultiCraftingField = AccessTools.Field(typeof(InventoryGui), "m_multiCrafting");
        private static readonly FieldInfo MultiCraftAmountField = AccessTools.Field(typeof(InventoryGui), "m_multiCraftAmount");
        private static readonly FieldInfo TouchMultiCraftingField = AccessTools.Field(typeof(InventoryGui), "m_touchMultiCrafting");
        private static readonly FieldInfo CraftTimerField = AccessTools.Field(typeof(InventoryGui), "m_craftTimer");

        private static CraftingIntent _intent;
        private static CraftingResourcePlan _plan;
        private static PreparedCraftingResources _prepared;
        private static OwnershipBatch _ownership;
        private static int _generation;
        private static bool _resuming;

        internal static bool Prefix(InventoryGui gui)
        {
            if (_resuming) return true;
            if (!RuntimeContext.Compatibility.IsCompatible || RuntimeContext.Plugin == null ||
                !RuntimeContext.Plugin.CraftingFromNearbyChestsEnabled.Value) return true;
            var localPlayer = Player.m_localPlayer;
            if (localPlayer == null || localPlayer.NoCostCheat() ||
                (ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoCraftCost))) return true;

            if (Lifecycle.IsActive)
            {
                if (_intent != null && IsSelectionCurrent(_intent, requireCraftFields: false))
                {
                    Show("Stackmaster: securing the required nearby materials.");
                    return false;
                }
                Cancel("a newer crafting intent replaced the pending craft", false);
            }

            CraftingIntent intent;
            if (!TryCaptureIntent(gui, out intent)) return true;

            CraftingResourcePlan plan;
            string failure;
            if (!NearbyResourceService.TryPlanCraftingResources(
                    intent.Player,
                    intent.Recipe,
                    intent.Quality,
                    intent.Multiplier,
                    out plan,
                    out failure))
            {
                return true;
            }

            if (plan.Plan.RequiredUnits == 0) return true;

            _intent = intent;
            _plan = plan;
            var unowned = plan.RequiredHandles
                .Where(handle => !handle.NetworkView.IsOwner() || !handle.Container.IsOwner())
                .ToArray();
            _generation = Lifecycle.Begin(unowned.Length != 0);

            if (unowned.Length == 0)
            {
                if (!TryPrepare(out failure))
                {
                    Cancel(failure, true);
                    return false;
                }
                return true;
            }

            Show("Stackmaster: securing the required nearby materials.");
            RuntimeContext.Plugin.StartCoroutine(AcquireAndResume(_generation, intent.Session, unowned));
            return false;
        }

        internal static void AfterVanillaStart(InventoryGui gui)
        {
            if (!Lifecycle.Matches(_generation) || Lifecycle.Phase != CraftingActionPhase.Reserved ||
                _intent == null || !ReferenceEquals(gui, _intent.Gui))
            {
                return;
            }

            if (_resuming)
            {
                // The ownership handshake may outlive the physical modifier-key press. Preserve
                // the captured click's exact multi-craft intent rather than reading fresh input.
                MultiCraftingField.SetValue(gui, _intent.MultiCrafting);
                if (_intent.MultiCrafting) MultiCraftAmountField.SetValue(gui, _intent.Multiplier);
            }

            if (!IsSelectionCurrent(_intent, requireCraftFields: true) || GetCraftTimer(gui) < 0f ||
                !Lifecycle.MarkCrafting(_generation))
            {
                Cancel("vanilla did not start the reserved craft", true);
            }
        }

        internal static Exception Finalizer(Exception exception)
        {
            if (exception != null && Lifecycle.IsActive)
            {
                Cancel("craft start threw " + exception.GetType().Name, true);
            }
            return exception;
        }

        internal static void Update(InventoryGui gui)
        {
            if (!Lifecycle.IsActive || _intent == null || !ReferenceEquals(gui, _intent.Gui)) return;
            if (!RuntimeContext.Compatibility.IsCompatible || !_intent.Session.IsCurrent())
            {
                Cancel("the network session changed during crafting", false);
                return;
            }

            var requireCraftFields = Lifecycle.Phase == CraftingActionPhase.Crafting;
            if (!IsSelectionCurrent(_intent, requireCraftFields))
            {
                Cancel("the player, crafting selection, station, or storage scope changed", true);
                return;
            }
            if (requireCraftFields && GetCraftTimer(gui) < 0f)
            {
                Cancel("the craft was canceled before completion", false);
            }
        }

        internal static bool TryBeginPreparedTransaction(
            InventoryGui gui,
            Player player,
            Recipe recipe,
            int quality,
            int multiplier,
            out bool handled,
            out string failure)
        {
            handled = Lifecycle.IsActive;
            failure = null;
            if (!handled) return false;

            if (_intent == null || _prepared == null ||
                !ReferenceEquals(gui, _intent.Gui) || !ReferenceEquals(player, _intent.Player) ||
                !ReferenceEquals(recipe, _intent.Recipe) || quality != _intent.Quality ||
                multiplier != _intent.Multiplier ||
                !IsSelectionCurrent(_intent, requireCraftFields: true) ||
                !Lifecycle.TransferToTransaction(_generation))
            {
                failure = "the prepared crafting intent no longer matches the finishing craft";
                Cancel(failure, true);
                return false;
            }

            var prepared = _prepared;
            _prepared = null;
            ClearReferences();
            Lifecycle.FinishTransferred(_generation);
            if (!NearbyResourceService.TryBeginPreparedCraftingTransaction(player, prepared, out failure))
            {
                NearbyResourceService.ReleasePreparedCraftingResources(prepared);
                return false;
            }
            return true;
        }

        internal static void Cancel(string reason, bool notify)
        {
            if (!Lifecycle.IsActive && _intent == null && _prepared == null && _ownership == null) return;
            if (_intent != null && _intent.Gui != null) CraftTimerField.SetValue(_intent.Gui, -1f);
            Lifecycle.Cancel();
            if (_prepared != null) NearbyResourceService.ReleasePreparedCraftingResources(_prepared);
            if (_ownership != null) OwnershipCoordinator.Cancel(_ownership, reason);
            ClearReferences();
            if (notify && !string.IsNullOrWhiteSpace(reason)) Show("Stackmaster: craft canceled — " + reason + ".");
        }

        internal static void Shutdown(string reason)
        {
            Cancel(reason, false);
        }

        private static IEnumerator AcquireAndResume(int generation, SessionToken session, ContainerHandle[] unowned)
        {
            if (!Lifecycle.Matches(generation) || !session.IsCurrent()) yield break;

            OwnershipBatch ownership;
            try
            {
                ownership = OwnershipCoordinator.Begin(unowned);
                _ownership = ownership;
            }
            catch (Exception exception)
            {
                Cancel("ownership request failed: " + exception.GetType().Name, true);
                yield break;
            }

            var deadline = Time.realtimeSinceStartup + OwnershipTimeoutSeconds;
            while (Lifecycle.Matches(generation) && session.IsCurrent())
            {
                try
                {
                    ownership.Refresh();
                }
                catch (Exception exception)
                {
                    Cancel("ownership refresh failed: " + exception.GetType().Name, true);
                    yield break;
                }
                if (ownership.IsComplete) break;
                if (Time.realtimeSinceStartup >= deadline)
                {
                    ownership.Timeout();
                    break;
                }
                yield return null;
            }

            if (!Lifecycle.Matches(generation) || !session.IsCurrent())
            {
                if (ReferenceEquals(_ownership, ownership))
                {
                    OwnershipCoordinator.Cancel(ownership, "craft acquisition became stale");
                    _ownership = null;
                }
                yield break;
            }

            try
            {
                ownership.Refresh();
                if (!ownership.IsComplete) ownership.Timeout();
                if (ownership.FailedContainerIds.Count != 0)
                {
                    var busy = ownership.OwnerRejectedContainerIds.Count != 0;
                    Cancel(busy ? NearbyResourceOwnership.InUseMessage :
                        "Stackmaster could not safely acquire the required materials", true);
                    yield break;
                }

                OwnershipLeaseManager.HoldForCrafting(ownership);
                OwnershipCoordinator.End(ownership);
                _ownership = null;

                string failure;
                if (!TryPrepare(out failure))
                {
                    OwnershipLeaseManager.ReleaseBatch(ownership, "craft reservation failed");
                    Cancel(failure, true);
                    yield break;
                }
                if (!Lifecycle.MarkReserved(generation))
                {
                    Cancel("the ownership callback was stale", true);
                    yield break;
                }

                TouchMultiCraftingField.SetValue(_intent.Gui, _intent.MultiCrafting);
                SelectedVariantField.SetValue(_intent.Gui, _intent.Variant);
                try
                {
                    _resuming = true;
                    OnCraftPressedMethod.Invoke(_intent.Gui, null);
                }
                finally
                {
                    _resuming = false;
                }
            }
            catch (TargetInvocationException exception)
            {
                Cancel("vanilla craft start threw " + (exception.InnerException ?? exception).GetType().Name, true);
            }
            catch (Exception exception)
            {
                Cancel("ownership preparation threw " + exception.GetType().Name, true);
            }
            finally
            {
                if (ReferenceEquals(_ownership, ownership))
                {
                    OwnershipCoordinator.Cancel(ownership, "craft acquisition ended before reservation");
                    _ownership = null;
                }
            }
        }

        private static bool TryPrepare(out string failure)
        {
            failure = null;
            if (_intent == null || _plan == null || !IsSelectionCurrent(_intent, requireCraftFields: false))
            {
                failure = "the crafting selection changed before reservation";
                return false;
            }
            return NearbyResourceService.TryPrepareCraftingResources(_intent.Player, _plan, out _prepared, out failure);
        }

        private static bool TryCaptureIntent(InventoryGui gui, out CraftingIntent intent)
        {
            intent = null;
            var player = Player.m_localPlayer;
            var selected = SelectedRecipeField.GetValue(gui);
            var recipe = selected == null ? null : SelectedRecipeProperty.GetValue(selected, null) as Recipe;
            if (player == null || selected == null || recipe == null) return false;
            var upgrade = SelectedUpgradeItemProperty.GetValue(selected, null) as ItemDrop.ItemData;
            var quality = upgrade == null ? 1 : upgrade.m_quality + 1;
            var multi = upgrade == null &&
                (ZInput.GetButton("AltPlace") || ZInput.GetButton("JoyLStick") ||
                 (bool)TouchMultiCraftingField.GetValue(gui));
            var multiplier = multi ? Math.Max(1, (int)MultiCraftAmountField.GetValue(gui)) : 1;
            var scope = StorageScopeProvider.Resolve(player);
            if (scope == null || !SessionToken.TryCapture(out var session)) return false;
            intent = new CraftingIntent(
                gui,
                player,
                recipe,
                upgrade,
                quality,
                (int)SelectedVariantField.GetValue(gui),
                multi,
                multiplier,
                player.GetCurrentCraftingStation(),
                scope,
                session);
            return true;
        }

        private static bool IsSelectionCurrent(CraftingIntent intent, bool requireCraftFields)
        {
            if (intent == null || intent.Player == null || intent.Gui == null ||
                !ReferenceEquals(Player.m_localPlayer, intent.Player) ||
                intent.Player.IsDead() || intent.Player.IsTeleporting() || intent.Player.InCutscene() ||
                !ReferenceEquals(intent.Player.GetCurrentCraftingStation(), intent.Station) ||
                (intent.Station != null &&
                 (!intent.Station.CheckUsable(intent.Player, false) || intent.Station.GetLevel(true) != intent.StationLevel)) ||
                (intent.Player.transform.position - intent.PlayerPosition).sqrMagnitude > 0.0025f)
            {
                return false;
            }
            var selected = SelectedRecipeField.GetValue(intent.Gui);
            if (selected == null ||
                !ReferenceEquals(SelectedRecipeProperty.GetValue(selected, null), intent.Recipe) ||
                !ReferenceEquals(SelectedUpgradeItemProperty.GetValue(selected, null), intent.UpgradeItem) ||
                (int)SelectedVariantField.GetValue(intent.Gui) != intent.Variant)
            {
                return false;
            }
            var scope = StorageScopeProvider.Resolve(intent.Player);
            if (scope == null || !string.Equals(scope.Signature, intent.ScopeSignature, StringComparison.Ordinal)) return false;
            if (!requireCraftFields) return true;
            return ReferenceEquals(CraftRecipeField.GetValue(intent.Gui), intent.Recipe) &&
                   ReferenceEquals(CraftUpgradeItemField.GetValue(intent.Gui), intent.UpgradeItem) &&
                   (int)CraftVariantField.GetValue(intent.Gui) == intent.Variant &&
                   (bool)MultiCraftingField.GetValue(intent.Gui) == intent.MultiCrafting &&
                   (!intent.MultiCrafting || (int)MultiCraftAmountField.GetValue(intent.Gui) == intent.Multiplier);
        }

        private static float GetCraftTimer(InventoryGui gui) => (float)CraftTimerField.GetValue(gui);

        private static void ClearReferences()
        {
            _intent = null;
            _plan = null;
            _prepared = null;
            _ownership = null;
        }

        private static void Show(string message)
        {
            MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, message);
        }
    }
}
