#nullable disable
using System;
using System.Collections.Generic;
using Stackmaster.Core;

namespace Stackmaster
{
    /// <summary>
    /// Sorts only containers that accepted at least one successful Quick Stack deposit. The
    /// caller deduplicates container identities while executing transfers and invokes this once,
    /// after every transfer step, while the exact action ownership batch is still held.
    /// </summary>
    internal static class QuickStackDestinationSorter
    {
        internal static void SortTouched(
            Player player,
            StorageScope executionScope,
            IReadOnlyDictionary<string, ContainerHandle> handles,
            IEnumerable<string> touchedContainerIds,
            IReadOnlyDictionary<string, uint> expectedDataRevisions,
            TransferExecutionResult result)
        {
            foreach (var containerId in touchedContainerIds)
            {
                if (result.FailedContainers.ContainsKey(containerId))
                {
                    continue;
                }

                ContainerHandle handle;
                uint expectedDataRevision;
                string failure;
                if (!handles.TryGetValue(containerId, out handle) ||
                    !expectedDataRevisions.TryGetValue(containerId, out expectedDataRevision))
                {
                    RecordFailure(result, containerId, "container disappeared before destination auto-sort");
                    continue;
                }
                if (!TransferExecutor.RevalidateAndOwn(
                        player,
                        executionScope,
                        handle,
                        expectedDataRevision,
                        out failure))
                {
                    RecordFailure(result, containerId, failure);
                    continue;
                }

                string preferenceKey;
                bool enabled;
                if (!ChestSortPreferences.TryGet(handle.Container, out preferenceKey, out enabled) || !enabled)
                {
                    continue;
                }

                if (!SortExecutor.Sort(handle.Container.GetInventory(), false, null, null, out failure))
                {
                    RecordFailure(result, containerId, failure);
                    continue;
                }

                result.SortedDestinationContainerIds.Add(containerId);
            }
        }

        private static void RecordFailure(TransferExecutionResult result, string containerId, string failure)
        {
            result.DestinationSortFailures[containerId] = failure;
            RuntimeContext.Plugin?.Log.LogWarning(
                "Quick Stack destination auto-sort skipped safely: " + failure);
        }
    }
}
