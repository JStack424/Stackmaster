using System;
using System.Collections.Generic;

namespace Stackmaster.Core
{
    /// <summary>
    /// Resolves every detached item's shared metadata before applying any of it. A single
    /// unresolved row rejects the whole snapshot so resource accounting is never partial.
    /// </summary>
    public static class DetachedItemHydrator
    {
        public static bool TryHydrate<TItem, TMetadata>(
            IEnumerable<TItem> items,
            Func<TItem, TMetadata?> resolveMetadata,
            Action<TItem, TMetadata> applyMetadata)
            where TItem : class
            where TMetadata : class
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            if (resolveMetadata == null) throw new ArgumentNullException(nameof(resolveMetadata));
            if (applyMetadata == null) throw new ArgumentNullException(nameof(applyMetadata));

            var resolved = new List<KeyValuePair<TItem, TMetadata>>();
            foreach (var item in items)
            {
                if (item == null) return false;
                var metadata = resolveMetadata(item);
                if (metadata == null) return false;
                resolved.Add(new KeyValuePair<TItem, TMetadata>(item, metadata));
            }

            foreach (var pair in resolved)
            {
                applyMetadata(pair.Key, pair.Value);
            }
            return true;
        }
    }
}
