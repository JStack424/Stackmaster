using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Stackmaster.Core
{
    public sealed class RememberedDestinationRecord
    {
        public RememberedDestinationRecord(string itemKey, string containerId, long observedUtcTicks)
        {
            if (string.IsNullOrWhiteSpace(itemKey)) throw new ArgumentException("An item key is required.", nameof(itemKey));
            if (string.IsNullOrWhiteSpace(containerId)) throw new ArgumentException("A container id is required.", nameof(containerId));
            if (observedUtcTicks <= 0) throw new ArgumentOutOfRangeException(nameof(observedUtcTicks));
            ItemKey = itemKey;
            ContainerId = containerId;
            ObservedUtcTicks = observedUtcTicks;
        }

        public string ItemKey { get; }
        public string ContainerId { get; }
        public long ObservedUtcTicks { get; }
    }

    /// <summary>
    /// Bounded local routing hints. Entries are never authoritative: the integration layer must
    /// freshly resolve and validate the exact chest before every planned transfer.
    /// </summary>
    public sealed class RememberedDestinationState
    {
        public const int MaximumEntries = 512;
        public static readonly TimeSpan Retention = TimeSpan.FromDays(180);
        public static readonly TimeSpan SameDestinationRefreshInterval = TimeSpan.FromDays(1);
        private const int MaximumItemKeyLength = 16384;
        private const int MaximumContainerIdLength = 256;
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);
        private readonly Dictionary<string, RememberedDestinationRecord> _records;

        public RememberedDestinationState(IEnumerable<RememberedDestinationRecord>? records = null)
        {
            _records = new Dictionary<string, RememberedDestinationRecord>(StringComparer.Ordinal);
            if (records == null) return;
            foreach (var record in records)
            {
                if (record == null) throw new ArgumentException("Destination records cannot contain null entries.", nameof(records));
                ValidateLengths(record.ItemKey, record.ContainerId);
                if (_records.ContainsKey(record.ItemKey))
                    throw new ArgumentException("Destination item keys must be unique.", nameof(records));
                _records.Add(record.ItemKey, record);
            }
            TrimToMaximum();
        }

        public IReadOnlyCollection<RememberedDestinationRecord> Records => _records.Values.ToArray();

        public bool Remember(string itemKey, string containerId, long observedUtcTicks)
        {
            ValidateLengths(itemKey, containerId);
            if (observedUtcTicks <= 0) throw new ArgumentOutOfRangeException(nameof(observedUtcTicks));
            RememberedDestinationRecord existing;
            if (_records.TryGetValue(itemKey, out existing) &&
                string.Equals(existing.ContainerId, containerId, StringComparison.Ordinal) &&
                (observedUtcTicks <= existing.ObservedUtcTicks ||
                 observedUtcTicks - existing.ObservedUtcTicks <= SameDestinationRefreshInterval.Ticks))
            {
                return false;
            }

            _records[itemKey] = new RememberedDestinationRecord(itemKey, containerId, observedUtcTicks);
            TrimToMaximum();
            return true;
        }

        public bool TryGet(string itemKey, out RememberedDestinationRecord record)
        {
            if (itemKey == null)
            {
                record = null!;
                return false;
            }
            return _records.TryGetValue(itemKey, out record!);
        }

        public int Prune(long nowUtcTicks)
        {
            if (nowUtcTicks <= 0) throw new ArgumentOutOfRangeException(nameof(nowUtcTicks));
            var cutoff = nowUtcTicks - Retention.Ticks;
            var stale = _records.Values
                .Where(record => record.ObservedUtcTicks < cutoff || record.ObservedUtcTicks > nowUtcTicks + TimeSpan.FromDays(1).Ticks)
                .Select(record => record.ItemKey)
                .ToArray();
            foreach (var itemKey in stale) _records.Remove(itemKey);
            var beforeTrim = _records.Count;
            TrimToMaximum();
            return stale.Length + beforeTrim - _records.Count;
        }

        public string Serialize()
        {
            var lines = new List<string> { "v1" };
            lines.AddRange(_records.Values
                .OrderBy(record => record.ItemKey, StringComparer.Ordinal)
                .Select(record => Encode(record.ItemKey) + "," + Encode(record.ContainerId) + "," + record.ObservedUtcTicks.ToString(CultureInfo.InvariantCulture)));
            return string.Join(";", lines);
        }

        public static bool TryParse(string raw, out RememberedDestinationState state)
        {
            state = new RememberedDestinationState();
            if (string.IsNullOrEmpty(raw)) return true;
            try
            {
                var parts = raw.Split(';');
                if (parts.Length == 0 || !string.Equals(parts[0], "v1", StringComparison.Ordinal)) return false;
                var records = new List<RememberedDestinationRecord>();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                for (var index = 1; index < parts.Length; index++)
                {
                    if (string.IsNullOrEmpty(parts[index])) return false;
                    var fields = parts[index].Split(',');
                    long ticks;
                    if (fields.Length != 3 || !long.TryParse(fields[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out ticks) || ticks <= 0) return false;
                    var itemKey = Decode(fields[0]);
                    var containerId = Decode(fields[1]);
                    ValidateLengths(itemKey, containerId);
                    if (!seen.Add(itemKey)) return false;
                    records.Add(new RememberedDestinationRecord(itemKey, containerId, ticks));
                    if (records.Count > MaximumEntries) return false;
                }
                state = new RememberedDestinationState(records);
                return true;
            }
            catch (Exception exception) when (exception is FormatException || exception is DecoderFallbackException || exception is ArgumentException)
            {
                state = new RememberedDestinationState();
                return false;
            }
        }

        private static string Encode(string value) => Convert.ToBase64String(StrictUtf8.GetBytes(value));
        private static string Decode(string value) => StrictUtf8.GetString(Convert.FromBase64String(value));

        private static void ValidateLengths(string itemKey, string containerId)
        {
            if (string.IsNullOrWhiteSpace(itemKey) || itemKey.Length > MaximumItemKeyLength)
                throw new ArgumentException("The remembered item identity is invalid.", nameof(itemKey));
            if (string.IsNullOrWhiteSpace(containerId) || containerId.Length > MaximumContainerIdLength)
                throw new ArgumentException("The remembered container identity is invalid.", nameof(containerId));
        }

        private void TrimToMaximum()
        {
            foreach (var itemKey in _records.Values
                .OrderByDescending(record => record.ObservedUtcTicks)
                .ThenBy(record => record.ItemKey, StringComparer.Ordinal)
                .Skip(MaximumEntries)
                .Select(record => record.ItemKey)
                .ToArray())
            {
                _records.Remove(itemKey);
            }
        }
    }
}
