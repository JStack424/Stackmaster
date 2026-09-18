# ADR 0001: Target framework and private reference boundary

Status: Accepted on September 11, 2026

## Context

Joe's read-only environment report identified:

- Valheim API `1.0.14` / Steam build `25364309`;
- Unity `6000.0.75f1`;
- `assembly_valheim.dll` SHA-256 `e5af0669755ed3b098f71b4dd0753f8a997761b99bca1e8dac3d5ca4c706a0be`, MVID `a63433e8-968e-407a-918a-9f9fe7e7ba9a`;
- game `mscorlib.dll` assembly version `4.0.0.0` and `netstandard.dll` assembly version `2.1.0.0`;
- the Valheim BepInEx pack's `BepInEx.dll` version `5.4.23.5`;
- HarmonyX `0Harmony.dll` version `2.9.0.0`.

The current Valheim Modding guidance still recommends .NET Framework 4.8 for a plain BepInEx 5 plugin. Stackmaster must also keep its inventory policy testable without loading Unity, Valheim, BepInEx, or Harmony.

## Decision

- The deployable `Stackmaster` plugin targets **.NET Framework 4.8 (`net48`)**.
- Pure inventory models and planners target **.NET Standard 2.0 (`netstandard2.0`)**, which is consumable by the `net48` plugin and by the local modern test runner.
- Automated planner tests target **.NET 8 (`net8.0`)**. .NET 8 is a build/test tool only; it is not Stackmaster's in-game runtime target.
- The plugin source-compiles the pure planner files rather than shipping a second runtime DLL. The package remains one deployable plugin assembly.
- The local build pins .NET SDK `8.0.425` in `global.json` and uses the SDK installed under `~/workspace/toolchains/dotnet-8`. It does not alter the system toolchain or Joe's gaming PC.
- `net48` reference assemblies come from Microsoft's build-only reference package. They are not copied into output.
- Valheim, Unity, BepInEx, and Harmony references come only from `lib/local/StackmasterReferences/` (or an ignored `StackmasterReferencePath` override). All such DLLs remain private and gitignored, use `<Private>false>`, and must never be copied into build or release output.

## Consequences

- Pure planners can be built and exercised immediately without touching Valheim state.
- A harmless plugin can compile against Joe's exact private reference bundle while containing no gameplay patches or inventory mutation code.
- Game integration stays behind adapters added in a later milestone, after the skeleton's clean-profile load is verified.
- If the exact runtime rejects the `net48` skeleton, that smoke-test result reopens this decision before gameplay code is added.
