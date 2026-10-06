using System;
using System.Collections.Generic;
using System.Globalization;

namespace Stackmaster.Core
{
    public enum DiagnosticEmissionDecision
    {
        Initial,
        Changed,
        SuppressDuplicate,
        SuppressCapacity
    }

    public sealed class BoundedDiagnosticTransitionTracker
    {
        private readonly int _maximumStreams;
        private readonly Dictionary<string, string> _fingerprints =
            new Dictionary<string, string>(StringComparer.Ordinal);

        public BoundedDiagnosticTransitionTracker(int maximumStreams)
        {
            if (maximumStreams <= 0) throw new ArgumentOutOfRangeException(nameof(maximumStreams));
            _maximumStreams = maximumStreams;
        }

        public int Count => _fingerprints.Count;

        public DiagnosticEmissionDecision Observe(string streamKey, string fingerprint)
        {
            if (string.IsNullOrWhiteSpace(streamKey)) throw new ArgumentException("A diagnostic stream key is required.", nameof(streamKey));
            fingerprint = fingerprint ?? string.Empty;

            string previous;
            if (_fingerprints.TryGetValue(streamKey, out previous))
            {
                if (string.Equals(previous, fingerprint, StringComparison.Ordinal))
                {
                    return DiagnosticEmissionDecision.SuppressDuplicate;
                }
                _fingerprints[streamKey] = fingerprint;
                return DiagnosticEmissionDecision.Changed;
            }

            if (_fingerprints.Count >= _maximumStreams)
            {
                return DiagnosticEmissionDecision.SuppressCapacity;
            }
            _fingerprints.Add(streamKey, fingerprint);
            return DiagnosticEmissionDecision.Initial;
        }

        public void Reset() => _fingerprints.Clear();
    }

    public sealed class ContainerStreamDiagnosticState
    {
        public ContainerStreamDiagnosticState(
            bool containerLoaded,
            bool capturedContainerReferenceMatches,
            bool networkViewPresent,
            bool networkViewValid,
            bool capturedNetworkViewReferenceMatches,
            bool sceneZdoPresent,
            bool managerZdoPresent,
            bool sceneAndManagerZdoReferenceMatch,
            bool capturedZdoReferenceMatches,
            long owner,
            ushort ownerRevision,
            uint dataRevision,
            bool scopeContainsKnownPosition,
            bool sourceIncluded,
            int sourceUnits)
        {
            ContainerLoaded = containerLoaded;
            CapturedContainerReferenceMatches = capturedContainerReferenceMatches;
            NetworkViewPresent = networkViewPresent;
            NetworkViewValid = networkViewValid;
            CapturedNetworkViewReferenceMatches = capturedNetworkViewReferenceMatches;
            SceneZdoPresent = sceneZdoPresent;
            ManagerZdoPresent = managerZdoPresent;
            SceneAndManagerZdoReferenceMatch = sceneAndManagerZdoReferenceMatch;
            CapturedZdoReferenceMatches = capturedZdoReferenceMatches;
            Owner = owner;
            OwnerRevision = ownerRevision;
            DataRevision = dataRevision;
            ScopeContainsKnownPosition = scopeContainsKnownPosition;
            SourceIncluded = sourceIncluded;
            SourceUnits = sourceUnits;
        }

        public bool ContainerLoaded { get; }
        public bool CapturedContainerReferenceMatches { get; }
        public bool NetworkViewPresent { get; }
        public bool NetworkViewValid { get; }
        public bool CapturedNetworkViewReferenceMatches { get; }
        public bool SceneZdoPresent { get; }
        public bool ManagerZdoPresent { get; }
        public bool SceneAndManagerZdoReferenceMatch { get; }
        public bool CapturedZdoReferenceMatches { get; }
        public long Owner { get; }
        public ushort OwnerRevision { get; }
        public uint DataRevision { get; }
        public bool ScopeContainsKnownPosition { get; }
        public bool SourceIncluded { get; }
        public int SourceUnits { get; }

        public string Fingerprint => string.Join("|", new[]
        {
            ContainerLoaded ? "1" : "0",
            CapturedContainerReferenceMatches ? "1" : "0",
            NetworkViewPresent ? "1" : "0",
            NetworkViewValid ? "1" : "0",
            CapturedNetworkViewReferenceMatches ? "1" : "0",
            SceneZdoPresent ? "1" : "0",
            ManagerZdoPresent ? "1" : "0",
            SceneAndManagerZdoReferenceMatch ? "1" : "0",
            CapturedZdoReferenceMatches ? "1" : "0",
            Owner.ToString(CultureInfo.InvariantCulture),
            OwnerRevision.ToString(CultureInfo.InvariantCulture),
            DataRevision.ToString(CultureInfo.InvariantCulture),
            ScopeContainsKnownPosition ? "1" : "0",
            SourceIncluded ? "1" : "0",
            SourceUnits.ToString(CultureInfo.InvariantCulture)
        });
    }

    public sealed class OwnershipDiagnosticState
    {
        public OwnershipDiagnosticState(
            long owner,
            ushort ownerRevision,
            uint dataRevision,
            bool hasOwner,
            bool networkViewIsOwner,
            bool containerIsOwner,
            bool containerLoaded,
            bool capturedContainerReferenceMatches,
            bool networkViewValid,
            bool capturedNetworkViewReferenceMatches,
            bool managerZdoPresent,
            bool sceneZdoPresent,
            bool zdoIdentityMatches,
            bool capturedZdoReferenceMatches,
            bool sceneAndManagerZdoReferenceMatch)
        {
            Owner = owner;
            OwnerRevision = ownerRevision;
            DataRevision = dataRevision;
            HasOwner = hasOwner;
            NetworkViewIsOwner = networkViewIsOwner;
            ContainerIsOwner = containerIsOwner;
            ContainerLoaded = containerLoaded;
            CapturedContainerReferenceMatches = capturedContainerReferenceMatches;
            NetworkViewValid = networkViewValid;
            CapturedNetworkViewReferenceMatches = capturedNetworkViewReferenceMatches;
            ManagerZdoPresent = managerZdoPresent;
            SceneZdoPresent = sceneZdoPresent;
            ZdoIdentityMatches = zdoIdentityMatches;
            CapturedZdoReferenceMatches = capturedZdoReferenceMatches;
            SceneAndManagerZdoReferenceMatch = sceneAndManagerZdoReferenceMatch;
        }

        public long Owner { get; }
        public ushort OwnerRevision { get; }
        public uint DataRevision { get; }
        public bool HasOwner { get; }
        public bool NetworkViewIsOwner { get; }
        public bool ContainerIsOwner { get; }
        public bool ContainerLoaded { get; }
        public bool CapturedContainerReferenceMatches { get; }
        public bool NetworkViewValid { get; }
        public bool CapturedNetworkViewReferenceMatches { get; }
        public bool ManagerZdoPresent { get; }
        public bool SceneZdoPresent { get; }
        public bool ZdoIdentityMatches { get; }
        public bool CapturedZdoReferenceMatches { get; }
        public bool SceneAndManagerZdoReferenceMatch { get; }

        public string Fingerprint => string.Join("|", new[]
        {
            Owner.ToString(CultureInfo.InvariantCulture),
            OwnerRevision.ToString(CultureInfo.InvariantCulture),
            DataRevision.ToString(CultureInfo.InvariantCulture),
            HasOwner ? "1" : "0",
            NetworkViewIsOwner ? "1" : "0",
            ContainerIsOwner ? "1" : "0",
            ContainerLoaded ? "1" : "0",
            CapturedContainerReferenceMatches ? "1" : "0",
            NetworkViewValid ? "1" : "0",
            CapturedNetworkViewReferenceMatches ? "1" : "0",
            ManagerZdoPresent ? "1" : "0",
            SceneZdoPresent ? "1" : "0",
            ZdoIdentityMatches ? "1" : "0",
            CapturedZdoReferenceMatches ? "1" : "0",
            SceneAndManagerZdoReferenceMatch ? "1" : "0"
        });
    }
}
