#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Stackmaster.Core
{
    public struct Slot : IEquatable<Slot>
    {
        public Slot(int column, int row)
        {
            if (column < 0) throw new ArgumentOutOfRangeException(nameof(column));
            if (row < 0) throw new ArgumentOutOfRangeException(nameof(row));
            Column = column;
            Row = row;
        }

        public int Column { get; }
        public int Row { get; }

        public bool Equals(Slot other) => Column == other.Column && Row == other.Row;
        public override bool Equals(object obj) => obj is Slot && Equals((Slot)obj);
        public override int GetHashCode() => unchecked((Column * 397) ^ Row);
    }

    /// <summary>
    /// One current player-inventory stack that a protection record may follow. ItemKey is a
    /// persistent, conservative identity supplied by the game integration layer.
    /// </summary>
    public sealed class ProtectionCandidate
    {
        public ProtectionCandidate(Slot slot, string itemKey)
        {
            if (string.IsNullOrEmpty(itemKey)) throw new ArgumentException("An item key is required.", nameof(itemKey));
            Slot = slot;
            ItemKey = itemKey;
        }

        public Slot Slot { get; }
        public string ItemKey { get; }
    }

    public sealed class ProtectionResolution
    {
        internal ProtectionResolution(IDictionary<Slot, ProtectionRecord> assignments, bool changed)
        {
            Assignments = new Dictionary<Slot, ProtectionRecord>(assignments);
            Changed = changed;
        }

        public IReadOnlyDictionary<Slot, ProtectionRecord> Assignments { get; }
        public bool Changed { get; }
        public bool TryGet(Slot slot, out ProtectionRecord record) => Assignments.TryGetValue(slot, out record);
    }

    /// <summary>
    /// Versioned, culture-invariant persistence for protected inventory stacks. Slot is the
    /// preferred location of the matching stack, not ownership of that location. Reconciliation
    /// keeps a matching item at its preferred slot when possible, otherwise follows exactly one
    /// compatible stack in deterministic row-major order and persists the new preference.
    /// </summary>
    public sealed class ProtectionState
    {
        // v1 stored exact-slot protection and allowed protect-only records without an item key.
        // v2 keeps the same compact fields but treats slot as a preference and requires identity.
        public const string CurrentVersion = "v2";
        private const string LegacyVersion = "v1";

        private readonly List<ProtectionRecord> _records;

        public ProtectionState(IEnumerable<ProtectionRecord> records = null)
        {
            _records = (records ?? Enumerable.Empty<ProtectionRecord>())
                .Where(record => record != null)
                .ToList();
        }

        public IReadOnlyCollection<ProtectionRecord> Records => _records.ToArray();

        // These methods refer only to persisted preferred locations. Runtime item protection must
        // use Reconcile so an unrelated replacement item never inherits a record.
        public bool IsProtected(Slot slot) => _records.Any(record => record.Slot.Equals(slot));

        public bool TryGet(Slot slot, out ProtectionRecord record)
        {
            record = _records.FirstOrDefault(value => value.Slot.Equals(slot));
            return record != null;
        }

        public void Protect(Slot slot, int? targetQuantity, string targetItemKey)
        {
            if (targetQuantity.HasValue && targetQuantity.Value <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(targetQuantity));
            }
            if (string.IsNullOrEmpty(targetItemKey))
            {
                throw new ArgumentException("Protection requires an item key.", nameof(targetItemKey));
            }

            var existing = _records.FindIndex(record =>
                record.Slot.Equals(slot) && string.Equals(record.TargetItemKey, targetItemKey, StringComparison.Ordinal));
            var replacement = new ProtectionRecord(slot, targetQuantity, targetItemKey);
            if (existing >= 0)
            {
                _records[existing] = replacement;
            }
            else
            {
                _records.Add(replacement);
            }
        }

        public bool Unprotect(Slot slot)
        {
            var index = _records.FindIndex(record => record.Slot.Equals(slot));
            if (index < 0) return false;
            _records.RemoveAt(index);
            return true;
        }

        public bool Unprotect(ProtectionRecord record)
        {
            if (record == null) return false;
            return _records.Remove(record);
        }

        public ProtectionResolution Reconcile(IEnumerable<ProtectionCandidate> candidates, bool pruneUnresolved = false)
        {
            if (candidates == null) throw new ArgumentNullException(nameof(candidates));

            var supplied = candidates.ToArray();
            if (supplied.Any(candidate => candidate == null))
            {
                throw new ArgumentException("Protection candidates cannot contain null.", nameof(candidates));
            }
            var orderedCandidates = supplied
                .OrderBy(candidate => candidate.Slot.Row)
                .ThenBy(candidate => candidate.Slot.Column)
                .ToArray();
            if (orderedCandidates.Select(candidate => candidate.Slot).Distinct().Count() != orderedCandidates.Length)
            {
                throw new ArgumentException("Protection candidates must occupy unique slots.", nameof(candidates));
            }

            var bySlot = orderedCandidates.ToDictionary(candidate => candidate.Slot);
            var claimedSlots = new HashSet<Slot>();
            var assignments = new Dictionary<Slot, ProtectionRecord>();
            var updated = _records.ToArray();
            var assignedRecords = new HashSet<int>();
            var changed = false;

            // First preserve every identified record whose preferred slot still contains a match.
            for (var index = 0; index < updated.Length; index++)
            {
                var record = updated[index];
                ProtectionCandidate candidate;
                if (string.IsNullOrEmpty(record.TargetItemKey) ||
                    !bySlot.TryGetValue(record.Slot, out candidate) ||
                    !string.Equals(record.TargetItemKey, candidate.ItemKey, StringComparison.Ordinal) ||
                    !claimedSlots.Add(candidate.Slot))
                {
                    continue;
                }

                assignments.Add(candidate.Slot, record);
                assignedRecords.Add(index);
            }

            // Legacy protect-only records had no identity. Bind them only when their original
            // preferred slot still holds an item; never guess after that item has moved away.
            for (var index = 0; index < updated.Length; index++)
            {
                var record = updated[index];
                ProtectionCandidate candidate;
                if (!string.IsNullOrEmpty(record.TargetItemKey) ||
                    !bySlot.TryGetValue(record.Slot, out candidate) ||
                    !claimedSlots.Add(candidate.Slot))
                {
                    continue;
                }

                var bound = new ProtectionRecord(candidate.Slot, record.TargetQuantity, candidate.ItemKey);
                updated[index] = bound;
                assignments.Add(candidate.Slot, bound);
                assignedRecords.Add(index);
                changed = true;
            }

            // Follow every remaining identified record to one unclaimed compatible stack. Record
            // and candidate traversal are stable, so duplicate choices are deterministic.
            foreach (var index in Enumerable.Range(0, updated.Length)
                .Where(value => !assignedRecords.Contains(value) && !string.IsNullOrEmpty(updated[value].TargetItemKey))
                .OrderBy(value => updated[value].Slot.Row)
                .ThenBy(value => updated[value].Slot.Column)
                .ThenBy(value => value))
            {
                var record = updated[index];
                var candidate = orderedCandidates.FirstOrDefault(value =>
                    !claimedSlots.Contains(value.Slot) &&
                    string.Equals(value.ItemKey, record.TargetItemKey, StringComparison.Ordinal));
                if (candidate == null)
                {
                    continue;
                }

                var moved = new ProtectionRecord(candidate.Slot, record.TargetQuantity, record.TargetItemKey);
                updated[index] = moved;
                assignments.Add(candidate.Slot, moved);
                claimedSlots.Add(candidate.Slot);
                assignedRecords.Add(index);
                changed |= !candidate.Slot.Equals(record.Slot);
            }

            // If duplicate protected stacks merged into fewer survivors, keep exactly the
            // records that won a surviving stack. An extra record must not later reactivate on
            // an unrelated replacement stack. If no compatible stack exists at all, retain the
            // dormant identified record so it can safely follow that item when it returns.
            var assignedItemKeys = new HashSet<string>(
                assignedRecords.Select(index => updated[index].TargetItemKey),
                StringComparer.Ordinal);
            var survivors = updated.Where((record, index) =>
                string.IsNullOrEmpty(record.TargetItemKey) ||
                assignedRecords.Contains(index) ||
                !assignedItemKeys.Contains(record.TargetItemKey)).ToArray();
            changed |= survivors.Length != updated.Length;

            // A legacy protect-only record has no identity to follow once its old slot is empty
            // or replaced. Drop that ambiguous record rather than ever protecting an unrelated
            // item; this is the only safe migration from the former exact-slot model.
            var migrated = survivors.Where(record => !string.IsNullOrEmpty(record.TargetItemKey)).ToArray();
            changed |= migrated.Length != survivors.Length;

            // Ordinary inventory reconciliation keeps an identified record dormant so a
            // transient cursor drag can complete safely. A deliberate maintenance boundary
            // (currently the storage hotkey) may instead prune records that cannot resolve any
            // compatible player-inventory item. This self-heals orphaned targets from older
            // builds without changing item-following during normal inventory interaction.
            if (pruneUnresolved)
            {
                var assigned = new HashSet<ProtectionRecord>(assignments.Values);
                var resolvedOnly = migrated.Where(assigned.Contains).ToArray();
                changed |= resolvedOnly.Length != migrated.Length;
                migrated = resolvedOnly;
            }

            if (changed)
            {
                _records.Clear();
                _records.AddRange(migrated);
            }
            return new ProtectionResolution(assignments, changed);
        }

        public string Serialize()
        {
            var parts = _records
                .OrderBy(record => record.Slot.Row)
                .ThenBy(record => record.Slot.Column)
                .ThenBy(record => record.TargetItemKey ?? string.Empty, StringComparer.Ordinal)
                .ThenBy(record => record.TargetQuantity ?? 0)
                .Select(record => string.Join(",", new[]
                {
                    record.Slot.Column.ToString(CultureInfo.InvariantCulture),
                    record.Slot.Row.ToString(CultureInfo.InvariantCulture),
                    record.TargetQuantity?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                    Encode(record.TargetItemKey ?? string.Empty)
                }));

            return CurrentVersion + ";" + string.Join(";", parts);
        }

        public static ProtectionState Parse(string raw)
        {
            ProtectionState state;
            return TryParse(raw, out state) ? state : new ProtectionState();
        }

        public static bool TryParse(string raw, out ProtectionState state)
        {
            state = new ProtectionState();
            if (string.IsNullOrWhiteSpace(raw))
            {
                return true;
            }

            var parts = raw.Split(';');
            if (parts.Length == 0 ||
                (!string.Equals(parts[0], CurrentVersion, StringComparison.Ordinal) &&
                 !string.Equals(parts[0], LegacyVersion, StringComparison.Ordinal)))
            {
                return false;
            }
            var legacy = string.Equals(parts[0], LegacyVersion, StringComparison.Ordinal);

            var records = new List<ProtectionRecord>();
            var slots = new HashSet<Slot>();
            for (var index = 1; index < parts.Length; index++)
            {
                if (string.IsNullOrWhiteSpace(parts[index]))
                {
                    continue;
                }

                var fields = parts[index].Split(',');
                int column;
                int row;
                if (fields.Length != 4 ||
                    !int.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out column) ||
                    !int.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out row) ||
                    column < 0 || row < 0)
                {
                    return false;
                }

                int parsedTarget;
                int? target = null;
                if (!string.IsNullOrEmpty(fields[2]))
                {
                    if (!int.TryParse(fields[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedTarget) || parsedTarget <= 0)
                    {
                        return false;
                    }
                    target = parsedTarget;
                }

                string itemKey;
                try
                {
                    itemKey = Decode(fields[3]);
                }
                catch (FormatException)
                {
                    return false;
                }
                catch (System.Text.DecoderFallbackException)
                {
                    return false;
                }

                if (string.IsNullOrEmpty(itemKey) && (!legacy || target.HasValue))
                {
                    return false;
                }

                var slot = new Slot(column, row);
                if (legacy && !slots.Add(slot))
                {
                    return false;
                }
                records.Add(new ProtectionRecord(slot, target, itemKey));
            }

            state = new ProtectionState(records);
            return true;
        }

        private static string Encode(string value) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(value));

        private static string Decode(string value)
            => new System.Text.UTF8Encoding(false, true).GetString(Convert.FromBase64String(value));
    }

    public sealed class ProtectionRecord
    {
        public ProtectionRecord(Slot slot, int? targetQuantity, string targetItemKey)
        {
            Slot = slot;
            TargetQuantity = targetQuantity;
            TargetItemKey = targetItemKey;
        }

        /// <summary>The preferred location of this record's matching stack.</summary>
        public Slot Slot { get; }
        public int? TargetQuantity { get; }
        public string TargetItemKey { get; }
    }
}
