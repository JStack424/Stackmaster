using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Stackmaster.Core
{
    public enum ExpeditionReservationAddResult
    {
        Added,
        Incremented,
        RecipeChanged,
        CapacityExceeded
    }

    /// <summary>
    /// One persistent reserved build-piece identity. Requirements are the exact normalized recipe
    /// that accompanied the successful Quick Grab which created the reservation. Keeping the
    /// recipe snapshot makes the reservation describe the materials actually grabbed, even when a
    /// later game or mod update changes the live recipe.
    /// </summary>
    public sealed class ExpeditionReservationRecord
    {
        public ExpeditionReservationRecord(
            string pieceKey,
            string displayName,
            int count,
            IEnumerable<ResourceRequirement> requirements)
        {
            if (string.IsNullOrWhiteSpace(pieceKey)) throw new ArgumentException("A stable piece key is required.", nameof(pieceKey));
            if (string.IsNullOrWhiteSpace(displayName)) throw new ArgumentException("A piece display name is required.", nameof(displayName));
            if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
            if (requirements == null) throw new ArgumentNullException(nameof(requirements));

            PieceKey = pieceKey;
            DisplayName = displayName;
            Count = count;
            Requirements = new ReadOnlyCollection<ResourceRequirement>(NormalizeRequirements(requirements));
            if (Requirements.Count == 0)
            {
                throw new ArgumentException("A reservation requires at least one material.", nameof(requirements));
            }
        }

        public string PieceKey { get; }
        public string DisplayName { get; }
        public int Count { get; }
        public IReadOnlyList<ResourceRequirement> Requirements { get; }

        internal ExpeditionReservationRecord WithCountAndDisplayName(int count, string displayName)
            => new ExpeditionReservationRecord(PieceKey, displayName, count, Requirements);

        internal static ResourceRequirement[] NormalizeRequirements(IEnumerable<ResourceRequirement> requirements)
        {
            var supplied = requirements.ToArray();
            if (supplied.Any(requirement => requirement == null))
            {
                throw new ArgumentException("Reservation requirements cannot contain null.", nameof(requirements));
            }

            return supplied
                .GroupBy(requirement => new RequirementIdentity(requirement.ItemName, requirement.Quality))
                .Select(group => new ResourceRequirement(
                    group.Key.ItemName,
                    checked(group.Sum(requirement => requirement.Quantity)),
                    group.Key.Quality))
                .OrderBy(requirement => requirement.ItemName, StringComparer.Ordinal)
                .ThenBy(requirement => requirement.Quality)
                .ToArray();
        }

        private sealed class RequirementIdentity : IEquatable<RequirementIdentity>
        {
            internal RequirementIdentity(string itemName, int quality)
            {
                ItemName = itemName;
                Quality = quality;
            }

            internal string ItemName { get; }
            internal int Quality { get; }

            public bool Equals(RequirementIdentity? other)
                => other != null && Quality == other.Quality &&
                   string.Equals(ItemName, other.ItemName, StringComparison.Ordinal);

            public override bool Equals(object? obj) => Equals(obj as RequirementIdentity);
            public override int GetHashCode() => unchecked((StringComparer.Ordinal.GetHashCode(ItemName) * 397) ^ Quality);
        }
    }

    /// <summary>
    /// Versioned local reservation state. The integration layer scopes each serialized value by
    /// player id and world id; this pure model owns exact piece counts and recipe aggregation only.
    /// Explicit icon removal releases only reservation state; this model deliberately does not
    /// decide material-vs-explicit-target precedence or mutate physical items.
    /// </summary>
    public sealed class ExpeditionReservationState
    {
        public const string CurrentVersion = "v1";
        public const int MaximumRecords = 128;
        public const int MaximumCountPerPiece = 1000000;
        public const int MaximumRequirementsPerPiece = 64;
        private const int MaximumPieceKeyLength = 1024;
        private const int MaximumDisplayNameLength = 1024;
        private const int MaximumItemNameLength = 16384;
        private const int MaximumSerializedLength = 1024 * 1024;
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);
        private readonly Dictionary<string, ExpeditionReservationRecord> _records;

        public ExpeditionReservationState(IEnumerable<ExpeditionReservationRecord>? records = null)
        {
            _records = new Dictionary<string, ExpeditionReservationRecord>(StringComparer.Ordinal);
            if (records == null) return;

            foreach (var record in records)
            {
                if (record == null) throw new ArgumentException("Reservation records cannot contain null.", nameof(records));
                ValidateRecord(record);
                if (_records.ContainsKey(record.PieceKey))
                {
                    throw new ArgumentException("Reservation piece keys must be unique.", nameof(records));
                }
                _records.Add(record.PieceKey, record);
                if (_records.Count > MaximumRecords)
                {
                    throw new ArgumentException("Reservation state exceeds its record limit.", nameof(records));
                }
            }
            EnsureAggregateFits(_records.Values);
        }

        public IReadOnlyCollection<ExpeditionReservationRecord> Records
            => _records.Values.OrderBy(record => record.PieceKey, StringComparer.Ordinal).ToArray();

        public ExpeditionReservationAddResult RecordSuccessfulQuickGrab(
            string pieceKey,
            string displayName,
            IEnumerable<ResourceRequirement> requirements)
        {
            ValidateIdentity(pieceKey, displayName);
            var normalized = ExpeditionReservationRecord.NormalizeRequirements(requirements ?? throw new ArgumentNullException(nameof(requirements)));
            if (normalized.Length == 0 || normalized.Length > MaximumRequirementsPerPiece)
            {
                return ExpeditionReservationAddResult.CapacityExceeded;
            }
            ValidateRequirements(normalized);

            ExpeditionReservationRecord existing;
            if (_records.TryGetValue(pieceKey, out existing!))
            {
                if (!RequirementsEqual(existing.Requirements, normalized))
                {
                    return ExpeditionReservationAddResult.RecipeChanged;
                }
                if (existing.Count >= MaximumCountPerPiece)
                {
                    return ExpeditionReservationAddResult.CapacityExceeded;
                }

                var replacement = existing.WithCountAndDisplayName(existing.Count + 1, displayName);
                if (!AggregateFits(_records.Values.Where(record => !ReferenceEquals(record, existing)).Concat(new[] { replacement })))
                {
                    return ExpeditionReservationAddResult.CapacityExceeded;
                }
                _records[pieceKey] = replacement;
                return ExpeditionReservationAddResult.Incremented;
            }

            if (_records.Count >= MaximumRecords)
            {
                return ExpeditionReservationAddResult.CapacityExceeded;
            }
            var added = new ExpeditionReservationRecord(pieceKey, displayName, 1, normalized);
            if (!AggregateFits(_records.Values.Concat(new[] { added })))
            {
                return ExpeditionReservationAddResult.CapacityExceeded;
            }
            _records.Add(pieceKey, added);
            return ExpeditionReservationAddResult.Added;
        }

        /// <summary>
        /// Releases only the persistent reservation count. This pure state transition cannot move
        /// carried items or invoke Quick Stack; those remain separate, explicit player actions.
        /// </summary>
        public bool TryReleaseReservation(string pieceKey, int quantity)
        {
            if (string.IsNullOrWhiteSpace(pieceKey)) throw new ArgumentException("A stable piece key is required.", nameof(pieceKey));
            if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));

            ExpeditionReservationRecord existing;
            if (!_records.TryGetValue(pieceKey, out existing!) || quantity > existing.Count)
            {
                return false;
            }

            var remaining = ExpeditionReservationRemovalPolicy.RemainingCountAfterExplicitRelease(
                existing.Count,
                quantity);
            if (remaining == 0)
            {
                return _records.Remove(pieceKey);
            }
            _records[pieceKey] = existing.WithCountAndDisplayName(remaining, existing.DisplayName);
            return true;
        }

        public IReadOnlyList<ResourceRequirement> AggregateRequirements()
        {
            var totals = new Dictionary<RequirementIdentity, int>();
            foreach (var record in _records.Values)
            {
                foreach (var requirement in record.Requirements)
                {
                    var key = new RequirementIdentity(requirement.ItemName, requirement.Quality);
                    var prior = totals.TryGetValue(key, out var value) ? value : 0;
                    totals[key] = checked(prior + checked(requirement.Quantity * record.Count));
                }
            }

            return totals
                .OrderBy(pair => pair.Key.ItemName, StringComparer.Ordinal)
                .ThenBy(pair => pair.Key.Quality)
                .Select(pair => new ResourceRequirement(pair.Key.ItemName, pair.Value, pair.Key.Quality))
                .ToArray();
        }

        public string Serialize()
        {
            var records = _records.Values
                .OrderBy(record => record.PieceKey, StringComparer.Ordinal)
                .Select(record => string.Join(",", new[]
                {
                    Encode(record.PieceKey),
                    Encode(record.DisplayName),
                    record.Count.ToString(CultureInfo.InvariantCulture),
                    EncodeRequirements(record.Requirements)
                }));
            var serializedRecords = string.Join(";", records);
            var raw = serializedRecords.Length == 0
                ? CurrentVersion
                : CurrentVersion + ";" + serializedRecords;
            if (raw.Length > MaximumSerializedLength)
            {
                throw new InvalidOperationException("Reservation state exceeds its serialized size limit.");
            }
            return raw;
        }

        public static bool TryParse(string raw, out ExpeditionReservationState state)
        {
            state = new ExpeditionReservationState();
            if (string.IsNullOrEmpty(raw)) return true;
            if (raw.Length > MaximumSerializedLength) return false;

            try
            {
                var parts = raw.Split(';');
                if (parts.Length == 0 || !string.Equals(parts[0], CurrentVersion, StringComparison.Ordinal)) return false;
                var records = new List<ExpeditionReservationRecord>();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                for (var index = 1; index < parts.Length; index++)
                {
                    if (string.IsNullOrEmpty(parts[index])) return false;
                    var fields = parts[index].Split(',');
                    int count;
                    if (fields.Length != 4 ||
                        !int.TryParse(fields[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out count) ||
                        count <= 0 || count > MaximumCountPerPiece)
                    {
                        return false;
                    }

                    var pieceKey = Decode(fields[0]);
                    var displayName = Decode(fields[1]);
                    if (!seen.Add(pieceKey)) return false;
                    var requirements = DecodeRequirements(fields[3]);
                    records.Add(new ExpeditionReservationRecord(pieceKey, displayName, count, requirements));
                    if (records.Count > MaximumRecords) return false;
                }

                state = new ExpeditionReservationState(records);
                return true;
            }
            catch (Exception exception) when (
                exception is FormatException ||
                exception is DecoderFallbackException ||
                exception is ArgumentException ||
                exception is OverflowException ||
                exception is InvalidOperationException)
            {
                state = new ExpeditionReservationState();
                return false;
            }
        }

        private static string EncodeRequirements(IEnumerable<ResourceRequirement> requirements)
            => string.Join("|", requirements.Select(requirement =>
                Encode(requirement.ItemName) + ":" +
                requirement.Quantity.ToString(CultureInfo.InvariantCulture) + ":" +
                requirement.Quality.ToString(CultureInfo.InvariantCulture)));

        private static ResourceRequirement[] DecodeRequirements(string encoded)
        {
            if (string.IsNullOrEmpty(encoded)) throw new FormatException("A reservation recipe is required.");
            var parts = encoded.Split('|');
            if (parts.Length > MaximumRequirementsPerPiece) throw new FormatException("Reservation recipe is too large.");
            var requirements = new List<ResourceRequirement>(parts.Length);
            foreach (var part in parts)
            {
                var fields = part.Split(':');
                int quantity;
                int quality;
                if (fields.Length != 3 ||
                    !int.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out quantity) ||
                    !int.TryParse(fields[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out quality))
                {
                    throw new FormatException("Reservation recipe is malformed.");
                }
                requirements.Add(new ResourceRequirement(Decode(fields[0]), quantity, quality));
            }
            return requirements.ToArray();
        }

        private static bool RequirementsEqual(
            IReadOnlyList<ResourceRequirement> left,
            IReadOnlyList<ResourceRequirement> right)
        {
            if (left.Count != right.Count) return false;
            for (var index = 0; index < left.Count; index++)
            {
                if (!string.Equals(left[index].ItemName, right[index].ItemName, StringComparison.Ordinal) ||
                    left[index].Quality != right[index].Quality ||
                    left[index].Quantity != right[index].Quantity)
                {
                    return false;
                }
            }
            return true;
        }

        private static void ValidateRecord(ExpeditionReservationRecord record)
        {
            ValidateIdentity(record.PieceKey, record.DisplayName);
            if (record.Count > MaximumCountPerPiece || record.Requirements.Count > MaximumRequirementsPerPiece)
                throw new ArgumentException("Reservation record exceeds its limits.", nameof(record));
            ValidateRequirements(record.Requirements);
        }

        private static void ValidateIdentity(string pieceKey, string displayName)
        {
            if (string.IsNullOrWhiteSpace(pieceKey) || pieceKey.Length > MaximumPieceKeyLength)
                throw new ArgumentException("The stable piece identity is invalid.", nameof(pieceKey));
            if (string.IsNullOrWhiteSpace(displayName) || displayName.Length > MaximumDisplayNameLength)
                throw new ArgumentException("The piece display name is invalid.", nameof(displayName));
        }

        private static void ValidateRequirements(IEnumerable<ResourceRequirement> requirements)
        {
            foreach (var requirement in requirements)
            {
                if (requirement.ItemName.Length > MaximumItemNameLength)
                    throw new ArgumentException("A reservation material identity is too long.", nameof(requirements));
            }
        }

        private static bool AggregateFits(IEnumerable<ExpeditionReservationRecord> records)
        {
            try
            {
                EnsureAggregateFits(records);
                return true;
            }
            catch (OverflowException)
            {
                return false;
            }
        }

        private static void EnsureAggregateFits(IEnumerable<ExpeditionReservationRecord> records)
        {
            var totals = new Dictionary<RequirementIdentity, int>();
            foreach (var record in records)
            {
                foreach (var requirement in record.Requirements)
                {
                    var key = new RequirementIdentity(requirement.ItemName, requirement.Quality);
                    var prior = totals.TryGetValue(key, out var value) ? value : 0;
                    totals[key] = checked(prior + checked(requirement.Quantity * record.Count));
                }
            }
        }

        private static string Encode(string value) => Convert.ToBase64String(StrictUtf8.GetBytes(value));
        private static string Decode(string value) => StrictUtf8.GetString(Convert.FromBase64String(value));

        private sealed class RequirementIdentity : IEquatable<RequirementIdentity>
        {
            internal RequirementIdentity(string itemName, int quality)
            {
                ItemName = itemName;
                Quality = quality;
            }

            internal string ItemName { get; }
            internal int Quality { get; }

            public bool Equals(RequirementIdentity? other)
                => other != null && Quality == other.Quality &&
                   string.Equals(ItemName, other.ItemName, StringComparison.Ordinal);

            public override bool Equals(object? obj) => Equals(obj as RequirementIdentity);
            public override int GetHashCode() => unchecked((StringComparer.Ordinal.GetHashCode(ItemName) * 397) ^ Quality);
        }
    }

    /// <summary>
    /// Settled policy for a top-row reservation-icon removal: release reservation state only.
    /// Carried items stay exactly where they are, and any later deposit must come from a separate,
    /// explicit Quick Stack action.
    /// </summary>
    public static class ExpeditionReservationRemovalPolicy
    {
        public const bool MovesCarriedMaterialsAfterRelease = false;
        public const bool StartsQuickStackAfterRelease = false;

        public static int RemainingCountAfterExplicitRelease(int currentCount, int releasedCount)
        {
            if (currentCount <= 0) throw new ArgumentOutOfRangeException(nameof(currentCount));
            if (releasedCount <= 0 || releasedCount > currentCount)
                throw new ArgumentOutOfRangeException(nameof(releasedCount));
            return currentCount - releasedCount;
        }
    }

    /// <summary>
    /// Settled policy: building never consumes a reservation. Only a future explicit-removal
    /// interaction may change the count. Keeping this decision in one pure boundary prevents a
    /// placement callback from silently gaining reservation side effects.
    /// </summary>
    public static class ExpeditionReservationBuildPolicy
    {
        public static int CountAfterSuccessfulBuild(int currentCount)
        {
            if (currentCount < 0) throw new ArgumentOutOfRangeException(nameof(currentCount));
            return currentCount;
        }
    }
}
