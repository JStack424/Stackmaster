using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx.Configuration;
using Stackmaster;
using Stackmaster.Core;

internal static class Program
{
    private static int Main()
    {
        var tests = new Action[]
        {
            ChestSortDefaultsToEnabled,
            ChestSortKeyScopesPlayerWorldAndChest,
            ChestSortRejectsUnsafeIdentityAndStoredValues,
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
            OrdinaryBaseInspectionIgnoresElapsedBudgetUntilComplete,
            OrdinaryBaseRoutesItemsIntoNonTargetChest,
            DenseBaseInspectionStopsAfterGuaranteedPrefix,
            PlanningBudgetStopsSafelyAndReportsPartialSearch,
            MixedPlanConservesEveryItemCount,
            ProtectionStateRoundTripsTargets,
            ProtectionStateRejectsMalformedRecords,
            ProtectionStateRejectsUnknownVersions,
            ProtectionStateValidatesTargets,
            ProtectionLeftClickProtectsWithoutTargetOrDialog,
            ProtectionLeftClickClearsProtectionAndTarget,
            ProtectionRightClickOpensTargetDialogWithoutMutatingState,
            ProtectionRightClickConfirmAddsTarget,
            ProtectionRightClickConfirmEditsTarget,
            ProtectionRightClickCancelPreservesExactState,
            ProtectionRightClickNonStackableLeavesStateUnchanged,
            ProtectionOrdinaryClicksRemainVanilla,
            ProtectionConfiguredModifierRequiresEveryKey,
            ReplacementItemDoesNotInheritProtection,
            MatchingItemAtPreferredSlotWins,
            MovedMatchingStackInheritsProtection,
            DuplicateChoiceIsDeterministic,
            MergeSurvivorKeepsOneRecord,
            NoMatchingItemProtectsNothingUnrelated,
            FullChestTransferClearsProtection,
            FullWorldDropClearsProtection,
            PartialChestTransferKeepsProtection,
            PartialWorldDropKeepsProtection,
            InternalMoveKeepsProtection,
            TransientDragKeepsProtection,
            HotkeyPrunesLegacyOrphanedTarget,
            HotkeyPruningPreservesValidMovedTarget,
            ResourceAvailabilityCountsPlayerNearbyAndQuality,
            ResourceDisplayAggregatesDuplicateRequirements,
            ResourceDisplayUsesUpgradeAndMultiCraftTotals,
            ResourceDisplayAlternativesRequireOneQualityTier,
            RequirementPresentationFormatsAggregateTotals,
            RequirementPresentationRetainsLongExactTotals,
            RequirementPresentationUsesRedOnlyForTrueShortages,
            RequirementUiPolicyCoversEveryConfigurationCombination,
            RequirementUiPolicyLeavesVanillaUntouchedOnlyWhenBothFeaturesAreOff,
            RenamedStoragePermissionMigratesLegacyTrueFalseAndMissing,
            RenamedStoragePermissionMigrationIsIdempotent,
            ResourcePlanAggregatesPlayerAndNearbyStacks,
            ResourcePlanRejectsFiftyWhenOnlyTwentyFiveExist,
            ResourcePlanConsumesExactlyFiftyAcrossPartialStacks,
            ResourcePlanNormalizesDuplicateRequirements,
            ResourcePlanDoesNotDoubleConsume,
            CraftingExactDebitConsumesPlayerAndSelectedChest,
            CraftingExactDebitCannotDoubleCharge,
            CraftingIncompleteDebitCannotCommitFreeOutput,
            CraftingCancellationRollsBackExactDebit,
            ResourcePlanHonorsExactQualityAndMultiplierTotals,
            ResourcePlanIsAllOrNothingAcrossDifferentMaterials,
            ExpeditionClickRequiresEveryConfiguredModifier,
            ExpeditionNormalClickIsUnchanged,
            ExpeditionKitIgnoresCarriedMaterials,
            ExpeditionRepeatedClicksPlanRepeatedFullKits,
            ExpeditionKitUsesLargestStockFirst,
            ExpeditionKitBreaksStockTiesDeterministically,
            ExpeditionKitSplitsAcrossSources,
            ExpeditionShortageHasNoMutationSteps,
            ExpeditionCapacityFillsStacksThenEmptySlots,
            ExpeditionCapacityRejectsSharedSlotOverbooking,
            ExpeditionCapacityRejectsOverweightKit,
            ExpeditionCapacityAcceptsExactWeightLimit,
            ExpeditionFallbackRollbackPreservesEquippedItemIdentityAndState,
            ReservationCleanupMustPrecedeOwnershipRelease,
            ResourcePlanUsesPlayerThenDeterministicContainerOrder,
            ResourcePlanMinimizesDistinctContainers,
            ResourcePlanMinimizesAcrossDifferentMaterials,
            ResourceOwnershipSelectionUsesPlayerFirstRemainder,
            ResourceOwnershipSelectionUsesOnlyNeededChests,
            ResourceOwnershipSelectionDeduplicatesChestStacks,
            ResourceOwnershipSelectionHasBuildCraftParity,
            ResourceOwnershipRevisionRejectsStaleRequiredChest,
            ResourceOwnershipRevisionIgnoresUnrelatedChest,
            ResourceReadOnlyRemoteUnopenedUsesDetachedInventory,
            ResourceReadOnlyDecodeFailureFailsClosed,
            ResourceLiveSnapshotRequiresKnownInventory,
            DetachedHydrationMakesLoadedItemsCountable,
            DetachedHydrationRejectsIncompleteMetadata,
            OwnershipReleaseRequiresExactIdentity,
            OwnershipReleaseRequiresOriginalLocalSession,
            OwnershipReleaseRequiresCurrentLocalOwner,
            OwnershipReleaseRequiresExactOwnerRevision,
            OwnershipRevisionSuccessorWrapsExactly,
            OwnershipReleaseAlwaysReturnsToVanillaUnownedState,
            OwnershipBuildLeaseRequiresDemonstrableAcquisition,
            OwnershipBuildLeaseRequiresSuccessfulUse,
            OwnershipBuildLeaseRenewsFromCurrentTime,
            OwnershipBuildLeaseExpiryUsesBoundary,
            OwnershipBuildLeaseYieldsToRemoteManualOpen,
            OwnershipBuildLeaseDoesNotYieldToLocalManualOpen,
            OwnershipBuildLeaseDoesNotYieldWhileLogicallyReserved,
            OwnershipRetryLeaseDoesNotUseBuildPreemption,
            CraftingIntentDefersThenStartsExactlyOneBar,
            CraftingReservationPrecedesCrafting,
            CraftingCancelInvalidatesDelayedOwnership,
            CraftingTransferCanHappenOnlyOnce,
            CraftingRepeatedAttemptsUseNewGeneration,
            CraftingDisconnectInvalidatesPreviousSessionCallback,
            SessionDisconnectSuspendsAndSafeReconnectRearms,
            SessionReconnectRequiresDifferentNetworkAndSafeCleanup,
            PermanentDisableNeverRearms,
            RepeatedDisconnectReconnectCyclesAreIdempotent,
            LifecycleShutdownIsTerminal,
            WorkbenchMeshIgnoresVerticalDistance,
            WorkbenchMeshUsesStrictBoundaries,
            TangentWorkbenchZonesDoNotConnect,
            WorkbenchMeshConnectsTransitively,
            WorkbenchMeshSeedsEveryZoneContainingPlayer,
            WorkbenchMeshExcludesDisconnectedIsland,
            FarChestIsIncludedThroughOverlapChain,
            ChestInGeometricGapIsExcluded,
            OutsideMeshUsesConfiguredThreeDimensionalFallback,
            InvalidWorkbenchZonesAreRejected,
            WorkbenchEnumerationOrderIsDeterministic,
            ConflictingDuplicateWorkbenchIdsAreRejected,
            PlayerMovementSwitchesMeshAndFallback,
            WorkbenchPlacementAndDestructionChangeMesh
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

    private static void ChestSortDefaultsToEnabled()
    {
        bool enabled;
        True(ChestSortPreferencePolicy.TryInterpretStoredValue(false, 0, out enabled), "missing preference is valid");
        True(enabled, "missing preference defaults to enabled");
        True(ChestSortPreferencePolicy.TryInterpretStoredValue(true, 0, out enabled), "disabled preference is valid");
        True(!enabled, "stored zero disables only the keyed chest");
    }

    private static void ChestSortKeyScopesPlayerWorldAndChest()
    {
        string first;
        string otherPlayer;
        string otherWorld;
        string otherChest;
        True(ChestSortPreferencePolicy.TryCreateKey(11, 22, "33:44", out first), "base key is stable");
        True(ChestSortPreferencePolicy.TryCreateKey(12, 22, "33:44", out otherPlayer), "other player key");
        True(ChestSortPreferencePolicy.TryCreateKey(11, 23, "33:44", out otherWorld), "other world key");
        True(ChestSortPreferencePolicy.TryCreateKey(11, 22, "33:45", out otherChest), "other chest key");
        True(first != otherPlayer, "player identity scopes the preference");
        True(first != otherWorld, "world identity scopes the preference");
        True(first != otherChest, "ZDO identity scopes the preference");
        True(first.StartsWith(ChestSortPreferencePolicy.KeyPrefix + "/", StringComparison.Ordinal), "versioned local key prefix");
    }

    private static void ChestSortRejectsUnsafeIdentityAndStoredValues()
    {
        string key;
        bool enabled;
        True(!ChestSortPreferencePolicy.TryCreateKey(0, 22, "33:44", out key), "missing player identity is rejected");
        True(!ChestSortPreferencePolicy.TryCreateKey(11, 0, "33:44", out key), "missing world identity is rejected");
        True(!ChestSortPreferencePolicy.TryCreateKey(11, 22, "  ", out key), "missing chest identity is rejected");
        True(!ChestSortPreferencePolicy.TryInterpretStoredValue(true, 2, out enabled), "unknown stored value fails closed");
        True(!enabled, "unknown stored value never enables sorting");
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

    private static void OrdinaryBaseInspectionIgnoresElapsedBudgetUntilComplete()
    {
        var policy = new NearbyInspectionPolicy(minimumBeforeBudget: 8, maximumInspections: 128, budgetMilliseconds: 100);
        for (var inspected = 0; inspected < 3; inspected++)
        {
            True(policy.CanInspectNext(inspected, 5000), "ordinary three-chest base remains fully inspectable after a slow object scan");
        }
    }

    private static void OrdinaryBaseRoutesItemsIntoNonTargetChest()
    {
        var player = Player(2, Item("player-stone", "stone", "Stone", 10, 50, 0));
        var target = Chest("target", 0, true, 2, Item("target-wood", "wood", "Wood", 1, 50, 0));
        var nearbyMatch = Chest("nearby-match", 1, false, 2, Item("nearby-stone", "stone", "Stone", 45, 50, 0));
        var otherNearby = Chest("other-nearby", 2, false, 2, Item("other-wood", "wood", "Wood", 1, 50, 0));
        var policy = new NearbyInspectionPolicy(minimumBeforeBudget: 8, maximumInspections: 128, budgetMilliseconds: 100);
        var discovered = new List<ContainerSnapshot> { target };
        var nearby = new[] { nearbyMatch, otherNearby };
        for (var index = 0; index < nearby.Length; index++)
        {
            if (!policy.CanInspectNext(index, 5000)) break;
            discovered.Add(nearby[index]);
        }

        Equal(3, discovered.Count, "all three ordinary-base chests discovered despite elapsed time");
        var plan = new StorageTransferPlanner().Plan(player, discovered);
        True(plan.Steps.Any(step => step.Destination.InventoryId == "nearby-match"), "non-target matching chest receives deposited items");
        Equal(10, plan.DepositedUnits, "all matching items route beyond the targeted chest");
        Equal(0, plan.LeftBehindUnits, "ordinary nearby discovery leaves no matching remainder");
        Valid(PlanValidator.ValidateTransferConservation(player, discovered, plan));

        var replenishPlayer = Player(4, Item("protected-arrows", "arrow", "Wood Arrow", 5, 100, 1, protectedSlot: true, target: 20));
        var replenishTarget = Chest("replenish-target", 0, true, 2, Item("target-wood", "wood", "Wood", 1, 50, 0));
        var nearbyStock = Chest("nearby-stock", 1, false, 2, Item("nearby-arrows", "arrow", "Wood Arrow", 20, 100, 0));
        var replenishDiscovered = new List<ContainerSnapshot> { replenishTarget };
        var replenishNearby = new[] { nearbyStock, otherNearby };
        for (var index = 0; index < replenishNearby.Length; index++)
        {
            if (!policy.CanInspectNext(index, 5000)) break;
            replenishDiscovered.Add(replenishNearby[index]);
        }

        var replenishPlan = new StorageTransferPlanner().Plan(replenishPlayer, replenishDiscovered);
        True(replenishPlan.Steps.Any(step => step.Kind == TransferKind.Replenishment && step.Source.InventoryId == "nearby-stock"),
            "non-target nearby chest supplies protected-slot replenishment");
        Equal(15, replenishPlan.ReplenishedUnits, "full target quantity pulls from a non-target chest");
        Valid(PlanValidator.ValidateTransferConservation(replenishPlayer, replenishDiscovered, replenishPlan));
    }

    private static void DenseBaseInspectionStopsAfterGuaranteedPrefix()
    {
        var policy = new NearbyInspectionPolicy(minimumBeforeBudget: 8, maximumInspections: 128, budgetMilliseconds: 100);
        True(policy.CanInspectNext(7, 5000), "guaranteed ordinary-base prefix completes even after budget");
        True(!policy.CanInspectNext(8, 5000), "elapsed budget stops additional dense-base inspection after guaranteed prefix");
        Equal("inspection time budget reached", policy.StopReason(8, 5000), "time stop reason remains diagnostic");
        True(!policy.CanInspectNext(128, 0), "hard maximum bounds dense-base inspection even when fast");
        Equal("hard safety limit reached", policy.StopReason(128, 0), "hard-limit stop reason remains diagnostic");
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

    private static void ProtectionLeftClickProtectsWithoutTargetOrDialog()
    {
        var slot = new Slot(1, 2);
        var state = new ProtectionState();
        var route = ProtectionInteractionPolicy.Route(
            true, true, true, true, false, ProtectionPointerButton.Left, new[] { true });

        Equal(ProtectionClickRoute.ProtectOnly, route, "modified left click routes directly to protection-only");
        True(route != ProtectionClickRoute.OpenTargetDialog, "modified left click never opens a target dialog");
        ProtectionInteractionPolicy.ApplyLeftClick(state, null!, slot, "wood");
        ProtectionRecord record;
        True(state.TryGet(slot, out record), "modified left click protects the item");
        Equal(null, record.TargetQuantity, "modified left click creates no restocking target");
    }

    private static void ProtectionLeftClickClearsProtectionAndTarget()
    {
        var slot = new Slot(2, 1);
        var state = new ProtectionState(new[] { new ProtectionRecord(slot, 37, "arrow") });
        ProtectionRecord existing;
        True(state.TryGet(slot, out existing), "targeted item starts protected");
        var route = ProtectionInteractionPolicy.Route(
            true, true, true, true, true, ProtectionPointerButton.Left, new[] { true });

        Equal(ProtectionClickRoute.Unprotect, route, "modified left click routes protected items to unprotect");
        ProtectionInteractionPolicy.ApplyLeftClick(state, existing, slot, "arrow");
        True(!state.IsProtected(slot), "modified left click removes protection and its target together");
        Equal(0, state.Records.Count, "target record is fully removed");
    }

    private static void ProtectionRightClickOpensTargetDialogWithoutMutatingState()
    {
        var slot = new Slot(0, 2);
        var state = new ProtectionState(new[] { new ProtectionRecord(slot, null, "food") });
        var before = state.Serialize();
        var route = ProtectionInteractionPolicy.Route(
            true, true, true, true, true, ProtectionPointerButton.Right, new[] { true });

        Equal(ProtectionClickRoute.OpenTargetDialog, route, "modified right click routes stackable items to target dialog");
        Equal(before, state.Serialize(), "opening the right-click dialog does not alter protection or target state");
    }

    private static void ProtectionRightClickConfirmAddsTarget()
    {
        var slot = new Slot(0, 3);
        var state = new ProtectionState(new[] { new ProtectionRecord(slot, null, "food") });
        int target;
        True(ProtectionInteractionPolicy.TryApplyTarget(state, slot, "food", 20, " 12 ", out target),
            "right-click target confirmation accepts a legal quantity");
        Equal(12, target, "confirmed target is parsed exactly");
        ProtectionRecord record;
        True(state.TryGet(slot, out record), "protected item remains protected after adding a target");
        Equal(12, record.TargetQuantity, "right-click confirmation adds the requested target");
    }

    private static void ProtectionRightClickConfirmEditsTarget()
    {
        var slot = new Slot(3, 1);
        var state = new ProtectionState(new[] { new ProtectionRecord(slot, 8, "arrow") });
        int target;
        True(ProtectionInteractionPolicy.TryApplyTarget(state, slot, "arrow", 100, "42", out target),
            "right-click target confirmation edits an existing target");
        ProtectionRecord record;
        True(state.TryGet(slot, out record), "edited item remains protected");
        Equal(42, record.TargetQuantity, "existing target is replaced rather than duplicated");
        Equal(1, state.Records.Count, "editing a target keeps exactly one protection record");
    }

    private static void ProtectionRightClickCancelPreservesExactState()
    {
        var slot = new Slot(4, 2);
        var state = new ProtectionState(new[] { new ProtectionRecord(slot, 19, "resin") });
        var before = state.Serialize();
        var route = ProtectionInteractionPolicy.Route(
            true, true, true, true, true, ProtectionPointerButton.Right, new[] { true });

        Equal(ProtectionClickRoute.OpenTargetDialog, route, "modified right click opens editing for an existing target");
        // Cancel invokes no confirmation transition.
        Equal(before, state.Serialize(), "cancel preserves the exact prior protection and target payload");

        int ignored;
        True(!ProtectionInteractionPolicy.TryApplyTarget(state, slot, "resin", 50, "0", out ignored),
            "zero is not a valid restocking target in the dedicated target dialog");
        Equal(before, state.Serialize(), "invalid confirmation also preserves exact prior state");
    }

    private static void ProtectionRightClickNonStackableLeavesStateUnchanged()
    {
        var slot = new Slot(1, 0);
        var state = new ProtectionState(new[] { new ProtectionRecord(slot, null, "hammer") });
        var before = state.Serialize();
        var route = ProtectionInteractionPolicy.Route(
            true, true, true, false, true, ProtectionPointerButton.Right, new[] { true });

        Equal(ProtectionClickRoute.SuppressWithoutChange, route, "modified right click suppresses target editing for non-stackable items");
        Equal(before, state.Serialize(), "non-stackable right click leaves protection state unchanged");
    }

    private static void ProtectionOrdinaryClicksRemainVanilla()
    {
        Equal(ProtectionClickRoute.Vanilla,
            ProtectionInteractionPolicy.Route(true, true, true, true, false, ProtectionPointerButton.Left, new[] { false }),
            "ordinary left click remains vanilla");
        Equal(ProtectionClickRoute.Vanilla,
            ProtectionInteractionPolicy.Route(true, true, true, true, true, ProtectionPointerButton.Right, new[] { false }),
            "ordinary right click remains vanilla even for a protected item");
        Equal(ProtectionClickRoute.Vanilla,
            ProtectionInteractionPolicy.Route(true, false, true, true, false, ProtectionPointerButton.Right, new[] { true }),
            "container-grid clicks remain vanilla");
    }

    private static void ProtectionConfiguredModifierRequiresEveryKey()
    {
        Equal(ProtectionClickRoute.ProtectOnly,
            ProtectionInteractionPolicy.Route(true, true, true, true, false, ProtectionPointerButton.Left, new[] { true, true }),
            "every configured modifier enables the protection route");
        Equal(ProtectionClickRoute.Vanilla,
            ProtectionInteractionPolicy.Route(true, true, true, true, false, ProtectionPointerButton.Left, new[] { true, false }),
            "one missing configured modifier leaves the click vanilla");
        Equal(ProtectionClickRoute.Vanilla,
            ProtectionInteractionPolicy.Route(true, true, true, true, false, ProtectionPointerButton.Left, Array.Empty<bool>()),
            "a shortcut without modifiers does not steal ordinary inventory clicks");
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

    private static void FullChestTransferClearsProtection()
    {
        True(ProtectionExitPolicy.ShouldClear(
            wasProtected: true,
            destinationWasExternal: true,
            sourceStillContainsProtectedItem: false),
            "a confirmed full transfer from player inventory clears protection");
    }

    private static void FullWorldDropClearsProtection()
    {
        True(ProtectionExitPolicy.ShouldClear(
            wasProtected: true,
            destinationWasExternal: true,
            sourceStillContainsProtectedItem: false),
            "a confirmed full world drop clears protection");
    }

    private static void PartialChestTransferKeepsProtection()
    {
        True(!ProtectionExitPolicy.ShouldClear(
            wasProtected: true,
            destinationWasExternal: true,
            sourceStillContainsProtectedItem: true),
            "a protected remainder after a partial chest transfer keeps protection");
    }

    private static void PartialWorldDropKeepsProtection()
    {
        True(!ProtectionExitPolicy.ShouldClear(
            wasProtected: true,
            destinationWasExternal: true,
            sourceStillContainsProtectedItem: true),
            "a protected remainder after a partial world drop keeps protection");
    }

    private static void InternalMoveKeepsProtection()
    {
        True(!ProtectionExitPolicy.ShouldClear(
            wasProtected: true,
            destinationWasExternal: false,
            sourceStillContainsProtectedItem: false),
            "an internal move or merge never uses external-exit cleanup");
    }

    private static void TransientDragKeepsProtection()
    {
        True(!ProtectionExitPolicy.ShouldClear(
            wasProtected: true,
            destinationWasExternal: true,
            sourceStillContainsProtectedItem: true),
            "starting or failing an external drag leaves the item and its protection intact");
        True(!ProtectionExitPolicy.ShouldClear(
            wasProtected: false,
            destinationWasExternal: true,
            sourceStillContainsProtectedItem: false),
            "unprotected items never mutate protection state");
    }

    private static void HotkeyPrunesLegacyOrphanedTarget()
    {
        var state = new ProtectionState(new[]
        {
            new ProtectionRecord(new Slot(2, 0), 7, "obsolete-food")
        });

        var resolution = state.Reconcile(new[]
        {
            new ProtectionCandidate(new Slot(2, 0), "wood"),
            new ProtectionCandidate(new Slot(0, 2), "stone")
        }, pruneUnresolved: true);

        Equal(0, resolution.Assignments.Count, "orphan has no runtime assignment");
        Equal(0, state.Records.Count, "hotkey maintenance removes the orphaned target record");
        True(resolution.Changed, "orphan pruning requests persistence");
    }

    private static void HotkeyPruningPreservesValidMovedTarget()
    {
        var state = new ProtectionState(new[]
        {
            new ProtectionRecord(new Slot(3, 2), 12, "food")
        });

        var resolution = state.Reconcile(new[]
        {
            new ProtectionCandidate(new Slot(3, 2), "stone"),
            new ProtectionCandidate(new Slot(1, 0), "food")
        }, pruneUnresolved: true);

        True(resolution.TryGet(new Slot(1, 0), out var record), "valid moved target is rebound before pruning");
        Equal(12, record.TargetQuantity, "valid moved target quantity survives pruning");
        Equal(1, state.Records.Count, "valid target record remains persisted");
    }

    private static void ResourceAvailabilityCountsPlayerNearbyAndQuality()
    {
        var stacks = new[]
        {
            Resource("player", "player-wood", "wood", 1, 2, 0, 0),
            Resource("near", "near-wood", "wood", 1, 10, 1, 0),
            Resource("far", "far-wood", "wood", 2, 5, 2, 0),
            Resource("far", "far-stone", "stone", 1, 99, 2, 1)
        };

        Equal(17, ResourceAvailability.CountAvailable(stacks, "wood"), "HUD total includes player and every eligible captured chest stack");
        Equal(12, ResourceAvailability.CountAvailable(stacks, "wood", 1), "quality filter includes only exact-quality stacks");
        Equal(5, ResourceAvailability.CountAvailable(stacks, "wood", 2), "second quality total");
        Equal(0, ResourceAvailability.CountAvailable(stacks, "resin"), "missing material total");
    }

    private static void ResourceDisplayAggregatesDuplicateRequirements()
    {
        var display = ResourceDisplayAvailability.Evaluate(
            new[]
            {
                new ResourceRequirement("Wood", 2),
                new ResourceRequirement("Wood", 3)
            },
            new[]
            {
                Resource("player", "p", "Wood", 1, 1, 0, 0),
                Resource("near", "n", "Wood", 1, 3, 1, 0)
            });

        Equal(2, display.Count, "duplicate crafting rows are preserved for rendering");
        Equal(2, display[0].Required, "first row keeps its own displayed cost");
        Equal(3, display[1].Required, "second row keeps its own displayed cost");
        True(display.All(entry => entry.TotalRequired == 5), "duplicate requirements share the complete combined cost");
        True(display.All(entry => entry.Available == 4), "each duplicate row shows the same aggregate stock");
        True(display.All(entry => !entry.IsSatisfied), "stock cannot be reused to satisfy duplicate costs");
    }

    private static void ResourceDisplayUsesUpgradeAndMultiCraftTotals()
    {
        var display = ResourceDisplayAvailability.Evaluate(
            new[] { new ResourceRequirement("Iron", 12) },
            new[]
            {
                Resource("player", "p", "Iron", 1, 2, 0, 0),
                Resource("near", "n", "Iron", 1, 13, 1, 0)
            });

        Equal(12, display.Single().Required, "quality and multi-craft multiplication is reflected in the displayed requirement");
        Equal(15, display.Single().Available, "craft display total includes player and eligible nearby stock");
        True(display.Single().IsSatisfied, "combined 12 / 15 stock is rendered as available");
    }

    private static void ResourceDisplayAlternativesRequireOneQualityTier()
    {
        var requirement = new[] { new ResourceRequirement("Fish", 3) };
        var splitQuality = ResourceDisplayAvailability.Evaluate(
            requirement,
            new[]
            {
                Resource("player", "q1", "Fish", 1, 2, 0, 0),
                Resource("near", "q2", "Fish", 2, 2, 1, 0)
            },
            alternatives: true,
            requireSingleQuality: true).Single();

        Equal(4, splitQuality.Available, "one-ingredient display still reports the complete visible stock");
        True(!splitQuality.IsSatisfied, "mixed quality tiers cannot satisfy one quality-specific ingredient choice");

        var matchingQuality = ResourceDisplayAvailability.Evaluate(
            requirement,
            new[]
            {
                Resource("player", "q1", "Fish", 1, 2, 0, 0),
                Resource("near", "q2a", "Fish", 2, 2, 1, 0),
                Resource("far", "q2b", "Fish", 2, 1, 2, 0)
            },
            alternatives: true,
            requireSingleQuality: true).Single();
        Equal(5, matchingQuality.Available, "all quality tiers remain visible in the total");
        True(matchingQuality.IsSatisfied, "one complete quality tier satisfies the alternative requirement");
    }

    private static void RequirementPresentationFormatsAggregateTotals()
    {
        Equal("2 / 17", ResourceRequirementPresentation.Format(2, 17),
            "building and crafting rows share required / total available text");
        Equal("50 / 25", ResourceRequirementPresentation.Format(50, 25),
            "a true shortage still displays the exact aggregate total");
    }

    private static void RequirementPresentationRetainsLongExactTotals()
    {
        Equal("45 / 172", ResourceRequirementPresentation.Format(45, 172),
            "five total digits remain present in the crafting requirement text");
        Equal("999 / 999", ResourceRequirementPresentation.Format(999, 999),
            "balanced three-digit values remain exact instead of being shortened");
        Equal("2 / 17", ResourceRequirementPresentation.Format(2, 17),
            "short values keep the normal readable required / available form after longer values");
    }

    private static void RequirementPresentationUsesRedOnlyForTrueShortages()
    {
        True(!ResourceRequirementPresentation.ShouldUseShortageColor(false, true, 1f),
            "aggregate-satisfied crafting rows never flash red");
        True(ResourceRequirementPresentation.ShouldUseShortageColor(false, false, 1f),
            "a true shortage uses red during the positive flash phase");
        True(!ResourceRequirementPresentation.ShouldUseShortageColor(false, false, -1f),
            "a true shortage keeps vanilla white during the opposite flash phase");
        True(!ResourceRequirementPresentation.ShouldUseShortageColor(true, false, 1f),
            "no-cost mode suppresses shortage flashing");
    }

    private static void RequirementUiPolicyCoversEveryConfigurationCombination()
    {
        foreach (var showStorageAmounts in new[] { false, true })
        {
            foreach (var allowCraftingFromStorage in new[] { false, true })
            {
                foreach (var allowBuildingFromStorage in new[] { false, true })
                {
                    var crafting = RequirementUiPolicy.Resolve(
                        showStorageAmounts,
                        allowCraftingFromStorage,
                        aggregateSatisfied: true,
                        playerSatisfied: false);
                    var building = RequirementUiPolicy.Resolve(
                        showStorageAmounts,
                        allowBuildingFromStorage,
                        aggregateSatisfied: true,
                        playerSatisfied: false);

                    Equal(showStorageAmounts, crafting.ShouldOverrideText,
                        "craft text override follows only the independent totals setting");
                    Equal(showStorageAmounts, building.ShouldOverrideText,
                        "build text override follows only the independent totals setting");
                    Equal(showStorageAmounts || allowCraftingFromStorage, crafting.ShouldApply,
                        "craft UI runs only for visible totals or storage-backed affordability");
                    Equal(showStorageAmounts || allowBuildingFromStorage, building.ShouldApply,
                        "build UI runs only for visible totals or storage-backed affordability");
                    Equal(allowCraftingFromStorage, crafting.IsSatisfied,
                        "craft flashing follows craft consumption permission, not displayed aggregate stock");
                    Equal(allowBuildingFromStorage, building.IsSatisfied,
                        "build flashing follows build consumption permission, not displayed aggregate stock");
                }
            }
        }
    }

    private static void RequirementUiPolicyLeavesVanillaUntouchedOnlyWhenBothFeaturesAreOff()
    {
        var vanilla = RequirementUiPolicy.Resolve(
            showStorageAmounts: false,
            allowStorageUse: false,
            aggregateSatisfied: true,
            playerSatisfied: false);
        True(!vanilla.ShouldApply, "disabled totals plus disabled storage use leaves vanilla requirement UI untouched");
        True(!vanilla.ShouldOverrideText, "disabled totals never replace vanilla count text");

        var displayOnly = RequirementUiPolicy.Resolve(
            showStorageAmounts: true,
            allowStorageUse: false,
            aggregateSatisfied: true,
            playerSatisfied: false);
        True(displayOnly.ShouldApply && displayOnly.ShouldOverrideText,
            "display-only mode shows aggregate totals");
        True(!displayOnly.IsSatisfied,
            "display-only mode still flashes when player-held stock is insufficient");
    }

    private static void RenamedStoragePermissionMigratesLegacyTrueFalseAndMissing()
    {
        var scenarios = new[]
        {
            new { Name = "legacy false", Contents = "[General]\nEnable building from nearby chests = false\n", Expected = false },
            new { Name = "legacy true", Contents = "[General]\nEnable building from nearby chests = true\n", Expected = true },
            new { Name = "missing legacy and current", Contents = string.Empty, Expected = true },
            new { Name = "current false without legacy", Contents = "[General]\nAllow building from storage = false\n", Expected = false }
        };

        foreach (var scenario in scenarios)
        {
            var path = NewTemporaryConfigPath();
            try
            {
                File.WriteAllText(path, scenario.Contents);
                var config = new ConfigFile(path, true);
                var migrated = ConfigMigration.BindRenamedDefaultEnabledBoolean(
                    config,
                    "General",
                    "Enable building from nearby chests",
                    "Allow building from storage",
                    "Current building permission.");

                Equal(scenario.Expected, migrated.Value, scenario.Name + " resolves to the exact intended value");
                var saved = File.ReadAllText(path);
                True(!saved.Contains("Enable building from nearby chests =", StringComparison.Ordinal),
                    scenario.Name + " removes the obsolete option from the saved config");
                True(saved.Contains("Allow building from storage = " + scenario.Expected, StringComparison.OrdinalIgnoreCase),
                    scenario.Name + " persists the replacement option");
                Equal(1, config.Keys.Count,
                    scenario.Name + " exposes only the replacement option after migration");
            }
            finally
            {
                DeleteTemporaryConfigPath(path);
            }
        }
    }

    private static void RenamedStoragePermissionMigrationIsIdempotent()
    {
        var path = NewTemporaryConfigPath();
        try
        {
            File.WriteAllText(path, "[General]\nEnable crafting from nearby chests = false\n");

            for (var launch = 1; launch <= 2; launch++)
            {
                var config = new ConfigFile(path, true);
                var migrated = ConfigMigration.BindRenamedDefaultEnabledBoolean(
                    config,
                    "General",
                    "Enable crafting from nearby chests",
                    "Allow crafting from storage",
                    "Current crafting permission.");

                True(!migrated.Value, "legacy false remains false on launch " + launch);
                var saved = File.ReadAllText(path);
                True(!saved.Contains("Enable crafting from nearby chests =", StringComparison.Ordinal),
                    "legacy crafting key stays retired on launch " + launch);
                Equal(1, CountOccurrences(saved, "Allow crafting from storage ="),
                    "replacement crafting key is written exactly once on launch " + launch);
            }
        }
        finally
        {
            DeleteTemporaryConfigPath(path);
        }
    }

    private static string NewTemporaryConfigPath()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Stackmaster.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "com.jstack424.stackmaster.cfg");
    }

    private static void DeleteTemporaryConfigPath(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
    }

    private static void ResourcePlanAggregatesPlayerAndNearbyStacks()
    {
        var plan = ResourcePlan(
            new[] { new ResourceRequirement("Stone", 50) },
            Resource("player", "p", "Stone", 1, 20, 0, 0),
            Resource("near", "n", "Stone", 1, 30, 1, 0));
        True(plan.IsSatisfiable, "player and nearby stock satisfies the cost together");
        Equal(50, plan.PlannedUnits, "exact aggregate quantity planned");
        SequenceEqual(new[] { 20, 30 }, plan.Steps.Select(step => step.Quantity), "player is consumed before nearby storage");
    }

    private static void ResourcePlanRejectsFiftyWhenOnlyTwentyFiveExist()
    {
        var plan = ResourcePlan(
            new[] { new ResourceRequirement("Stone", 50) },
            Resource("player", "p", "Stone", 1, 10, 0, 0),
            Resource("near", "n", "Stone", 1, 15, 1, 0));
        True(!plan.IsSatisfiable, "50 required with 25 available is rejected");
        Equal(0, plan.Steps.Count, "shortage exposes no partial withdrawal steps");
        Equal(25, plan.Shortages.Single().Available, "shortage reports exact available stock");
        Equal(25, plan.Shortages.Single().Missing, "shortage reports exact missing stock");
    }

    private static void ResourcePlanConsumesExactlyFiftyAcrossPartialStacks()
    {
        var plan = ResourcePlan(
            new[] { new ResourceRequirement("Stone", 50) },
            Resource("player", "p1", "Stone", 1, 7, 0, 0),
            Resource("player", "p2", "Stone", 1, 11, 0, 1),
            Resource("near", "n1", "Stone", 1, 13, 1, 0),
            Resource("far", "f1", "Stone", 1, 40, 2, 0));
        True(plan.IsSatisfiable, "partial stacks satisfy exact cost");
        Equal(50, plan.Steps.Sum(step => step.Quantity), "exactly the 50-unit cost is planned");
        Equal(19, plan.Steps.Last().Quantity, "last stack is only partially consumed");
    }

    private static void ResourcePlanNormalizesDuplicateRequirements()
    {
        var plan = ResourcePlan(
            new[] { new ResourceRequirement("Stone", 30), new ResourceRequirement("Stone", 20) },
            Resource("near", "n", "Stone", 1, 49, 1, 0));
        True(!plan.IsSatisfiable, "duplicate requirements cannot each reuse the same stock");
        Equal(1, plan.NormalizedRequirements.Count, "duplicate requirements are combined");
        Equal(50, plan.NormalizedRequirements.Single().Quantity, "combined requirement quantity");
        Equal(0, plan.Steps.Count, "combined shortage performs no partial withdrawal");
    }

    private static void ResourcePlanDoesNotDoubleConsume()
    {
        var plan = ResourcePlan(
            new[] { new ResourceRequirement("Wood", 25), new ResourceRequirement("Wood", 25) },
            Resource("player", "p", "Wood", 1, 25, 0, 0),
            Resource("near", "n", "Wood", 1, 100, 1, 0));
        True(plan.IsSatisfiable, "combined duplicate cost is satisfiable");
        Equal(50, plan.RequiredUnits, "required units are the exact combined cost");
        Equal(50, plan.PlannedUnits, "plan never consumes the cost twice");
        SequenceEqual(new[] { 25, 25 }, plan.Steps.Select(step => step.Quantity), "only the needed chest remainder is used");
    }

    private static void CraftingExactDebitConsumesPlayerAndSelectedChest()
    {
        var plan = ResourcePlan(
            new[] { new ResourceRequirement("Wood", 5) },
            Resource("player", "player-wood", "Wood", 1, 2, 0, 0),
            Resource("selected-chest", "selected-wood", "Wood", 1, 3, 1, 0),
            Resource("unselected-chest", "unselected-wood", "Wood", 1, 50, 2, 0));
        var stock = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["player-wood"] = 2,
            ["selected-wood"] = 3,
            ["unselected-wood"] = 50
        };
        var debit = new ExactResourceDebit<FakeDebitReceipt>(plan);

        True(debit.TryApply(step => Debit(stock, step), out var failure), failure ?? "exact debit failed");
        Equal(5, debit.RemovedUnits, "remote craft removes the complete exact cost");
        Equal(0, stock["player-wood"], "remote craft removes its player-carried share");
        Equal(0, stock["selected-wood"], "remote craft removes its selected-chest share");
        Equal(50, stock["unselected-wood"], "remote craft never touches an unselected chest");
        debit.Commit();
    }

    private static void CraftingExactDebitCannotDoubleCharge()
    {
        var plan = ResourcePlan(
            new[] { new ResourceRequirement("Resin", 4) },
            Resource("player", "player-resin", "Resin", 1, 1, 0, 0),
            Resource("selected-chest", "chest-resin", "Resin", 1, 3, 1, 0));
        var stock = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["player-resin"] = 1,
            ["chest-resin"] = 3
        };
        var debit = new ExactResourceDebit<FakeDebitReceipt>(plan);
        True(debit.TryApply(step => Debit(stock, step), out var failure), failure ?? "exact debit failed");
        Throws<InvalidOperationException>(() => debit.TryApply(step => Debit(stock, step), out _),
            "the same prepared craft cannot debit twice");
        Equal(0, stock["player-resin"], "duplicate suppression leaves the one player charge intact");
        Equal(0, stock["chest-resin"], "duplicate suppression leaves the one chest charge intact");
        debit.Commit();
    }

    private static void CraftingIncompleteDebitCannotCommitFreeOutput()
    {
        var plan = ResourcePlan(
            new[] { new ResourceRequirement("Stone", 5) },
            Resource("player", "player-stone", "Stone", 1, 2, 0, 0),
            Resource("selected-chest", "chest-stone", "Stone", 1, 3, 1, 0));
        var stock = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["player-stone"] = 2,
            ["chest-stone"] = 3
        };
        var debit = new ExactResourceDebit<FakeDebitReceipt>(plan);
        var applied = debit.TryApply(step =>
        {
            if (step.StackId != "chest-stone") return Debit(stock, step);
            stock[step.StackId] -= 2;
            return ResourceDebitAttempt<FakeDebitReceipt>.FailureAfterMutation(
                2,
                new FakeDebitReceipt(step.StackId, 2),
                "selected chest could not provide the exact planned quantity");
        }, out var failure);

        True(!applied, "a partial selected-chest debit fails closed");
        True(failure != null && failure.Contains("exact planned quantity", StringComparison.Ordinal),
            "the partial debit reports the exact failure");
        Throws<InvalidOperationException>(debit.Commit,
            "an incomplete charge cannot be committed after output");
        True(debit.Rollback(receipt => Restore(stock, receipt)), "the incomplete charge rolls back");
        Equal(2, stock["player-stone"], "failed craft restores the player share");
        Equal(3, stock["chest-stone"], "failed craft restores the chest share");
    }

    private static void CraftingCancellationRollsBackExactDebit()
    {
        var plan = ResourcePlan(
            new[] { new ResourceRequirement("FineWood", 6) },
            Resource("player", "player-finewood", "FineWood", 1, 1, 0, 0),
            Resource("selected-chest", "chest-finewood", "FineWood", 1, 5, 1, 0));
        var stock = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["player-finewood"] = 1,
            ["chest-finewood"] = 5
        };
        var debit = new ExactResourceDebit<FakeDebitReceipt>(plan);
        True(debit.TryApply(step => Debit(stock, step), out var failure), failure ?? "exact debit failed");
        True(debit.Rollback(receipt => Restore(stock, receipt)), "cancel restores every journaled mutation");
        Equal(1, stock["player-finewood"], "cancel restores the player inventory exactly");
        Equal(5, stock["chest-finewood"], "cancel restores the selected chest exactly");
        True(!debit.Rollback(receipt => Restore(stock, receipt)), "rollback cannot apply twice");
        Equal(6, stock.Values.Sum(), "double rollback cannot duplicate resources");
    }

    private static ResourceDebitAttempt<FakeDebitReceipt> Debit(
        IDictionary<string, int> stock,
        ResourceWithdrawalStep step)
    {
        var available = stock[step.StackId];
        var removed = Math.Min(available, step.Quantity);
        stock[step.StackId] = available - removed;
        var receipt = new FakeDebitReceipt(step.StackId, removed);
        return removed == step.Quantity
            ? ResourceDebitAttempt<FakeDebitReceipt>.Success(removed, receipt)
            : ResourceDebitAttempt<FakeDebitReceipt>.FailureAfterMutation(
                removed,
                receipt,
                "fake inventory could not satisfy the exact debit");
    }

    private static bool Restore(IDictionary<string, int> stock, FakeDebitReceipt receipt)
    {
        stock[receipt.StackId] += receipt.Quantity;
        return true;
    }

    private static void ResourcePlanHonorsExactQualityAndMultiplierTotals()
    {
        var plan = ResourcePlan(
            new[] { new ResourceRequirement("Resin", 12, 2) },
            Resource("player", "q1", "Resin", 1, 50, 0, 0),
            Resource("near", "q2a", "Resin", 2, 5, 1, 0),
            Resource("far", "q2b", "Resin", 2, 7, 2, 0));
        True(plan.IsSatisfiable, "quality-two stock satisfies multiplied total");
        Equal(12, plan.PlannedUnits, "quality/multiplier result is consumed exactly");
        True(plan.Steps.All(step => step.Quality == 2), "wrong-quality stock is untouched");
    }

    private static void ResourcePlanIsAllOrNothingAcrossDifferentMaterials()
    {
        var plan = ResourcePlan(
            new[] { new ResourceRequirement("Wood", 10), new ResourceRequirement("Stone", 10) },
            Resource("player", "wood", "Wood", 1, 10, 0, 0),
            Resource("near", "stone", "Stone", 1, 9, 1, 0));
        True(!plan.IsSatisfiable, "one missing material rejects the complete action");
        Equal(0, plan.Steps.Count, "available materials are not partially charged");
        Equal("Stone", plan.Shortages.Single().ItemName, "exact missing material is reported");
    }

    private static void ExpeditionClickRequiresEveryConfiguredModifier()
    {
        True(ExpeditionClickPolicy.ShouldIntercept(true, true, true, new[] { true }),
            "one held configured modifier intercepts the piece click");
        True(!ExpeditionClickPolicy.ShouldIntercept(true, true, true, new[] { true, false }),
            "every configured modifier must be held");
        True(!ExpeditionClickPolicy.ShouldIntercept(true, true, true, Array.Empty<bool>()),
            "a shortcut without a modifier never changes ordinary build-menu clicks");
    }

    private static void ExpeditionNormalClickIsUnchanged()
    {
        True(!ExpeditionClickPolicy.ShouldIntercept(true, true, true, new[] { false }),
            "an unmodified click remains vanilla");
        True(!ExpeditionClickPolicy.ShouldIntercept(false, true, true, new[] { true }),
            "an incompatible runtime remains vanilla");
        True(!ExpeditionClickPolicy.ShouldIntercept(true, false, true, new[] { true }),
            "a missing local player remains vanilla");
        True(!ExpeditionClickPolicy.ShouldIntercept(true, true, false, new[] { true }),
            "a missing clicked piece remains vanilla");
    }

    private static void ExpeditionKitIgnoresCarriedMaterials()
    {
        var plan = ExpeditionPlan(
            new[] { new ResourceRequirement("Wood", 10) },
            Resource("player", "carried", "Wood", 1, 99, 0, 0),
            Resource("chest", "stored", "Wood", 1, 10, 1, 0));
        True(plan.IsSatisfiable, "storage alone contains a full kit");
        Equal(10, plan.PlannedUnits, "the full recipe is withdrawn despite carried stock");
        True(plan.Steps.All(step => step.InventoryId != "player"), "player stock is never a kit source");
    }

    private static void ExpeditionRepeatedClicksPlanRepeatedFullKits()
    {
        var requirements = new[] { new ResourceRequirement("Wood", 10), new ResourceRequirement("Stone", 5) };
        var stacks = new[]
        {
            Resource("chest", "wood", "Wood", 1, 30, 1, 0),
            Resource("chest", "stone", "Stone", 1, 15, 1, 1)
        };
        var first = ExpeditionPlan(requirements, stacks);
        var second = ExpeditionPlan(requirements, stacks);
        Equal(15, first.PlannedUnits, "first click plans one complete kit");
        Equal(15, second.PlannedUnits, "next click independently plans one complete additional kit");
        Equal(30, first.PlannedUnits + second.PlannedUnits, "two clicks add exactly two kits");
    }

    private static void ExpeditionKitUsesLargestStockFirst()
    {
        var plan = ExpeditionPlan(
            new[] { new ResourceRequirement("Wood", 12) },
            Resource("split-largest", "l1", "Wood", 1, 6, 9, 0),
            Resource("single", "s", "Wood", 1, 10, 1, 0),
            Resource("split-largest", "l2", "Wood", 1, 5, 9, 1),
            Resource("smaller", "m", "Wood", 1, 9, 2, 0));
        SequenceEqual(new[] { "split-largest", "split-largest", "single" }, plan.Steps.Select(step => step.InventoryId),
            "largest aggregate chest stock is consumed before smaller sources regardless of distance order");
        SequenceEqual(new[] { 6, 5, 1 }, plan.Steps.Select(step => step.Quantity),
            "all stacks in the largest-stock chest precede the exact remainder from the next source");
    }

    private static void ExpeditionKitBreaksStockTiesDeterministically()
    {
        var forward = ExpeditionPlan(
            new[] { new ResourceRequirement("Stone", 7) },
            Resource("z-chest", "z", "Stone", 1, 7, 1, 0),
            Resource("a-chest", "a", "Stone", 1, 7, 2, 0));
        var reverse = ExpeditionPlan(
            new[] { new ResourceRequirement("Stone", 7) },
            Resource("a-chest", "a", "Stone", 1, 7, 2, 0),
            Resource("z-chest", "z", "Stone", 1, 7, 1, 0));
        Equal("a-chest", forward.Steps.Single().InventoryId, "stable id breaks equal-stock ties");
        Equal("a-chest", reverse.Steps.Single().InventoryId, "enumeration order cannot change the tie result");
    }

    private static void ExpeditionKitSplitsAcrossSources()
    {
        var plan = ExpeditionPlan(
            new[] { new ResourceRequirement("FineWood", 25) },
            Resource("large", "a", "FineWood", 1, 12, 1, 0),
            Resource("medium", "b", "FineWood", 1, 8, 2, 0),
            Resource("small", "c", "FineWood", 1, 5, 3, 0));
        True(plan.IsSatisfiable, "split chest stock satisfies the complete kit");
        SequenceEqual(new[] { "large", "medium", "small" }, plan.Steps.Select(step => step.InventoryId),
            "split sources retain largest-stock-first ordering");
        Equal(25, plan.PlannedUnits, "all split source quantities are exact");
    }

    private static void ExpeditionShortageHasNoMutationSteps()
    {
        var plan = ExpeditionPlan(
            new[] { new ResourceRequirement("Wood", 10), new ResourceRequirement("Stone", 10) },
            Resource("chest", "wood", "Wood", 1, 10, 1, 0),
            Resource("chest", "stone", "Stone", 1, 9, 1, 1));
        True(!plan.IsSatisfiable, "one short ingredient rejects the kit");
        Equal(0, plan.Steps.Count, "atomic shortage exposes no executable chest steps");
    }

    private static void ExpeditionCapacityFillsStacksThenEmptySlots()
    {
        var player = Player(3, Item("existing", "wood", "Wood", 7, 10, 0));
        var plan = new ExpeditionCapacityPlanner().Plan(
            player,
            new[] { new ExpeditionCargoStack("source", "wood", 15, 10, 15) },
            10,
            100);
        True(plan.IsFeasible, "partial stacks plus empty slots fit the kit");
        SequenceEqual(new[] { 0, 1, 2 }, plan.Steps.Select(step => step.DestinationSlot),
            "compatible partial stack is filled before deterministic empty slots");
        SequenceEqual(new[] { 3, 10, 2 }, plan.Steps.Select(step => step.Quantity),
            "the cargo is split exactly by destination capacity");
    }

    private static void ExpeditionCapacityRejectsSharedSlotOverbooking()
    {
        var player = Player(1);
        var plan = new ExpeditionCapacityPlanner().Plan(
            player,
            new[]
            {
                new ExpeditionCargoStack("wood", "wood", 10, 10, 10),
                new ExpeditionCargoStack("stone", "stone", 10, 10, 10)
            },
            0,
            100);
        True(!plan.FitsSlots, "different materials cannot both claim the same empty slot");
        Equal(0, plan.Steps.Count, "slot failure exposes no executable destination steps");
    }

    private static void ExpeditionCapacityRejectsOverweightKit()
    {
        var plan = new ExpeditionCapacityPlanner().Plan(
            Player(2),
            new[] { new ExpeditionCargoStack("wood", "wood", 10, 50, 10.01) },
            90,
            100);
        True(!plan.FitsWeight, "the entire added kit must fit the current carry limit");
        Equal(0, plan.Steps.Count, "weight failure exposes no executable destination steps");
    }

    private static void ExpeditionCapacityAcceptsExactWeightLimit()
    {
        var plan = new ExpeditionCapacityPlanner().Plan(
            Player(2),
            new[] { new ExpeditionCargoStack("wood", "wood", 10, 50, 10) },
            90,
            100);
        True(plan.IsFeasible, "an exact carry-weight boundary is accepted");
        Equal(10, plan.Steps.Sum(step => step.Quantity), "the full exact-boundary kit is planned");
    }

    private static void ExpeditionFallbackRollbackPreservesEquippedItemIdentityAndState()
    {
        var equipped = new FakeRollbackItem(
            "hammer", 1, 73.5, 4, 17, new Slot(2, 3),
            new Dictionary<string, string> { ["upgrade"] = "ancient", ["owner"] = "joe" });
        var carried = new FakeRollbackItem(
            "wood", 37, 12.25, 2, 6, new Slot(4, 1),
            new Dictionary<string, string> { ["source"] = "forest" });
        var equipment = new FakeEquipmentReferences(equipped, carried);
        var live = new List<FakeRollbackItem> { equipped, carried };
        var backup = IdentityPreservingInventoryBackup<FakeRollbackItem, FakeRollbackItem>.Capture(
            live,
            item => item.Clone());

        equipped.Name = "corrupted";
        equipped.Stack = 99;
        equipped.Durability = 0;
        equipped.Quality = 1;
        equipped.Variant = 0;
        equipped.Position = new Slot(0, 0);
        equipped.CustomData.Clear();
        carried.Name = "removed";
        carried.Stack = 0;
        live = new List<FakeRollbackItem> { equipped.Clone() };

        // This is the emergency snapshot path used only after reverse rollback fails.
        var restore = backup.PrepareRestore(item => item.Clone());
        restore.RestoreStates((original, snapshot) => original.RestoreFrom(snapshot));
        live = restore.RebuildInventoryList();

        True(restore.HasExactOriginalReferences(live), "fallback rebuild contains every original ItemData reference in order");
        True(ReferenceEquals(equipped, live[0]), "equipped inventory entry is the same original object");
        foreach (var retained in equipment.All)
        {
            True(live.Any(item => ReferenceEquals(retained, item)),
                "every pinned Humanoid equipment field remains attached to an original restored object");
        }
        Equal("hammer", equipped.Name, "item name is restored");
        Equal(1, equipped.Stack, "item stack is restored");
        Equal(73.5, equipped.Durability, "item durability is restored");
        Equal(4, equipped.Quality, "item quality is restored");
        Equal(17, equipped.Variant, "item variant is restored");
        Equal(new Slot(2, 3), equipped.Position, "item grid position is restored");
        Equal("ancient", equipped.CustomData["upgrade"], "item custom data is restored");
        Equal("joe", equipped.CustomData["owner"], "all captured custom data survives fallback");
        Equal("wood", carried.Name, "second item state is restored without accepting a partial result");
        Equal(37, carried.Stack, "second item stack is restored exactly");
        True(restore.StatesMatch((original, snapshot) => original.Matches(snapshot)),
            "complete restored state is accepted");
        carried.Quality++;
        True(!restore.StatesMatch((original, snapshot) => original.Matches(snapshot)),
            "a partial state restore is detected rather than accepted");
    }

    private static void ReservationCleanupMustPrecedeOwnershipRelease()
    {
        True(!ReservationOwnershipCleanupPolicy.CanRelinquishOwnership(true),
            "pending exact local reservation blocks ownership relinquish, including shutdown");
        True(ReservationOwnershipCleanupPolicy.CanRelinquishOwnership(false),
            "ownership cleanup resumes only after the exact reservation is no longer pending");
    }

    private static void ResourcePlanUsesPlayerThenDeterministicContainerOrder()
    {
        var plan = ResourcePlan(
            new[] { new ResourceRequirement("Wood", 35) },
            Resource("far", "f", "Wood", 1, 20, 3, 0),
            Resource("near-b", "b", "Wood", 1, 10, 2, 0),
            Resource("player", "p", "Wood", 1, 5, 0, 0),
            Resource("near-a", "a", "Wood", 1, 20, 1, 0));
        SequenceEqual(new[] { "player", "near-a", "near-b" }, plan.Steps.Select(step => step.InventoryId),
            "withdrawals use player, then nearest deterministic container order");
        Equal(35, plan.PlannedUnits, "ordered plan remains exact");
    }

    private static void ResourcePlanMinimizesDistinctContainers()
    {
        var planner = new ResourceWithdrawalPlanner();
        ResourceWithdrawalPlan plan;
        True(planner.TryPlanWithMinimumContainers(
                new[] { new ResourceRequirement("Wood", 10) },
                new[]
                {
                    Resource("player", "p", "Wood", 1, 5, 0, 0),
                    Resource("near", "n", "Wood", 1, 1, 1, 0),
                    Resource("far", "f", "Wood", 1, 5, 2, 0)
                },
                "player",
                out plan),
            "bounded exact minimum search completes");
        SequenceEqual(new[] { "far" }, ResourceOwnershipSelection.RequiredContainerIds(plan),
            "one sufficient farther chest beats two greedy chest claims");
        Equal(10, plan.PlannedUnits, "minimum-container plan remains exact");
    }

    private static void ResourcePlanMinimizesAcrossDifferentMaterials()
    {
        var planner = new ResourceWithdrawalPlanner();
        ResourceWithdrawalPlan plan;
        True(planner.TryPlanWithMinimumContainers(
                new[] { new ResourceRequirement("Wood", 5), new ResourceRequirement("Stone", 5) },
                new[]
                {
                    Resource("wood-only", "w", "Wood", 1, 5, 1, 0),
                    Resource("stone-only", "s", "Stone", 1, 5, 2, 0),
                    Resource("combined", "cw", "Wood", 1, 5, 3, 0),
                    Resource("combined", "cs", "Stone", 1, 5, 3, 1)
                },
                "player",
                out plan),
            "cross-material minimum search completes");
        SequenceEqual(new[] { "combined" }, ResourceOwnershipSelection.RequiredContainerIds(plan),
            "one chest covering all requirements beats separate per-material chests");
    }

    private static void ResourceOwnershipSelectionUsesPlayerFirstRemainder()
    {
        var plan = ResourcePlan(
            new[] { new ResourceRequirement("Wood", 30) },
            Resource("player", "p", "Wood", 1, 25, 0, 0),
            Resource("near", "n", "Wood", 1, 50, 1, 0));
        SequenceEqual(new[] { "near" }, ResourceOwnershipSelection.RequiredContainerIds(plan),
            "ownership excludes player stock and selects only the chest covering the remainder");
        Equal(5, plan.Steps.Last().Quantity, "only the exact post-player remainder is planned from storage");
    }

    private static void ResourceOwnershipSelectionUsesOnlyNeededChests()
    {
        var plan = ResourcePlan(
            new[] { new ResourceRequirement("Stone", 20) },
            Resource("player", "p", "Stone", 1, 5, 0, 0),
            Resource("near", "n", "Stone", 1, 15, 1, 0),
            Resource("unrelated", "u", "Stone", 1, 50, 2, 0));
        SequenceEqual(new[] { "near" }, ResourceOwnershipSelection.RequiredContainerIds(plan),
            "a satisfiable earlier chest prevents any unrelated ownership request");
    }

    private static void ResourceOwnershipSelectionDeduplicatesChestStacks()
    {
        var plan = ResourcePlan(
            new[] { new ResourceRequirement("Wood", 30) },
            Resource("chest", "a", "Wood", 1, 10, 1, 0),
            Resource("chest", "b", "Wood", 1, 20, 1, 1));
        SequenceEqual(new[] { "chest" }, ResourceOwnershipSelection.RequiredContainerIds(plan),
            "multiple exact stack withdrawals require only one chest ownership handshake");
    }

    private static void ResourceOwnershipSelectionHasBuildCraftParity()
    {
        var requirements = new[] { new ResourceRequirement("Iron", 12) };
        var stacks = new[]
        {
            Resource("player", "p", "Iron", 1, 2, 0, 0),
            Resource("forge-chest", "c", "Iron", 1, 10, 1, 0)
        };
        var buildPlan = ResourcePlan(requirements, stacks);
        var craftPlan = ResourcePlan(requirements, stacks);
        SequenceEqual(
            ResourceOwnershipSelection.RequiredContainerIds(buildPlan),
            ResourceOwnershipSelection.RequiredContainerIds(craftPlan),
            "building and crafting derive the same exact ownership set from the shared planner");
    }

    private static void ResourceOwnershipRevisionRejectsStaleRequiredChest()
    {
        var required = new[] { "needed" };
        var expected = new Dictionary<string, uint> { ["needed"] = 7 };
        var current = new Dictionary<string, uint> { ["needed"] = 8 };
        True(!ResourceOwnershipSelection.RequiredRevisionsMatch(required, expected, current),
            "a changed required chest revision cancels before mutation");
    }

    private static void ResourceOwnershipRevisionIgnoresUnrelatedChest()
    {
        var required = new[] { "needed" };
        var expected = new Dictionary<string, uint> { ["needed"] = 7, ["unrelated"] = 1 };
        var current = new Dictionary<string, uint> { ["needed"] = 7, ["unrelated"] = 99 };
        True(ResourceOwnershipSelection.RequiredRevisionsMatch(required, expected, current),
            "an unrelated chest revision does not broaden or invalidate the selected ownership set");
    }

    private static void ResourceReadOnlyRemoteUnopenedUsesDetachedInventory()
    {
        var plan = ResourceSnapshotPolicy.Evaluate(
            resourceReadOnly: true,
            liveInventoryKnown: false,
            accessGranted: true,
            inUse: false,
            detachedInventoryDecoded: true);
        True(!plan.CaptureLiveInventory,
            "an unopened or remote-owned read-only chest never snapshots a null live inventory");
        True(plan.UseDetachedInventory,
            "an accessible remote-owned chest uses only its successfully decoded detached inventory");
    }

    private static void ResourceReadOnlyDecodeFailureFailsClosed()
    {
        var plan = ResourceSnapshotPolicy.Evaluate(
            resourceReadOnly: true,
            liveInventoryKnown: false,
            accessGranted: true,
            inUse: false,
            detachedInventoryDecoded: false);
        True(!plan.CaptureLiveInventory,
            "a failed read-only decode never falls back to the unknown live inventory");
        True(!plan.UseDetachedInventory,
            "a failed read-only decode omits only that container from nearby-resource totals");
    }

    private static void ResourceLiveSnapshotRequiresKnownInventory()
    {
        var unknown = ResourceSnapshotPolicy.Evaluate(false, false, true, false, false);
        var known = ResourceSnapshotPolicy.Evaluate(false, true, true, false, false);
        var busy = ResourceSnapshotPolicy.Evaluate(false, true, true, true, false);
        True(!unknown.CaptureLiveInventory, "unknown live inventory is never exposed as accessible");
        True(known.CaptureLiveInventory, "known accessible idle live inventory remains eligible");
        True(!busy.CaptureLiveInventory, "known live inventory remains ineligible while in use");
        True(!unknown.UseDetachedInventory && !known.UseDetachedInventory && !busy.UseDetachedInventory,
            "ordinary storage discovery never substitutes a detached resource snapshot");
    }

    private static void DetachedHydrationMakesLoadedItemsCountable()
    {
        var wood = new FakeDetachedItem(17, new FakeSharedMetadata("Wood"));
        var stone = new FakeDetachedItem(4, new FakeSharedMetadata("Stone"));
        var loaded = new[] { wood, stone };

        var hydrated = DetachedItemHydrator.TryHydrate(
            loaded,
            item => item.ResolvedPrefabMetadata,
            (item, shared) => item.Shared = shared);
        var countableWood = loaded
            .Where(item => item.Shared != null && item.Shared.Name == "Wood")
            .Sum(item => item.Quantity);

        True(hydrated, "complete detached prefab metadata hydrates successfully");
        Equal(17, countableWood, "hydrated detached items contribute to nearby-resource totals");
    }

    private static void DetachedHydrationRejectsIncompleteMetadata()
    {
        var complete = new FakeDetachedItem(17, new FakeSharedMetadata("Wood"));
        var incomplete = new FakeDetachedItem(4, null);
        var loaded = new[] { complete, incomplete };

        var hydrated = DetachedItemHydrator.TryHydrate(
            loaded,
            item => item.ResolvedPrefabMetadata,
            (item, shared) => item.Shared = shared);

        True(!hydrated, "one unresolved detached row rejects the entire chest snapshot");
        True(complete.Shared == null && incomplete.Shared == null,
            "metadata is resolved for every row before any detached item is mutated");
    }

    private static void OwnershipReleaseRequiresExactIdentity()
    {
        var decision = OwnershipLeasePolicy.Decide(false, true, true, true);
        True(!decision.ShouldRelease, "a recycled or mismatched ZDO identity is never released");
    }

    private static void OwnershipReleaseRequiresOriginalLocalSession()
    {
        var decision = OwnershipLeasePolicy.Decide(true, false, true, true);
        True(!decision.ShouldRelease, "a later local session never inherits an older Stackmaster lease");
    }

    private static void OwnershipReleaseRequiresCurrentLocalOwner()
    {
        var decision = OwnershipLeasePolicy.Decide(true, true, false, true);
        True(!decision.ShouldRelease, "ownership already transferred by vanilla is never overwritten");
    }

    private static void OwnershipReleaseRequiresExactOwnerRevision()
    {
        var decision = OwnershipLeasePolicy.Decide(true, true, true, false);
        True(!decision.ShouldRelease, "a newer ownership revision is never overwritten");
    }

    private static void OwnershipRevisionSuccessorWrapsExactly()
    {
        Equal((ushort)0, OwnershipLeasePolicy.NextOwnerRevision(ushort.MaxValue),
            "owner revision successor preserves Valheim ushort rollover");
    }

    private static void OwnershipReleaseAlwaysReturnsToVanillaUnownedState()
    {
        var decision = OwnershipLeasePolicy.Decide(true, true, true, true);
        True(decision.ShouldRelease, "an exact Stackmaster acquisition is released");
        Equal(0L, decision.TargetOwner, "release never restores a stale peer or assigns a topology-dependent server");
    }

    private static void OwnershipBuildLeaseRequiresDemonstrableAcquisition()
    {
        True(!OwnershipLeaseRetentionPolicy.ShouldRenewForSuccessfulBuild(false, true, true),
            "a locally/manual-owned or merely inspected chest never becomes a Stackmaster build lease");
    }

    private static void OwnershipBuildLeaseRequiresSuccessfulUse()
    {
        True(!OwnershipLeaseRetentionPolicy.ShouldRenewForSuccessfulBuild(true, false, true),
            "an acquired but unrelated chest is not retained");
        True(!OwnershipLeaseRetentionPolicy.ShouldRenewForSuccessfulBuild(true, true, false),
            "cancellation, failure, and rollback do not retain ownership");
        True(OwnershipLeaseRetentionPolicy.ShouldRenewForSuccessfulBuild(true, true, true),
            "an exact acquired chest used by a successful placement can be retained");
    }

    private static void OwnershipBuildLeaseRenewsFromCurrentTime()
    {
        Equal(142f, OwnershipLeaseRetentionPolicy.RenewedExpiry(112f, 30f),
            "every successful placement renews the full sliding lease from now");
    }

    private static void OwnershipBuildLeaseExpiryUsesBoundary()
    {
        True(!OwnershipLeaseRetentionPolicy.IsExpired(141.999f, 142f),
            "lease remains active immediately before expiry");
        True(OwnershipLeaseRetentionPolicy.IsExpired(142f, 142f),
            "lease expires exactly at its deadline");
        True(OwnershipLeaseRetentionPolicy.IsExpired(143f, 142f),
            "lease remains expired after its deadline");
    }

    private static void OwnershipBuildLeaseYieldsToRemoteManualOpen()
    {
        True(OwnershipLeaseRetentionPolicy.ShouldYieldToManualOpen(
                OwnershipLeasePurpose.Building, 202L, 101L, false),
            "an idle build lease yields when vanilla accepts a remote manual open");
    }

    private static void OwnershipBuildLeaseDoesNotYieldToLocalManualOpen()
    {
        True(!OwnershipLeaseRetentionPolicy.ShouldYieldToManualOpen(
                OwnershipLeasePurpose.Building, 101L, 101L, false),
            "the local owner can open a leased chest without invalidating its own lease");
    }

    private static void OwnershipBuildLeaseDoesNotYieldWhileLogicallyReserved()
    {
        True(!OwnershipLeaseRetentionPolicy.ShouldYieldToManualOpen(
                OwnershipLeasePurpose.Building, 202L, 101L, true),
            "an active atomic mutation remains protected rather than being preempted mid-transaction");
    }

    private static void OwnershipRetryLeaseDoesNotUseBuildPreemption()
    {
        True(!OwnershipLeaseRetentionPolicy.ShouldYieldToManualOpen(
                OwnershipLeasePurpose.Retry, 202L, 101L, false),
            "the build-only preemption policy does not broaden retry or crafting behavior");
    }

    private static void CraftingIntentDefersThenStartsExactlyOneBar()
    {
        var lifecycle = new CraftingActionLifecycle();
        var generation = lifecycle.Begin(needsOwnership: true);
        Equal(CraftingActionPhase.Acquiring, lifecycle.Phase, "remote ownership defers the vanilla bar");
        True(lifecycle.MarkReserved(generation), "the exact plan becomes reserved");
        True(lifecycle.MarkCrafting(generation), "the original intent starts one vanilla bar");
        True(!lifecycle.MarkCrafting(generation), "the same intent cannot start a second bar");
    }

    private static void CraftingReservationPrecedesCrafting()
    {
        var lifecycle = new CraftingActionLifecycle();
        var generation = lifecycle.Begin(needsOwnership: true);
        True(!lifecycle.MarkCrafting(generation), "crafting cannot start before reservation");
        True(lifecycle.MarkReserved(generation), "reservation follows acquisition");
        True(lifecycle.MarkCrafting(generation), "crafting starts only after reservation");
    }

    private static void CraftingCancelInvalidatesDelayedOwnership()
    {
        var lifecycle = new CraftingActionLifecycle();
        var staleGeneration = lifecycle.Begin(needsOwnership: true);
        lifecycle.Cancel();
        Equal(CraftingActionPhase.Idle, lifecycle.Phase, "cancel immediately returns to idle");
        True(!lifecycle.MarkReserved(staleGeneration), "a delayed grant cannot revive a canceled craft");
    }

    private static void CraftingTransferCanHappenOnlyOnce()
    {
        var lifecycle = new CraftingActionLifecycle();
        var generation = lifecycle.Begin(needsOwnership: false);
        True(lifecycle.MarkCrafting(generation), "locally owned resources start normally");
        True(lifecycle.TransferToTransaction(generation), "prepared reservation transfers at DoCrafting");
        True(!lifecycle.TransferToTransaction(generation), "reservation cannot transfer twice");
        lifecycle.FinishTransferred(generation);
        Equal(CraftingActionPhase.Idle, lifecycle.Phase, "completed craft clears lifecycle");
    }

    private static void CraftingRepeatedAttemptsUseNewGeneration()
    {
        var lifecycle = new CraftingActionLifecycle();
        var first = lifecycle.Begin(needsOwnership: true);
        lifecycle.Cancel();
        var second = lifecycle.Begin(needsOwnership: true);
        True(first != second, "repeated attempts have different callback generations");
        True(!lifecycle.MarkReserved(first), "first attempt cannot reserve for the second");
        True(lifecycle.MarkReserved(second), "second attempt can proceed independently");
    }

    private static void CraftingDisconnectInvalidatesPreviousSessionCallback()
    {
        var lifecycle = new CraftingActionLifecycle();
        var previousSession = lifecycle.Begin(needsOwnership: true);
        lifecycle.Cancel();
        True(!lifecycle.Matches(previousSession), "disconnect cleanup invalidates the previous callback");
        var rejoinedSession = lifecycle.Begin(needsOwnership: false);
        True(rejoinedSession != previousSession, "rejoin receives an isolated generation");
    }

    private static void SessionDisconnectSuspendsAndSafeReconnectRearms()
    {
        var lifecycle = new SessionLifecycleState(true);
        Equal(true, lifecycle.IsOperational, "compatible startup is operational");

        lifecycle.BeginDisconnect();
        Equal(SessionLifecyclePhase.AwaitingReconnect, lifecycle.Phase, "disconnect suspends features");
        Equal(false, lifecycle.IsOperational, "features stay fail-closed between sessions");

        Equal(true, lifecycle.TryRearm(true, true), "safe different session rearms");
        Equal(true, lifecycle.IsOperational, "features operate after reconnect");
    }

    private static void SessionReconnectRequiresDifferentNetworkAndSafeCleanup()
    {
        var sameSession = new SessionLifecycleState(true);
        sameSession.BeginDisconnect();
        Equal(false, sameSession.TryRearm(false, true), "same network session cannot rearm");
        Equal(SessionLifecyclePhase.AwaitingReconnect, sameSession.Phase, "same session remains suspended");

        var unsafeCleanup = new SessionLifecycleState(true);
        unsafeCleanup.BeginDisconnect();
        Equal(false, unsafeCleanup.TryRearm(true, false), "unsafe cleanup cannot rearm");
        Equal(SessionLifecyclePhase.AwaitingReconnect, unsafeCleanup.Phase, "unsafe cleanup remains suspended");
    }

    private static void PermanentDisableNeverRearms()
    {
        var lifecycle = new SessionLifecycleState(true);
        lifecycle.DisablePermanently();
        lifecycle.BeginDisconnect();
        Equal(false, lifecycle.TryRearm(true, true), "permanent safety disable cannot rearm");
        Equal(SessionLifecyclePhase.PermanentlyDisabled, lifecycle.Phase, "permanent disable survives reconnect");
    }

    private static void RepeatedDisconnectReconnectCyclesAreIdempotent()
    {
        var lifecycle = new SessionLifecycleState(true);
        for (var cycle = 0; cycle < 3; cycle++)
        {
            lifecycle.BeginDisconnect();
            lifecycle.BeginDisconnect();
            Equal(SessionLifecyclePhase.AwaitingReconnect, lifecycle.Phase, "repeated disconnect remains suspended");
            Equal(true, lifecycle.TryRearm(true, true), "cycle rearms once");
            Equal(false, lifecycle.TryRearm(true, true), "already-operational duplicate rearm is ignored");
        }
        Equal(true, lifecycle.IsOperational, "repeated cycles end operational");
    }

    private static void LifecycleShutdownIsTerminal()
    {
        var lifecycle = new SessionLifecycleState(true);
        lifecycle.ShutDown();
        lifecycle.BeginDisconnect();
        lifecycle.DisablePermanently();
        Equal(false, lifecycle.TryRearm(true, true), "unloaded plugin cannot rearm");
        Equal(SessionLifecyclePhase.ShutDown, lifecycle.Phase, "shutdown remains terminal");
    }

    private static void WorkbenchMeshIgnoresVerticalDistance()
    {
        var scope = Scope(P(0, 100, 0), Zone("a", 0, 0, 0, 10));
        Equal(StorageScopeKind.WorkbenchMesh, scope.Kind, "player is inside the vertical workbench cylinder");
        True(scope.Contains(P(0, -500, 9)), "mesh chest containment ignores vertical separation");
    }

    private static void WorkbenchMeshUsesStrictBoundaries()
    {
        var onBoundary = Scope(P(10, 0, 0), Zone("a", 0, 0, 0, 10));
        Equal(StorageScopeKind.NearbyRadius, onBoundary.Kind, "player exactly on build-radius boundary is outside");
        var inside = Scope(P(9.999, 0, 0), Zone("a", 0, 0, 0, 10));
        True(!inside.Contains(P(10, 0, 0)), "chest exactly on build-radius boundary is excluded");
    }

    private static void TangentWorkbenchZonesDoNotConnect()
    {
        var scope = Scope(P(0, 0, 0), Zone("a", 0, 0, 0, 10), Zone("b", 20, 0, 0, 10));
        SequenceEqual(new[] { "a" }, scope.ConnectedZones.Select(zone => zone.Id), "exactly tangent zones do not overlap");
    }

    private static void WorkbenchMeshConnectsTransitively()
    {
        var scope = Scope(P(0, 0, 0),
            Zone("a", 0, 0, 0, 10), Zone("b", 15, 0, 0, 10), Zone("c", 30, 0, 0, 10));
        SequenceEqual(new[] { "a", "b", "c" }, scope.ConnectedZones.Select(zone => zone.Id), "overlap graph is transitive");
    }

    private static void WorkbenchMeshSeedsEveryZoneContainingPlayer()
    {
        var scope = Scope(P(0, 0, 0),
            Zone("a", -9, 0, 0, 10), Zone("b", 9, 0, 0, 10), Zone("c", 27, 0, 0, 10));
        SequenceEqual(new[] { "a", "b", "c" }, scope.ConnectedZones.Select(zone => zone.Id), "all player-containing zones seed the graph");
    }

    private static void WorkbenchMeshExcludesDisconnectedIsland()
    {
        var scope = Scope(P(0, 0, 0), Zone("home", 0, 0, 0, 10), Zone("island", 100, 0, 0, 10));
        SequenceEqual(new[] { "home" }, scope.ConnectedZones.Select(zone => zone.Id), "disconnected base is excluded");
        True(!scope.Contains(P(100, 0, 0)), "chest on disconnected island is excluded");
    }

    private static void FarChestIsIncludedThroughOverlapChain()
    {
        var scope = Scope(P(0, 0, 0),
            Zone("a", 0, 0, 0, 10), Zone("b", 15, 0, 0, 10), Zone("c", 30, 0, 0, 10));
        True(scope.Contains(P(39, 0, 0)), "a chest far from the player is included by the connected union");
    }

    private static void ChestInGeometricGapIsExcluded()
    {
        var scope = Scope(P(0, 0, 0),
            Zone("a", 0, 0, 0, 10), Zone("b", 15, 0, 0, 10), Zone("c", 15, 0, 15, 10));
        True(!scope.Contains(P(0, 0, 14)), "the mesh is a disk union, not a convex hull");
    }

    private static void OutsideMeshUsesConfiguredThreeDimensionalFallback()
    {
        var scope = StorageScopePolicy.Resolve(P(0, 0, 0), 7, new[] { Zone("far", 100, 0, 0, 10) });
        Equal(StorageScopeKind.NearbyRadius, scope.Kind, "no containing workbench selects fallback");
        True(scope.Contains(P(0, 0, 7)), "configured fallback includes its exact 3D boundary");
        True(!scope.Contains(P(0, 0, 7.001)), "configured fallback excludes points beyond its chosen radius");
    }

    private static void InvalidWorkbenchZonesAreRejected()
    {
        var scope = Scope(P(0, 0, 0),
            Zone("", 0, 0, 0, 10), Zone("zero", 0, 0, 0, 0),
            Zone("negative", 0, 0, 0, -1), Zone("nan", double.NaN, 0, 0, 10),
            Zone("infinite", 0, 0, 0, double.PositiveInfinity));
        Equal(StorageScopeKind.NearbyRadius, scope.Kind, "invalid workbench data cannot seed a mesh");
    }

    private static void WorkbenchEnumerationOrderIsDeterministic()
    {
        var zones = new[] { Zone("c", 30, 0, 0, 10), Zone("a", 0, 0, 0, 10), Zone("b", 15, 0, 0, 10) };
        var forward = StorageScopePolicy.Resolve(P(0, 0, 0), 20, zones);
        var reverse = StorageScopePolicy.Resolve(P(0, 0, 0), 20, zones.Reverse());
        SequenceEqual(forward.ConnectedZones.Select(zone => zone.Id), reverse.ConnectedZones.Select(zone => zone.Id),
            "station enumeration order does not change the component");
    }

    private static void ConflictingDuplicateWorkbenchIdsAreRejected()
    {
        var scope = Scope(P(0, 0, 0), Zone("duplicate", 0, 0, 0, 10), Zone("duplicate", 100, 0, 0, 10));
        Equal(StorageScopeKind.Unavailable, scope.Kind, "ambiguous duplicate stable identities fail closed");
    }

    private static void PlayerMovementSwitchesMeshAndFallback()
    {
        var zones = new[] { Zone("home", 0, 0, 0, 10) };
        Equal(StorageScopeKind.WorkbenchMesh, StorageScopePolicy.Resolve(P(0, 0, 0), 20, zones).Kind,
            "inside position uses mesh");
        Equal(StorageScopeKind.NearbyRadius, StorageScopePolicy.Resolve(P(11, 0, 0), 20, zones).Kind,
            "moving beyond the strict zone boundary uses fallback");
    }

    private static void WorkbenchPlacementAndDestructionChangeMesh()
    {
        var home = Zone("home", 0, 0, 0, 10);
        var bridge = Zone("bridge", 15, 0, 0, 10);
        var far = Zone("far", 30, 0, 0, 10);
        var before = StorageScopePolicy.Resolve(P(0, 0, 0), 20, new[] { home, far });
        var placed = StorageScopePolicy.Resolve(P(0, 0, 0), 20, new[] { home, bridge, far });
        var destroyed = StorageScopePolicy.Resolve(P(0, 0, 0), 20, new[] { home, far });
        SequenceEqual(new[] { "home" }, before.ConnectedZones.Select(zone => zone.Id), "separated station starts outside mesh");
        SequenceEqual(new[] { "bridge", "far", "home" }, placed.ConnectedZones.Select(zone => zone.Id),
            "placing a bridge expands the connected mesh deterministically");
        SequenceEqual(new[] { "home" }, destroyed.ConnectedZones.Select(zone => zone.Id),
            "destroying the bridge contracts the mesh again");
    }

    private static StorageScopePlan Scope(ScopePoint player, params WorkbenchZone[] zones)
        => StorageScopePolicy.Resolve(player, 20, zones);

    private static ScopePoint P(double x, double y, double z) => new ScopePoint(x, y, z);

    private static WorkbenchZone Zone(string id, double x, double y, double z, double range)
        => new WorkbenchZone(id, P(x, y, z), range);

    private static ResourceWithdrawalPlan ResourcePlan(IEnumerable<ResourceRequirement> requirements, params ResourceStack[] stacks)
        => new ResourceWithdrawalPlanner().Plan(requirements, stacks);

    private static ResourceWithdrawalPlan ExpeditionPlan(IEnumerable<ResourceRequirement> requirements, params ResourceStack[] stacks)
        => new ExpeditionKitWithdrawalPlanner().Plan(
            requirements,
            stacks.Where(stack => !string.Equals(stack.InventoryId, "player", StringComparison.Ordinal)));

    private static ResourceStack Resource(
        string inventoryId,
        string stackId,
        string itemName,
        int quality,
        int quantity,
        int inventoryOrder,
        int slot)
        => new ResourceStack(inventoryId, stackId, itemName, quality, quantity, inventoryOrder, slot);

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

    private sealed class FakeDebitReceipt
    {
        internal FakeDebitReceipt(string stackId, int quantity)
        {
            StackId = stackId;
            Quantity = quantity;
        }

        internal string StackId { get; }
        internal int Quantity { get; }
    }

    private sealed class FakeEquipmentReferences
    {
        internal FakeEquipmentReferences(FakeRollbackItem primary, FakeRollbackItem secondary)
        {
            Right = primary;
            Left = secondary;
            Chest = primary;
            Legs = secondary;
            Ammo = secondary;
            Helmet = primary;
            Shoulder = primary;
            Utility = secondary;
            Trinket = primary;
            HiddenLeft = secondary;
            HiddenRight = primary;
        }

        internal FakeRollbackItem Right { get; }
        internal FakeRollbackItem Left { get; }
        internal FakeRollbackItem Chest { get; }
        internal FakeRollbackItem Legs { get; }
        internal FakeRollbackItem Ammo { get; }
        internal FakeRollbackItem Helmet { get; }
        internal FakeRollbackItem Shoulder { get; }
        internal FakeRollbackItem Utility { get; }
        internal FakeRollbackItem Trinket { get; }
        internal FakeRollbackItem HiddenLeft { get; }
        internal FakeRollbackItem HiddenRight { get; }

        internal IEnumerable<FakeRollbackItem> All => new[]
        {
            Right, Left, Chest, Legs, Ammo, Helmet, Shoulder, Utility, Trinket, HiddenLeft, HiddenRight
        };
    }

    private sealed class FakeRollbackItem
    {
        internal FakeRollbackItem(
            string name,
            int stack,
            double durability,
            int quality,
            int variant,
            Slot position,
            Dictionary<string, string> customData)
        {
            Name = name;
            Stack = stack;
            Durability = durability;
            Quality = quality;
            Variant = variant;
            Position = position;
            CustomData = customData;
        }

        internal string Name { get; set; }
        internal int Stack { get; set; }
        internal double Durability { get; set; }
        internal int Quality { get; set; }
        internal int Variant { get; set; }
        internal Slot Position { get; set; }
        internal Dictionary<string, string> CustomData { get; private set; }

        internal FakeRollbackItem Clone()
            => new FakeRollbackItem(
                Name,
                Stack,
                Durability,
                Quality,
                Variant,
                Position,
                new Dictionary<string, string>(CustomData));

        internal void RestoreFrom(FakeRollbackItem snapshot)
        {
            Name = snapshot.Name;
            Stack = snapshot.Stack;
            Durability = snapshot.Durability;
            Quality = snapshot.Quality;
            Variant = snapshot.Variant;
            Position = snapshot.Position;
            CustomData = new Dictionary<string, string>(snapshot.CustomData);
        }

        internal bool Matches(FakeRollbackItem snapshot)
            => string.Equals(Name, snapshot.Name, StringComparison.Ordinal) &&
               Stack == snapshot.Stack &&
               Durability.Equals(snapshot.Durability) &&
               Quality == snapshot.Quality &&
               Variant == snapshot.Variant &&
               Position.Equals(snapshot.Position) &&
               CustomData.Count == snapshot.CustomData.Count &&
               snapshot.CustomData.All(pair =>
                   CustomData.TryGetValue(pair.Key, out var value) &&
                   string.Equals(value, pair.Value, StringComparison.Ordinal));
    }

    private sealed class FakeDetachedItem
    {
        internal FakeDetachedItem(int quantity, FakeSharedMetadata? resolvedPrefabMetadata)
        {
            Quantity = quantity;
            ResolvedPrefabMetadata = resolvedPrefabMetadata;
        }

        internal int Quantity { get; }
        internal FakeSharedMetadata? ResolvedPrefabMetadata { get; }
        internal FakeSharedMetadata? Shared { get; set; }
    }

    private sealed class FakeSharedMetadata
    {
        internal FakeSharedMetadata(string name)
        {
            Name = name;
        }

        internal string Name { get; }
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

    private static void Throws<TException>(Action action, string message) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }
        throw new Exception(message);
    }

    private static void SequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual, string message)
    {
        if (!expected.SequenceEqual(actual))
            throw new Exception(message + ": expected [" + string.Join(", ", expected) + "], got [" + string.Join(", ", actual) + "]");
    }
}
