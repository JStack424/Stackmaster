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

    def test_exactly_five_user_settings_are_bound(self):
        self.assertEqual(5, self.gameplay.count("Config.Bind("))
        self.assertIn('"Auto-sort enabled"', self.gameplay)
        self.assertIn('"Nearby-storage radius"', self.gameplay)
        self.assertIn('"Storage-action keybind"', self.gameplay)
        self.assertIn('"Enable building from nearby chests", true', self.gameplay)
        self.assertIn('"Enable crafting from nearby chests", true', self.gameplay)

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
        self.assertNotIn('Postfix(typeof(InventoryGui), "Update"', installer)
        self.assertIn('Postfix(typeof(InventoryGrid), "UpdateInventory"', installer)
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

    def test_open_container_shortcut_uses_open_target_without_closing_inventory(self):
        action = (PLUGIN_DIR / "StorageAction.cs").read_text(encoding="utf-8")
        discovery = (PLUGIN_DIR / "ContainerDiscovery.cs").read_text(encoding="utf-8")
        executor = (PLUGIN_DIR / "TransferExecutor.cs").read_text(encoding="utf-8")
        installer = (PLUGIN_DIR / "PatchInstaller.cs").read_text(encoding="utf-8")
        gate = (PLUGIN_DIR / "CompatibilityGate.cs").read_text(encoding="utf-8")
        self.assertIn("var openContainer = CurrentOpenContainer(gui)", action)
        self.assertIn("gui.IsContainerOpen()", action)
        self.assertIn('AccessTools.Field(typeof(InventoryGui), "m_currentContainer")', action)
        self.assertIn('ZInput.ResetButtonStatus("Use")', action)
        self.assertIn("Begin(player, openContainer)", action)
        self.assertLess(action.index('ZInput.ResetButtonStatus("Use")'), action.index("Begin(player, openContainer)"))
        self.assertIn('[HarmonyPatch(typeof(InventoryGui), "Update")]', action)
        self.assertIn("return !RuntimeContext.Compatibility.IsCompatible || StorageAction.HandleOpenContainerShortcut(__instance)", action)
        self.assertIn("return false;", action[action.index("internal static bool HandleOpenContainerShortcut"):action.index("internal static bool IsLocalOpenTarget")])
        self.assertIn('Prefix(typeof(InventoryGui), "Update"', installer)
        self.assertIn('RequireMethod(failures, typeof(InventoryGui), "Update")', gate)
        self.assertIn('RequireMethod(failures, typeof(InventoryGui), "IsContainerOpen")', gate)
        self.assertIn('RequireMethod(failures, typeof(ZInput), "ResetButtonStatus", typeof(string))', gate)
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

    def test_target_prompt_prefills_and_selects_full_legal_stack_size(self):
        protection = (PLUGIN_DIR / "ProtectionInteraction.cs").read_text(encoding="utf-8")
        self.assertIn("if (item.m_shared.m_maxStackSize <= 1)", protection)
        self.assertIn("_text = _maxStack.ToString(CultureInfo.InvariantCulture)", protection)
        self.assertIn("Math.Max(4, defaultTargetText.Length)", protection)
        request = protection.index("TextInput.instance.RequestText")
        select = protection.index("SelectPrefilledTarget(TextInput.instance", request)
        self.assertLess(request, select)
        self.assertIn("inputField.selectionAnchorPosition = 0", protection)
        self.assertIn("inputField.selectionFocusPosition = textLength", protection)
        self.assertIn("target < 0 || target > _maxStack", protection)
        self.assertIn("if (target == 0)", protection)
        self.assertIn("state.Protect(_slot, target, _itemKey)", protection)

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
        self.assertNotIn('[HarmonyPatch(typeof(InventoryGui), "Update")]', integration)
        self.assertIn("_observedPlayerInventory.m_onChanged += InventoryChangedHandler", integration)
        self.assertIn("_observedPlayerInventory.m_onChanged -= InventoryChangedHandler", integration)
        self.assertIn("_overlayRefreshPending = true", integration)
        self.assertIn('[HarmonyPatch(typeof(InventoryGrid), "UpdateInventory"', integration)
        self.assertIn("InventoryIntegration.FlushPendingProtectionOverlayRefresh(__instance, inventory)", integration)
        self.assertIn("!ReferenceEquals(grid, gui.m_playerGrid)", integration)
        self.assertIn("InventoryIntegration.BindPlayerInventory(Player.m_localPlayer)", integration)
        self.assertIn("InventoryIntegration.RequestProtectionOverlayRefresh()", integration)
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
        installer = (PLUGIN_DIR / "PatchInstaller.cs").read_text(encoding="utf-8")
        gate = (PLUGIN_DIR / "CompatibilityGate.cs").read_text(encoding="utf-8")
        self.assertIn("GroupBy", core)
        self.assertIn("shortages.Count == 0 ? tentative", core)
        # HarmonyX binds unannotated patch arguments by the original parameter name.
        # Keep every game-method argument in the new resource hooks position-bound so
        # metadata names such as Valheim's Recipe `piece` cannot break startup.
        bindings = [int(index) for index in re.findall(r"\[HarmonyArgument\((\d+)\)\]", nearby)]
        self.assertEqual(
            [0, 1, 2, 3, 0, 1, 0, 0, 1, 2, 3, 4, 5, 0, 1, 2, 3, 4, 5, 0, 0, 0, 1, 2, 3],
            bindings,
        )
        self.assertNotIn("RecipePostfix(Player __instance, Recipe recipe", nearby)
        self.assertIn("public int PlannedUnits", core)
        self.assertIn("TryBeginRecipeTransaction(player, ___m_craftRecipe, qualityLevel, multiplier, out failure)", nearby)
        self.assertIn("TryBeginPieceTransaction(__instance, piece, out failure)", nearby)
        self.assertIn("ContainerDiscovery.Discover(player, null, catalog, radius, true, true)", nearby)
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
        self.assertGreaterEqual(nearby.count("ResourceTransactionContext.Complete()"), 2)
        self.assertIn("RuntimeContext.Disable", nearby)
        self.assertIn('Transactional(typeof(InventoryGui), "DoCrafting"', installer)
        self.assertIn('Transactional(typeof(Player), "UpdatePlacement"', installer)
        self.assertIn('Both(typeof(Player), "TryPlacePiece"', installer)
        self.assertIn('Prefix(typeof(Inventory), "RemoveItem"', installer)
        for method in ("HaveRequirementItems", "HaveRequirements", "GetFirstRequiredItem", "DoCrafting", "UpdatePlacement", "TryPlacePiece"):
            self.assertIn(f'"{method}"', gate)

    def test_remote_owned_resource_stock_is_read_only_and_claimed_only_for_exact_action_plan(self):
        nearby = (PLUGIN_DIR / "NearbyResources.cs").read_text(encoding="utf-8")
        discovery = (PLUGIN_DIR / "ContainerDiscovery.cs").read_text(encoding="utf-8")
        ownership = (PLUGIN_DIR / "OwnershipCoordinator.cs").read_text(encoding="utf-8")
        core = (ROOT / "src" / "Stackmaster.Core" / "ResourceAccounting.cs").read_text(encoding="utf-8")
        gate = (PLUGIN_DIR / "CompatibilityGate.cs").read_text(encoding="utf-8")

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
        self.assertIn("OwnerRevision != handle.ResourceOwnerRevision", ownership)
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
        rollback_context = nearby[nearby.index("internal static bool Rollback()") : nearby.index("private static void Clear()")]
        self.assertLess(rollback_context.index("NearbyResourceService.Rollback"), rollback_context.index("ReleaseReservations"))
        self.assertIn("var actualRemoved = before - after", nearby)
        self.assertIn("clone.m_stack = actualRemoved", nearby)
        self.assertLess(nearby.index("TryCaptureOwnedPlan"), nearby.index("ExecuteWithRollback", nearby.index("TryBeginTransaction")))
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

    def test_remote_ownership_is_staged_because_vanilla_rpc_cannot_complete_synchronously(self):
        nearby = (PLUGIN_DIR / "NearbyResources.cs").read_text(encoding="utf-8")
        self.assertIn("A Harmony prefix cannot synchronously wait for remote RPCs", nearby)
        self.assertIn("required materials ready — try the action again", nearby)
        self.assertIn("yield return null", nearby)
        self.assertIn("nothing was consumed", nearby)
        self.assertNotIn("ClaimOwnership()", nearby)

    def test_build_hud_uses_aggregate_nearby_totals_and_matching_availability_color(self):
        nearby = (PLUGIN_DIR / "NearbyResources.cs").read_text(encoding="utf-8")
        core = (ROOT / "src" / "Stackmaster.Core" / "ResourceAccounting.cs").read_text(encoding="utf-8")
        installer = (PLUGIN_DIR / "PatchInstaller.cs").read_text(encoding="utf-8")
        gate = (PLUGIN_DIR / "CompatibilityGate.cs").read_text(encoding="utf-8")
        self.assertIn('Postfix(typeof(Hud), "SetupPieceInfo", new[] { typeof(Piece) }, typeof(NearbyBuildHudPatch))', installer)
        self.assertIn('RequireMethod(failures, typeof(Hud), "SetupPieceInfo", typeof(Piece))', gate)
        self.assertIn('RequireField(failures, typeof(Hud), "m_requirementItems")', gate)
        self.assertIn("internal static class NearbyBuildHudPatch", nearby)
        self.assertIn("!RuntimeContext.Plugin.BuildingFromNearbyChestsEnabled.Value", nearby)
        self.assertIn("ResourceDisplayAvailability.Evaluate(validRequirements, capture.Stacks)", nearby)
        self.assertIn("new RuntimeRequirementAvailability(entry.Required, entry.Available, entry.IsSatisfied)", nearby)
        self.assertIn('requirementRoot.transform.Find("res_amount")', nearby)
        self.assertIn('entry.Required.ToString(CultureInfo.InvariantCulture) + " / " +', nearby)
        self.assertIn("entry.Available.ToString(CultureInfo.InvariantCulture)", nearby)
        self.assertIn("entry.IsSatisfied || Mathf.Sin(Time.time * 10f) <= 0f", nearby)
        self.assertIn("? Color.white", nearby)
        self.assertIn(": Color.red", nearby)
        self.assertIn("RefreshIntervalSeconds = 0.25f", nearby)
        self.assertIn("public static class ResourceAvailability", core)
        self.assertIn("string.Equals(stack.ItemName, itemName, StringComparison.Ordinal)", core)

    def test_crafting_hud_uses_aggregate_nearby_totals_for_every_station_path(self):
        nearby = (PLUGIN_DIR / "NearbyResources.cs").read_text(encoding="utf-8")
        core = (ROOT / "src" / "Stackmaster.Core" / "ResourceAccounting.cs").read_text(encoding="utf-8")
        installer = (PLUGIN_DIR / "PatchInstaller.cs").read_text(encoding="utf-8")
        gate = (PLUGIN_DIR / "CompatibilityGate.cs").read_text(encoding="utf-8")
        self.assertIn('Postfix(typeof(InventoryGui), "SetupRequirement", new[]', installer)
        self.assertIn('typeof(UnityEngine.Transform), typeof(Piece.Requirement), typeof(Player), typeof(bool), typeof(int), typeof(int)', installer)
        self.assertIn('RequireMethod(failures, typeof(InventoryGui), "SetupRequirement", typeof(Transform), typeof(Piece.Requirement), typeof(Player), typeof(bool), typeof(int), typeof(int))', gate)
        self.assertIn('RequireField(failures, typeof(InventoryGui), "m_selectedRecipe")', gate)
        self.assertIn('RequireField(failures, typeof(InventoryGui), "m_reqList")', gate)
        self.assertIn("internal static class NearbyCraftingHudPatch", nearby)
        self.assertIn("!RuntimeContext.Plugin.CraftingFromNearbyChestsEnabled.Value", nearby)
        self.assertIn("GetRecipeRequirementAvailability", nearby)
        self.assertIn("checked(requirement.GetAmount(qualityLevel) * craftMultiplier)", nearby)
        self.assertIn("alternatives: recipe.m_requireOnlyOneIngredient", nearby)
        self.assertIn("requireSingleQuality: recipe.m_requireOnlyOneIngredient", nearby)
        self.assertIn('var amountTransform = elementRoot.Find("res_amount")', nearby)
        self.assertIn('entry.Required.ToString(CultureInfo.InvariantCulture) + " / " +', nearby)
        self.assertIn("entry.Available.ToString(CultureInfo.InvariantCulture)", nearby)
        self.assertIn("entry.IsSatisfied || Mathf.Sin(Time.time * 10f) <= 0f", nearby)
        self.assertIn("RefreshIntervalSeconds = 0.25f", nearby)
        self.assertIn("public static class ResourceDisplayAvailability", core)
        self.assertIn("GroupBy(requirement => new RequirementKey", core)
        self.assertIn("GroupBy(stack => stack.Quality)", core)

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
