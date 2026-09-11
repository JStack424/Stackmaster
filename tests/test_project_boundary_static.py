"""Repository safety checks for the private-reference and one-DLL gameplay boundary."""

from pathlib import Path
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
        self.assertNotIn("PatchAll", self.plugin)
        self.assertLess(
            self.plugin.index("CompatibilityGate.Evaluate()"),
            self.plugin.index("PatchInstaller.Install"),
        )

    def test_compatibility_gate_fingerprints_the_exact_runtime(self):
        gate = (PLUGIN_DIR / "CompatibilityGate.cs").read_text(encoding="utf-8")
        self.assertIn('SupportedGameVersion = "1.0.12"', gate)
        self.assertIn('SupportedUnityVersion = "6000.0.75f1"', gate)
        self.assertIn('SupportedBepInExVersion = "5.4.23.5"', gate)
        self.assertIn('SupportedHarmonyVersion = "2.9.0.0"', gate)
        self.assertIn("SupportedValheimMvid", gate)
        self.assertIn("SupportedValheimSha256", gate)
        self.assertIn("SHA256.Create()", gate)

    def test_plugin_targets_net48_and_does_not_copy_private_references(self):
        self.assertIn("<TargetFramework>net48</TargetFramework>", self.project)
        self.assertGreaterEqual(self.project.count("<Private>false</Private>"), 10)
        self.assertIn("VerifyReferencesWereNotCopied", self.project)
        self.assertNotIn("Stackmaster.Core.csproj", self.project)

    def test_exactly_three_user_settings_are_bound(self):
        self.assertEqual(3, self.gameplay.count("Config.Bind("))
        self.assertIn('"Auto-sort enabled"', self.gameplay)
        self.assertIn('"Nearby-storage radius"', self.gameplay)
        self.assertIn('"Storage-action keybind"', self.gameplay)

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
        self.assertIn("AccessTools.DeclaredMethod", installer)
        self.assertIn("Resolve the complete exact target set before installing the first patch", installer)
        self.assertNotIn("PatchAll", self.plugin)
        self.assertIn("if (!compatibility.IsCompatible)", self.plugin)
        self.assertIn("return;", self.plugin)

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
        self.assertIn('"</color>] Auto-Stack All"', action)
        self.assertNotIn('"</color>] Stackmaster: deposit + replenish"', action)
        self.assertIn("new KeyboardShortcut(KeyCode.E, KeyCode.LeftAlt)", self.plugin)

    def test_auto_sort_toggle_uses_visible_bottom_anchor_and_stays_interactive(self):
        integration = (PLUGIN_DIR / "InventoryIntegration.cs").read_text(encoding="utf-8")
        self.assertIn('ToggleAnchorName = "StackmasterAutoSortAnchor"', integration)
        self.assertIn("_toggleAnchor.transform.SetParent(gui.m_player, false)", integration)
        self.assertIn("anchorRect.anchorMin = new Vector2(0f, 0f)", integration)
        self.assertIn("anchorRect.anchoredPosition = new Vector2(12f, 4f)", integration)
        self.assertIn("ignoreLayout = true", integration)
        self.assertIn("_toggle.gameObject.SetActive(true)", integration)
        self.assertIn("_toggle.interactable = true", integration)
        self.assertIn("_toggle.SetIsOnWithoutNotify(RuntimeContext.Plugin.AutoSortEnabled.Value)", integration)
        unsubscribe = integration.index("AutoSortEnabled.SettingChanged -= OnAutoSortSettingChanged")
        subscribe = integration.index("AutoSortEnabled.SettingChanged += OnAutoSortSettingChanged")
        self.assertLess(unsubscribe, subscribe)
        self.assertIn("Object.Destroy(_toggleAnchor)", integration)

    def test_explicit_target_is_inspected_before_budgeted_nearby_search(self):
        discovery = (PLUGIN_DIR / "ContainerDiscovery.cs").read_text(encoding="utf-8")
        target_inspection = discovery.index("handles.Add(Inspect(player, target, catalog, targetDistance, true, targetDiagnostic))")
        stopwatch = discovery.index("var stopwatch = Stopwatch.StartNew()")
        object_search = discovery.index("FindObjectsByType<Container>")
        budget_check = discovery.index("stopwatch.Elapsed.TotalMilliseconds >= SearchBudgetMilliseconds")
        self.assertLess(target_inspection, stopwatch)
        self.assertLess(target_inspection, object_search)
        self.assertLess(target_inspection, budget_check)
        self.assertIn("container != target", discovery)
        self.assertIn("return new DiscoveryResult(handles, inspectedNearby < nearby.Length", discovery)
        self.assertIn("observedType == typeof(Container)", discovery)
        self.assertIn("TryRefreshFromNetwork(container, out refreshFailure)", discovery)
        self.assertIn("TryCheckAccess(player, container, out accessFailure)", discovery)

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
        self.assertIn("modifier != InventoryGrid.Modifier.Select", protection)
        self.assertIn("grid.GetInventory() != player.GetInventory()", protection)
        self.assertIn("if (item == null)", protection)

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

    def test_protected_item_indicators_use_reconciled_assignments_and_do_not_intercept_input(self):
        integration = (PLUGIN_DIR / "InventoryIntegration.cs").read_text(encoding="utf-8")
        interaction = (PLUGIN_DIR / "ProtectionInteraction.cs").read_text(encoding="utf-8")
        snapshots = (PLUGIN_DIR / "InventorySnapshots.cs").read_text(encoding="utf-8")
        self.assertIn("InventorySnapshots.ResolveProtection(player, state)", integration)
        self.assertIn("resolution.TryGet(new Slot(element.Position.x, element.Position.y), out record)", integration)
        self.assertIn("ProtectedBorderColor", integration)
        self.assertIn("new Color(0.22f, 0.78f, 0.84f, 0.82f)", integration)
        self.assertIn("_targetLabel.gameObject.SetActive(hasTarget)", integration)
        self.assertIn("_lockIcon.SetActive(!hasTarget)", integration)
        self.assertIn("image.raycastTarget = false", integration)
        self.assertIn("targetLabel.raycastTarget = false", integration)
        self.assertIn("InventoryGuiUpdatePatch", integration)
        self.assertGreaterEqual(interaction.count("InventoryIntegration.RefreshProtectionOverlays()"), 2)
        self.assertIn("HideProtectionOverlays();", integration)
        self.assertIn("DestroyProtectionOverlays();", integration)
        self.assertIn("protectionResolution.Assignments.Keys", snapshots)

    def test_generic_target_rejection_is_paired_with_local_diagnostics(self):
        action = (PLUGIN_DIR / "StorageAction.cs").read_text(encoding="utf-8")
        discovery = (PLUGIN_DIR / "ContainerDiscovery.cs").read_text(encoding="utf-8")
        generic = 'targeted container is inaccessible, in use, unknown, or outside the configured radius.'
        self.assertIn(generic, action)
        initial_log = action.index('LogTargetRejection("initial discovery", discovery)')
        initial_ui = action.index(generic, initial_log)
        self.assertLess(initial_log, initial_ui)
        self.assertIn("RuntimeContext.Plugin.Log.LogWarning", action)
        for signal in (
            "targetPresent=", "discovered=", "distance=", "radius=", "withinRadius=",
            "searchTruncated=", "searchMs=", "budgetMs=", "type=", "vanilla=",
            "nview=", "nviewValid=", "zdo=", "refresh=", "inventory=", "inUse=", "access=",
        ):
            self.assertIn(signal, discovery)
        self.assertIn("RefreshFailure", discovery)
        self.assertIn("AccessFailure", discovery)

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
