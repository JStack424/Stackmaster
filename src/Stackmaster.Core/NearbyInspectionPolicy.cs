#nullable enable
using System;

namespace Stackmaster.Core
{
    /// <summary>
    /// Keeps ordinary nearby-storage actions complete while bounding pathological dense-base
    /// inspection. The minimum prefix is unconditional; elapsed time is considered only after
    /// that prefix, and a separate hard maximum remains as a final safety backstop.
    /// </summary>
    public sealed class NearbyInspectionPolicy
    {
        public NearbyInspectionPolicy(int minimumBeforeBudget, int maximumInspections, double budgetMilliseconds)
        {
            if (minimumBeforeBudget < 0) throw new ArgumentOutOfRangeException(nameof(minimumBeforeBudget));
            if (maximumInspections < minimumBeforeBudget) throw new ArgumentOutOfRangeException(nameof(maximumInspections));
            if (budgetMilliseconds <= 0 || double.IsNaN(budgetMilliseconds) || double.IsInfinity(budgetMilliseconds))
            {
                throw new ArgumentOutOfRangeException(nameof(budgetMilliseconds));
            }

            MinimumBeforeBudget = minimumBeforeBudget;
            MaximumInspections = maximumInspections;
            BudgetMilliseconds = budgetMilliseconds;
        }

        public int MinimumBeforeBudget { get; }
        public int MaximumInspections { get; }
        public double BudgetMilliseconds { get; }

        public bool CanInspectNext(int inspectedCount, double elapsedInspectionMilliseconds)
        {
            if (inspectedCount < 0) throw new ArgumentOutOfRangeException(nameof(inspectedCount));
            if (elapsedInspectionMilliseconds < 0 || double.IsNaN(elapsedInspectionMilliseconds))
            {
                throw new ArgumentOutOfRangeException(nameof(elapsedInspectionMilliseconds));
            }

            if (inspectedCount >= MaximumInspections) return false;
            return inspectedCount < MinimumBeforeBudget || elapsedInspectionMilliseconds < BudgetMilliseconds;
        }

        public string StopReason(int inspectedCount, double elapsedInspectionMilliseconds)
        {
            if (CanInspectNext(inspectedCount, elapsedInspectionMilliseconds)) return string.Empty;
            return inspectedCount >= MaximumInspections
                ? "hard safety limit reached"
                : "inspection time budget reached";
        }
    }
}
