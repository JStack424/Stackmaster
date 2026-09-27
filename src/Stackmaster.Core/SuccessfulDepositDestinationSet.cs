using System;
using System.Collections.Generic;

namespace Stackmaster.Core
{
    /// <summary>
    /// Records destination identities only after a deposit step commits successfully. It preserves
    /// first-touch order while ensuring every chest can be considered for post-deposit sorting at
    /// most once. Replenishments, failed moves, and untouched plan destinations are never added.
    /// </summary>
    public sealed class SuccessfulDepositDestinationSet
    {
        private readonly HashSet<string> _seen = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<string> _ordered = new List<string>();

        public IReadOnlyList<string> OrderedIds => _ordered;

        public void Add(string containerId)
        {
            if (string.IsNullOrWhiteSpace(containerId))
                throw new ArgumentException("A successful deposit destination needs a stable identity.", nameof(containerId));
            if (_seen.Add(containerId))
            {
                _ordered.Add(containerId);
            }
        }
    }
}
