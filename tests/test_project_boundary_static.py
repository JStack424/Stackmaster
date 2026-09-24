"""Repository safety checks for the private-reference and one-DLL gameplay boundary."""

from pathlib import Path
import re
import subprocess
import unittest

ROOT = Path(__file__).resolve().parents[1]
PLUGIN_PROJECT = ROOT / "src" / "Stackmaster" / "Stackmaster.csproj"
PLUGIN_DIR = ROOT / "src" / "Stackmaster"
PLUGIN = PLUGIN_DIR / "Plugin.cs"


class ProjectBoundaryTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.project = PLUGIN_PROJECT.read_text(encoding="utf-8")
        cls.plugin = PLUGIN.read_text(encoding="utf-8")
        cls.gameplay = "\n".join(
            path.read_text(encoding="utf-8")
            for path in sorted(PLUGIN_DIR.glob("*.cs"))
        )

    def test_plugin_identity_and_compatibility_gate(self):
        self.assertIn('PluginGuid = "com.jstack424.stackmaster"', self.plugin)
        self.assertIn('PluginName = "Stackmaster"', self.plugin)
        self.assertIn("PluginVersion = GeneratedBuildInfo.Version", self.plugin)
        self.assertIn("CompatibilityGate.Evaluate()", self.plugin)
        self.assertIn("fully disabled before any inventory hooks were installed", self.plugin)
        self.assertIn("_harmony?.UnpatchSelf()", self.plugin)
        self.assertIn("RuntimeContext.Disable", self.plugin)
        self.assertIn("Compatibility validation threw before patch installation", self.plugin)
        self.assertNotIn("PatchAll", self.plugin)
        self.assertLess(
            self.plugin.index("CompatibilityGate.Evaluate()"),
            self.plugin.index("PatchInstaller.Install"),
        )

    def test_compatibility_gate_uses_contracts_not_runtime_identity(self):
        gate = (PLUGIN_DIR / "CompatibilityGate.cs").read_text(encoding="utf-8") + (PLUGIN_DIR / "HarmonyTargetManifest.cs").read_text(encoding="utf-8")
        validator = (PLUGIN_DIR / "RuntimeContractValidator.cs").read_text(encoding="utf-8")
        installer = (PLUGIN_DIR / "PatchInstaller.cs").read_text(encoding="utf-8") + (PLUGIN_DIR / "HarmonyTargetManifest.cs").read_text(encoding="utf-8")
        for forbidden in (
            "SupportedGameVersion", "SupportedUnityVersion", "SupportedBepInExVersion",
            "SupportedHarmonyVersion", "SupportedValheimMvid", "SupportedValheimSha256",
            "SHA256.Create()",
        ):
            self.assertNotIn(forbidden, gate)
        self.assertIn("Runtime diagnostics only", self.plugin)
        self.assertIn("RuntimeContractValidator.RequireType", gate)
        self.assertGreaterEqual(gate.count("RequireMethod(failures"), 50)
        self.assertIn("HarmonyTargetManifest.Validate(failures)", gate)
        self.assertIn("HarmonyTargetManifest.ResolveAll()", installer)
        self.assertGreaterEqual(gate.count("RequireField(failures"), 30)
        self.assertGreaterEqual(gate.count("RequireProperty(failures"), 2)
        self.assertIn("matches.Length == 0", validator)
        self.assertIn("matches.Length > 1", validator)
        self.assertIn("AmbiguousMatchException", validator)
        self.assertIn("PatchInstaller.Prepare()", self.plugin)
        self.assertLess(self.plugin.index("PatchInstaller.Prepare()"), self.plugin.index("new Harmony"))
        self.assertIn("ResolveExactMethod", installer)
        self.assertIn("ResolveUniqueNamedMethod", installer)

    def test_harmony_targets_have_one_manifest_and_release_gate(self):
        gate = (PLUGIN_DIR / "CompatibilityGate.cs").read_text(encoding="utf-8")
        installer = (PLUGIN_DIR / "PatchInstaller.cs").read_text(encoding="utf-8")
        manifest = (PLUGIN_DIR / "HarmonyTargetManifest.cs").read_text(encoding="utf-8")
        build = (ROOT / "scripts" / "build.sh").read_text(encoding="utf-8")
        package = (ROOT / "scripts" / "package.py").read_text(encoding="utf-8")

        self.assertEqual(1, gate.count("HarmonyTargetManifest.Validate(failures)"))
        self.assertEqual(1, installer.count("HarmonyTargetManifest.ResolveAll()"))
        self.assertNotIn("ResolveExactMethod", installer)
        self.assertNotRegex(installer, r"typeof\((InventoryGui|Container|InventoryGrid|Player|Hud|BuildUi|Game|ZNet)\)")
        self.assertEqual(
            34,
            len(re.findall(r"^\s{12}(?:Prefix|Postfix|Both|Transactional)\(", manifest, flags=re.MULTILINE)),
        )
        self.assertIn(
            'Both(typeof(InventoryGui), "SetupRequirement", true, typeof(bool), new[]',
            manifest,
        )
        self.assertIn("RuntimeContractValidator.ResolveExactMethod(", manifest)
        self.assertIn("HarmonyPatchCompatibility.Validate(original, prefix, postfix, finalizer)", manifest)
        self.assertIn("tests/Stackmaster.Compatibility.Tests/Stackmaster.Compatibility.Tests.csproj", build)
        self.assertIn("run_release_gate()", package)
        self.assertNotIn("TEST_ONLY", package)
        self.assertIn('f"JStack424-Stackmaster-{VERSION}.zip"', package)
        self.assertNotIn('output filename must end with -test.zip', package)
        self.assertLess(package.index("run_release_gate()", package.index("def main")),
                        package.index('run_git("status"', package.index("def main")))

    def test_client_static_utility_contracts_are_validated_with_their_real_shape(self):
        gate = (PLUGIN_DIR / "CompatibilityGate.cs").read_text(encoding="utf-8") + (PLUGIN_DIR / "HarmonyTargetManifest.cs").read_text(encoding="utf-8")
        for contract in (
            'RequireStaticMethod(failures, typeof(ZDOMan), "GetSessionID")',
            'RequireStaticMethod(failures, typeof(GameCamera), "InFreeFly")',
            'RequireStaticMethod(failures, typeof(ZInput), "ResetButtonStatus", typeof(string))',
            'RequireStaticMethod(failures, typeof(PrivateArea), "CheckAccess", typeof(Vector3), typeof(float), typeof(bool), typeof(bool))',
        ):
            self.assertIn(contract, gate)
            self.assertNotIn(contract.replace("RequireStaticMethod", "RequireMethod"), gate)

    def test_plugin_targets_net48_and_does_not_copy_private_references(self):
        self.assertIn("<TargetFramework>net48</TargetFramework>", self.project)
        self.assertGreaterEqual(self.project.count("<Private>false</Private>"), 10)
        self.assertIn("VerifyReferencesWereNotCopied", self.project)
        self.assertNotIn("Stackmaster.Core.csproj", self.project)

    def test_exactly_six_current_user_settings_are_bound(self):
        migration_call = "ConfigMigration.BindRenamedDefaultEnabledBoolean("
        self.assertEqual(3, self.plugin.count("Config.Bind("))
        self.assertEqual(2, self.plugin.count(migration_call))
        self.assertEqual(1, self.plugin.count("ConfigMigration.BindRenamedKeyboardShortcut("))
        for key in (
            "Auto-sort enabled",
            "Nearby-storage radius",
            "Quick Stack keybind",
            "Allow building from storage",
            "Allow crafting from storage",
            "Show storage amounts in craft and build menus",
        ):
            self.assertEqual(1, self.plugin.count(f'"{key}"'))

    def test_quick_stack_keybind_uses_public_migration_and_preserves_custom_shortcuts(self):
        migration = (PLUGIN_DIR / "ConfigMigration.cs").read_text(encoding="utf-8")
        self.assertEqual(1, self.plugin.count('"Storage-action keybind"'))
        self.assertEqual(1, self.plugin.count('"Quick Stack keybind"'))
        self.assertIn("BindRenamedKeyboardShortcut", migration)
        self.assertIn("current.Value.Equals(defaultShortcut)", migration)
        self.assertIn("!legacy.Value.Equals(defaultShortcut)", migration)
        self.assertIn("current.Value = legacy.Value", migration)
        self.assertIn("config.Remove(legacy.Definition)", migration)

    def test_renamed_storage_permissions_use_public_idempotent_bepinex_migration(self):
        migration = (PLUGIN_DIR / "ConfigMigration.cs").read_text(encoding="utf-8")
        self.assertIn('"Enable building from nearby chests"', self.plugin)
        self.assertIn('"Enable crafting from nearby chests"', self.plugin)
        self.assertIn("config.SaveOnConfigSet = false", migration)
        self.assertIn("config.Bind(section, legacyKey, true", migration)
        self.assertIn("config.Bind(section, currentKey, true", migration)
        self.assertIn("current.Value = legacy.Value && current.Value", migration)
        self.assertIn("config.Remove(legacy.Definition)", migration)
        self.assertIn("config.Save()", migration)
        self.assertIn("config.SaveOnConfigSet = originalSaveOnConfigSet", migration)
        self.assertNotIn("OrphanedEntries", migration)
        self.assertNotIn("Reflection", migration)

    def test_all_chest_features_share_the_workbench_mesh_or_fallback_scope(self):
        scope = (PLUGIN_DIR / "StorageScope.cs").read_text(encoding="utf-8")
        policy = (ROOT / "src" / "Stackmaster.Core" / "StorageScopePolicy.cs").read_text(encoding="utf-8")
        discovery = (PLUGIN_DIR / "ContainerDiscovery.cs").read_text(encoding="utf-8")
        action = (PLUGIN_DIR / "StorageAction.cs").read_text(encoding="utf-8")
        nearby = (PLUGIN_DIR / "NearbyResources.cs").read_text(encoding="utf-8")
        executor = (PLUGIN_DIR / "TransferExecutor.cs").read_text(encoding="utf-8")
        gate = (PLUGIN_DIR / "CompatibilityGate.cs").read_text(encoding="utf-8") + (PLUGIN_DIR / "HarmonyTargetManifest.cs").read_text(encoding="utf-8")
        runtime = (PLUGIN_DIR / "RuntimeContext.cs").read_text(encoding="utf-8")

        self.assertIn('WorkbenchPrefabName = "piece_workbench"', scope)
        self.assertIn("CraftingStation.Instances", scope)
        self.assertIn("stations.OfType<CraftingStation>()", scope)
        self.assertIn("station.GetType() != typeof(CraftingStation)", scope)
        self.assertIn("station.GetComponent<ZNetView>()", scope)
        self.assertIn("zdo.GetPrefab() != workbenchPrefabHash", scope)
        self.assertIn("station.GetStationBuildRange()", scope)
        self.assertIn("DistanceSquaredXZ", policy)
        self.assertIn("< zone.BuildRange * zone.BuildRange", policy)
        self.assertIn("< combinedRange * combinedRange", policy)
        self.assertIn("DistanceSquared3D", policy)
        self.assertIn("<= _fallbackRadius * _fallbackRadius", policy)
        self.assertIn("while (queue.Count > 0)", policy)
        self.assertNotIn("m_allStations", scope)
        self.assertNotIn("m_nview", scope)

        self.assertIn("StorageScope scope", discovery)
        self.assertIn("scope.Contains(target.transform.position)", discovery)
        self.assertIn("scope.Contains(candidate.Container.transform.position)", discovery)
        self.assertIn("!scope.RequiresCompleteDiscovery", discovery)
        self.assertNotIn("float radius,\n            bool requireComplete", discovery)
        self.assertGreaterEqual(action.count("StorageScopeProvider.Resolve(player)"), 2)
        self.assertIn("StorageScopeProvider.Resolve(player)", nearby)
        self.assertIn("ContainerDiscovery.Discover(player, null, catalog, scope, true, true)", nearby)
        self.assertIn("StorageScope executionScope", executor)
        self.assertIn("executionScope.Contains(container.transform.position)", executor)
        self.assertNotIn("StorageScopeProvider.Resolve", executor)
        self.assertIn("ValidateReadOnlyHandle(player, freshScope, handle", nearby)
        transaction = nearby[nearby.index("private static bool TryBeginTransaction") : nearby.index("internal static ResourceWithdrawalPlan Plan")]
        self.assertIn("TryReserveContainers(player, mutableCapture.Scope", transaction)
        self.assertIn("RevalidateReservedContainers(player, mutableCapture.Scope", transaction)
        self.assertNotIn("StorageScopeProvider.Resolve", transaction)
        self.assertIn("freshScope,\n                        freshHandles", action)
        self.assertNotIn("ChestSortPreferences", scope + policy + discovery + action + nearby + executor)
        self.assertGreaterEqual(runtime.count("StorageScopeProvider.Reset()"), 3)

        for signature in (
            'RequireStaticMethod(failures, typeof(CraftingStation), "get_Instances")',
            'RequireMethod(failures, typeof(CraftingStation), "GetStationBuildRange")',
            'RequireStaticMethod(failures, typeof(ZNetScene), "get_instance")',
            'RequireMethod(failures, typeof(ZNetScene), "GetPrefab", typeof(string))',
            'RequireMethod(failures, typeof(ZNetScene), "GetPrefabHash", typeof(GameObject))',
            'RequireMethod(failures, typeof(ZNetView), "GetZDO")',
            'RequireMethod(failures, typeof(ZDO), "GetPrefab")',
        ):
            self.assertIn(signature, gate)

    def test_mutation_uses_verified_game_primitive_and_never_claims_blindly(self):
        self.assertIn("MoveItemToThis", self.gameplay)
        self.assertIn("container.StackAll()", self.gameplay)
        self.assertIn("RPC_StackResponse", self.gameplay)
        self.assertNotIn("ClaimOwnership()", self.gameplay)
        self.assertIn("exactPostcondition", self.gameplay)
        sort_executor = (PLUGIN_DIR / "SortExecutor.cs").read_text(encoding="utf-8")
        self.assertIn("inventory.MoveItemToThis", sort_executor)
        self.assertNotIn("item.m_stack = placement.Quantity", sort_executor)

    def test_inventory_mutation_hooks_are_installed_only_after_gate(self):
        installer = (PLUGIN_DIR / "PatchInstaller.cs").read_text(encoding="utf-8")
        manifest = (PLUGIN_DIR / "HarmonyTargetManifest.cs").read_text(encoding="utf-8")
        self.assertIn("HarmonyTargetManifest.ResolveAll()", installer)
        self.assertIn("Every target and patch entrypoint is resolved before the first Harmony write", installer)
        self.assertNotIn("PatchAll", self.plugin)
        self.assertNotIn('Postfix(typeof(InventoryGui), "Update"', installer)
        self.assertIn('Postfix(typeof(InventoryGrid), "UpdateInventory"', manifest)
        self.assertIn("if (!compatibility.IsCompatible)", self.plugin)
        self.assertIn("return;", self.plugin)

    def test_quick_grab_materials_intercepts_only_modified_build_ui_piece_clicks(self):
        installer = (PLUGIN_DIR / "PatchInstaller.cs").read_text(encoding="utf-8") + (PLUGIN_DIR / "HarmonyTargetManifest.cs").read_text(encoding="utf-8")
        action = (PLUGIN_DIR / "QuickGrabMaterialsAction.cs").read_text(encoding="utf-8")
        core = (ROOT / "src" / "Stackmaster.Core" / "QuickGrabMaterials.cs").read_text(encoding="utf-8")
        self.assertIn('Prefix(typeof(BuildUi), "OnSelectPiece", false, typeof(void), new[] { typeof(Piece) }, typeof(QuickGrabMaterialsClickPatch))', installer)
        self.assertIn("plugin.StorageActionShortcut.Value.Modifiers", action)
        self.assertIn("modifiers.Select(Input.GetKey)", action)
        self.assertIn("QuickGrabClickPolicy.ShouldIntercept", action)
        self.assertIn("return true;", action[action.index("internal static class QuickGrabMaterialsClickPatch"):action.index("internal sealed class QuickGrabInventoryBackup")])
        self.assertIn("return !intercepting;", action)
        self.assertIn("PendingRequests.Enqueue(Tuple.Create(player, piece))", action)
        self.assertIn("ReferenceEquals(candidate.Item1, Player.m_localPlayer)", action)
        self.assertIn("!ReferenceEquals(player, Player.m_localPlayer)", action)
        self.assertIn("return states.Length > 0 && states.All(state => state)", core)
        click_patch = action[action.index("internal static class QuickGrabMaterialsClickPatch"):action.index("internal sealed class QuickGrabInventoryBackup")]
        self.assertNotIn("Hud.CloseBuildUi()", click_patch)
        self.assertNotIn("SetSelectedPiece(piece)", click_patch)

    def test_quick_grab_build_menu_hint_is_dynamic_keyboard_only_and_cleans_up_only_itself(self):
        hint = (PLUGIN_DIR / "QuickGrabMaterialsBuildHint.cs").read_text(encoding="utf-8")
        manifest = (PLUGIN_DIR / "HarmonyTargetManifest.cs").read_text(encoding="utf-8")
        runtime = (PLUGIN_DIR / "RuntimeContext.cs").read_text(encoding="utf-8")
        gate = (PLUGIN_DIR / "CompatibilityGate.cs").read_text(encoding="utf-8")
        compatibility = (ROOT / "tests" / "Stackmaster.Compatibility.Tests" / "Program.cs").read_text(encoding="utf-8")

        self.assertIn('Postfix(typeof(KeyHints), "Update", false, typeof(void), Type.EmptyTypes, typeof(QuickGrabMaterialsBuildHintPatch))', manifest)
        self.assertIn('internal const string ActionText = "Quick Grab Materials"', hint)
        self.assertIn("RuntimeContext.Plugin.StorageActionShortcut.Value.Modifiers", hint)
        self.assertIn('keys.Add("Click")', hint)
        self.assertIn('case KeyCode.LeftAlt: return "Left Alt"', hint)
        self.assertIn("owner.m_buildMenuHintsKB", hint)
        self.assertNotIn("m_buildMenuHintsGP", hint)
        self.assertIn("ZInput.IsGamepadActive()", hint)
        self.assertIn("if (_hint != null) _hint.SetActive(false);", hint)
        self.assertIn("vanillaAndThirdPartyHints.Concat(new[] { hint }).ToArray()", hint)
        self.assertIn("string.Equals(item.name, ObjectName, StringComparison.Ordinal)", hint)
        self.assertIn("!ReferenceEquals(item, _hint)", hint)
        self.assertIn("if (ReferenceEquals(_failedOwner, owner)) return;", hint)
        self.assertIn("QuickGrabMaterialsBuildHint.FailForOwner(__instance)", hint)
        self.assertNotIn("_originalKeyboardHints", hint)
        self.assertGreaterEqual(runtime.count("QuickGrabMaterialsBuildHint.Detach"), 3)
        self.assertIn('RequireStaticMethod(failures, typeof(ZInput), "IsGamepadActive")', gate)
        self.assertIn('RequireMethod(failures, typeof(Localization), "RemoveTextFromCache", typeof(UnityEngine.UI.Text))', gate)
        self.assertIn('RequireField(failures, typeof(KeyHints), "m_buildMenuHintsKB")', gate)
        self.assertIn('(\"KeyHints\", \"Update\", Array.Empty<string>())', compatibility)
        self.assertIn('(\"KeyHints\", \"m_buildMenuHintsKB\")', compatibility)
        self.assertIn('\"KeyHints\", \"UpdateHints\", Array.Empty<string>(), \"KeyHints\", \"m_buildMenuHintsKB\"', compatibility)

    def test_quick_grab_materials_is_storage_only_largest_stock_first_and_exact_per_click(self):
        action = (PLUGIN_DIR / "QuickGrabMaterialsAction.cs").read_text(encoding="utf-8")
        core = (ROOT / "src" / "Stackmaster.Core" / "QuickGrabMaterials.cs").read_text(encoding="utf-8")
        self.assertIn('!string.Equals(stack.InventoryId, "player", StringComparison.Ordinal)', action)
        self.assertIn(".OrderByDescending(group => group.Total)", core)
        self.assertIn(".ThenBy(group => group.InventoryId, StringComparer.Ordinal)", core)
        self.assertIn("A piece-specific material grab is indivisible", core)
        self.assertIn("Array.Empty<ResourceWithdrawalStep>()", core)
        self.assertIn("Every modified click is one independent complete-material request", action)

    def test_quick_grab_materials_preflights_capacity_and_weight_before_ownership(self):
        action = (PLUGIN_DIR / "QuickGrabMaterialsAction.cs").read_text(encoding="utf-8")
        begin = action[action.index("internal static void Begin"):action.index("internal static void Shutdown")]
        self.assertLess(begin.index("TryPlanCapacity"), begin.index("StartCoroutine"))
        self.assertIn("playerInventory.GetTotalWeight()", action)
        self.assertIn("player.GetMaxCarryWeight()", action)
        self.assertIn("runtime.Item.GetWeight(step.Quantity)", action)
        self.assertIn("QuickGrabCapacityPlanner", action)
        self.assertIn("ExpectedDestinationQuantity", action)

    def test_quick_grab_materials_reserves_revalidates_rolls_back_and_preserves_build_leases(self):
        action = (PLUGIN_DIR / "QuickGrabMaterialsAction.cs").read_text(encoding="utf-8")
        nearby = (PLUGIN_DIR / "NearbyResources.cs").read_text(encoding="utf-8")
        ownership = (PLUGIN_DIR / "OwnershipCoordinator.cs").read_text(encoding="utf-8")
        self.assertIn("RevalidateContainers(player, plan, capture", action)
        self.assertIn("TryReserveContainers(", action)
        self.assertIn("RevalidateReservedContainers", action)
        self.assertIn("RevalidateStacks", action)
        self.assertIn("MoveItemToThis", action)
        self.assertIn("RollbackCompleted", action)
        self.assertIn("RestoreBackups", action)
        self.assertIn('catch (Exception exception)\n            {\n                RuntimeContext.Plugin?.Log.LogError("Quick-grab transaction threw after mutation began:', action)
        transfer_catch = action[action.index('RuntimeContext.Plugin?.Log.LogError("Quick-grab transfer primitive threw:'):]
        self.assertIn("throw;", transfer_catch[:500])
        self.assertIn("_items.PrepareRestore(snapshot => snapshot.Clone())", action)
        self.assertIn("preserveItemIdentity: true", action)
        self.assertIn("preserveItemIdentity: false", action)
        self.assertIn("target.Items.RestoreStates(RestoreItemDataState)", action)
        self.assertIn("target.Items.HasExactOriginalReferences", action)
        self.assertIn("target.Items.StatesMatch(ItemDataStateMatches)", action)
        self.assertIn("InventoryItemsField.SetValue(target.Inventory, current)", action)
        self.assertIn("InventoryItemsField.SetValue(target.Inventory, restoredLists[index])", action)
        for field_name in (
            "m_shared", "m_stack", "m_durability", "m_equipped", "m_quality", "m_variant",
            "m_crafterID", "m_crafterName", "m_worldLevel", "m_pickedUp", "m_cheated", "m_gridPos",
            "m_dropPrefab", "m_lastAttackTime", "m_lastProjectile", "m_customData",
        ):
            self.assertIn(f"original.{field_name} = snapshot.{field_name}", action)
        self.assertIn("Quick-grab cache refresh failed after commit", action)
        self.assertIn("reservationsReleased = NearbyResourceService.ReleaseReservations", action)
        self.assertIn("for (var attempt = 0; attempt < 2 && !released; attempt++)", nearby)
        self.assertIn("PendingReservationReleases", nearby)
        self.assertIn("UpdatePendingReservationReleases", nearby)
        self.assertIn("NearbyResourceService.UpdatePendingReservationReleases();", ownership)
        rollback = action[action.index("private static bool RollbackCompleted"):action.index("private static bool RestoreBackups")]
        self.assertLess(rollback.index("var sourceBefore = TotalUnits(move.Source.Inventory)"),
                        rollback.index("var playerBefore = TotalUnits(playerInventory)"))
        self.assertIn("RuntimeContext.Disable", action)
        self.assertIn("ReleaseReservations(reservations, false)", action)
        self.assertIn("OwnershipLeaseManager.ReleaseBatch(ownership", action)
        self.assertIn("if (releaseMatchingOwnership)", nearby)
        self.assertNotIn("ClaimOwnership()", action)

    def test_pending_reservation_cleanup_precedes_exact_ownership_release(self):
        nearby = (PLUGIN_DIR / "NearbyResources.cs").read_text(encoding="utf-8")
        ownership = (PLUGIN_DIR / "OwnershipCoordinator.cs").read_text(encoding="utf-8")
        runtime = (PLUGIN_DIR / "RuntimeContext.cs").read_text(encoding="utf-8")
        plugin = (PLUGIN_DIR / "Plugin.cs").read_text(encoding="utf-8")
        policy = (ROOT / "src" / "Stackmaster.Core" / "IdentityPreservingRollback.cs").read_text(encoding="utf-8")

        transaction_release = nearby[
            nearby.index("private static void ReleaseReservations("):
            nearby.index("internal sealed class RuntimeResourceStack")
        ]
        self.assertIn("NearbyResourceService.ReleaseReservations", transaction_release)
        self.assertIn("releaseMatchingOwnership: false", transaction_release)
        self.assertIn("if (!reservationsReleased)", transaction_release)
        self.assertNotIn("container.SetInUse(false)", transaction_release)
        self.assertIn("HasPendingReservationRelease(string containerId)", nearby)
        self.assertIn("PendingReservationReleases.ContainsKey(containerId)", nearby)
        self.assertIn("FlushPendingReservationReleasesBeforeOwnershipShutdown", nearby)
        disable = runtime[runtime.index("internal static void Disable(string reason)"):runtime.index("internal static void Shutdown()")]
        self.assertLess(
            disable.index("FlushPendingReservationReleasesBeforeOwnershipShutdown"),
            disable.index("OwnershipCoordinator.Shutdown(reason)"),
        )
        relinquish = ownership[ownership.index("private static bool TryRelinquish("):ownership.index("internal static class OwnershipCoordinator")]
        self.assertIn("ReservationOwnershipCleanupPolicy.CanRelinquishOwnership", relinquish)
        self.assertIn("NearbyResourceService.HasPendingReservationRelease(acquisition.Id)", relinquish)
        self.assertLess(
            relinquish.index("ReservationOwnershipCleanupPolicy.CanRelinquishOwnership"),
            relinquish.index("if (!shutdownRelease"),
        )
        safety = ownership[ownership.index("internal static class OwnershipSafetyUpdatePatch"):ownership.index("internal static class ContainerOpenRequestLeasePatch")]
        self.assertLess(
            safety.index("NearbyResourceService.UpdatePendingReservationReleases();"),
            safety.index("OwnershipLeaseManager.Update();"),
        )
        clear = nearby[nearby.index("private static bool TryClearReservation"):nearby.index("internal static bool RevalidateContainers")]
        self.assertIn("!string.Equals(zdo.m_uid.ToString(), handle.Id", clear)
        self.assertIn("zdo.OwnerRevision != reservation.OwnerRevision", clear)
        self.assertLess(clear.index("zdo.OwnerRevision != reservation.OwnerRevision"), clear.index("container.SetInUse(false)"))
        self.assertIn("OwnershipCoordinator.HasUnresolvedCleanup || NearbyResourceService.HasPendingReservationReleases", plugin)
        self.assertIn("=> !hasPendingReservationCleanup", policy)

    def test_quick_grab_materials_runtime_surface_is_compatibility_gated(self):
        gate = (PLUGIN_DIR / "CompatibilityGate.cs").read_text(encoding="utf-8") + (PLUGIN_DIR / "HarmonyTargetManifest.cs").read_text(encoding="utf-8")
        for signature in (
            'Prefix(typeof(BuildUi), "OnSelectPiece", false, typeof(void), new[] { typeof(Piece) }, typeof(QuickGrabMaterialsClickPatch))',
            'RequireMethod(failures, typeof(Inventory), "GetWidth")',
            'RequireMethod(failures, typeof(Inventory), "GetHeight")',
            'RequireMethod(failures, typeof(Inventory), "GetTotalWeight")',
            'RequireMethod(failures, typeof(Inventory), "RemoveAll")',
            'RequireMethod(failures, typeof(ItemDrop.ItemData), "GetWeight", typeof(int))',
            'RequireMethod(failures, typeof(Player), "GetMaxCarryWeight")',
        ):
            self.assertIn(signature, gate)

    def test_player_snapshot_reserves_the_entire_quick_bar_row(self):
        snapshots = (PLUGIN_DIR / "InventorySnapshots.cs").read_text(encoding="utf-8")
        self.assertIn("Enumerable.Range(0, width)", snapshots)
        self.assertIn("The entire quick-bar row is fixed", snapshots)

    def test_character_persistence_is_versioned_and_item_following(self):
        core = (ROOT / "src" / "Stackmaster.Core" / "ProtectionState.cs").read_text(encoding="utf-8")
        runtime = (PLUGIN_DIR / "RuntimeContext.cs").read_text(encoding="utf-8")
        self.assertIn('CurrentVersion = "v2"', core)
        self.assertIn('LegacyVersion = "v1"', core)
        self.assertIn("List<ProtectionRecord>", core)
        self.assertIn("ProtectionResolution Reconcile", core)
        self.assertIn("player.m_customData", runtime)
        self.assertIn("CharacterDataKey", runtime)
        self.assertIn("ProtectionState.TryParse", runtime)
        self.assertIn("Saved protection data is malformed or from an unsupported version", runtime)
        self.assertIn("saved protection payload", runtime)
        self.assertIn("new System.Text.UTF8Encoding(false, true)", core)
        self.assertIn("DecoderFallbackException", core)
        snapshots = (PLUGIN_DIR / "InventorySnapshots.cs").read_text(encoding="utf-8")
        self.assertIn("record.TargetItemKey, PersistentItemKey(item)", snapshots)

    def test_transfer_revalidates_before_every_game_mutation(self):
        executor = (PLUGIN_DIR / "TransferExecutor.cs").read_text(encoding="utf-8")
        move_index = executor.index("MoveItemToThis")
        self.assertLess(executor.index("RevalidateAndOwn"), move_index)
        self.assertLess(executor.index("source stack changed before transfer"), move_index)
        self.assertLess(executor.index("destination stack changed before transfer"), move_index)
        self.assertIn("exactPostcondition", executor)
        self.assertIn("FatalPostconditionFailure", executor)
        self.assertIn("RuntimeContext.Disable", executor)
        action = (PLUGIN_DIR / "StorageAction.cs").read_text(encoding="utf-8")
        self.assertIn("Restart Valheim before using it again", action)

    def test_container_tooltip_formats_modifiers_before_main_key_with_exact_action_text(self):
        action = (PLUGIN_DIR / "StorageAction.cs").read_text(encoding="utf-8")
        self.assertIn("shortcut.Modifiers.Select(key => key.ToString())", action)
        self.assertIn("Concat(new[] { shortcut.MainKey.ToString() })", action)
        self.assertIn('"</color>] Quick Stack"', action)
        self.assertNotIn('"</color>] Auto-Stack All"', action)
        self.assertNotIn('"</color>] Stackmaster: deposit + replenish"', action)
        self.assertIn("new KeyboardShortcut(KeyCode.E, KeyCode.LeftAlt)", self.plugin)

    def test_auto_sort_toggle_is_purpose_built_visible_and_synchronized(self):
        integration = (PLUGIN_DIR / "InventoryIntegration.cs").read_text(encoding="utf-8")
        self.assertIn('ToggleAnchorName = "StackmasterAutoSortAnchor"', integration)
        self.assertIn("_toggleAnchor.transform.SetParent(gui.m_player, false)", integration)
        self.assertIn("anchorRect.anchorMin = new Vector2(0f, 0f)", integration)
        self.assertIn("anchorRect.anchoredPosition = new Vector2(12f, -24f)", integration)
        self.assertNotIn("anchorRect.anchoredPosition = new Vector2(12f, 4f)", integration)
        self.assertIn("anchorRect.sizeDelta = new Vector2(164f, 28f)", integration)
        self.assertIn("ignoreLayout = true", integration)
        self.assertIn('typeof(Toggle)', integration)
        self.assertIn('new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI))', integration)
        self.assertIn('label.text = "Auto-sort"', integration)
        self.assertIn('CreateImage(_toggleAnchor.transform, "HitArea", Color.clear)', integration)
        self.assertIn("hitArea.raycastTarget = true", integration)
        self.assertIn('new GameObject("CheckedX", typeof(RectTransform))', integration)
        self.assertIn('CreateCheckedXStroke(checkRect, "ForwardStroke", 45f)', integration)
        self.assertIn('CreateCheckedXStroke(checkRect, "BackStroke", -45f)', integration)
        self.assertIn("rect.anchoredPosition = new Vector2(10f, 10f)", integration)
        self.assertIn("rect.sizeDelta = new Vector2(3f, 14f)", integration)
        self.assertNotIn("CreateCheckmarkStroke", integration)
        self.assertNotIn('checkText.text = "✓"', integration)
        self.assertNotIn("Object.Instantiate(gui.m_pvp", integration)
        self.assertNotIn('label.text = "Enable PvP"', integration)
        self.assertIn("_toggleAnchor.SetActive(true)", integration)
        self.assertIn("_toggle.interactable = true", integration)
        self.assertIn("_toggle.SetIsOnWithoutNotify(RuntimeContext.Plugin.AutoSortEnabled.Value)", integration)
        unsubscribe = integration.index("AutoSortEnabled.SettingChanged -= OnAutoSortSettingChanged")
        subscribe = integration.index("AutoSortEnabled.SettingChanged += OnAutoSortSettingChanged")
        self.assertLess(unsubscribe, subscribe)
        self.assertIn("Object.Destroy(_toggleAnchor)", integration)

    def test_chest_auto_sort_is_local_scoped_and_not_a_seventh_setting(self):
        integration = (PLUGIN_DIR / "InventoryIntegration.cs").read_text(encoding="utf-8")
        preferences = (PLUGIN_DIR / "ChestSortPreferences.cs").read_text(encoding="utf-8")
        policy = (ROOT / "src" / "Stackmaster.Core" / "ChestSortPreferencePolicy.cs").read_text(encoding="utf-8")
        gate = (PLUGIN_DIR / "CompatibilityGate.cs").read_text(encoding="utf-8") + (PLUGIN_DIR / "HarmonyTargetManifest.cs").read_text(encoding="utf-8")
        self.assertIn('label.text = "Auto-sort chest"', integration)
        self.assertIn("_chestToggleAnchor.transform.SetParent(gui.m_container, false)", integration)
        self.assertIn("_chestToggle.SetIsOnWithoutNotify(enabled)", integration)
        self.assertIn("ChestSortPreferences.TrySet(_boundChestPreferenceKey, enabled)", integration)
        self.assertIn("Player.m_localPlayer.GetPlayerID()", preferences)
        self.assertIn("ZNet.instance.GetWorldUID()", preferences)
        self.assertIn("zdo.m_uid.IsNone()", preferences)
        self.assertIn("zdo.m_uid.ToString()", preferences)
        self.assertIn("PlayerPrefs.HasKey(key)", preferences)
        self.assertIn("PlayerPrefs.SetInt(key, 0)", preferences)
        self.assertIn("PlayerPrefs.DeleteKey(key)", preferences)
        self.assertIn("PlayerPrefs.Save()", preferences)
        self.assertNotIn("m_customData", preferences)
        self.assertNotIn("SetOwner", preferences)
        self.assertNotIn("ZDO.Set", preferences)
        self.assertIn('KeyPrefix = "com.jstack424.stackmaster/chest-auto-sort/v1"', policy)
        self.assertIn("if (!hasStoredValue)", policy)
        self.assertIn("enabled = true", policy)
        self.assertIn('RequireMethod(failures, typeof(Player), "GetPlayerID")', gate)
        self.assertIn('RequireMethod(failures, typeof(ZNet), "GetWorldUID")', gate)
        self.assertIn('RequireField(failures, typeof(InventoryGui), "m_container")', gate)

    def test_enabled_chest_sorts_on_open_and_close_without_sorting_player_on_close(self):
        integration = (PLUGIN_DIR / "InventoryIntegration.cs").read_text(encoding="utf-8")
        hide_patch = integration[integration.index("internal static class InventoryGuiHidePatch"):]
        self.assertIn("private static void Prefix()", hide_patch)
        self.assertIn("InventoryIntegration.SortClosingChest();", hide_patch)
        installer = (PLUGIN_DIR / "PatchInstaller.cs").read_text(encoding="utf-8") + (PLUGIN_DIR / "HarmonyTargetManifest.cs").read_text(encoding="utf-8")
        self.assertIn('Both(typeof(InventoryGui), "Hide", false, typeof(void), Type.EmptyTypes, typeof(InventoryGuiHidePatch))', installer)
        close_start = integration.index("internal static void SortClosingChest()")
        close_end = integration.index("internal static void OnInventoryHidden()", close_start)
        close_body = integration[close_start:close_end]
        self.assertIn("_sortingChestOnClose", close_body)
        self.assertIn("_boundChest", close_body)
        self.assertIn("container.GetType() != typeof(Container)", close_body)
        self.assertIn("!container.IsOwner()", close_body)
        self.assertIn("ChestSortPreferences.TryGet(container", close_body)
        self.assertIn("!enabled", close_body)
        self.assertIn("SortExecutor.Sort(container.GetInventory(), false, null, null", close_body)
        self.assertNotIn("player.GetInventory()", close_body)
        self.assertNotIn("Player.m_localPlayer", close_body)
        self.assertIn("catch (Exception exception)", close_body)
        self.assertIn("finally", close_body)
        self.assertIn("UnbindChestToggle();", integration[integration.index("internal static void OnInventoryHidden()"):
                                                         integration.index("private static void HideProtectionOverlays")])
        open_start = integration.index("internal static void SortOpenedInventories(Container container)")
        open_end = close_start
        open_body = integration[open_start:open_end]
        self.assertIn("ChestSortPreferences.TryGet(container", open_body)
        self.assertIn("chestAutoSortEnabled", open_body)
        self.assertIn("SortExecutor.Sort(container.GetInventory(), false, null, null", open_body)

    def test_explicit_target_and_ordinary_base_are_inspected_before_budget_can_stop_search(self):
        discovery = (PLUGIN_DIR / "ContainerDiscovery.cs").read_text(encoding="utf-8")
        target_inspection = discovery.index("handles.Add(Inspect(player, target, catalog, targetDistance, true, targetDiagnostic, resourceReadOnly))")
        object_search = discovery.index("FindObjectsByType<Container>")
        inspection_timer = discovery.index("var inspectionStopwatch = Stopwatch.StartNew()")
        budget_check = discovery.index("NearbyPolicy.CanInspectNext")
        nearby_inspection = discovery.index("handles.Add(Inspect(player, candidate.Container")
        self.assertLess(target_inspection, object_search)
        self.assertLess(object_search, inspection_timer)
        self.assertLess(inspection_timer, budget_check)
        self.assertLess(budget_check, nearby_inspection)
        self.assertIn("MinimumNearbyContainersBeforeBudget = 8", discovery)
        self.assertIn("MaximumNearbyContainers = 128", discovery)
        self.assertIn("SearchBudgetMilliseconds = 25.0", discovery)
        self.assertIn("container != target", discovery)
        self.assertIn("var truncated = inspectedNearby < nearby.Length", discovery)
        self.assertIn("observedType == typeof(Container)", discovery)
        self.assertIn("TryRefreshFromNetwork(container, out refreshFailure)", discovery)
        self.assertIn("TryCheckAccess(player, container, out accessFailure)", discovery)

    def test_release_build_normalizes_and_verifies_sourcelink_paths(self):
        props = (ROOT / "Directory.Build.props").read_text(encoding="utf-8")
        build = (ROOT / "scripts" / "build.sh").read_text(encoding="utf-8")
        verifier = (ROOT / "scripts" / "verify_sourcelink.py").read_text(encoding="utf-8")

        self.assertIn("<Deterministic>true</Deterministic>", props)
        self.assertIn("<ContinuousIntegrationBuild>true</ContinuousIntegrationBuild>", props)
        self.assertIn("verify_sourcelink.py", build)
        self.assertIn('"/_/*"', verifier)
        self.assertIn("raw.githubusercontent.com/JStack424/Stackmaster", verifier)

    def test_manifest_is_the_only_harmony_target_declaration_and_resolution_path(self):
        manifest = (PLUGIN_DIR / "HarmonyTargetManifest.cs").read_text(encoding="utf-8")
        installer = (PLUGIN_DIR / "PatchInstaller.cs").read_text(encoding="utf-8")
        plugin = PLUGIN.read_text(encoding="utf-8")
        patch_sources = "\n".join(path.read_text(encoding="utf-8") for path in PLUGIN_DIR.glob("*.cs"))

        self.assertNotIn("[HarmonyPatch(", patch_sources)
        self.assertIn("PreserveDuringCleanup", manifest)
        self.assertIn("ResolveCleanupSafety", manifest)
        self.assertIn("HarmonyTargetManifest.ResolveCleanupSafety()", installer)
        self.assertIn("PatchInstaller.Install(safetyHarmony, PatchInstaller.PrepareCleanupSafety())", plugin)
        self.assertNotIn("AccessTools.DeclaredMethod", plugin)

    def test_input_paths_are_narrow_and_modal_safe(self):
        action = (PLUGIN_DIR / "StorageAction.cs").read_text(encoding="utf-8")
        protection = (PLUGIN_DIR / "ProtectionInteraction.cs").read_text(encoding="utf-8")
        for guard in (
            "InventoryGui.IsVisible()",
            "TextInput.IsVisible()",
            "UnifiedPopup.IsVisible()",
            "Menu.IsVisible()",
            "Console.IsVisible()",
            "Minimap.IsOpen()",
            "Hud.InRadial()",
            "Chat.instance.HasFocus()",
        ):
            self.assertIn(guard, action)
        self.assertIn("plugin.StorageActionShortcut.Value.Modifiers.ToArray()", protection)
        self.assertIn("modifiers.Length > 0 && modifiers.All(Input.GetKey)", protection)
        self.assertIn("ReferenceEquals(grid.GetInventory(), player.GetInventory())", protection)
        self.assertIn("item != null", protection)
        self.assertNotIn("[HarmonyPatch(", protection)
        self.assertIn('Prefix(typeof(InventoryGui), "OnRightClickItem"',
                      (PLUGIN_DIR / "HarmonyTargetManifest.cs").read_text(encoding="utf-8"))
        self.assertIn("HarmonyTargetManifest.Validate(failures)",
                      (PLUGIN_DIR / "CompatibilityGate.cs").read_text(encoding="utf-8"))

    def test_open_container_shortcut_uses_open_target_without_closing_inventory(self):
        action = (PLUGIN_DIR / "StorageAction.cs").read_text(encoding="utf-8")
        discovery = (PLUGIN_DIR / "ContainerDiscovery.cs").read_text(encoding="utf-8")
        executor = (PLUGIN_DIR / "TransferExecutor.cs").read_text(encoding="utf-8")
        installer = (PLUGIN_DIR / "PatchInstaller.cs").read_text(encoding="utf-8") + (PLUGIN_DIR / "HarmonyTargetManifest.cs").read_text(encoding="utf-8")
        gate = (PLUGIN_DIR / "CompatibilityGate.cs").read_text(encoding="utf-8") + (PLUGIN_DIR / "HarmonyTargetManifest.cs").read_text(encoding="utf-8")
        self.assertIn("var openContainer = CurrentOpenContainer(gui)", action)
        self.assertIn("gui.IsContainerOpen()", action)
        self.assertIn('AccessTools.Field(typeof(InventoryGui), "m_currentContainer")', action)
        self.assertIn('ZInput.ResetButtonStatus("Use")', action)
        self.assertIn("Begin(player, openContainer)", action)
        self.assertLess(action.index('ZInput.ResetButtonStatus("Use")'), action.index("Begin(player, openContainer)"))
        self.assertNotIn("[HarmonyPatch(", action)
        self.assertIn("return !RuntimeContext.Compatibility.IsCompatible || StorageAction.HandleOpenContainerShortcut(__instance)", action)
        self.assertIn("return false;", action[action.index("internal static bool HandleOpenContainerShortcut"):action.index("internal static bool IsLocalOpenTarget")])
        self.assertIn('Both(typeof(InventoryGui), "Update", false, typeof(void)', installer)
        self.assertIn("HarmonyTargetManifest.Validate(failures)", gate)
        self.assertIn('RequireMethod(failures, typeof(InventoryGui), "IsContainerOpen")', gate)
        self.assertIn('RequireStaticMethod(failures, typeof(ZInput), "ResetButtonStatus", typeof(string))', gate)
        self.assertIn('RequireMethod(failures, typeof(SplitDialog), "get_IsActive")', gate)
        self.assertIn('RequireField(failures, typeof(InventoryGui), "m_currentContainer")', gate)
        self.assertIn('AccessTools.Field(typeof(InventoryGui), "m_craftTimer")', action)
        self.assertIn('AccessTools.Field(typeof(InventoryGui), "m_dragItem")', action)
        self.assertIn('RequireField(failures, typeof(InventoryGui), "m_craftTimer")', gate)
        self.assertIn('RequireField(failures, typeof(InventoryGui), "m_dragItem")', gate)
        self.assertIn("InventoryUiHasBlockingState(gui)", action)
        for guard in (
            "player.IsDead()", "player.InCutscene()", "player.IsTeleporting()",
            "textViewer.IsVisible()", "GameCamera.InFreeFly()",
        ):
            self.assertIn(guard, action)
        for modal in (
            "m_trophiesPanel", "m_achievementsPanel", "m_skillsDialog", "m_textsDialog",
            "m_splitDialog", "m_variantDialog",
        ):
            self.assertIn(modal, action)
            self.assertIn(f'RequireField(failures, typeof(InventoryGui), "{modal}")', gate)
        self.assertIn("isTarget && StorageAction.IsLocalOpenTarget(container)", discovery)
        self.assertIn("handle.Snapshot.IsTarget && StorageAction.IsLocalOpenTarget(container)", executor)
        self.assertIn("(!locallyOpenTarget && container.IsInUse())", discovery)
        self.assertIn("(!locallyOpenTarget && container.IsInUse())", executor)
        self.assertIn("(!allowOpenContainerUi && InventoryGui.IsVisible())", action)
        self.assertNotIn("InventoryIntegration.RefreshProtectionOverlays", action)

    def test_protection_clicks_split_instant_toggle_from_target_prompt(self):
        protection = (PLUGIN_DIR / "ProtectionInteraction.cs").read_text(encoding="utf-8")
        core = (ROOT / "src" / "Stackmaster.Core" / "ProtectionInteractionPolicy.cs").read_text(encoding="utf-8")
        left_start = protection.index("internal static void HandleLeftClick")
        right_start = protection.index("internal static void HandleRightClick")
        left_body = protection[left_start:right_start]
        self.assertIn("ProtectionInteractionPolicy.ApplyLeftClick", left_body)
        self.assertNotIn("RequestText", left_body)
        self.assertIn("ProtectionInteractionPolicy.Route", protection[right_start:])
        self.assertIn("ProtectionClickRoute.SuppressWithoutChange", protection[right_start:])
        right_body = protection[right_start:protection.index("private static bool ConfiguredModifiersHeld", right_start)]
        self.assertNotIn("Unprotect", right_body)
        self.assertIn("protectedRecord?.TargetQuantity ?? _maxStack", protection)
        self.assertIn("Math.Max(4, defaultTargetText.Length)", protection)
        request = protection.index("TextInput.instance.RequestText")
        select = protection.index("SelectPrefilledTarget(TextInput.instance", request)
        self.assertLess(request, select)
        self.assertIn("inputField.selectionAnchorPosition = 0", protection)
        self.assertIn("inputField.selectionFocusPosition = textLength", protection)
        self.assertIn("target >= 1", protection)
        self.assertIn("target <= _maxStack", protection)
        self.assertIn("ProtectionInteractionPolicy.TryApplyTarget", protection)
        self.assertIn("state.Protect(slot, null, itemKey)", core)
        self.assertIn("state.Unprotect(existingRecord)", core)
        self.assertIn("state.Protect(slot, target, itemKey)", core)

    def test_ownership_flow_recaptures_after_network_refresh(self):
        action = (PLUGIN_DIR / "StorageAction.cs").read_text(encoding="utf-8")
        refresh_comment = action.index("Re-capture and re-plan from the synchronized inventories")
        fresh_capture = action.index("InventorySnapshots.CapturePlayer", refresh_comment)
        fresh_discovery = action.index("ContainerDiscovery.Discover", refresh_comment)
        mutation = action.index("TransferExecutor.Execute", refresh_comment)
        self.assertLess(fresh_capture, mutation)
        self.assertLess(fresh_discovery, mutation)
        self.assertIn('LogTargetRejection("post-ownership refresh", freshDiscovery)', action)
        ownership = (PLUGIN_DIR / "OwnershipCoordinator.cs").read_text(encoding="utf-8")
        self.assertIn("RPC_StackResponse", ownership)
        self.assertIn("LateResponseSuppressions", ownership)
        self.assertIn("previous ownership response is still pending", ownership)
        self.assertIn("Even after a session-fatal disable", ownership)
        self.assertNotIn("!RuntimeContext.Compatibility.IsCompatible || !OwnershipCoordinator.HandleResponse", ownership)

    def test_acquired_ownership_uses_exact_bounded_leases_and_vanilla_release(self):
        ownership = (PLUGIN_DIR / "OwnershipCoordinator.cs").read_text(encoding="utf-8")
        policy = (ROOT / "src" / "Stackmaster.Core" / "OwnershipLeasePolicy.cs").read_text(encoding="utf-8")
        plugin = PLUGIN.read_text(encoding="utf-8")

        self.assertIn("RetryLeaseSeconds = 10f", ownership)
        self.assertIn("AcquiredOwnerships", ownership)
        self.assertIn("!handle.NetworkView.HasOwner()", ownership)
        self.assertIn('FailedContainerIds[handle.Id] = "container is temporarily unowned"', ownership)
        self.assertIn("handle.ResourceOwner", ownership)
        self.assertIn("AcquiredSession", ownership)
        self.assertIn("ZDOMan.GetSessionID() == acquisition.AcquiredSession", ownership)
        self.assertIn("OwnershipLeasePolicy.NextOwnerRevision(handle.ResourceOwnerRevision)", ownership)
        self.assertIn("zdo.OwnerRevision == expectedOwnerRevision", ownership)
        self.assertIn("OwnershipLeaseManager.Update()", ownership)
        self.assertIn('Postfix(typeof(ZNet), "Update"', (PLUGIN_DIR / "HarmonyTargetManifest.cs").read_text(encoding="utf-8"))
        self.assertIn("OwnershipLeasePolicy.Decide", ownership)
        self.assertIn("zdo.SetOwner(decision.TargetOwner)", ownership)
        self.assertIn("ForceSendZDO(zdo.m_uid)", ownership)
        self.assertIn("return new OwnershipReleaseDecision(true, 0)", policy)
        self.assertNotIn("GetServerPeerID", ownership)
        self.assertNotIn("IsPeerConnected", ownership)
        self.assertNotIn("previousOwnerConnected", policy)
        self.assertNotIn("serverOwner", policy)
        release = ownership[ownership.index("private static bool TryRelinquish") : ownership.index("internal static class OwnershipCoordinator")]
        self.assertIn("StorageAction.IsLocalOpenTarget(container) || container.IsInUse()", release)
        self.assertNotIn("container.SetInUse(false)", release)
        self.assertIn("Leases[acquisition.Id] = new OwnershipLease", ownership)

    def test_build_ownership_lease_is_sliding_scoped_and_preemptible(self):
        ownership = (PLUGIN_DIR / "OwnershipCoordinator.cs").read_text(encoding="utf-8")
        nearby = (PLUGIN_DIR / "NearbyResources.cs").read_text(encoding="utf-8")
        installer = (PLUGIN_DIR / "PatchInstaller.cs").read_text(encoding="utf-8") + (PLUGIN_DIR / "HarmonyTargetManifest.cs").read_text(encoding="utf-8")
        gate = (PLUGIN_DIR / "CompatibilityGate.cs").read_text(encoding="utf-8") + (PLUGIN_DIR / "HarmonyTargetManifest.cs").read_text(encoding="utf-8")

        self.assertIn("BuildingLeaseSeconds = 30f", ownership)
        self.assertIn("OwnershipLeasePurpose.Building", ownership)
        self.assertIn("RenewForSuccessfulBuild", ownership)
        self.assertIn("LeaseStillMatchesExactAcquisition", ownership)
        self.assertIn("IdentityMatches(usedHandle, zdo)", ownership)
        self.assertIn("zdo.OwnerRevision == acquisition.AcquiredOwnerRevision", ownership)
        self.assertIn("lease.ExpiresAt = OwnershipLeaseRetentionPolicy.RenewedExpiry(now, BuildingLeaseSeconds)", ownership)
        self.assertIn("Clear(actionKind == ResourceActionKind.Building)", nearby)
        self.assertIn("ResourceTransactionContext.Complete(ResourceActionKind.Crafting)", nearby)
        self.assertIn("ResourceTransactionContext.Complete(ResourceActionKind.Building)", nearby)
        self.assertIn("if (retainSuccessfulBuildOwnership)", nearby)
        self.assertIn("OwnershipLeaseManager.RenewForSuccessfulBuild(held.Select(item => item.Handle))", nearby)
        transaction_release = nearby[
            nearby.index("private static void ReleaseReservations("):
            nearby.index("internal sealed class RuntimeResourceStack")
        ]
        self.assertLess(
            transaction_release.index("NearbyResourceService.ReleaseReservations"),
            transaction_release.index("if (retainSuccessfulBuildOwnership)"),
        )
        self.assertIn("ObserveRemoteManualOpen", ownership)
        self.assertIn("requesterSession != acquiredSession", (ROOT / "src" / "Stackmaster.Core" / "OwnershipLeasePolicy.cs").read_text(encoding="utf-8"))
        self.assertIn("Leases.Remove(lease.Acquisition.Id)", ownership)
        self.assertIn("never set owner 0", ownership)
        self.assertIn('Postfix(typeof(Container), "RPC_RequestOpen", false, typeof(void)', installer)
        self.assertIn("HarmonyTargetManifest.Validate(failures)", gate)

    def test_all_terminal_paths_release_only_exact_stackmaster_acquisitions(self):
        nearby = (PLUGIN_DIR / "NearbyResources.cs").read_text(encoding="utf-8")
        storage = (PLUGIN_DIR / "StorageAction.cs").read_text(encoding="utf-8")
        ownership = (PLUGIN_DIR / "OwnershipCoordinator.cs").read_text(encoding="utf-8")

        self.assertIn("OwnershipLeaseManager.HoldForRetry(ownership)", nearby)
        self.assertIn("if (!keepRetryLease)", nearby)
        self.assertIn("OwnershipLeaseManager.ReleaseBatch(ownership", nearby)
        self.assertIn("if (!transactionBegan)", nearby)
        self.assertIn("OwnershipLeaseManager.ReleaseMatching(requiredHandles", nearby)
        transaction_release = nearby.index("OwnershipLeaseManager.ReleaseMatching(held.Select(item => item.Handle)")
        reservation_clear = nearby.rfind("container.SetInUse(false)", 0, transaction_release)
        self.assertLess(reservation_clear, transaction_release)
        rollback = nearby[nearby.index("internal static bool Rollback()") : nearby.index("private static void Clear(bool retainSuccessfulBuildOwnership)")]
        self.assertLess(rollback.index("NearbyResourceService.Rollback"), rollback.index("ReleaseReservations"))
        self.assertIn('OwnershipLeaseManager.ReleaseBatch(ownership, "storage action ended")', storage)
        self.assertLess(storage.index('OwnershipLeaseManager.ReleaseBatch(ownership, "storage action ended")'),
                        storage.index("OwnershipCoordinator.End(ownership)", storage.index("private static IEnumerator FinishAfterOwnership")))
        self.assertIn("foreach (var acquisition in batch.AcquiredOwnerships", ownership)
        self.assertNotIn("Leases[handle.Id]", ownership)

    def test_timeout_disconnect_disable_and_hot_unload_keep_ownership_cleanup_safe(self):
        ownership = (PLUGIN_DIR / "OwnershipCoordinator.cs").read_text(encoding="utf-8")
        runtime = (PLUGIN_DIR / "RuntimeContext.cs").read_text(encoding="utf-8")
        installer = (PLUGIN_DIR / "PatchInstaller.cs").read_text(encoding="utf-8") + (PLUGIN_DIR / "HarmonyTargetManifest.cs").read_text(encoding="utf-8")
        plugin = PLUGIN.read_text(encoding="utf-8")

        self.assertIn("TimedOutAcquisitionHandle", ownership)
        self.assertIn("TimedOutAcquisitionHandle = _waitingHandle", ownership)
        self.assertIn("var handle = _waitingHandle", ownership)
        self.assertNotIn("if (!_grantReceived || _waitingFor == null)", ownership)
        self.assertIn("WatchPotentialAcquisition", ownership)
        self.assertIn("PendingOwnershipCleanup", ownership)
        self.assertIn("late ownership update after timed-out request", ownership)
        self.assertIn("ZDOMan.GetSessionID() != pending.RequestSession", ownership)
        self.assertIn("HasPotentialAcquisition(handle.Id)", ownership)
        self.assertIn("requiredHandles.Any(handle => OwnershipLeaseManager.HasPotentialAcquisition(handle.Id))", (PLUGIN_DIR / "NearbyResources.cs").read_text(encoding="utf-8"))
        self.assertIn("ZDOMan.instance.GetZDO(handle.ResourceZdoId)", ownership)
        self.assertIn("Keep retrying through scene unload", ownership)
        release_all = ownership[ownership.index("internal static void ReleaseAll") : ownership.index("private static void Release(")]
        self.assertNotIn("Pending.Clear()", release_all)
        ended_session = ownership[ownership.index("internal static void DiscardEndedSessionState()") : ownership.index("internal static void CancelPotentialAcquisition")]
        self.assertIn("Pending.Clear()", ended_session)
        self.assertIn("Leases.Clear()", ended_session)
        self.assertNotIn("pending.ExpiresAt", ownership)
        self.assertIn("if (!granted) OwnershipLeaseManager.CancelPotentialAcquisition(container)", ownership)
        self.assertIn("internal static void Shutdown()", (PLUGIN_DIR / "NearbyResources.cs").read_text(encoding="utf-8"))
        self.assertLess(runtime.index("ResourceTransactionContext.Shutdown"), runtime.index("OwnershipCoordinator.Shutdown(reason)"))
        self.assertIn('Prefix(typeof(Game), "Shutdown"', installer)
        self.assertIn('Prefix(typeof(ZNet), "Shutdown"', installer)
        self.assertIn('Prefix(typeof(ZNet), "ShutdownWithoutSave"', installer)
        self.assertIn("private void OnDisable()", plugin)
        self.assertIn('RuntimeContext.Disable("plugin disabled")', plugin)
        self.assertIn("OwnershipCoordinator.HasUnresolvedCleanup", plugin)
        self.assertIn("NearbyResourceService.HasPendingReservationReleases", plugin)
        self.assertIn('PluginGuid + ".ownership-cleanup-safety"', plugin)
        self.assertIn("OwnershipSafetyUpdatePatch", installer)
        self.assertIn("shutdownRelease", ownership)
        shutdown = ownership[ownership.index("internal static void Shutdown(string reason)") : ownership.index("internal static void SuppressLateResponse")]
        self.assertIn("active.Refresh()", shutdown)
        self.assertIn("finally", shutdown)
        self.assertIn("OwnershipLeaseManager.ReleaseAll(reason)", shutdown)
        self.assertIn("catch (Exception exception)", shutdown)
        self.assertIn("PatchInstaller.Install(safetyHarmony, PatchInstaller.PrepareCleanupSafety())", plugin)
        self.assertLess(plugin.index("PatchInstaller.Install(safetyHarmony"), plugin.index("_harmony?.UnpatchSelf()", plugin.index("private void OnDestroy")))
        self.assertNotIn("AccessTools.DeclaredMethod", plugin)

    def test_disconnect_is_session_scoped_and_reconnect_rearms_only_after_full_cleanup(self):
        runtime = (PLUGIN_DIR / "RuntimeContext.cs").read_text(encoding="utf-8")
        ownership = (PLUGIN_DIR / "OwnershipCoordinator.cs").read_text(encoding="utf-8")
        nearby = (PLUGIN_DIR / "NearbyResources.cs").read_text(encoding="utf-8")
        storage = (PLUGIN_DIR / "StorageAction.cs").read_text(encoding="utf-8")
        quick_grab = (PLUGIN_DIR / "QuickGrabMaterialsAction.cs").read_text(encoding="utf-8")
        plugin = PLUGIN.read_text(encoding="utf-8")

        lifecycle_patch = ownership[ownership.index("internal static class OwnershipLifecyclePatch") : ownership.index("internal static class OwnershipSafetyUpdatePatch")]
        self.assertIn("RuntimeContext.DisconnectSession()", lifecycle_patch)
        self.assertNotIn("RuntimeContext.Disable", lifecycle_patch)

        disconnect = runtime[runtime.index("internal static void DisconnectSession()") : runtime.index("internal static void TryRearmSession")]
        self.assertIn("_lifecycle.BeginDisconnect()", disconnect)
        self.assertIn('Compatibility = new CompatibilityResult(false, "between server sessions")', disconnect)
        self.assertIn('RunSafetyCleanup("session disconnect")', disconnect)

        cleanup = runtime[runtime.index("private static bool RunSafetyCleanup") : runtime.index("private static bool TryCleanup")]
        ordered = [
            "ResourceTransactionContext.Shutdown",
            "QuickGrabMaterialsAction.Shutdown",
            "StorageAction.Shutdown",
            "NearbyResourceOwnership.Shutdown",
            "NearbyResourceService.FlushPendingReservationReleasesBeforeOwnershipShutdown",
            "OwnershipCoordinator.Shutdown",
            "ResourceActionContext.Reset",
            "InventoryIntegration.OnSessionDisconnected",
            "ChestSortPreferences.Shutdown",
            "StorageScopeProvider.Reset",
            "NearbyResourceService.ResetCaches",
            "NearbyBuildHudPatch.ResetCache",
            "NearbyCraftingHudPatch.ResetCache",
            "NearbyHudFailOpen.ResetSession",
        ]
        positions = [cleanup.index(item) for item in ordered]
        self.assertEqual(positions, sorted(positions))

        rearm = runtime[runtime.index("internal static void TryRearmSession") : runtime.index("internal static void Disable")]
        self.assertIn("ReferenceEquals(network, _disconnectingNetwork)", rearm)
        self.assertLess(rearm.index("NearbyResourceService.DiscardEndedSessionState()"), rearm.index("_lifecycle.TryRearm"))
        self.assertLess(rearm.index("OwnershipCoordinator.DiscardEndedSessionState()"), rearm.index("_lifecycle.TryRearm"))
        self.assertLess(rearm.index("StorageAction.RearmSession()"), rearm.index("Compatibility = _verifiedCompatibility"))
        self.assertLess(rearm.index("NearbyResourceOwnership.RearmSession()"), rearm.index("Compatibility = _verifiedCompatibility"))
        self.assertLess(rearm.index("QuickGrabMaterialsAction.RearmSession()"), rearm.index("Compatibility = _verifiedCompatibility"))
        self.assertIn("_disconnectCleanupCompleted = RunSafetyCleanup", disconnect)
        self.assertIn("cleanupCompletedSafely: _disconnectCleanupCompleted", rearm)
        self.assertNotIn("cleanupCompletedSafely: true", rearm)
        self.assertIn("throw new InvalidOperationException(", ownership[ownership.index("internal static void Shutdown(string reason)") : ownership.index("private static void LogCleanupFailure")])
        self.assertIn("_lifecycle.DisablePermanently()", runtime)
        self.assertIn('RuntimeContext.Disable("plugin disabled")', plugin)
        self.assertIn('RuntimeContext.Disable("plugin unloading")', plugin)
        self.assertIn("if (RuntimeContext.IsAwaitingReconnect)", plugin)

        safety_update = ownership[ownership.index("internal static class OwnershipSafetyUpdatePatch") : ownership.index("internal static class ContainerOpenRequestLeasePatch")]
        self.assertLess(safety_update.index("UpdatePendingReservationReleases"), safety_update.index("TryRearmSession"))
        self.assertLess(safety_update.index("OwnershipLeaseManager.Update"), safety_update.index("TryRearmSession"))
        self.assertIn("LateResponseSuppressions.Clear()", ownership)
        self.assertIn("PendingReservationReleases.Clear()", nearby)
        self.assertIn("_generation++", storage)
        self.assertIn("generation == _generation", storage)
        self.assertIn("_generation++", nearby)
        self.assertIn("generation == _generation", nearby)
        self.assertIn("_generation++", quick_grab)
        self.assertIn("generation == _generation", quick_grab)
        for action in (storage, nearby, quick_grab):
            self.assertIn("generation != _generation || !RuntimeContext.Compatibility.IsCompatible", action)
            self.assertIn("if (generation == _generation)", action)
        self.assertIn("!SameAcquisition(existing.Acquisition, acquisition)", ownership)
        self.assertIn("SameReservation(pending.Reservation, reservation)", nearby)

    def test_protected_item_indicators_use_reconciled_assignments_and_do_not_intercept_input(self):
        integration = (PLUGIN_DIR / "InventoryIntegration.cs").read_text(encoding="utf-8")
        interaction = (PLUGIN_DIR / "ProtectionInteraction.cs").read_text(encoding="utf-8")
        snapshots = (PLUGIN_DIR / "InventorySnapshots.cs").read_text(encoding="utf-8")
        self.assertIn("InventorySnapshots.ResolveProtection(player, state)", integration)
        self.assertIn("resolution.TryGet(new Slot(element.Position.x, element.Position.y), out record)", integration)
        self.assertIn("ProtectedBorderColor", integration)
        self.assertIn("new Color(0.22f, 0.78f, 0.84f, 0.82f)", integration)
        self.assertIn("targetLabel.color = ProtectedBorderColor", integration)
        self.assertIn("targetLabel.alignment = TextAlignmentOptions.TopLeft", integration)
        self.assertIn("targetRect.anchorMin = new Vector2(0f, 1f)", integration)
        self.assertIn("targetRect.anchorMax = new Vector2(0f, 1f)", integration)
        self.assertIn("targetRect.pivot = new Vector2(0f, 1f)", integration)
        self.assertIn("targetRect.anchoredPosition = new Vector2(3f, -2f)", integration)
        self.assertIn("lockRect.anchorMin = new Vector2(0f, 1f)", integration)
        self.assertIn("lockRect.anchorMax = new Vector2(0f, 1f)", integration)
        self.assertIn("lockRect.pivot = new Vector2(0f, 1f)", integration)
        self.assertIn("lockRect.anchoredPosition = new Vector2(3f, -3f)", integration)
        self.assertNotIn("targetLabel.alignment = TextAlignmentOptions.BottomLeft", integration)
        self.assertNotIn("targetLabel.color = Color.white", integration)
        self.assertIn("_targetLabel.gameObject.SetActive(hasTarget)", integration)
        self.assertIn("_lockIcon.SetActive(!hasTarget)", integration)
        self.assertIn("image.raycastTarget = false", integration)
        self.assertIn("targetLabel.raycastTarget = false", integration)
        self.assertNotIn("InventoryGuiUpdatePatch", integration)
        self.assertNotIn("[HarmonyPatch(", integration)
        self.assertIn("_observedPlayerInventory.m_onChanged += InventoryChangedHandler", integration)
        self.assertIn("_observedPlayerInventory.m_onChanged -= InventoryChangedHandler", integration)
        self.assertIn("_overlayRefreshPending = true", integration)
        self.assertIn('Postfix(typeof(InventoryGrid), "UpdateInventory"',
                      (PLUGIN_DIR / "HarmonyTargetManifest.cs").read_text(encoding="utf-8"))
        self.assertIn("InventoryIntegration.FlushPendingProtectionOverlayRefresh(__instance, inventory)", integration)
        self.assertIn("!ReferenceEquals(grid, gui.m_playerGrid)", integration)
        self.assertIn("InventoryIntegration.BindPlayerInventory(Player.m_localPlayer)", integration)
        self.assertIn("InventoryIntegration.RequestProtectionOverlayRefresh()", integration)
        self.assertGreaterEqual(interaction.count("InventoryIntegration.RefreshProtectionOverlays()"), 2)
        self.assertIn("HideProtectionOverlays();", integration)
        self.assertIn("DestroyProtectionOverlays();", integration)
        self.assertIn("protectionResolution.Assignments.Keys", snapshots)

    def test_manual_item_exit_clears_only_after_confirmed_external_removal_and_hotkey_prunes_orphans(self):
        lifecycle = (PLUGIN_DIR / "ProtectionLifecycle.cs").read_text(encoding="utf-8")
        installer = (PLUGIN_DIR / "PatchInstaller.cs").read_text(encoding="utf-8") + (PLUGIN_DIR / "HarmonyTargetManifest.cs").read_text(encoding="utf-8")
        gate = (PLUGIN_DIR / "CompatibilityGate.cs").read_text(encoding="utf-8") + (PLUGIN_DIR / "HarmonyTargetManifest.cs").read_text(encoding="utf-8")
        snapshots = (PLUGIN_DIR / "InventorySnapshots.cs").read_text(encoding="utf-8")
        action = (PLUGIN_DIR / "StorageAction.cs").read_text(encoding="utf-8")
        core = (ROOT / "src" / "Stackmaster.Core" / "ProtectionState.cs").read_text(encoding="utf-8")

        self.assertIn('Both(typeof(InventoryGui), "OnSelectedItem"', installer)
        self.assertIn('Both(typeof(InventoryGui), "OnDropOutside"', installer)
        self.assertIn("ManualProtectionExitSelectionPatch", installer)
        self.assertIn("ManualProtectionExitDropOutsidePatch", installer)
        self.assertIn("ReferenceEquals(dragInventory, playerInventory)", lifecycle)
        self.assertIn("!ReferenceEquals(destination, playerInventory)", lifecycle)
        self.assertIn("modifier == InventoryGrid.Modifier.Move", lifecycle)
        self.assertIn("modifier == InventoryGrid.Modifier.Drop", lifecycle)
        self.assertIn("attempt.SourceInventory.ContainsItem(attempt.ProtectedItem)", lifecycle)
        self.assertIn("ProtectionExitPolicy.ShouldClear", lifecycle)
        self.assertIn("attempt.Protection.Unprotect(attempt.Record)", lifecycle)
        self.assertIn("InventoryIntegration.RefreshProtectionOverlays()", lifecycle)
        self.assertNotIn('Patch(typeof(Inventory), "MoveItemToThis"', installer)
        self.assertIn("HarmonyTargetManifest.Validate(failures)", gate)
        self.assertIn('RequireField(failures, typeof(InventoryGui), "m_dragInventory")', gate)
        self.assertIn("protection.Reconcile(candidates, pruneUnresolved)", snapshots)
        self.assertIn("if (pruneUnresolved)", core)
        self.assertGreaterEqual(action.count("pruneUnresolvedProtection: true"), 2)
        self.assertIn("ResolveProtection(player, protection, pruneUnresolved: true)", action)

    def test_generic_target_rejection_is_paired_with_local_diagnostics(self):
        action = (PLUGIN_DIR / "StorageAction.cs").read_text(encoding="utf-8")
        discovery = (PLUGIN_DIR / "ContainerDiscovery.cs").read_text(encoding="utf-8")
        generic = 'targeted container is inaccessible, in use, unknown, or outside the active storage scope.'
        self.assertIn(generic, action)
        initial_log = action.index('LogTargetRejection("initial discovery", discovery)')
        initial_ui = action.index(generic, initial_log)
        self.assertLess(initial_log, initial_ui)
        self.assertIn("RuntimeContext.Plugin.Log.LogWarning", action)
        for signal in (
            "targetPresent=", "discovered=", "distance=", "scope=", "withinScope=",
            "searchTruncated=", "truncationReason=", "searchMs=", "objectScanMs=", "inspectionMs=",
            "budgetMs=", "candidates=", "inspected=", "minimumBeforeBudget=", "maximumNearby=", "type=", "vanilla=",
            "nview=", "nviewValid=", "zdo=", "refresh=", "inventory=", "inUse=", "access=",
        ):
            self.assertIn(signal, discovery)
        self.assertIn("RefreshFailure", discovery)
        self.assertIn("AccessFailure", discovery)

    def test_nearby_resource_paths_are_exact_fresh_and_fail_closed(self):
        nearby = (PLUGIN_DIR / "NearbyResources.cs").read_text(encoding="utf-8")
        core = (ROOT / "src" / "Stackmaster.Core" / "ResourceAccounting.cs").read_text(encoding="utf-8")
        installer = (PLUGIN_DIR / "PatchInstaller.cs").read_text(encoding="utf-8") + (PLUGIN_DIR / "HarmonyTargetManifest.cs").read_text(encoding="utf-8")
        gate = (PLUGIN_DIR / "CompatibilityGate.cs").read_text(encoding="utf-8") + (PLUGIN_DIR / "HarmonyTargetManifest.cs").read_text(encoding="utf-8")
        self.assertIn("GroupBy", core)
        self.assertIn("shortages.Count == 0 ? tentative", core)
        # HarmonyX binds unannotated patch arguments by the original parameter name.
        # Keep every game-method argument in the new resource hooks position-bound so
        # metadata names such as Valheim's Recipe `piece` cannot break startup.
        bindings = [int(index) for index in re.findall(r"\[HarmonyArgument\((\d+)\)\]", nearby)]
        self.assertEqual(
            [0, 1, 2, 3, 0, 1, 0, 0, 0, 1, 2, 3, 4, 5, 0, 1, 2, 3, 4, 5, 0, 0, 0, 1, 2, 3],
            bindings,
        )
        self.assertNotIn("RecipePostfix(Player __instance, Recipe recipe", nearby)
        self.assertIn("public int PlannedUnits", core)
        self.assertIn("TryBeginRecipeTransaction(player, ___m_craftRecipe, qualityLevel, multiplier, out failure)", nearby)
        self.assertIn("TryBeginPieceTransaction(__instance, piece, out failure)", nearby)
        self.assertIn("ContainerDiscovery.Discover(player, null, catalog, scope, true, true)", nearby)
        self.assertIn("handle.ResourceReadable", nearby)
        self.assertIn("handle.ResourceInventory", nearby)
        self.assertNotIn("handle.Snapshot.IsEligible &&\n                                 handle.NetworkView", nearby)
        self.assertIn("handle.NetworkView.IsOwner()", nearby)
        self.assertIn("handle.Container.IsOwner()", nearby)
        self.assertIn("handle.Container.IsInUse()", nearby)
        self.assertIn("ContainerDiscovery.CheckAccess", nearby)
        self.assertIn("ContainerDiscovery.RefreshFromNetwork", nearby)
        self.assertIn("ExecuteWithRollback", nearby)
        self.assertIn("Rollback(removed)", nearby)
        self.assertIn("actualRemoved != step.Quantity", nearby)
        self.assertIn("clone.m_stack = actualRemoved", nearby)
        self.assertIn("after == before + entry.Quantity", nearby)
        self.assertIn("AddItemAtMethod.Invoke", nearby)
        self.assertIn("ResourceTransactionContext.AcknowledgeVanillaRemoval(amount)", nearby)
        self.assertIn("ResourceTransactionContext.Complete(ResourceActionKind.Crafting)", nearby)
        self.assertIn("ResourceTransactionContext.Complete(ResourceActionKind.Building)", nearby)
        self.assertIn("RuntimeContext.Disable", nearby)
        self.assertIn('Transactional(typeof(InventoryGui), "DoCrafting"', installer)
        self.assertIn('Transactional(typeof(Player), "UpdatePlacement"', installer)
        self.assertIn('Both(typeof(Player), "TryPlacePiece"', installer)
        self.assertIn('Prefix(typeof(Inventory), "RemoveItem"', installer)
        for method in ("HaveRequirementItems", "HaveRequirements", "GetFirstRequiredItem", "DoCrafting", "UpdatePlacement", "TryPlacePiece"):
            self.assertIn(f'"{method}"', gate)

    def test_crafting_is_one_click_reserved_then_one_vanilla_bar_with_full_cleanup(self):
        action = (PLUGIN_DIR / "CraftingPreflightAction.cs").read_text(encoding="utf-8")
        nearby = (PLUGIN_DIR / "NearbyResources.cs").read_text(encoding="utf-8")
        installer = (PLUGIN_DIR / "PatchInstaller.cs").read_text(encoding="utf-8") + (PLUGIN_DIR / "HarmonyTargetManifest.cs").read_text(encoding="utf-8")
        runtime = (PLUGIN_DIR / "RuntimeContext.cs").read_text(encoding="utf-8")
        inventory = (PLUGIN_DIR / "InventoryIntegration.cs").read_text(encoding="utf-8")
        storage = (PLUGIN_DIR / "StorageAction.cs").read_text(encoding="utf-8")
        gate = (PLUGIN_DIR / "CompatibilityGate.cs").read_text(encoding="utf-8") + (PLUGIN_DIR / "HarmonyTargetManifest.cs").read_text(encoding="utf-8")
        ownership = (PLUGIN_DIR / "OwnershipCoordinator.cs").read_text(encoding="utf-8")

        self.assertIn('Transactional(typeof(InventoryGui), "OnCraftPressed"', installer)
        self.assertIn('Prefix(typeof(InventoryGui), "OnCraftCancelPressed"', installer)
        self.assertIn('Prefix(typeof(InventoryGui), "OnTabCraftPressed"', installer)
        self.assertIn('Prefix(typeof(InventoryGui), "OnTabUpgradePressed"', installer)
        self.assertIn('Prefix(typeof(InventoryGui), "OnSelectedRecipe"', installer)
        self.assertIn("CraftingPreflightAction.Update(__instance)", storage)
        self.assertIn("CraftingPreflightAction.Cancel", inventory)
        self.assertIn("Crafting preflight shutdown", runtime)

        self.assertIn("TryPlanCraftingResources", action)
        self.assertIn("plan.RequiredHandles", action)
        self.assertIn("Lifecycle.Begin(unowned.Length != 0)", action)
        self.assertIn("return false;", action[action.index("AcquireAndResume"):])
        self.assertEqual(1, action.count("OnCraftPressedMethod.Invoke"))
        self.assertIn("TryPrepareCraftingResources", action)
        self.assertIn("Lifecycle.MarkReserved", action)
        self.assertIn("Lifecycle.MarkCrafting", action)
        self.assertIn("TryBeginPreparedTransaction", nearby)
        self.assertIn("TryBeginPreparedCraftingTransaction", nearby)
        self.assertIn("CraftingInventorySignature", nearby)
        self.assertIn("SameWithdrawalPlan", nearby)
        self.assertIn("RevalidateReservedContainers", nearby)
        self.assertIn("TakeReservations", nearby)
        self.assertIn("ResourceTransactionContext.Begin", nearby)

        self.assertIn("CraftTimerField.SetValue(_intent.Gui, -1f)", action)
        self.assertIn("intent.Player.IsDead()", action)
        self.assertIn("intent.Player.IsTeleporting()", action)
        self.assertIn("intent.Player.InCutscene()", action)
        self.assertIn("intent.Station.CheckUsable(intent.Player, false)", action)
        self.assertIn("intent.Station.GetLevel(true) != intent.StationLevel", action)
        self.assertIn("intent.Player.transform.position - intent.PlayerPosition", action)
        self.assertIn("OwnershipCoordinator.Cancel(_ownership", action)
        self.assertIn("ReleasePreparedCraftingResources", action)
        self.assertIn("HoldForCrafting", ownership)
        self.assertIn("OwnershipLeasePurpose.Crafting", ownership)
        self.assertNotIn("try the action again", action.lower())
        for dependency in (
            "OnCraftPressed", "OnCraftCancelPressed", "OnTabCraftPressed", "OnTabUpgradePressed",
            "OnSelectedRecipe", "UpdateRecipe", "GetLevel", "CheckUsable", "RecipeDataPair", "ItemData",
            "m_selectedVariant", "m_craftVariant", "m_touchMultiCrafting",
        ):
            self.assertIn(f'"{dependency}"', gate)

    def test_remote_owned_resource_stock_is_read_only_and_claimed_only_for_exact_action_plan(self):
        nearby = (PLUGIN_DIR / "NearbyResources.cs").read_text(encoding="utf-8")
        discovery = (PLUGIN_DIR / "ContainerDiscovery.cs").read_text(encoding="utf-8")
        ownership = (PLUGIN_DIR / "OwnershipCoordinator.cs").read_text(encoding="utf-8")
        core = (ROOT / "src" / "Stackmaster.Core" / "ResourceAccounting.cs").read_text(encoding="utf-8")
        gate = (PLUGIN_DIR / "CompatibilityGate.cs").read_text(encoding="utf-8") + (PLUGIN_DIR / "HarmonyTargetManifest.cs").read_text(encoding="utf-8")

        # Display/cost discovery decodes ZDO inventory bytes into a detached Inventory and
        # contains no ownership request of any kind.
        self.assertIn("TryReadSerializedInventory", discovery)
        self.assertIn("zdo.GetByteArray(ZDOVars.s_items, null)", discovery)
        self.assertIn("var snapshot = new Inventory(true)", discovery)
        self.assertIn("snapshot.Load(new ZPackage(bytes))", discovery)
        self.assertIn("bool resourceReadOnly = false", discovery)
        self.assertIn("!resourceReadOnly && isVanilla", discovery)
        self.assertIn("true, true", nearby)
        self.assertIn("handle.ResourceReadable", nearby)
        self.assertIn("handle.ResourceInventory", nearby)
        capture = nearby[nearby.index("private static NearbyResourceCapture Capture"):nearby.index("private static void AddInventory")]
        self.assertNotIn("IsOwner()", capture)
        self.assertNotIn("StackAll()", capture)
        self.assertNotIn("ClaimOwnership", capture)

        # Action-time acquisition is derived from the complete player-first plan, requests only
        # its distinct chest ids, then requires unchanged revision, identity, ownership, access,
        # stack identity/quality/world level, and exact quantity before any removal.
        self.assertIn("ResourceOwnershipSelection.RequiredContainerIds", nearby)
        self.assertIn("TryPlanWithMinimumContainers", nearby)
        self.assertIn("MinimumContainerSearchNodeLimit", core)
        self.assertIn('item.m_gridPos.x.ToString(CultureInfo.InvariantCulture) + ","', nearby)
        self.assertNotIn("var width = inventory.GetWidth()", nearby)
        self.assertIn("var unowned = requiredHandles", nearby)
        self.assertIn("OwnershipCoordinator.Begin(unownedHandles)", nearby)
        self.assertIn("!handle.NetworkView.HasOwner()", nearby)
        self.assertIn("zdo.OwnerRevision == expectedOwnerRevision", ownership)
        self.assertIn("RequiredRevisionsMatch", nearby)
        self.assertIn("the exact minimum container plan changed before consumption", nearby)
        self.assertIn("RevalidateContainers", nearby)
        self.assertIn("RevalidateStacks", nearby)
        self.assertIn("TryReserveContainers", nearby)
        self.assertIn("SetInUse(true)", nearby)
        self.assertIn("SetInUse(false)", nearby)
        self.assertIn("CaptureRevisionBaseline", nearby)
        self.assertIn("zdo.DataRevision != reservation.DataRevision", nearby)
        self.assertIn("zdo.OwnerRevision != reservation.OwnerRevision", nearby)
        self.assertIn("ownerBaselines[handle.Id] != handle.ResourceOwnerRevision", nearby)
        transaction_start = nearby.index("ResourceTransactionContext.Begin(removed")
        removal_start = nearby.index("ExecuteWithRollback(player", transaction_start)
        self.assertLess(transaction_start, removal_start)
        rollback_context = nearby[nearby.index("internal static bool Rollback()") : nearby.index("private static void Clear(bool retainSuccessfulBuildOwnership)")]
        self.assertLess(rollback_context.index("NearbyResourceService.Rollback"), rollback_context.index("ReleaseReservations"))
        self.assertIn("var actualRemoved = before - after", nearby)
        self.assertIn("clone.m_stack = actualRemoved", nearby)
        self.assertIn("TryCaptureOwnedPlan(", nearby)
        self.assertIn("source.Requirements,", nearby)
        self.assertIn("var liveInventory = handle.Container.GetInventory()", nearby)
        self.assertIn("var mutableHandle = new ContainerHandle(", nearby)
        self.assertIn("new NearbyResourceCapture(freshScope, mutableHandles", nearby)
        self.assertIn("var handles = mutableCapture.Containers.ToArray()", nearby)
        self.assertIn("ReferenceEquals(runtime.Inventory, runtime.Container.Container.GetInventory())", nearby)
        self.assertIn("prepared crafting source was not the live container inventory", nearby)
        self.assertNotIn("var liveCapture = Capture(player, prepared.SourcePlan.Scope);", nearby)
        ordinary_begin = nearby.index("private static bool TryBeginTransaction(")
        self.assertLess(nearby.index("TryCaptureOwnedPlan", ordinary_begin), nearby.index("ExecuteWithRollback", ordinary_begin))
        self.assertIn("CancelRemaining", ownership)
        self.assertIn("OwnerRejectedContainerIds", ownership)
        cleanup = nearby.index("Stopping/disposal of the coroutine must not strand the coordinator")
        cleanup_timeout = nearby.index("if (!ownership.IsComplete) ownership.Timeout()", cleanup)
        cleanup_end = nearby.index("OwnershipCoordinator.End(ownership)", cleanup)
        self.assertLess(cleanup_timeout, cleanup_end)
        self.assertIn("Nearby-resource ownership refresh failed safely", nearby)
        self.assertIn("ContainerDiscovery.CheckAccess(player, handle.Container)", nearby)
        self.assertIn('InUseMessage = "The required materials are currently in use"', nearby)
        self.assertIn("public static class ResourceOwnershipSelection", core)
        self.assertIn("RequiredContainerIds", core)
        self.assertIn("RequiredRevisionsMatch", core)

        # Compatibility gate covers every newly relied-upon serialization/revision surface.
        for signature in (
            'RequireMethod(failures, typeof(ZDO), "GetByteArray", typeof(int), typeof(byte[]))',
            'RequireMethod(failures, typeof(ZDO), "get_DataRevision")',
            'RequireMethod(failures, typeof(Inventory), "Load", typeof(ZPackage))',
            'RequireConstructor(failures, typeof(Inventory), typeof(bool))',
            'RequireMethod(failures, typeof(ZDO), "get_OwnerRevision")',
            'RequireMethod(failures, typeof(ZNetView), "HasOwner")',
            'RequireMethod(failures, typeof(Container), "IsOwner")',
            'RequireMethod(failures, typeof(Container), "SetInUse", typeof(bool))',
            'RequireConstructor(failures, typeof(ZPackage), typeof(byte[]))',
            'RequireField(failures, typeof(Container), "m_wagon")',
            'RequireField(failures, typeof(ZDO), "m_uid")',
            'RequireField(failures, typeof(ZDOVars), "s_items")',
        ):
            self.assertIn(signature, gate)

    def test_outside_workbench_player_only_crafting_stays_vanilla_and_chest_crafting_stays_guarded(self):
        action = (PLUGIN_DIR / "CraftingPreflightAction.cs").read_text(encoding="utf-8")
        nearby = (PLUGIN_DIR / "NearbyResources.cs").read_text(encoding="utf-8")
        runtime = (PLUGIN_DIR / "RuntimeContext.cs").read_text(encoding="utf-8")
        policy = (ROOT / "src" / "Stackmaster.Core" / "CraftingResourcePathPolicy.cs").read_text(encoding="utf-8")
        tests = (ROOT / "tests" / "Stackmaster.Tests" / "Program.cs").read_text(encoding="utf-8")

        self.assertIn("scopeKind == StorageScopeKind.NearbyRadius && playerInventorySatisfiesFullCost", policy)
        self.assertIn("OutsideWorkbenchPlayerOnlyCraftIgnoresUnrelatedStateChurn", tests)
        self.assertIn("OutsideWorkbenchCraftWithInventoryShortageKeepsGuardedTransaction", tests)
        self.assertIn("WorkbenchCraftWithPlayerMaterialsKeepsGuardedTransaction", tests)

        player_only_start = nearby.index("internal static bool ShouldUseVanillaPlayerInventoryCraft(")
        player_only_end = nearby.index("internal static bool TryBeginPieceTransaction", player_only_start)
        player_only = nearby[player_only_start:player_only_end]
        self.assertIn("scope.Kind != StorageScopeKind.NearbyRadius", player_only)
        self.assertIn("HaveRequirementItemsMethod.Invoke", player_only)
        self.assertIn("VanillaPlayerCraftContext.Begin()", player_only)
        self.assertIn("finally", player_only)
        self.assertIn("VanillaPlayerCraftContext.End()", player_only)
        self.assertIn("CraftingResourcePathPolicy.Select(scope.Kind, vanillaSatisfied)", player_only)
        self.assertIn("CraftingResourcePath.VanillaPlayerInventory", player_only)
        self.assertNotIn("Capture(player", player_only)
        self.assertNotIn("AddInventory(", player_only)
        self.assertNotIn("ContainerDiscovery", player_only)
        self.assertNotIn("ContainerHandle", player_only)
        self.assertNotIn("Ownership", player_only)
        self.assertNotIn("Reservation", player_only)

        bypass = action.index("if (ShouldUseVanillaPlayerInventory(gui))")
        preflight = action.index("NearbyResourceService.TryPlanCraftingResources(")
        self.assertLess(bypass, preflight)
        self.assertIn("VanillaPlayerCraftContext.Begin()", action[bypass:preflight])
        self.assertIn("VanillaPlayerCraftContext.End()", action)

        crafting_patch = nearby[nearby.index("internal static class NearbyCraftingActionPatch"):nearby.index("internal static class NearbyBuildingActionPatch")]
        finish_bypass = crafting_patch.index("ShouldUseVanillaPlayerInventoryCraft(")
        guarded_begin = crafting_patch.index("TryBeginPreparedTransaction(")
        guarded_fallback = crafting_patch.index("TryBeginRecipeTransaction(")
        self.assertLess(guarded_begin, finish_bypass)
        self.assertLess(finish_bypass, guarded_fallback)
        self.assertIn("!handledPrepared && NearbyResourceService.ShouldUseVanillaPlayerInventoryCraft", crafting_patch)
        self.assertIn("VanillaPlayerCraftContext.Begin()", crafting_patch[guarded_begin:guarded_fallback])
        self.assertIn("ResourceActionContext.Restore(__state)", crafting_patch[guarded_begin:guarded_fallback])
        self.assertGreaterEqual(crafting_patch.count("VanillaPlayerCraftContext.End()"), 2)

        self.assertGreaterEqual(nearby.count("VanillaPlayerCraftContext.Active"), 2)
        self.assertIn('TryCleanup("Vanilla player-craft context cleanup", VanillaPlayerCraftContext.End)', runtime)
        self.assertIn("TryPrepareCraftingResources", action)
        self.assertIn("TryBeginPreparedCraftingTransaction", nearby)
        self.assertIn("RevalidateReservedContainers", nearby)
        self.assertIn("RevalidateStacks", nearby)
        self.assertIn("ResourceTransactionContext.Begin", nearby)
        self.assertIn("ResourceTransactionContext.Rollback", nearby)

    def test_remembered_chest_destinations_are_bounded_local_hints_and_fail_closed(self):
        state = (ROOT / "src" / "Stackmaster.Core" / "RememberedDestinationState.cs").read_text(encoding="utf-8")
        planner = (ROOT / "src" / "Stackmaster.Core" / "StorageTransferPlanner.cs").read_text(encoding="utf-8")
        validator = (ROOT / "src" / "Stackmaster.Core" / "PlanValidator.cs").read_text(encoding="utf-8")
        runtime = (PLUGIN_DIR / "RememberedChestDestinations.cs").read_text(encoding="utf-8")
        action = (PLUGIN_DIR / "StorageAction.cs").read_text(encoding="utf-8")
        discovery = (PLUGIN_DIR / "ContainerDiscovery.cs").read_text(encoding="utf-8")
        executor = (PLUGIN_DIR / "TransferExecutor.cs").read_text(encoding="utf-8")
        context = (PLUGIN_DIR / "RuntimeContext.cs").read_text(encoding="utf-8")
        gate = (PLUGIN_DIR / "CompatibilityGate.cs").read_text(encoding="utf-8")

        self.assertIn("MaximumEntries = 512", state)
        self.assertIn("TimeSpan.FromDays(180)", state)
        self.assertIn("new UTF8Encoding(false, true)", state)
        self.assertIn('string.Equals(parts[0], "v1"', state)
        self.assertIn("if (records.Count > MaximumEntries) return false", state)
        self.assertIn("TrimToMaximum()", state)

        self.assertIn("PlayerPrefs", runtime)
        self.assertIn("player.GetPlayerID()", runtime)
        self.assertIn("ZNet.instance.GetWorldUID()", runtime)
        self.assertIn("zdo.m_uid.ToString()", runtime)
        self.assertIn("handle.Snapshot.IsEligible", runtime)
        self.assertIn("eligibleIds.Contains(record.ContainerId)", runtime)
        self.assertIn("result[itemSnapshot.StackId] = record.ContainerId", runtime)
        self.assertIn("view.IsOwner() && container.IsOwner()", runtime)
        self.assertIn("InventorySnapshots.PersistentItemKey", runtime)
        self.assertNotIn("m_customData[", runtime)
        self.assertEqual(2, context.count("RememberedChestDestinations.Initialize()"))
        self.assertEqual(2, context.count("RememberedChestDestinations.Shutdown"))

        self.assertIn("movedToCurrent == 0", planner)
        self.assertIn("rememberedDestinations.TryGetValue", planner)
        self.assertIn("string.Equals(container.Id, rememberedContainerId", planner)
        self.assertIn("rememberedDestinations.TryGetValue", validator)
        self.assertGreaterEqual(action.count("rememberedDestinations:"), 2)
        self.assertGreaterEqual(action.count("RememberedChestDestinations.ResolveFallbacks"), 2)
        self.assertGreaterEqual(action.count("RememberedChestDestinations.ObserveDirect"), 1)

        self.assertIn("ObservedDataRevision", discovery)
        self.assertIn("zdo.DataRevision != expectedDataRevision", executor)
        self.assertIn("zdo.m_uid.ToString()", executor)
        self.assertIn("step.SourcePersistentItemKey", executor)
        self.assertIn("InventorySnapshots.PersistentItemKey(source)", executor)
        self.assertIn("expectedDataRevisions[containerId] = zdo.DataRevision", executor)
        for signature in (
            'RequireMethod(failures, typeof(Player), "GetPlayerID")',
            'RequireMethod(failures, typeof(ZNet), "GetWorldUID")',
            'RequireMethod(failures, typeof(ZDO), "get_DataRevision")',
            'RequireStaticMethod(failures, typeof(PlayerPrefs), "GetString", typeof(string), typeof(string))',
            'RequireStaticMethod(failures, typeof(PlayerPrefs), "SetString", typeof(string), typeof(string))',
        ):
            self.assertIn(signature, gate)

    def test_failed_deposit_warning_is_ephemeral_pointer_driven_and_quantity_exact(self):
        warnings = (PLUGIN_DIR / "FailedDepositWarnings.cs").read_text(encoding="utf-8")
        policy = (ROOT / "src" / "Stackmaster.Core" / "FailedDepositPolicy.cs").read_text(encoding="utf-8")
        inventory = (PLUGIN_DIR / "InventoryIntegration.cs").read_text(encoding="utf-8")
        sorter = (PLUGIN_DIR / "SortExecutor.cs").read_text(encoding="utf-8")
        action = (PLUGIN_DIR / "StorageAction.cs").read_text(encoding="utf-8")
        manifest = (PLUGIN_DIR / "HarmonyTargetManifest.cs").read_text(encoding="utf-8")
        gate = (PLUGIN_DIR / "CompatibilityGate.cs").read_text(encoding="utf-8")
        plugin = (PLUGIN_DIR / "Plugin.cs").read_text(encoding="utf-8")

        self.assertIn("ReferenceComparer<ItemDrop.ItemData>.Instance", warnings)
        self.assertIn("var replacement = new Dictionary<ItemDrop.ItemData, int>", warnings)
        self.assertIn("replacement[candidate.Item] = failed", warnings)
        self.assertIn("Warnings = replacement", warnings)
        self.assertNotIn("Warnings.Clear();\n            var inventory", warnings)
        self.assertIn("FailedDepositPolicy.FailedRemainder", warnings)
        self.assertIn("Warnings.Remove(item)", warnings)
        self.assertIn("ReconcileAfterSuccessfulSort", warnings)
        self.assertIn("if (_playerSortDepth > 0) return", warnings)
        self.assertIn("FailedDepositWarnings.BeginPlayerSort()", sorter)
        self.assertIn("FailedDepositWarnings.ReconcileAfterSuccessfulSort", sorter)
        self.assertIn("FailedDepositWarnings.EndPlayerSort()", sorter)
        self.assertIn("inventory.ContainsItem(candidate.Item)", warnings)
        self.assertNotIn("m_customData", warnings)
        self.assertNotIn("Update()", warnings)
        self.assertIn("FailedDepositWarnings.CaptureCandidates", action)
        self.assertGreaterEqual(action.count("FailedDepositWarnings.Replace"), 2)
        self.assertGreaterEqual(action.count("FailedDepositWarnings.Clear()"), 4)
        self.assertGreaterEqual(action.count("InventoryIntegration.HideAllFailedDepositOverlays()"), 3)

        self.assertIn("item.IsQuickBar || item.IsEquipped", policy)
        self.assertIn("!item.IsProtected", policy)
        self.assertIn("source.Quantity - original.ReplenishmentTarget", (ROOT / "src" / "Stackmaster.Core" / "StorageTransferPlanner.cs").read_text(encoding="utf-8"))
        self.assertIn("survivingStackQuantity - intentionallyRetained", policy)
        self.assertIn('FailedDepositOverlayName = "StackmasterFailedDepositOverlay"', inventory)
        self.assertIn("FailedDepositBorderColor", inventory)
        self.assertIn('quantityLabel.text = "!" + failedQuantity', inventory)
        self.assertIn("FailedDepositWarnings.Clear()", inventory)

        self.assertIn('Postfix(typeof(InventoryGrid), "OnPointerEnter"', manifest)
        self.assertIn("FailedDepositWarnings.AcknowledgeHover(__instance)", warnings)
        self.assertIn('RequireMethod(failures, typeof(InventoryGrid), "GetHoveredElement")', gate)
        self.assertIn('RequireField(failures, typeof(InventoryGui), "m_playerGrid")', gate)
        self.assertIn('RequireProperty(failures, typeof(InventoryElement), "Position")', gate)
        self.assertIn("Quick Stack", plugin)

    def test_quick_stack_summary_is_an_unclipped_multiline_top_left_list(self):
        formatter = (ROOT / "src" / "Stackmaster.Core" / "QuickStackSummaryFormatter.cs").read_text(encoding="utf-8")
        action = (PLUGIN_DIR / "StorageAction.cs").read_text(encoding="utf-8")
        context = (PLUGIN_DIR / "RuntimeContext.cs").read_text(encoding="utf-8")

        self.assertIn('return "Stackmaster:\\n" +', formatter)
        self.assertIn('"• " + depositedUnits', formatter)
        self.assertIn('" deposited\\n"', formatter)
        self.assertIn('"• " + replenishedUnits', formatter)
        self.assertIn('" replenished\\n"', formatter)
        self.assertIn('"• " + leftBehindUnits', formatter)
        self.assertIn('" left behind"', formatter)
        self.assertLess(formatter.index("depositedUnits"), formatter.index("replenishedUnits"))
        self.assertLess(formatter.index("replenishedUnits"), formatter.index("leftBehindUnits"))
        self.assertIn("QuickStackSummaryFormatter.Format(", action)
        self.assertIn('RuntimeContext.ShowTopLeft(string.Join("\\n", lines))', action)
        self.assertIn("Player.m_localPlayer.Message(MessageHud.MessageType.TopLeft, message, 0, null, false)", context)
        self.assertNotIn('Replace("\\n"', action)
        self.assertNotIn("Substring(", action[action.index("private static void ShowSummary"):])

    def test_destination_and_warning_work_add_no_frame_or_inventory_change_scans(self):
        runtime = (PLUGIN_DIR / "RememberedChestDestinations.cs").read_text(encoding="utf-8")
        warnings = (PLUGIN_DIR / "FailedDepositWarnings.cs").read_text(encoding="utf-8")
        inventory = (PLUGIN_DIR / "InventoryIntegration.cs").read_text(encoding="utf-8")
        action = (PLUGIN_DIR / "StorageAction.cs").read_text(encoding="utf-8")

        self.assertNotIn("Update()", runtime)
        self.assertNotIn("Time.frameCount", runtime)
        self.assertNotIn("Update()", warnings)
        observed_changed = inventory[
            inventory.index("private static void OnObservedInventoryChanged()"):
            inventory.index("internal static void RequestProtectionOverlayRefresh()")
        ]
        self.assertNotIn("RememberedChestDestinations", observed_changed)
        self.assertNotIn("FailedDepositWarnings.CaptureCandidates", observed_changed)
        self.assertNotIn("NearbyResourceService", observed_changed)
        self.assertIn("RememberedChestDestinations.ObserveDirect(container)", inventory)
        self.assertIn("FailedDepositWarnings.Reconcile()", inventory)
        self.assertIn("FailedDepositWarnings.CaptureCandidates", action)

    def test_failed_display_performance_experiment_is_fully_removed(self):
        nearby = (PLUGIN_DIR / "NearbyResources.cs").read_text(encoding="utf-8")
        discovery = (PLUGIN_DIR / "ContainerDiscovery.cs").read_text(encoding="utf-8")
        core = (ROOT / "src" / "Stackmaster.Core" / "ResourceAccounting.cs").read_text(encoding="utf-8")
        inventory_integration = (PLUGIN_DIR / "InventoryIntegration.cs").read_text(encoding="utf-8")
        sort_executor = (PLUGIN_DIR / "SortExecutor.cs").read_text(encoding="utf-8")

        self.assertFalse((ROOT / "src" / "Stackmaster.Core" / "DisplayCaptureEpochPolicy.cs").exists())
        self.assertNotIn("DisplayCaptureEpoch", nearby)
        self.assertNotIn("TryReuseDisplayEpoch", nearby)
        self.assertNotIn("PlayerInventorySignature", nearby)
        self.assertNotIn("InvalidatePlayerContribution", nearby)
        self.assertNotIn("MembershipStabilityDistance", discovery)
        self.assertNotIn("ResourceAvailabilityIndex", core)

        # Restore the uncomplicated 1.1.6 display capture: one same-frame snapshot, raw
        # availability accounting, and no inventory-change invalidation/dirty machinery.
        self.assertIn("private static int _cachedFrame = -1;", nearby)
        self.assertIn("_cachedFrame == Time.frameCount", nearby)
        self.assertIn("ResourceDisplayAvailability.Evaluate(validRequirements, capture.Stacks)", nearby)
        self.assertNotIn("NearbyResourceService.", inventory_integration[
            inventory_integration.index("private static void OnObservedInventoryChanged()"):
            inventory_integration.index("internal static void RequestProtectionOverlayRefresh()")
        ])

        # Retain only the independent no-op sort improvement: no synthetic change event is
        # emitted when the plan already matches the inventory.
        self.assertIn("if (PlanAlreadyApplied(source, plan)) return true;", sort_executor)
        no_op_guard = sort_executor[
            sort_executor.index("private static bool PlanAlreadyApplied"):
            sort_executor.index("private static bool Execute")
        ]
        self.assertIn("source.Items.Count != plan.Placements.Count", no_op_guard)
        self.assertIn("placement.Quantity != item.Quantity", no_op_guard)
        self.assertNotIn("m_onChanged", no_op_guard)

    def test_crafting_exact_debit_is_one_shot_journaled_and_fail_closed(self):
        debit = (ROOT / "src" / "Stackmaster.Core" / "ExactResourceDebit.cs").read_text(encoding="utf-8")
        nearby = (PLUGIN_DIR / "NearbyResources.cs").read_text(encoding="utf-8")
        tests = (ROOT / "tests" / "Stackmaster.Tests" / "Program.cs").read_text(encoding="utf-8")

        self.assertIn("_applied || _finished", debit)
        self.assertIn("attempt.RemovedUnits != step.Quantity", debit)
        self.assertIn("RemovedUnits != ExpectedUnits", debit)
        self.assertIn("Only a complete exact debit can be committed", debit)
        self.assertIn("for (var index = _receipts.Count - 1; index >= 0; index--)", debit)
        self.assertIn("var exactDebit = new ExactResourceDebit<RemovedResource>(plan)", nearby)
        self.assertIn("foreach (var receipt in exactDebit.Receipts)", nearby)
        self.assertIn("CraftingExactDebitConsumesPlayerAndSelectedChest", tests)
        self.assertIn("CraftingExactDebitCannotDoubleCharge", tests)
        self.assertIn("CraftingIncompleteDebitCannotCommitFreeOutput", tests)
        self.assertIn("CraftingCancellationRollsBackExactDebit", tests)

    def test_read_only_snapshot_null_regression_and_hud_paths_fail_open(self):
        discovery = (PLUGIN_DIR / "ContainerDiscovery.cs").read_text(encoding="utf-8")
        nearby = (PLUGIN_DIR / "NearbyResources.cs").read_text(encoding="utf-8")
        policy = (ROOT / "src" / "Stackmaster.Core" / "ResourceSnapshotPolicy.cs").read_text(encoding="utf-8")

        # Read-only discovery deliberately has no live Inventory. It must keep ordinary
        # ContainerSnapshot eligibility false and carry only a successful detached decode.
        self.assertIn("public static class ResourceSnapshotPolicy", policy)
        self.assertIn("!resourceReadOnly && liveInventoryKnown && accessGranted && !inUse", policy)
        self.assertIn("resourceReadOnly && detachedInventoryDecoded && accessGranted", policy)
        self.assertIn("var readPlan = ResourceSnapshotPolicy.Evaluate(", discovery)
        self.assertIn("var accessible = readPlan.CaptureLiveInventory", discovery)
        self.assertIn("var items = accessible && inventory != null", discovery)
        self.assertIn("var resourceReadable = readPlan.UseDetachedInventory", discovery)
        self.assertNotIn("var accessible = accessGranted && !inUse", discovery)
        self.assertLess(discovery.index("TryReadSerializedInventory(container"), discovery.index("var readPlan = ResourceSnapshotPolicy.Evaluate("))

        # Availability-only Harmony postfixes contain their own local safety net. A failed
        # detached decode or unexpected capture exception restores vanilla return/ref values
        # and never escapes into Hud.Update or InventoryGui.Show.
        self.assertIn("internal static class NearbyHudFailOpen", nearby)
        for surface in (
            'ReportOnce("crafting requirements", exception)',
            'ReportOnce("building requirements", exception)',
            'ReportOnce("building HUD", exception)',
            'ReportOnce("crafting HUD", exception)',
            'ReportOnce("first required crafting item", exception)',
        ):
            self.assertIn(surface, nearby)
        self.assertGreaterEqual(nearby.count("catch (Exception exception)"), 8)
        self.assertGreaterEqual(nearby.count("__result = vanillaResult"), 3)
        self.assertIn("amount = vanillaAmount", nearby)
        self.assertIn("extraAmount = vanillaExtraAmount", nearby)
        self.assertIn("var nearbyResult = NearbyResourceService.FindFirstRequiredItem", nearby)

    def test_detached_inventory_items_are_fully_hydrated_or_chest_is_rejected(self):
        discovery = (PLUGIN_DIR / "ContainerDiscovery.cs").read_text(encoding="utf-8")
        hydrator = (ROOT / "src" / "Stackmaster.Core" / "DetachedItemHydrator.cs").read_text(encoding="utf-8")
        nearby = (PLUGIN_DIR / "NearbyResources.cs").read_text(encoding="utf-8")

        self.assertIn("public static class DetachedItemHydrator", hydrator)
        self.assertIn("var resolved = new List<KeyValuePair<TItem, TMetadata>>()", hydrator)
        self.assertLess(hydrator.index("resolved.Add("), hydrator.index("applyMetadata(pair.Key, pair.Value)"))
        self.assertIn("var hydrated = DetachedItemHydrator.TryHydrate(", discovery)
        self.assertIn("item.m_dropPrefab.GetComponent<ItemDrop>()", discovery)
        self.assertIn("itemDrop.m_itemData.m_shared", discovery)
        self.assertIn("(item, shared) => item.m_shared = shared", discovery)
        self.assertIn('failure = "detached inventory item metadata could not be resolved"', discovery)
        self.assertLess(discovery.index("snapshot.Load(new ZPackage(bytes))"), discovery.index("var hydrated = DetachedItemHydrator.TryHydrate("))
        self.assertLess(discovery.index("var hydrated = DetachedItemHydrator.TryHydrate("), discovery.index("inventory = snapshot"))
        self.assertIn("item == null || item.m_stack <= 0 || item.m_shared == null", nearby)

    def test_remote_ownership_is_staged_because_vanilla_rpc_cannot_complete_synchronously(self):
        nearby = (PLUGIN_DIR / "NearbyResources.cs").read_text(encoding="utf-8")
        self.assertIn("A Harmony prefix cannot synchronously wait for remote RPCs", nearby)
        self.assertIn("required materials ready — try the action again", nearby)
        self.assertIn("yield return null", nearby)
        self.assertIn("nothing was consumed", nearby)
        self.assertNotIn("ClaimOwnership()", nearby)

    def test_requirement_display_and_consumption_settings_are_independent(self):
        nearby = (PLUGIN_DIR / "NearbyResources.cs").read_text(encoding="utf-8")
        requirement_patches = nearby[
            nearby.index("internal static class NearbyRequirementPatches"):
            nearby.index("internal static class NearbyBuildHudPatch")
        ]
        action_patches = nearby[nearby.index("internal static class NearbyFirstRequiredItemPatch"):]
        build_hud = nearby[
            nearby.index("internal static class NearbyBuildHudPatch"):
            nearby.index("internal static class RequirementAmountTextFitter")
        ]
        craft_hud = nearby[
            nearby.index("internal static class NearbyCraftingHudPatch"):
            nearby.index("internal static class NearbyFirstRequiredItemPatch")
        ]

        self.assertNotIn("ShowStorageAmountsInRequirementMenus", requirement_patches)
        self.assertNotIn("ShowStorageAmountsInRequirementMenus", action_patches)
        self.assertIn("CraftingFromNearbyChestsEnabled.Value", requirement_patches)
        self.assertIn("BuildingFromNearbyChestsEnabled.Value", requirement_patches)
        self.assertIn("CraftingFromNearbyChestsEnabled.Value", action_patches)
        self.assertIn("BuildingFromNearbyChestsEnabled.Value", action_patches)
        self.assertIn("ShowStorageAmountsInRequirementMenus.Value", build_hud)
        self.assertIn("ShowStorageAmountsInRequirementMenus.Value", craft_hud)
        self.assertIn("entry.AggregateSatisfied", build_hud)
        self.assertIn("entry.PlayerSatisfied", build_hud)
        self.assertIn("entry.AggregateSatisfied", craft_hud)
        self.assertIn("entry.PlayerSatisfied", craft_hud)

    def test_build_hud_uses_aggregate_nearby_totals_and_matching_availability_color(self):
        nearby = (PLUGIN_DIR / "NearbyResources.cs").read_text(encoding="utf-8")
        core = (ROOT / "src" / "Stackmaster.Core" / "ResourceAccounting.cs").read_text(encoding="utf-8")
        installer = (PLUGIN_DIR / "PatchInstaller.cs").read_text(encoding="utf-8") + (PLUGIN_DIR / "HarmonyTargetManifest.cs").read_text(encoding="utf-8")
        gate = (PLUGIN_DIR / "CompatibilityGate.cs").read_text(encoding="utf-8") + (PLUGIN_DIR / "HarmonyTargetManifest.cs").read_text(encoding="utf-8")
        self.assertIn('Postfix(typeof(Hud), "SetupPieceInfo", false, typeof(void), new[] { typeof(Piece) }, typeof(NearbyBuildHudPatch))', installer)
        self.assertIn("HarmonyTargetManifest.Validate(failures)", gate)
        self.assertIn('RequireField(failures, typeof(Hud), "m_requirementItems")', gate)
        self.assertIn("internal static class NearbyBuildHudPatch", nearby)
        build = nearby[nearby.index("internal static class NearbyBuildHudPatch"):nearby.index("internal static class RequirementAmountTextFitter")]
        self.assertIn("plugin.ShowStorageAmountsInRequirementMenus.Value", build)
        self.assertIn("plugin.BuildingFromNearbyChestsEnabled.Value", build)
        self.assertIn("ResourceDisplayAvailability.Evaluate(validRequirements, capture.Stacks)", nearby)
        self.assertIn("capture.Stacks.Where(stack => string.Equals(stack.InventoryId, PlayerInventoryId", nearby)
        self.assertIn('requirementRoot.transform.Find("res_amount")', build)
        self.assertIn("RequirementUiPolicy.Resolve(", build)
        self.assertIn("if (decision.ShouldOverrideText)", build)
        self.assertIn("RequirementAmountTextFitter.PrepareForVanilla(", build)
        self.assertIn("RequirementAmountTextFitter.Apply(", build)
        self.assertIn("ResourceRequirementPresentation.Format(entry.Required, entry.TotalAvailable)", build)
        self.assertIn("ResourceRequirementPresentation.ShouldUseShortageColor(", nearby)
        self.assertIn("? Color.red", nearby)
        self.assertIn(": Color.white", nearby)
        self.assertIn("RefreshIntervalSeconds = 0.25f", nearby)
        self.assertIn("public static class ResourceAvailability", core)
        self.assertIn("string.Equals(stack.ItemName, itemName, StringComparison.Ordinal)", core)

    def test_crafting_hud_uses_aggregate_nearby_totals_for_every_station_path(self):
        nearby = (PLUGIN_DIR / "NearbyResources.cs").read_text(encoding="utf-8")
        core = (ROOT / "src" / "Stackmaster.Core" / "ResourceAccounting.cs").read_text(encoding="utf-8")
        installer = (PLUGIN_DIR / "PatchInstaller.cs").read_text(encoding="utf-8") + (PLUGIN_DIR / "HarmonyTargetManifest.cs").read_text(encoding="utf-8")
        gate = (PLUGIN_DIR / "CompatibilityGate.cs").read_text(encoding="utf-8") + (PLUGIN_DIR / "HarmonyTargetManifest.cs").read_text(encoding="utf-8")
        self.assertIn('Both(typeof(InventoryGui), "SetupRequirement", true, typeof(bool), new[]', installer)
        self.assertIn('typeof(Transform), typeof(Piece.Requirement), typeof(Player), typeof(bool), typeof(int), typeof(int)', installer)
        self.assertIn('Both(typeof(InventoryGui), "SetupRequirement", true, typeof(bool), new[]', gate)
        self.assertIn("HarmonyTargetManifest.Validate(failures)", gate)
        self.assertIn('RequireStaticMethod(failures, typeof(InventoryGui), "get_instance")', gate)
        self.assertIn('RequireField(failures, typeof(InventoryGui), "m_selectedRecipe")', gate)
        self.assertIn('RequireField(failures, typeof(InventoryGui), "m_reqList")', gate)
        self.assertIn("internal static class NearbyCraftingHudPatch", nearby)
        self.assertIn("InventoryGui.SetupRequirement is static", nearby)
        self.assertNotIn("InventoryGui __instance,", nearby[nearby.index("internal static class NearbyCraftingHudPatch"):nearby.index("internal static class NearbyFirstRequiredItemPatch")])
        self.assertNotIn("___m_reqList", nearby)
        self.assertIn("var inventoryGui = InventoryGui.instance", nearby)
        self.assertIn("RequirementsField.GetValue(inventoryGui) as List<Piece.Requirement>", nearby)
        craft = nearby[nearby.index("internal static class NearbyCraftingHudPatch"):nearby.index("internal static class NearbyFirstRequiredItemPatch")]
        self.assertIn("plugin.ShowStorageAmountsInRequirementMenus.Value", craft)
        self.assertIn("plugin.CraftingFromNearbyChestsEnabled.Value", craft)
        self.assertIn("GetRecipeRequirementAvailability", nearby)
        self.assertIn("checked(requirement.GetAmount(qualityLevel) * craftMultiplier)", nearby)
        self.assertIn("alternatives: recipe.m_requireOnlyOneIngredient", nearby)
        self.assertIn("requireSingleQuality: recipe.m_requireOnlyOneIngredient", nearby)
        self.assertIn('var amountTransform = elementRoot.Find("res_amount")', craft)
        self.assertIn("RequirementUiPolicy.Resolve(", craft)
        self.assertIn("if (decision.ShouldOverrideText)", craft)
        self.assertIn("ResourceRequirementPresentation.Format(entry.Required, entry.TotalAvailable)", craft)
        self.assertIn("ResourceRequirementPresentation.ShouldUseShortageColor(", craft)
        self.assertIn("RefreshIntervalSeconds = 0.25f", nearby)
        self.assertIn("public static class ResourceDisplayAvailability", core)
        self.assertIn("public static class ResourceRequirementPresentation", core)
        self.assertIn('return required.ToString(CultureInfo.InvariantCulture) + " / " +', core)
        self.assertIn("=> !noCost && !isSatisfied && flashSignal > 0f", core)
        self.assertIn("GroupBy(requirement => new RequirementKey", core)
        self.assertIn("stackList", core)
        self.assertIn("GroupBy(stack => stack.Quality)", core)

    def test_requirement_text_uses_bounded_adaptive_fit_without_changing_exact_values(self):
        nearby = (PLUGIN_DIR / "NearbyResources.cs").read_text(encoding="utf-8")
        section = nearby[
            nearby.index("internal static class RequirementAmountTextFitter"):
            nearby.index("internal static class NearbyFirstRequiredItemPatch")
        ]
        self.assertIn("MinimumReadableFontSize = 10f", section)
        self.assertIn("MinimumFontScale = 0.55f", section)
        self.assertIn("label.text = exactText", section)
        self.assertIn("label.textWrappingMode = TextWrappingModes.NoWrap", section)
        self.assertIn("label.enableAutoSizing = true", section)
        self.assertIn("label.fontSizeMin = minimumSize", section)
        self.assertIn("label.fontSizeMax = normalSize", section)
        self.assertIn("label.fontSize = normalSize", section)
        self.assertIn("RequirementAmountTextFitter.Apply(", section)
        self.assertIn("ResourceRequirementPresentation.Format(entry.Required, entry.TotalAvailable)", section)
        self.assertNotIn("Substring(", section)
        self.assertNotIn("…", section)

    def test_requirement_text_restores_vanilla_state_for_reused_rows_and_disable_paths(self):
        nearby = (PLUGIN_DIR / "NearbyResources.cs").read_text(encoding="utf-8")
        installer = (PLUGIN_DIR / "PatchInstaller.cs").read_text(encoding="utf-8") + (PLUGIN_DIR / "HarmonyTargetManifest.cs").read_text(encoding="utf-8")
        section = nearby[
            nearby.index("internal static class RequirementAmountTextFitter"):
            nearby.index("internal static class NearbyFirstRequiredItemPatch")
        ]
        self.assertIn('Both(typeof(InventoryGui), "SetupRequirement", true, typeof(bool), new[]', installer)
        self.assertIn("internal static void Prefix([HarmonyArgument(0)] Transform elementRoot)", section)
        self.assertIn("RequirementAmountTextFitter.PrepareForVanilla(elementRoot)", section)
        self.assertIn("FontSize = label.fontSize", section)
        self.assertIn("EnableAutoSizing = label.enableAutoSizing", section)
        self.assertIn("FontSizeMin = label.fontSizeMin", section)
        self.assertIn("FontSizeMax = label.fontSizeMax", section)
        self.assertIn("TextWrappingMode = label.textWrappingMode", section)
        self.assertIn("Label.enableAutoSizing = EnableAutoSizing", section)
        self.assertIn("Label.fontSize = FontSize", section)
        self.assertIn("Label.textWrappingMode = TextWrappingMode", section)
        self.assertIn("RequirementAmountTextFitter.RestoreAll()", section)
        self.assertLess(section.index("internal static void Prefix"), section.index("internal static void Postfix"))

    def test_crafting_requirement_hook_matches_static_current_game_surface(self):
        nearby = (PLUGIN_DIR / "NearbyResources.cs").read_text(encoding="utf-8")
        gate = (PLUGIN_DIR / "CompatibilityGate.cs").read_text(encoding="utf-8") + (PLUGIN_DIR / "HarmonyTargetManifest.cs").read_text(encoding="utf-8")
        section = nearby[
            nearby.index("internal static class NearbyCraftingHudPatch"):
            nearby.index("internal static class NearbyFirstRequiredItemPatch")
        ]
        self.assertIn("InventoryGui.SetupRequirement is static", section)
        self.assertIn("var inventoryGui = InventoryGui.instance", section)
        self.assertIn("RequirementsField.GetValue(inventoryGui) as List<Piece.Requirement>", section)
        self.assertNotIn("InventoryGui __instance,", section)
        self.assertNotIn("___m_reqList", section)
        self.assertIn('Both(typeof(InventoryGui), "SetupRequirement", true, typeof(bool), new[]', gate)
        self.assertIn("HarmonyTargetManifest.Validate(failures)", gate)
        self.assertIn("method.IsStatic == mustBeStatic", (PLUGIN_DIR / "RuntimeContractValidator.cs").read_text(encoding="utf-8"))

    def test_release_output_is_single_plugin_binary_and_symbols(self):
        output = ROOT / "src" / "Stackmaster" / "bin" / "Release"
        files = sorted(path.name for path in output.iterdir() if path.is_file())
        self.assertEqual(["Stackmaster.dll", "Stackmaster.pdb"], files)

    def test_no_dll_is_tracked(self):
        tracked = subprocess.run(
            ["git", "ls-files", "*.dll"],
            cwd=ROOT,
            check=True,
            capture_output=True,
            text=True,
        ).stdout.strip()
        self.assertEqual("", tracked)


if __name__ == "__main__":
    unittest.main()
