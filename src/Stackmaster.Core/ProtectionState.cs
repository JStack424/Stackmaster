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
    /// Versioned, culture-invariant persistence for exact protected inventory slots.
    /// The optional item key prevents a replenishment target from silently applying
    /// to a different item that later occupies the same slot.
    /// </summary>
    public sealed class ProtectionState
    {
        public const string CurrentVersion = "v1";

        private readonly Dictionary<Slot, ProtectionRecord> _records;

        public ProtectionState(IEnumerable<ProtectionRecord> records = null)
        {
            _records = (records ?? Enumerable.Empty<ProtectionRecord>())
                .GroupBy(record => record.Slot)
                .ToDictionary(group => group.Key, group => group.Last());
        }

        public IReadOnlyCollection<ProtectionRecord> Records => _records.Values.ToArray();

        public bool IsProtected(Slot slot) => _records.ContainsKey(slot);

        public bool TryGet(Slot slot, out ProtectionRecord record) => _records.TryGetValue(slot, out record);

        public void Protect(Slot slot, int? targetQuantity, string targetItemKey)
        {
            if (targetQuantity.HasValue && targetQuantity.Value <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(targetQuantity));
            }

            if (targetQuantity.HasValue && string.IsNullOrEmpty(targetItemKey))
            {
                throw new ArgumentException("A replenishment target requires an item key.", nameof(targetItemKey));
            }

            _records[slot] = new ProtectionRecord(slot, targetQuantity, targetItemKey);
        }

        public bool Unprotect(Slot slot) => _records.Remove(slot);

        public string Serialize()
        {
            var parts = _records.Values
                .OrderBy(record => record.Slot.Row)
                .ThenBy(record => record.Slot.Column)
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
            if (string.IsNullOrWhiteSpace(raw))
            {
                return new ProtectionState();
            }

            var parts = raw.Split(';');
            if (parts.Length == 0 || !string.Equals(parts[0], CurrentVersion, StringComparison.Ordinal))
            {
                return new ProtectionState();
            }

            var records = new List<ProtectionRecord>();
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
                    continue;
                }

                int parsedTarget;
                int? target = null;
                if (!string.IsNullOrEmpty(fields[2]))
                {
                    if (!int.TryParse(fields[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedTarget) || parsedTarget <= 0)
                    {
                        continue;
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
                    continue;
                }

                if (target.HasValue && string.IsNullOrEmpty(itemKey))
                {
                    continue;
                }

                records.Add(new ProtectionRecord(new Slot(column, row), target, itemKey));
            }

            return new ProtectionState(records);
        }

        private static string Encode(string value) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(value));

        private static string Decode(string value) => System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(value));
    }

    public sealed class ProtectionRecord
    {
        public ProtectionRecord(Slot slot, int? targetQuantity, string targetItemKey)
        {
            Slot = slot;
            TargetQuantity = targetQuantity;
            TargetItemKey = targetItemKey;
        }

        public Slot Slot { get; }
        public int? TargetQuantity { get; }
        public string TargetItemKey { get; }
    }
}
