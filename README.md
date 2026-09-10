# Valheim QoL Mods

A workspace for small, configurable Valheim quality-of-life mods, beginning with inventory sorting and chest-storage workflows.

## Status

Research and planning are complete as of September 10, 2026, one day after Valheim 1.0 launched. No plugin has been scaffolded yet because the exact installed Valheim assemblies and supported C# target need to be verified first.

- [Valheim 1.0 modding research](docs/RESEARCH.md)
- [Implementation and release plan](docs/NEXT-STEPS.md)

Nothing has been installed, published, or copied from the game into this repository.

## Initial direction

Build one deployable `InventoryStorage` plugin/package with two independently configurable feature areas:

1. deterministic player-inventory sorting;
2. safe stacking into eligible nearby containers.

Keep those features internally separate so they can become independent packages later if multiplayer requirements, dependencies, or release cadence diverge. Do not introduce a separately installed shared “core” package until at least two mods prove it is useful.

## Technical baseline

- Valheim-specific BepInEx 5 pack from Thunderstore
- HarmonyX supplied by BepInEx
- Plain BepInEx first; add Jötunn only if the implementation needs its networking, synchronization, managers, or asset workflows
- Thunderstore package distribution
- r2modman development profiles, local-package import, and installation testing
- Local game/BepInEx assemblies referenced outside git

Every specific runtime/framework assumption remains provisional until it is tested against the installed Valheim 1.0 build.

## Proposed layout

```text
valheim-qol-mods/
├── README.md
├── docs/
│   ├── RESEARCH.md
│   └── NEXT-STEPS.md
├── Directory.Build.props
├── Directory.Packages.props
├── Environment.props.example
├── Valheim.QoL.sln
├── src/
│   └── Valheim.QoL.InventoryStorage/
├── tests/
│   └── Valheim.QoL.InventoryStorage.Tests/
├── packages/
│   └── InventoryStorage/
├── scripts/
└── artifacts/                 # ignored
```

The project files and empty directories will be created only after the current local toolchain and assembly layout are verified.

## Project principles

- Keep behavior focused, deterministic, and configurable.
- Default to explicit user actions and conservative item protection.
- Never move equipped, protected, or favorited items unless clearly enabled.
- Preserve item identity, quantity, quality, durability, and custom metadata.
- Treat shared-container mutation as network-affecting until proven otherwise.
- Test every claimed client/server combination instead of inferring compatibility.
- Keep generated files, local game assemblies, profiles, machine paths, and credentials out of git.
- Build and test the exact Thunderstore ZIP before any manual upload.
- Keep publishing separate and explicitly approved.
