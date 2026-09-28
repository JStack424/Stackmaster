#nullable enable
using System;
using System.Collections.Generic;

namespace Stackmaster.Core
{
    /// <summary>One authoritative source inventory's expected and observed debit.</summary>
    public readonly struct SourceDebitObservation
    {
        public SourceDebitObservation(int beforeUnits, int afterUnits, int expectedDebit)
        {
            BeforeUnits = beforeUnits;
            AfterUnits = afterUnits;
            ExpectedDebit = expectedDebit;
        }

        public int BeforeUnits { get; }
        public int AfterUnits { get; }
        public int ExpectedDebit { get; }
    }

    /// <summary>
    /// Validates conservation across authoritative source inventories and the player inventory.
    /// It intentionally accepts counts only after the caller has bound every source to the live
    /// storage object; detached planning snapshots are not authoritative transaction sources.
    /// </summary>
    public static class AuthoritativeTransferConservation
    {
        public static bool IsExactTransfer(
            IReadOnlyList<SourceDebitObservation>? sources,
            int playerBefore,
            int playerAfter,
            int expectedUnits)
        {
            if (sources == null || sources.Count == 0 || playerBefore < 0 || playerAfter < 0 || expectedUnits <= 0)
                return false;

            try
            {
                var expectedSourceDebit = 0;
                var observedSourceDebit = 0;
                var combinedBefore = playerBefore;
                var combinedAfter = playerAfter;
                foreach (var source in sources)
                {
                    if (source.BeforeUnits < 0 || source.AfterUnits < 0 || source.ExpectedDebit <= 0)
                        return false;
                    var observedDebit = source.BeforeUnits - source.AfterUnits;
                    if (observedDebit != source.ExpectedDebit)
                        return false;
                    expectedSourceDebit = checked(expectedSourceDebit + source.ExpectedDebit);
                    observedSourceDebit = checked(observedSourceDebit + observedDebit);
                    combinedBefore = checked(combinedBefore + source.BeforeUnits);
                    combinedAfter = checked(combinedAfter + source.AfterUnits);
                }

                var playerCredit = playerAfter - playerBefore;
                return expectedSourceDebit == expectedUnits &&
                       observedSourceDebit == expectedUnits &&
                       playerCredit == expectedUnits &&
                       observedSourceDebit == playerCredit &&
                       combinedBefore == combinedAfter;
            }
            catch (OverflowException)
            {
                return false;
            }
        }

        public static bool IsExactRollback(
            IReadOnlyList<SourceDebitObservation>? sources,
            int playerBefore,
            int playerAfter)
        {
            if (sources == null || playerBefore < 0 || playerAfter < 0 || playerBefore != playerAfter)
                return false;
            foreach (var source in sources)
            {
                if (source.BeforeUnits < 0 || source.AfterUnits < 0 || source.BeforeUnits != source.AfterUnits)
                    return false;
            }
            return true;
        }
    }
}
