using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Stackmaster.Core
{
    /// <summary>
    /// Captures detached item state while retaining the original live object identities. A fallback
    /// rollback can therefore restore the captured state into those same objects and rebuild the
    /// inventory around them without detaching external references such as equipped-item fields.
    /// </summary>
    public sealed class IdentityPreservingInventoryBackup<TItem, TState>
        where TItem : class
    {
        private readonly ReadOnlyCollection<ItemBackup> _items;

        private IdentityPreservingInventoryBackup(IEnumerable<ItemBackup> items)
        {
            _items = new ReadOnlyCollection<ItemBackup>(items.ToList());
        }

        public int Count => _items.Count;

        public static IdentityPreservingInventoryBackup<TItem, TState> Capture(
            IEnumerable<TItem> items,
            Func<TItem, TState> captureState)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            if (captureState == null) throw new ArgumentNullException(nameof(captureState));

            var backups = items.Select(item =>
            {
                if (item == null) throw new ArgumentException("Inventory snapshots cannot contain null items.", nameof(items));
                return new ItemBackup(item, captureState(item));
            });
            return new IdentityPreservingInventoryBackup<TItem, TState>(backups);
        }

        /// <summary>
        /// Clones every detached state before any live object is touched. Callers can therefore
        /// prepare all inventory restore plans and reflection surfaces before beginning rollback.
        /// </summary>
        public IdentityPreservingInventoryRestorePlan<TItem, TState> PrepareRestore(
            Func<TState, TState> cloneState)
        {
            if (cloneState == null) throw new ArgumentNullException(nameof(cloneState));
            return new IdentityPreservingInventoryRestorePlan<TItem, TState>(
                _items.Select(item => new IdentityPreservingRestoreEntry<TItem, TState>(
                    item.Original,
                    cloneState(item.State))));
        }

        private sealed class ItemBackup
        {
            internal ItemBackup(TItem original, TState state)
            {
                Original = original;
                State = state;
            }

            internal TItem Original { get; }
            internal TState State { get; }
        }
    }

    public sealed class IdentityPreservingInventoryRestorePlan<TItem, TState>
        where TItem : class
    {
        private readonly ReadOnlyCollection<IdentityPreservingRestoreEntry<TItem, TState>> _entries;

        internal IdentityPreservingInventoryRestorePlan(
            IEnumerable<IdentityPreservingRestoreEntry<TItem, TState>> entries)
        {
            _entries = new ReadOnlyCollection<IdentityPreservingRestoreEntry<TItem, TState>>(entries.ToList());
        }

        public IReadOnlyList<IdentityPreservingRestoreEntry<TItem, TState>> Entries => _entries;

        public List<TItem> RebuildInventoryList()
            => _entries.Select(entry => entry.Original).ToList();

        public void RestoreStates(Action<TItem, TState> restoreState)
        {
            if (restoreState == null) throw new ArgumentNullException(nameof(restoreState));
            foreach (var entry in _entries)
            {
                restoreState(entry.Original, entry.State);
            }
        }

        public bool HasExactOriginalReferences(IEnumerable<TItem> liveItems)
        {
            if (liveItems == null) return false;
            var live = liveItems.ToList();
            if (live.Count != _entries.Count) return false;
            for (var index = 0; index < live.Count; index++)
            {
                if (!ReferenceEquals(live[index], _entries[index].Original)) return false;
            }
            return true;
        }

        public bool StatesMatch(Func<TItem, TState, bool> stateMatches)
        {
            if (stateMatches == null) throw new ArgumentNullException(nameof(stateMatches));
            return _entries.All(entry => stateMatches(entry.Original, entry.State));
        }
    }

    public sealed class IdentityPreservingRestoreEntry<TItem, TState>
        where TItem : class
    {
        internal IdentityPreservingRestoreEntry(TItem original, TState state)
        {
            Original = original;
            State = state;
        }

        public TItem Original { get; }
        public TState State { get; }
    }

    public static class ReservationOwnershipCleanupPolicy
    {
        /// <summary>
        /// Stackmaster must clear its exact local in-use reservation before it relinquishes the
        /// exact ownership acquisition that authorizes that cleanup.
        /// </summary>
        public static bool CanRelinquishOwnership(bool hasPendingReservationCleanup)
            => !hasPendingReservationCleanup;
    }
}
