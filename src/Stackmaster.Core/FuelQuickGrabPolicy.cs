using System;
using System.Collections.Generic;
using System.Linq;

namespace Stackmaster.Core
{
    public sealed class FuelQuickGrabCandidate
    {
        public FuelQuickGrabCandidate(string identity, int available, int maximumStack)
        {
            if (string.IsNullOrWhiteSpace(identity)) throw new ArgumentException("A fuel identity is required.", nameof(identity));
            if (available < 0) throw new ArgumentOutOfRangeException(nameof(available));
            if (maximumStack <= 0) throw new ArgumentOutOfRangeException(nameof(maximumStack));
            Identity = identity;
            Available = available;
            MaximumStack = maximumStack;
        }

        public string Identity { get; }
        public int Available { get; }
        public int MaximumStack { get; }
    }

    public sealed class FuelQuickGrabSelection
    {
        internal FuelQuickGrabSelection(string identity, int quantity)
        {
            Identity = identity;
            Quantity = quantity;
        }

        public string Identity { get; }
        public int Quantity { get; }
    }

    /// <summary>Selects one fuel in declared priority order and requests at most one canonical stack.</summary>
    public static class FuelQuickGrabPolicy
    {
        public static FuelQuickGrabSelection? Select(IEnumerable<FuelQuickGrabCandidate> priorities)
        {
            if (priorities == null) throw new ArgumentNullException(nameof(priorities));
            var selected = priorities.FirstOrDefault(candidate => candidate != null && candidate.Available > 0);
            return selected == null
                ? null
                : new FuelQuickGrabSelection(selected.Identity, Math.Min(selected.Available, selected.MaximumStack));
        }
    }
}
