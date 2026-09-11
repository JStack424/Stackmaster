# Stackmaster

Stackmaster is a Valheim quality-of-life mod by **JStack424** that combines automatic inventory sorting, deliberate nearby-storage depositing, and protected-slot replenishment in one workflow.

> Turn a messy Viking inventory into a tidy, adventure-ready loadout.

## Status

The 18-checkpoint behavior contract was approved on September 11, 2026. The project is ready for **environment validation**, not gameplay implementation yet: Joe's installed Valheim, r2modman, BepInEx, Harmony, and managed-assembly layout must be inspected before selecting a C# target or scaffolding the plugin.

- [Approved behavior contract](docs/DESIGN-QUESTIONNAIRE.md)
- [Environment-validation and implementation plan](docs/NEXT-STEPS.md)
- [Valheim 1.0 modding research](docs/RESEARCH.md)

Nothing has been installed, published, or copied from Valheim into this repository.

## Approved v0.1 direction

Build one deployable `Stackmaster` BepInEx plugin/package with separate internal modules for:

1. automatic alphabetical inventory/chest sorting;
2. exact-slot protection and optional replenishment targets;
3. the explicit nearby-storage deposit/replenish action;
4. container discovery, validation, and safe transfer execution;
5. compact visual result feedback.

The public identity is `JStack424-Stackmaster`, with BepInEx GUID `com.jstack424.stackmaster` and an MIT license. Public repository creation and Thunderstore publication remain separate, explicitly approved future actions.

## Safe environment inspection

On the Windows gaming PC, from the repository root:

```powershell
.\scripts\Inspect-StackmasterEnvironment.ps1
```

The script writes `scripts\stackmaster-environment-report.json` by default. It reads likely Steam/r2modman paths and file metadata only; it does not install software, alter game/profile files, read logs or configuration contents, copy assemblies, or use the network. Paths under the Windows user profile are replaced with `%USERPROFILE%`.

If auto-discovery misses a nonstandard location:

```powershell
.\scripts\Inspect-StackmasterEnvironment.ps1 `
  -ValheimPath 'D:\SteamLibrary\steamapps\common\Valheim' `
  -R2ModManDataPath "$env:APPDATA\r2modmanPlus-local\Valheim" `
  -ProfileName 'Stackmaster Dev'
```

Review the report before sharing it. The generated report is gitignored.

## Technical baseline

- Valheim-specific BepInEx 5 pack from Thunderstore.
- HarmonyX supplied by that BepInEx profile.
- Plain BepInEx first; add Jötunn only if current Valheim APIs, networking, UI, assets, or synchronization make it necessary.
- Thunderstore distribution and fresh-profile r2modman package validation.
- Local game/BepInEx assemblies referenced in place through ignored machine-local paths; never copied into git or a release ZIP.

Every concrete runtime/framework choice remains provisional until checked against the installed Valheim 1.0 environment.

## Planned layout

```text
valheim-qol-mods/
├── README.md
├── docs/
│   ├── DESIGN-QUESTIONNAIRE.md
│   ├── RESEARCH.md
│   └── NEXT-STEPS.md
├── Environment.props.example
├── Stackmaster.sln
├── src/
│   └── Stackmaster/
├── tests/
│   └── Stackmaster.Tests/
├── packages/
│   └── Stackmaster/
├── scripts/
│   ├── Inspect-StackmasterEnvironment.ps1
│   ├── build.ps1
│   ├── package.ps1
│   └── verify-package.ps1
└── artifacts/                 # ignored
```

Project files and build scripts beyond the read-only inspector will be created only after the local environment determines the correct target framework and references.

## Project principles

- The approved behavior contract is the source of truth; material changes return to Joe for approval.
- Keep policy logic deterministic and unit-testable outside Unity.
- Preserve exact item identity, quantity, quality, durability, custom data, and legal stack limits.
- Never move quick-bar, equipped, or protected-slot contents.
- Treat shared-container mutation as network-affecting until the complete host/guest/dedicated test matrix passes.
- If compatibility cannot be trusted, disable all Stackmaster item-changing behavior for that session.
- Keep generated reports, local assemblies, profiles, machine paths, credentials, and build output out of git.
- Build and test the exact Thunderstore ZIP before any manual upload.
- Keep public repository creation and publishing separate and explicitly approved.
