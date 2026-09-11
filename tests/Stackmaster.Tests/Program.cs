using System;
using System.Collections.Generic;
using System.Linq;
using Stackmaster.Core;

internal static class Program
{
    private static int Main()
    {
        var tests = new Action[]
        {
            EmptyInventorySortsWithoutPlacements,
            SortKeepsFixedSlotsAndMergesMovableStacks,
            SortNeverUsesEmptyQuickBarSlots,
            SortReservesEmptyProtectedSlots,
            SortDoesNotMergeIncompatibleStacksWithEqualNames,
            DepositPreservesQuickBarEquippedAndProtectedSlots,
            DepositFillsEveryPartialStackBeforeCreatingAStack,
            DepositLeavesUnmatchedOverflowInPlayerInventory,
            DepositRoutesTargetThenNearestWithStableTies,
            ReplenishmentUsesTargetThenNearest,
            ReplenishmentReportsPartialStockShortage,
            ExcessProtectedQuantityUsesDepositRouting,
            EligibilityAndInitialMatchingAreEnforced,
            PlanningBudgetStopsSafelyAndReportsPartialSearch,
            MixedPlanConservesEveryItemCount,
            ProtectionStateRoundTripsTargets,
            ProtectionStateRejectsMalformedRecords,
            ProtectionStateRejectsUnknownVersions,
            ProtectionStateValidatesTargets,
            ReplacementItemDoesNotInheritProtection,
            MatchingItemAtPreferredSlotWins,
            MovedMatchingStackInheritsProtection,
            DuplicateChoiceIsDeterministic,
            MergeSurvivorKeepsOneRecord,
            NoMatchingItemProtectsNothingUnrelated
        };

        var failures = 0;
        foreach (var test in tests)
        {
            try
            {
                test();
                Console.WriteLine("PASS " + test.Method.Name);
            }
            catch (Exception error)
            {
                failures++;
                Console.WriteLine("FAIL " + test.Method.Name + ": " + error.Message);
            }
        }

        Console.WriteLine(tests.Length + " tests, " + failures + " failures");
        return failures == 0 ? 0 : 1;
    }

    private static void EmptyInventorySortsWithoutPlacements()
    {
        var inventory = Player(8);
        var plan = new InventorySortPlanner().Plan(inventory);
        Equal(0, plan.Placements.Count, "empty inventory placement count");
        Valid(PlanValidator.ValidateSortConservation(inventory, plan));
    }

    private static void SortKeepsFixedSlotsAndMergesMovableStacks()
    {
        var inventory = Player(8,
            Item("axe", "axe", "Axe", 1, 1, 0, quickBar: true),
            Item("fixed-berry", "berry", "Berry", 3, 50, 1, protectedSlot: true),
            Item("wood-a", "wood", "Wood", 20, 50, 2),
            Item("carrot", "carrot", "Carrot", 4, 50, 3),
            Item("wood-b", "wood", "Wood", 40, 50, 4),
            Item("loose-berry", "berry", "Berry", 7, 50, 5),
            Item("helmet", "helmet", "Bronze Helmet", 1, 1, 6, equipped: true));

        var plan = new InventorySortPlanner().Plan(inventory);
        Equal(7, plan.Placements.Count, "merged stack count");
        Placement(plan, 0, "axe", 1, true);
        Placement(plan, 1, "berry", 3, true);
        Placement(plan, 2, "berry", 7, false);
        Placement(plan, 3, "carrot", 4, false);
        Placement(plan, 4, "wood", 50, false);
        Placement(plan, 5, "wood", 10, false);
        Placement(plan, 6, "helmet", 1, true);
        Valid(PlanValidator.ValidateSortConservation(inventory, plan));
    }

    private static void SortNeverUsesEmptyQuickBarSlots()
    {
        var inventory = new InventorySnapshot(
            "player",
            12,
            new[]
            {
                Item("axe", "axe", "Axe", 1, 1, 1, quickBar: true),
                Item("wood", "wood", "Wood", 10, 50, 7),
                Item("stone", "stone", "Stone", 10, 50, 11)
            },
            new[] { 0, 1, 2, 3 });

        var plan = new InventorySortPlanner().Plan(inventory);
        Placement(plan, 1, "axe", 1, true);
        SequenceEqual(new[] { 1, 4, 5 }, plan.Placements.Select(item => item.Slot), "the entire quick-bar row remains unavailable as a sort destination");
        True(plan.Placements.All(item => item.IsFixed || item.Slot >= 4), "movable items stay outside every empty quick-bar slot");
        Valid(PlanValidator.ValidateSortConservation(inventory, plan));
    }

    private static void SortReservesEmptyProtectedSlots()
    {
        var inventory = new InventorySnapshot(
            "player",
            5,
            new[]
            {
                Item("wood", "wood", "Wood", 10, 50, 3),
                Item("stone", "stone", "Stone", 10, 50, 4)
            },
            new[] { 1 });

        var plan = new InventorySortPlanner().Plan(inventory);
        SequenceEqual(new[] { 0, 2 }, plan.Placements.Select(item => item.Slot), "empty protected slot remains reserved");
        Valid(PlanValidator.ValidateSortConservation(inventory, plan));
    }

    private static void SortDoesNotMergeIncompatibleStacksWithEqualNames()
    {
        var inventory = Player(4,
            Item("wood-plain", "wood:q1", "Wood", 30, 50, 2),
            Item("wood-special", "wood:q2", "Wood", 20, 50, 1));

        var plan = new InventorySortPlanner().Plan(inventory);
        Equal(2, plan.Placements.Count, "incompatible stacks remain separate");
        Equal("wood:q1", plan.Placements[0].CompatibilityKey, "deterministic compatibility-key tie breaker");
        Equal("wood:q2", plan.Placements[1].CompatibilityKey, "deterministic compatibility-key tie breaker");
        Valid(PlanValidator.ValidateSortConservation(inventory, plan));
    }

    private static void DepositPreservesQuickBarEquippedAndProtectedSlots()
    {
        var player = Player(6,
            Item("quick-wood", "wood", "Wood", 5, 50, 0, quickBar: true),
            Item("equipped-wood", "wood", "Wood", 5, 50, 1, equipped: true),
            Item("protected-wood", "wood", "Wood", 5, 50, 2, protectedSlot: true),
            Item("movable-wood", "wood", "Wood", 5, 50, 3));
        var target = Chest("target", 0, true, 2, Item("target-wood", "wood", "Wood", 10, 50, 1));

        var plan = new StorageTransferPlanner().Plan(player, new[] { target });
        Equal(1, plan.Steps.Count, "only the movable stack is deposited");
        Equal(3, plan.Steps[0].Source.Slot, "movable source slot");
        Equal(5, plan.DepositedUnits, "only movable units deposited");
        Equal(0, plan.LeftBehindUnits, "fixed units are retained, not left-behind deposit candidates");
        Valid(PlanValidator.ValidateTransferConservation(player, new[] { target }, plan));
    }

    private static void DepositFillsEveryPartialStackBeforeCreatingAStack()
    {
        var player = Player(3, Item("player-wood", "wood", "Wood", 20, 50, 0));
        var target = Chest("target", 9, true, 3, Item("target-wood", "wood", "Wood", 45, 50, 2));
        var near = Chest("near", 1, false, 2, Item("near-wood", "wood", "Wood", 40, 50, 1));

        var plan = new StorageTransferPlanner().Plan(player, new[] { near, target });
        Equal(3, plan.Steps.Count, "deposit step count");
        Step(plan.Steps[0], TransferKind.Deposit, "target", 2, 5);
        Step(plan.Steps[1], TransferKind.Deposit, "near", 1, 10);
        Step(plan.Steps[2], TransferKind.Deposit, "target", 0, 5);
        Equal(20, plan.DepositedUnits, "deposited units");
        Equal(0, plan.LeftBehindUnits, "left behind units");
        Valid(PlanValidator.ValidateTransferConservation(player, new[] { near, target }, plan));
    }

    private static void DepositLeavesUnmatchedOverflowInPlayerInventory()
    {
        var player = Player(2, Item("player-wood", "wood", "Wood", 20, 50, 0));
        var target = Chest("target", 0, true, 1, Item("target-wood", "wood", "Wood", 45, 50, 0));
        var unrelated = Chest("unrelated", 1, false, 3, Item("stone", "stone", "Stone", 1, 50, 2));
        var containers = new[] { unrelated, target };

        var plan = new StorageTransferPlanner().Plan(player, containers);
        Equal(1, plan.Steps.Count, "only the compatible partial stack is used");
        Step(plan.Steps[0], TransferKind.Deposit, "target", 0, 5);
        Equal(5, plan.DepositedUnits, "deposited overflow units");
        Equal(15, plan.LeftBehindUnits, "unmatched overflow retained");
        Valid(PlanValidator.ValidateTransferConservation(player, containers, plan));
    }

    private static void DepositRoutesTargetThenNearestWithStableTies()
    {
        var player = Player(2, Item("player-wood", "wood", "Wood", 40, 50, 0));
        var target = Chest("target", 30, true, 2, Item("target-match", "wood", "Wood", 50, 50, 1));
        var nearB = Chest("near-b", 2, false, 2, Item("b-match", "wood", "Wood", 50, 50, 1));
        var nearA = Chest("near-a", 2, false, 2, Item("a-match", "wood", "Wood", 50, 50, 1));
        var far = Chest("far", 8, false, 2, Item("far-match", "wood", "Wood", 50, 50, 1));

        var plan = new StorageTransferPlanner().Plan(player, new[] { far, nearB, target, nearA });
        Equal(1, plan.Steps.Count, "all units fit in target's first empty slot");
        Step(plan.Steps[0], TransferKind.Deposit, "target", 0, 40);
        SequenceEqual(new[] { "target", "near-a", "near-b", "far" }, plan.InspectedContainerIds, "inspection order");
        Valid(PlanValidator.ValidateTransferConservation(player, new[] { far, nearB, target, nearA }, plan));

        var largerPlayer = Player(4,
            Item("player-wood-a", "wood", "Wood", 50, 50, 0),
            Item("player-wood-b", "wood", "Wood", 50, 50, 1),
            Item("player-wood-c", "wood", "Wood", 50, 50, 2));
        var largerPlan = new StorageTransferPlanner().Plan(largerPlayer, new[] { far, nearB, target, nearA });
        Equal("target", largerPlan.Steps[0].Destination.InventoryId, "target first");
        Equal("near-a", largerPlan.Steps[1].Destination.InventoryId, "stable id tie breaker");
        Equal("near-b", largerPlan.Steps[2].Destination.InventoryId, "stable id tie breaker");
        Valid(PlanValidator.ValidateTransferConservation(largerPlayer, new[] { far, nearB, target, nearA }, largerPlan));
    }

    private static void ReplenishmentUsesTargetThenNearest()
    {
        var player = Player(4, Item("protected-arrows", "arrow", "Wood Arrow", 5, 100, 1, protectedSlot: true, target: 20));
        var target = Chest("target", 20, true, 2, Item("target-arrows", "arrow", "Wood Arrow", 7, 100, 1));
        var near = Chest("near", 1, false, 2, Item("near-arrows", "arrow", "Wood Arrow", 20, 100, 0));

        var plan = new StorageTransferPlanner().Plan(player, new[] { near, target });
        Equal(2, plan.Steps.Count, "replenishment step count");
        ReplenishmentStep(plan.Steps[0], "target", 7);
        ReplenishmentStep(plan.Steps[1], "near", 8);
        Equal(15, plan.ReplenishedUnits, "replenished units");
        Equal(0, plan.Shortages.Count, "no shortage");
        Valid(PlanValidator.ValidateTransferConservation(player, new[] { near, target }, plan));
    }

    private static void ReplenishmentReportsPartialStockShortage()
    {
        var player = Player(4, Item("protected-arrows", "arrow", "Wood Arrow", 5, 100, 1, protectedSlot: true, target: 20));
        var target = Chest("target", 0, true, 1, Item("target-arrows", "arrow", "Wood Arrow", 3, 100, 0));

        var plan = new StorageTransferPlanner().Plan(player, new[] { target });
        Equal(3, plan.ReplenishedUnits, "partial stock is still used");
        Equal(1, plan.Shortages.Count, "shortage count");
        Equal(12, plan.Shortages[0].MissingQuantity, "missing units");
        Valid(PlanValidator.ValidateTransferConservation(player, new[] { target }, plan));
    }

    private static void ExcessProtectedQuantityUsesDepositRouting()
    {
        var player = Player(4, Item("protected-wood", "wood", "Wood", 30, 50, 2, protectedSlot: true, target: 20));
        var target = Chest("target", 10, true, 1, Item("target-wood", "wood", "Wood", 45, 50, 0));
        var near = Chest("near", 1, false, 2, Item("near-wood", "wood", "Wood", 48, 50, 1));

        var plan = new StorageTransferPlanner().Plan(player, new[] { near, target });
        Equal(3, plan.Steps.Count, "excess route step count");
        Step(plan.Steps[0], TransferKind.ExcessDeposit, "target", 0, 5);
        Step(plan.Steps[1], TransferKind.ExcessDeposit, "near", 1, 2);
        Step(plan.Steps[2], TransferKind.ExcessDeposit, "near", 0, 3);
        Equal(10, plan.DepositedUnits, "all excess deposited");
        Equal(0, plan.LeftBehindUnits, "no excess remains");
        Valid(PlanValidator.ValidateTransferConservation(player, new[] { near, target }, plan));
    }

    private static void EligibilityAndInitialMatchingAreEnforced()
    {
        var player = Player(2, Item("player-stone", "stone", "Stone", 10, 50, 0));
        var targetWithoutMatch = Chest("target", 0, true, 2, Item("wood", "wood", "Wood", 1, 50, 1));
        var inaccessible = Chest("locked", 1, false, 2, Item("locked-stone", "stone", "Stone", 1, 50, 1), accessible: false);
        var modded = Chest("modded", 2, false, 2, Item("modded-stone", "stone", "Stone", 1, 50, 1), vanilla: false);
        var inUse = Chest("in-use", 2.5, false, 2, Item("used-stone", "stone", "Stone", 1, 50, 1), inUse: true);
        var eligible = Chest("eligible", 3, false, 2, Item("eligible-stone", "stone", "Stone", 45, 50, 1));

        var containers = new[] { modded, eligible, inUse, inaccessible, targetWithoutMatch };
        var plan = new StorageTransferPlanner().Plan(player, containers);
        Equal(2, plan.Steps.Count, "eligible matching container receives partial then new stack");
        Equal("eligible", plan.Steps[0].Destination.InventoryId, "only eligible matching destination");
        Equal("eligible", plan.Steps[1].Destination.InventoryId, "only eligible matching destination");
        True(plan.SkippedContainers.Any(item => item.ContainerId == "locked"), "locked container reported skipped");
        True(plan.SkippedContainers.Any(item => item.ContainerId == "modded"), "modded container reported skipped");
        True(plan.SkippedContainers.Any(item => item.ContainerId == "in-use" && item.Reason == "in use"), "in-use container reported skipped");
        Valid(PlanValidator.ValidateTransferConservation(player, containers, plan));
    }

    private static void PlanningBudgetStopsSafelyAndReportsPartialSearch()
    {
        var player = Player(2, Item("player-wood", "wood", "Wood", 10, 50, 0));
        var target = Chest("target", 0, true, 1, Item("target-wood", "wood", "Wood", 45, 50, 0));
        var near = Chest("near", 1, false, 2, Item("near-wood", "wood", "Wood", 1, 50, 1));
        var far = Chest("far", 2, false, 2, Item("far-wood", "wood", "Wood", 1, 50, 1));
        var containers = new[] { far, near, target };

        var plan = new StorageTransferPlanner().Plan(player, containers, new FixedWorkPlanningBudget(1));
        True(plan.SearchTruncated, "search truncated");
        Equal(TransferPlan.PartialSearchNotice, plan.Notice, "partial-search notice");
        SequenceEqual(new[] { "target" }, plan.InspectedContainerIds, "only budgeted prefix inspected");
        Equal(5, plan.DepositedUnits, "safe completed transfers kept");
        Equal(5, plan.LeftBehindUnits, "unrouted remainder retained");
        Valid(PlanValidator.ValidateTransferConservation(player, containers, plan));
    }

    private static void MixedPlanConservesEveryItemCount()
    {
        var player = Player(8,
            Item("protected-arrows", "arrow", "Wood Arrow", 5, 100, 0, protectedSlot: true, target: 10),
            Item("wood", "wood", "Wood", 60, 100, 2),
            Item("stone", "stone", "Stone", 7, 50, 3));
        var target = Chest("target", 9, true, 5,
            Item("arrows", "arrow", "Wood Arrow", 8, 100, 0),
            Item("wood-target", "wood", "Wood", 95, 100, 3));
        var near = Chest("near", 1, false, 4,
            Item("wood-near", "wood", "Wood", 50, 100, 1),
            Item("stone-near", "stone", "Stone", 49, 50, 2));
        var containers = new[] { near, target };
        var originalPlayerTotal = player.Items.Sum(item => item.Quantity);
        var originalContainerTotal = containers.SelectMany(container => container.Items).Sum(item => item.Quantity);

        var plan = new StorageTransferPlanner().Plan(player, containers);
        Valid(PlanValidator.ValidateTransferConservation(player, containers, plan));
        Equal(originalPlayerTotal, player.Items.Sum(item => item.Quantity), "player snapshot remains unchanged");
        Equal(originalContainerTotal, containers.SelectMany(container => container.Items).Sum(item => item.Quantity), "container snapshots remain unchanged");
        Equal(5, plan.ReplenishedUnits, "mixed replenished count");
        Equal(67, plan.DepositedUnits, "mixed deposited count");
    }

    private static void ProtectionStateRoundTripsTargets()
    {
        var state = new ProtectionState();
        state.Protect(new Slot(2, 3), 42, "wood|quality=1;custom=å");
        state.Protect(new Slot(0, 1), null, "hammer|quality=1");

        var parsed = ProtectionState.Parse(state.Serialize());
        Equal(2, parsed.Records.Count, "round-trip protected record count");
        ProtectionRecord target;
        True(parsed.TryGet(new Slot(2, 3), out target), "target slot survives round trip");
        Equal(42, target.TargetQuantity, "target quantity survives round trip");
        Equal("wood|quality=1;custom=å", target.TargetItemKey, "target identity survives round trip");
        True(parsed.IsProtected(new Slot(0, 1)), "protect-only slot survives round trip");
    }

    private static void ProtectionStateRejectsMalformedRecords()
    {
        var valid = new ProtectionState(new[] { new ProtectionRecord(new Slot(1, 2), null, "wood") }).Serialize();
        ProtectionState parsed;
        True(!ProtectionState.TryParse(valid + ";bad", out parsed), "malformed record invalidates the payload");
        Equal(0, parsed.Records.Count, "invalid payload exposes no partial protection state");
        True(!ProtectionState.TryParse(valid + ";1,-2,,", out parsed), "invalid slot invalidates the payload");
        True(!ProtectionState.TryParse(valid + ";3,4,2,%%%not-base64%%%", out parsed), "invalid base64 item key invalidates the payload");
        True(!ProtectionState.TryParse("v1;0,1,1,/w==", out parsed), "invalid UTF-8 item key invalidates the payload");
    }

    private static void ProtectionStateRejectsUnknownVersions()
    {
        ProtectionState parsed;
        True(!ProtectionState.TryParse("v999;1,2,,", out parsed), "unknown version is explicitly rejected");
        Equal(0, parsed.Records.Count, "unknown version exposes no protection state");
    }

    private static void ProtectionStateValidatesTargets()
    {
        var state = new ProtectionState();
        var missingKeyThrew = false;
        try { state.Protect(new Slot(0, 0), 1, null); }
        catch (ArgumentException) { missingKeyThrew = true; }
        True(missingKeyThrew, "target without item identity is rejected");

        var protectionOnlyMissingKeyThrew = false;
        try { state.Protect(new Slot(0, 0), null, null); }
        catch (ArgumentException) { protectionOnlyMissingKeyThrew = true; }
        True(protectionOnlyMissingKeyThrew, "protection-only record without item identity is rejected");

        var nonPositiveThrew = false;
        try { state.Protect(new Slot(0, 0), 0, "wood"); }
        catch (ArgumentOutOfRangeException) { nonPositiveThrew = true; }
        True(nonPositiveThrew, "non-positive target is rejected");
    }

    private static void ReplacementItemDoesNotInheritProtection()
    {
        var state = new ProtectionState(new[] { new ProtectionRecord(new Slot(1, 1), null, "wood") });
        var resolution = state.Reconcile(new[] { new ProtectionCandidate(new Slot(1, 1), "stone") });
        Equal(0, resolution.Assignments.Count, "replacement item remains unprotected");
        True(!resolution.TryGet(new Slot(1, 1), out _), "old slot does not confer protection");
    }

    private static void MatchingItemAtPreferredSlotWins()
    {
        var state = new ProtectionState(new[] { new ProtectionRecord(new Slot(2, 1), 20, "arrow") });
        var resolution = state.Reconcile(new[]
        {
            new ProtectionCandidate(new Slot(0, 0), "arrow"),
            new ProtectionCandidate(new Slot(2, 1), "arrow")
        });
        True(resolution.TryGet(new Slot(2, 1), out var record), "preferred matching stack is assigned");
        Equal(20, record.TargetQuantity, "preferred assignment retains target");
        True(!resolution.TryGet(new Slot(0, 0), out _), "only one matching stack is protected");
        True(!resolution.Changed, "unchanged preferred assignment does not rewrite persistence");
    }

    private static void MovedMatchingStackInheritsProtection()
    {
        var state = new ProtectionState(new[] { new ProtectionRecord(new Slot(3, 2), 12, "food") });
        var resolution = state.Reconcile(new[]
        {
            new ProtectionCandidate(new Slot(3, 2), "stone"),
            new ProtectionCandidate(new Slot(1, 0), "food")
        });
        True(resolution.TryGet(new Slot(1, 0), out var record), "moved compatible stack inherits protection");
        Equal(new Slot(1, 0), record.Slot, "preferred slot follows moved stack");
        True(resolution.Changed, "moved assignment requests persistence");
        True(!resolution.TryGet(new Slot(3, 2), out _), "replacement at old slot remains unprotected");
    }

    private static void DuplicateChoiceIsDeterministic()
    {
        var state = new ProtectionState(new[] { new ProtectionRecord(new Slot(3, 3), null, "wood") });
        var resolution = state.Reconcile(new[]
        {
            new ProtectionCandidate(new Slot(2, 2), "wood"),
            new ProtectionCandidate(new Slot(3, 0), "wood"),
            new ProtectionCandidate(new Slot(0, 1), "wood")
        });
        True(resolution.TryGet(new Slot(3, 0), out _), "row-major first duplicate wins deterministically");
        Equal(1, resolution.Assignments.Count, "exactly one duplicate stack is protected");
    }

    private static void MergeSurvivorKeepsOneRecord()
    {
        var state = new ProtectionState(new[]
        {
            new ProtectionRecord(new Slot(0, 1), null, "wood"),
            new ProtectionRecord(new Slot(1, 1), null, "wood")
        });
        var resolution = state.Reconcile(new[] { new ProtectionCandidate(new Slot(1, 1), "wood") });
        Equal(1, resolution.Assignments.Count, "merged survivor gets one assignment");
        Equal(1, state.Records.Count, "merged-away duplicate record is removed");
        True(resolution.TryGet(new Slot(1, 1), out _), "preferred surviving stack keeps protection");
        True(resolution.Changed, "record merge requests persistence");
    }

    private static void NoMatchingItemProtectsNothingUnrelated()
    {
        var state = new ProtectionState(new[] { new ProtectionRecord(new Slot(2, 0), 7, "berry") });
        var resolution = state.Reconcile(new[]
        {
            new ProtectionCandidate(new Slot(2, 0), "wood"),
            new ProtectionCandidate(new Slot(0, 2), "stone")
        });
        Equal(0, resolution.Assignments.Count, "no unrelated stack is protected");
        Equal(1, state.Records.Count, "identified record stays dormant for its item");
        True(!resolution.Changed, "dormant record does not churn persistence");
    }

    private static InventorySnapshot Player(int capacity, params ItemStackSnapshot[] items)
        => new InventorySnapshot("player", capacity, items);

    private static ContainerSnapshot Chest(
        string id,
        double distance,
        bool target,
        int capacity,
        ItemStackSnapshot item,
        bool known = true,
        bool accessible = true,
        bool vanilla = true,
        bool inUse = false)
        => new ContainerSnapshot(id, distance, target, known, accessible, vanilla, inUse, capacity, new[] { item });

    private static ContainerSnapshot Chest(
        string id,
        double distance,
        bool target,
        int capacity,
        ItemStackSnapshot first,
        ItemStackSnapshot second)
        => new ContainerSnapshot(id, distance, target, true, true, true, false, capacity, new[] { first, second });

    private static ItemStackSnapshot Item(
        string id,
        string key,
        string name,
        int quantity,
        int max,
        int slot,
        bool quickBar = false,
        bool equipped = false,
        bool protectedSlot = false,
        int? target = null)
        => new ItemStackSnapshot(id, key, name, quantity, max, slot, quickBar, equipped, protectedSlot, target);

    private static void Placement(SortPlan plan, int slot, string key, int quantity, bool fixedPlacement)
    {
        var item = plan.Placements.Single(placement => placement.Slot == slot);
        Equal(key, item.CompatibilityKey, "placement key at slot " + slot);
        Equal(quantity, item.Quantity, "placement quantity at slot " + slot);
        Equal(fixedPlacement, item.IsFixed, "fixed flag at slot " + slot);
    }

    private static void Step(TransferStep step, TransferKind kind, string destinationId, int destinationSlot, int quantity)
    {
        Equal(kind, step.Kind, "transfer kind");
        Equal(destinationId, step.Destination.InventoryId, "destination id");
        Equal(destinationSlot, step.Destination.Slot, "destination slot");
        Equal(quantity, step.Quantity, "transfer quantity");
    }

    private static void ReplenishmentStep(TransferStep step, string sourceId, int quantity)
    {
        Equal(TransferKind.Replenishment, step.Kind, "replenishment kind");
        Equal(sourceId, step.Source.InventoryId, "replenishment source");
        Equal(quantity, step.Quantity, "replenishment quantity");
    }

    private static void Valid(ValidationResult result)
    {
        if (!result.IsValid) throw new Exception(string.Join(" | ", result.Errors));
    }

    private static void True(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception(message + ": expected " + expected + ", got " + actual);
    }

    private static void SequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual, string message)
    {
        if (!expected.SequenceEqual(actual))
            throw new Exception(message + ": expected [" + string.Join(", ", expected) + "], got [" + string.Join(", ", actual) + "]");
    }
}
