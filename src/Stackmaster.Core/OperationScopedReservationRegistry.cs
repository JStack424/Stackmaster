using System;
using System.Collections.Generic;

namespace Stackmaster.Core
{
    /// <summary>
    /// Assigns a process-local generation to each active container reservation. Cleanup may
    /// mutate a container only while its exact generation is still current, so a delayed callback
    /// from an older operation cannot clear a newer operation that reused the same container.
    /// </summary>
    public sealed class OperationScopedReservationRegistry
    {
        private readonly Dictionary<string, long> _active =
            new Dictionary<string, long>(StringComparer.Ordinal);
        private long _nextGeneration;

        public bool TryBegin(string containerId, out long generation)
        {
            generation = 0;
            if (string.IsNullOrEmpty(containerId) || _active.ContainsKey(containerId))
            {
                return false;
            }

            do
            {
                _nextGeneration = unchecked(_nextGeneration + 1);
            }
            while (_nextGeneration == 0);

            generation = _nextGeneration;
            _active.Add(containerId, generation);
            return true;
        }

        public bool IsCurrent(string containerId, long generation)
            => !string.IsNullOrEmpty(containerId) &&
               generation != 0 &&
               _active.TryGetValue(containerId, out var current) &&
               current == generation;

        public bool Complete(string containerId, long generation)
        {
            if (!IsCurrent(containerId, generation))
            {
                return false;
            }

            _active.Remove(containerId);
            return true;
        }

        public bool HasActive(string containerId)
            => !string.IsNullOrEmpty(containerId) && _active.ContainsKey(containerId);

        public void Clear() => _active.Clear();
    }
}
