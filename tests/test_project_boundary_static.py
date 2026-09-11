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
        self.assertLess(
            self.plugin.index("CompatibilityGate.Evaluate()"),
            self.plugin.index("PatchAll"),
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
        self.assertIn("[HarmonyPatch(typeof(InventoryGui)", self.gameplay)
        self.assertIn("[HarmonyPatch(typeof(Container)", self.gameplay)
        self.assertIn("if (!compatibility.IsCompatible)", self.plugin)
        self.assertIn("return;", self.plugin)

    def test_character_persistence_is_versioned_and_slot_scoped(self):
        core = (ROOT / "src" / "Stackmaster.Core" / "ProtectionState.cs").read_text(encoding="utf-8")
        runtime = (PLUGIN_DIR / "RuntimeContext.cs").read_text(encoding="utf-8")
        self.assertIn('CurrentVersion = "v1"', core)
        self.assertIn("Dictionary<Slot, ProtectionRecord>", core)
        self.assertIn("player.m_customData", runtime)
        self.assertIn("CharacterDataKey", runtime)
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
        self.assertIn("Target container changed or became unavailable before transfer", action)
        ownership = (PLUGIN_DIR / "OwnershipCoordinator.cs").read_text(encoding="utf-8")
        self.assertIn("RPC_StackResponse", ownership)
        self.assertIn("LateResponseSuppressions", ownership)
        self.assertIn("previous ownership response is still pending", ownership)

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
