using System;

namespace Stackmaster.Core
{
    public enum PlanningWorkKind
    {
        ContainerInspection
    }

    public sealed class PlanningWork
    {
        public PlanningWork(PlanningWorkKind kind, int sequence, string subjectId)
        {
            if (sequence < 0) throw new ArgumentOutOfRangeException(nameof(sequence));
            SubjectId = subjectId ?? throw new ArgumentNullException(nameof(subjectId));
            Kind = kind;
            Sequence = sequence;
        }

        public PlanningWorkKind Kind { get; }
        public int Sequence { get; }
        public string SubjectId { get; }
    }

    /// <summary>
    /// An injected responsiveness policy. Production can implement this with elapsed time;
    /// tests can use deterministic work-unit budgets. The planner never imposes a public
    /// container-count cap.
    /// </summary>
    public interface IPlanningBudget
    {
        bool TryConsume(PlanningWork work);
    }

    public sealed class UnlimitedPlanningBudget : IPlanningBudget
    {
        public static readonly UnlimitedPlanningBudget Instance = new UnlimitedPlanningBudget();
        private UnlimitedPlanningBudget() { }
        public bool TryConsume(PlanningWork work) => true;
    }

    public sealed class FixedWorkPlanningBudget : IPlanningBudget
    {
        private int _remaining;

        public FixedWorkPlanningBudget(int allowedWorkUnits)
        {
            if (allowedWorkUnits < 0) throw new ArgumentOutOfRangeException(nameof(allowedWorkUnits));
            _remaining = allowedWorkUnits;
        }

        public bool TryConsume(PlanningWork work)
        {
            if (work == null) throw new ArgumentNullException(nameof(work));
            if (_remaining == 0) return false;
            _remaining--;
            return true;
        }
    }
}
