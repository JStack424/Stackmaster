"""Static safety checks for the private Windows reference collector."""

from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[1]
SCRIPT = ROOT / "scripts" / "Collect-StackmasterReferences.ps1"


class ReferenceCollectorStaticTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.script = SCRIPT.read_text(encoding="utf-8")

    def test_collects_only_compile_time_assemblies(self):
        for required in (
            "assembly_valheim.dll",
            "assembly_utils.dll",
            "assembly_guiutils.dll",
            "UnityEngine.CoreModule.dll",
            "UnityEngine.UI.dll",
            "BepInExCorePath",
            "Compress-Archive",
        ):
            self.assertIn(required, self.script)

    def test_has_no_network_or_install_commands(self):
        for forbidden in (
            "Invoke-WebRequest",
            "Invoke-RestMethod",
            "Install-Package",
            "Install-Module",
            "Start-BitsTransfer",
            "Set-ExecutionPolicy",
            "Remove-Item",
        ):
            self.assertIsNone(
                re.search(rf"(?im)^\s*{re.escape(forbidden)}\b", self.script),
                forbidden,
            )

    def test_does_not_collect_private_game_data(self):
        for forbidden in (
            "LogOutput.log",
            "worlds_local",
            "characters_local",
            "Player.log",
            "config\\",
            "save-data",
        ):
            self.assertNotIn(forbidden, self.script)

    def test_reports_exact_failure_line(self):
        self.assertIn("reference collection failed at line", self.script)
        self.assertIn("$_.InvocationInfo.ScriptLineNumber", self.script)

    def test_structure_is_balanced(self):
        for opening, closing in (("{", "}"), ("(", ")")):
            self.assertEqual(self.script.count(opening), self.script.count(closing))


if __name__ == "__main__":
    unittest.main()
