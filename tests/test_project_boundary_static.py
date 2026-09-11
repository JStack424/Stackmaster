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
        self.assertLess(
            self.plugin.index("CompatibilityGate.Evaluate()"),
            self.plugin.index("PatchAll"),
        )

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
        self.assertIn('InvokeRPC("RPC_RequestOpen"', self.gameplay)
        self.assertIn("RPC_OpenResponse", self.gameplay)
        self.assertNotIn("ClaimOwnership()", self.gameplay)
        self.assertIn("exactPostcondition", self.gameplay)

    def test_inventory_mutation_hooks_are_installed_only_after_gate(self):
        self.assertIn("[HarmonyPatch(typeof(InventoryGui)", self.gameplay)
        self.assertIn("[HarmonyPatch(typeof(Container)", self.gameplay)
        self.assertIn("if (!compatibility.IsCompatible)", self.plugin)
        self.assertIn("return;", self.plugin)

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
