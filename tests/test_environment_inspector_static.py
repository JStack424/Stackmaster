"""Static safety/contract checks for the Windows environment inspector.

These tests intentionally run with Python's standard library on Linux, where Windows
PowerShell and the target Steam/r2modman directories are unavailable.
"""

from pathlib import Path
import re
import unittest
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
SCRIPT = ROOT / "scripts" / "Inspect-StackmasterEnvironment.ps1"
NEXT_STEPS = ROOT / "docs" / "NEXT-STEPS.md"
RESEARCH = ROOT / "docs" / "RESEARCH.md"
README = ROOT / "README.md"
GITIGNORE = ROOT / ".gitignore"
ENVIRONMENT_PROPS_EXAMPLE = ROOT / "Environment.props.example"


class EnvironmentInspectorStaticTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.script = SCRIPT.read_text(encoding="utf-8")
        cls.next_steps = NEXT_STEPS.read_text(encoding="utf-8")
        cls.research = RESEARCH.read_text(encoding="utf-8")
        cls.readme = README.read_text(encoding="utf-8")
        cls.gitignore = GITIGNORE.read_text(encoding="utf-8")

    def test_script_has_expected_read_only_scope(self):
        for required in (
            "appmanifest_892970.acf",
            "valheim.exe",
            "valheim_Data\\Managed",
            "r2modmanPlus-local\\Valheim\\profiles",
            "BepInEx.dll",
            "0Harmony.dll",
            "ConvertTo-SanitizedPath",
            "%USERPROFILE%",
            "ConvertTo-Json",
        ):
            self.assertIn(required, self.script)

    def test_script_exposes_location_overrides_and_output_control(self):
        for parameter in (
            "$OutputPath",
            "$Format",
            "$ValheimPath",
            "$R2ModManDataPath",
            "$ProfileName",
        ):
            self.assertIn(parameter, self.script)

    def test_script_contains_no_network_install_or_mutating_commands(self):
        forbidden_commands = (
            "Invoke-WebRequest",
            "Invoke-RestMethod",
            "Start-BitsTransfer",
            "Install-Package",
            "Install-Module",
            "winget",
            "choco",
            "scoop",
            "Start-Process",
            "Copy-Item",
            "Move-Item",
            "Remove-Item",
            "Rename-Item",
            "Set-ItemProperty",
            "New-ItemProperty",
            "Set-ExecutionPolicy",
        )
        for command in forbidden_commands:
            self.assertIsNone(
                re.search(rf"(?im)^\s*{re.escape(command)}\b", self.script),
                f"forbidden command found: {command}",
            )

    def test_report_is_the_only_file_write(self):
        writes = re.findall(
            r"(?im)^\s*(Set-Content|Add-Content|Out-File|Export-Csv|Export-Clixml)\b",
            self.script,
        )
        self.assertEqual(["Set-Content"], writes)
        self.assertRegex(
            self.script,
            r"Set-Content\s+-LiteralPath\s+\$outputFullPath\s+-Value\s+\$content",
        )

    def test_script_structure_is_balanced(self):
        # A lightweight corruption check, not a replacement for parsing with PowerShell.
        for opening, closing in (("{", "}"), ("(", ")")):
            self.assertEqual(self.script.count(opening), self.script.count(closing))
        self.assertIn("#Requires -Version 5.1", self.script)
        self.assertIn("Set-StrictMode -Version 2.0", self.script)
        self.assertIn("$ErrorActionPreference = 'Stop'", self.script)

    def test_generated_reports_are_ignored(self):
        self.assertIn("scripts/stackmaster-environment-report*.json", self.gitignore)
        self.assertIn("scripts/stackmaster-environment-report*.txt", self.gitignore)
        self.assertIn("Environment.props", self.gitignore)

    def test_environment_props_example_is_parseable_and_placeholder_only(self):
        root = ET.parse(ENVIRONMENT_PROPS_EXAMPLE).getroot()
        values = {child.tag: child.text for group in root for child in group}
        self.assertEqual(
            {"ValheimInstall", "ValheimManaged", "BepInExCore", "ModDeployPath"},
            set(values),
        )
        self.assertTrue(all(value == "LOCAL_PATH_HERE" for value in values.values()))

    def test_next_steps_matches_approved_product_identity(self):
        self.assertNotIn("InventoryStorage", self.next_steps)
        self.assertIn("**Stackmaster**", self.next_steps)
        self.assertIn("com.jstack424.stackmaster", self.next_steps)
        self.assertIn("Keyboard and mouse only in v0.1", self.next_steps)
        self.assertIn("not** v0.1 release gates", self.next_steps)
        self.assertNotIn("### Decisions for Joe", self.next_steps)
        self.assertNotIn("## 9. Immediate next conversation", self.next_steps)

    def test_research_is_marked_historical_and_reconciled(self):
        self.assertNotIn("InventoryStorage", self.research)
        self.assertIn("pre-design ecosystem research", self.research)
        self.assertIn("## 11. Approved v0.1 test matrix", self.research)

    def test_readme_uses_stackmaster_identity(self):
        self.assertNotIn("InventoryStorage", self.readme)
        self.assertIn("# Stackmaster", self.readme)
        self.assertIn("com.jstack424.stackmaster", self.readme)
        self.assertIn("Inspect-StackmasterEnvironment.ps1", self.readme)


if __name__ == "__main__":
    unittest.main()
