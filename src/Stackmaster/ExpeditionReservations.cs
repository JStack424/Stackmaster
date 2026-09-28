#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using Stackmaster.Core;
using UnityEngine;

namespace Stackmaster
{
    /// <summary>
    /// Persistent local Quick Grab Materials reservations, scoped to one character and one world.
    /// The saved counts feed additive Quick Stack retention, player sorting, and optional UI.
    /// Each successful exact-piece placement and each explicit icon click releases one count.
    /// </summary>
    internal enum SuccessfulBuildReservationResult
    {
        NotApplicable,
        NotReserved,
        Consumed,
        PersistenceFailed
    }

    internal static class ExpeditionReservations
    {
        private const string CloneSuffix = "(Clone)";
        private static ExpeditionReservationState _state = new ExpeditionReservationState();
        private static string _loadedKey;
        private static bool _storageHealthy = true;
        private static bool _writesHealthy = true;
        private static bool _failureLogged;
        private static bool _buildConsumeWarningShown;

        internal static void Initialize()
        {
            _state = new ExpeditionReservationState();
            _loadedKey = null;
            _storageHealthy = true;
            _writesHealthy = true;
            _failureLogged = false;
            _buildConsumeWarningShown = false;
        }

        internal static void Shutdown()
        {
            _state = new ExpeditionReservationState();
            _loadedKey = null;
            _storageHealthy = false;
            _writesHealthy = false;
        }

        internal static bool CanCommitQuickGrab(Player player)
        {
            try
            {
                return _storageHealthy && _writesHealthy && player != null && EnsureLoaded(player);
            }
            catch (Exception exception)
            {
                DisableStorage("Expedition reservations could not be prepared and were disabled for this session: " + exception.GetType().Name);
                return false;
            }
        }

        internal static bool CanRecordQuickGrab(
            Player player,
            string pieceKey,
            string displayName,
            IReadOnlyList<ResourceRequirement> requirements,
            out string failure)
        {
            failure = null;
            try
            {
                if (!CanCommitQuickGrab(player) || string.IsNullOrEmpty(pieceKey) ||
                    string.IsNullOrEmpty(displayName) || requirements == null)
                {
                    failure = "reservations are unavailable";
                    return false;
                }

                ExpeditionReservationState ignored;
                ExpeditionReservationPersistenceResult result;
                var valid = ExpeditionReservationPersistence.TryAddDurably(
                    _state,
                    pieceKey,
                    displayName,
                    requirements,
                    payload => true,
                    out ignored,
                    out result);
                if (valid) return true;

                failure = FailureFor(result);
                return false;
            }
            catch (InvalidOperationException)
            {
                failure = "the safe reservation storage limit was reached";
                return false;
            }
            catch (Exception exception)
            {
                DisableStorage("Expedition reservations could not be validated and were disabled for this session: " + exception.GetType().Name);
                failure = "reservations are unavailable";
                return false;
            }
        }

        internal static bool TryAddQuickGrabReservation(
            Player player,
            string pieceKey,
            string displayName,
            IReadOnlyList<ResourceRequirement> requirements,
            out int committedCount,
            out string failure)
        {
            committedCount = 0;
            failure = null;
            try
            {
                if (!_storageHealthy || !_writesHealthy || player == null || string.IsNullOrEmpty(pieceKey) ||
                    string.IsNullOrEmpty(displayName) || requirements == null || !EnsureLoaded(player))
                {
                    failure = "reservations are unavailable";
                    return false;
                }

                var previousPayload = _state.Serialize();
                Exception persistenceFailure = null;
                ExpeditionReservationState committed;
                ExpeditionReservationPersistenceResult result;
                var saved = ExpeditionReservationPersistence.TryAddDurably(
                    _state,
                    pieceKey,
                    displayName,
                    requirements,
                    payload =>
                    {
                        try
                        {
                            return AtomicReservationFileStore.TryWrite(_loadedKey, previousPayload, payload);
                        }
                        catch (Exception exception)
                        {
                            persistenceFailure = exception;
                            _writesHealthy = false;
                            return false;
                        }
                    },
                    out committed,
                    out result);
                if (!saved)
                {
                    failure = FailureFor(result);
                    if (persistenceFailure != null)
                    {
                        LogFailureOnce("Quick Grab reservation could not be saved and was left unchanged: " + persistenceFailure.GetType().Name);
                    }
                    return false;
                }

                _state = committed;
                committedCount = committed.Records
                    .Where(record => string.Equals(record.PieceKey, pieceKey, StringComparison.Ordinal))
                    .Select(record => record.Count)
                    .Single();
                try
                {
                    InventoryIntegration.RequestExpeditionRefresh();
                }
                catch (Exception exception)
                {
                    // UI refresh is optional and must never make a completed durable write look
                    // like a failed click. The next ordinary inventory refresh will retry it.
                    RuntimeContext.Plugin?.Log.LogWarning(
                        "Quick Grab reservation UI refresh failed after save: " + exception.GetType().Name);
                }
                return true;
            }
            catch (InvalidOperationException)
            {
                failure = "the safe reservation storage limit was reached";
                return false;
            }
            catch (Exception exception)
            {
                DisableStorage("Expedition reservations could not be updated and were disabled for this session: " + exception.GetType().Name);
                failure = "reservations are unavailable";
                return false;
            }
        }

        private static string FailureFor(ExpeditionReservationPersistenceResult result)
        {
            switch (result)
            {
                case ExpeditionReservationPersistenceResult.RecipeChanged:
                    return "the reserved build-piece recipe changed";
                case ExpeditionReservationPersistenceResult.CapacityExceeded:
                    return "the safe reservation limit was reached";
                default:
                    return "the reservation could not be saved";
            }
        }

        internal static bool TryGetAggregateRequirements(
            Player player,
            out IReadOnlyList<ResourceRequirement> requirements)
        {
            requirements = Array.Empty<ResourceRequirement>();
            try
            {
                if (!_storageHealthy || player == null || !EnsureLoaded(player))
                {
                    return false;
                }
                requirements = _state.AggregateRequirements();
                return true;
            }
            catch (Exception exception)
            {
                DisableStorage("Expedition reservations could not be read and were disabled for this session: " + exception.GetType().Name);
                requirements = Array.Empty<ResourceRequirement>();
                return false;
            }
        }

        internal static bool TryGetRecords(
            Player player,
            out IReadOnlyList<ExpeditionReservationRecord> records)
        {
            records = Array.Empty<ExpeditionReservationRecord>();
            try
            {
                if (!_storageHealthy || player == null || !EnsureLoaded(player))
                {
                    return false;
                }
                records = _state.Records.ToArray();
                return true;
            }
            catch (Exception exception)
            {
                DisableStorage("Expedition reservations could not be read and were disabled for this session: " + exception.GetType().Name);
                records = Array.Empty<ExpeditionReservationRecord>();
                return false;
            }
        }

        internal static SuccessfulBuildReservationResult TryConsumeSuccessfulBuild(Player player, Piece piece)
        {
            string pieceKey;
            string ignoredDisplayName;
            if (player == null || !ReferenceEquals(player, Player.m_localPlayer) ||
                !TryGetStablePieceIdentity(piece, out pieceKey, out ignoredDisplayName))
            {
                return SuccessfulBuildReservationResult.NotApplicable;
            }

            try
            {
                if (!_storageHealthy || !EnsureLoaded(player))
                {
                    WarnBuildReservationUnchanged();
                    return SuccessfulBuildReservationResult.PersistenceFailed;
                }
                if (!_state.Records.Any(record => string.Equals(record.PieceKey, pieceKey, StringComparison.Ordinal)))
                {
                    return SuccessfulBuildReservationResult.NotReserved;
                }
                if (!_writesHealthy)
                {
                    WarnBuildReservationUnchanged();
                    return SuccessfulBuildReservationResult.PersistenceFailed;
                }

                ExpeditionReservationState committed;
                ExpeditionReservationPersistenceResult result;
                if (!TryReleaseOneDurably(pieceKey, out committed, out result))
                {
                    if (result == ExpeditionReservationPersistenceResult.NotFound)
                    {
                        return SuccessfulBuildReservationResult.NotReserved;
                    }
                    _writesHealthy = false;
                    LogFailureOnce("A successful build reservation decrement could not be saved and was left unchanged.");
                    WarnBuildReservationUnchanged();
                    return SuccessfulBuildReservationResult.PersistenceFailed;
                }

                _state = committed;
                // The placed unit no longer needs any matching optional gather that has not yet
                // committed inventory movement.
                QuickGrabMaterialsAction.CancelOneMaterialTransfer(pieceKey);
                RequestRefreshAfterDurableChange();
                return SuccessfulBuildReservationResult.Consumed;
            }
            catch (Exception exception)
            {
                _writesHealthy = false;
                LogFailureOnce("A successful build reservation decrement could not be saved and was left unchanged: " + exception.GetType().Name);
                WarnBuildReservationUnchanged();
                return SuccessfulBuildReservationResult.PersistenceFailed;
            }
        }

        internal static bool TryReleaseOne(Player player, string pieceKey)
        {
            try
            {
                if (!_storageHealthy || !_writesHealthy || player == null || string.IsNullOrEmpty(pieceKey) || !EnsureLoaded(player))
                {
                    return false;
                }
                // The explicit icon interaction changes saved intent only. It deliberately neither
                // transfers inventory nor invokes Quick Stack. Release is durable-first: a failed
                // persistence write cannot change the active in-session reservation count.
                ExpeditionReservationState committed;
                ExpeditionReservationPersistenceResult result;
                if (!TryReleaseOneDurably(pieceKey, out committed, out result))
                {
                    if (result != ExpeditionReservationPersistenceResult.NotFound)
                    {
                        _writesHealthy = false;
                        LogFailureOnce("Quick Grab reservation release could not be saved and was left unchanged.");
                    }
                    return false;
                }

                _state = committed;
                // Releasing saved intent also cancels one not-yet-committed optional material
                // gather for that same piece; the remove interaction itself can never move items.
                QuickGrabMaterialsAction.CancelOneMaterialTransfer(pieceKey);
                RequestRefreshAfterDurableChange();
                return true;
            }
            catch (Exception exception)
            {
                DisableStorage("An expedition reservation could not be released safely: " + exception.GetType().Name);
                return false;
            }
        }

        private static bool TryReleaseOneDurably(
            string pieceKey,
            out ExpeditionReservationState committed,
            out ExpeditionReservationPersistenceResult result)
        {
            var previousPayload = _state.Serialize();
            return ExpeditionReservationPersistence.TryReleaseDurably(
                _state,
                pieceKey,
                payload => AtomicReservationFileStore.TryWrite(_loadedKey, previousPayload, payload),
                out committed,
                out result);
        }

        internal static bool TryGetStablePieceIdentity(Piece piece, out string pieceKey, out string displayName)
        {
            pieceKey = null;
            displayName = null;
            if (piece == null || piece.gameObject == null)
            {
                return false;
            }

            var prefabName = (piece.gameObject.name ?? string.Empty).Trim();
            while (prefabName.EndsWith(CloneSuffix, StringComparison.Ordinal))
            {
                prefabName = prefabName.Substring(0, prefabName.Length - CloneSuffix.Length).TrimEnd();
            }
            if (string.IsNullOrWhiteSpace(prefabName))
            {
                return false;
            }

            pieceKey = "prefab:" + prefabName;
            displayName = string.IsNullOrWhiteSpace(piece.m_name) ? prefabName : piece.m_name;
            return true;
        }

        internal static bool TryGetStableItemIdentity(
            ItemDrop.ItemData item,
            out string prefabName,
            out int quality)
        {
            prefabName = null;
            quality = -1;
            if (item == null || item.m_dropPrefab == null)
            {
                return false;
            }

            prefabName = (item.m_dropPrefab.name ?? string.Empty).Trim();
            while (prefabName.EndsWith(CloneSuffix, StringComparison.Ordinal))
            {
                prefabName = prefabName.Substring(0, prefabName.Length - CloneSuffix.Length).TrimEnd();
            }
            if (string.IsNullOrWhiteSpace(prefabName))
            {
                prefabName = null;
                return false;
            }

            quality = item.m_quality;
            return quality >= 0;
        }

        internal static bool TryGetStableRequirements(
            Piece piece,
            out IReadOnlyList<ResourceRequirement> requirements)
        {
            requirements = Array.Empty<ResourceRequirement>();
            if (piece == null)
            {
                return false;
            }

            try
            {
                var stable = new List<ResourceRequirement>();
                foreach (var requirement in piece.m_resources ?? Array.Empty<Piece.Requirement>())
                {
                    if (requirement == null || requirement.m_resItem == null || requirement.m_amount <= 0)
                    {
                        // A mixed valid/malformed live recipe must not be truncated into a
                        // different reservation or partial material request.
                        return false;
                    }
                    var prefab = requirement.m_resItem.gameObject;
                    var prefabName = prefab == null ? string.Empty : (prefab.name ?? string.Empty).Trim();
                    while (prefabName.EndsWith(CloneSuffix, StringComparison.Ordinal))
                    {
                        prefabName = prefabName.Substring(0, prefabName.Length - CloneSuffix.Length).TrimEnd();
                    }
                    if (string.IsNullOrWhiteSpace(prefabName))
                    {
                        return false;
                    }
                    stable.Add(new ResourceRequirement(
                        prefabName,
                        requirement.m_amount,
                        requirement.m_resItem.m_itemData.m_quality));
                }
                requirements = stable;
                return stable.Count > 0;
            }
            catch
            {
                requirements = Array.Empty<ResourceRequirement>();
                return false;
            }
        }

        private static bool EnsureLoaded(Player player)
        {
            if (!_storageHealthy || player == null || ZNet.instance == null)
            {
                return false;
            }

            var key = ExpeditionReservationPreferencePolicy.Key(
                player.GetPlayerID(),
                ZNet.instance.GetWorldUID());
            if (string.Equals(_loadedKey, key, StringComparison.Ordinal))
            {
                return true;
            }

            try
            {
                string raw;
                bool fileExists;
                if (!AtomicReservationFileStore.TryRead(key, out raw, out fileExists))
                {
                    return false;
                }
                if (!fileExists)
                {
                    // One-way compatibility read for reservations created by 1.3.0-1.3.2. The
                    // first later mutation publishes the complete state through the atomic file.
                    raw = PlayerPrefs.GetString(key, string.Empty);
                }
                ExpeditionReservationState parsed;
                if (!ExpeditionReservationState.TryParse(raw, out parsed))
                {
                    DisableStorage("Saved expedition reservations were malformed or unsupported; the saved value was left untouched and reservations were disabled for this session.");
                    return false;
                }
                _state = parsed;
                _loadedKey = key;
                return true;
            }
            catch (Exception exception)
            {
                DisableStorage("Expedition reservations could not be read and were disabled for this session: " + exception.GetType().Name);
                return false;
            }
        }

        private static void RequestRefreshAfterDurableChange()
        {
            try
            {
                // Aggregate targets are read directly from _state; request both the inventory
                // overlays and reservation strip in the same frame after publishing the count.
                InventoryIntegration.RequestExpeditionRefresh();
            }
            catch (Exception exception)
            {
                RuntimeContext.Plugin?.Log.LogWarning(
                    "Expedition reservation UI refresh failed after save: " + exception.GetType().Name);
            }
        }

        private static void WarnBuildReservationUnchanged()
        {
            if (_buildConsumeWarningShown) return;
            _buildConsumeWarningShown = true;
            RuntimeContext.ShowCenter("Stackmaster: the piece was placed, but its reservation could not be updated and was left unchanged.");
        }

        private static void DisableStorage(string message)
        {
            _storageHealthy = false;
            _state = new ExpeditionReservationState();
            _loadedKey = null;
            LogFailureOnce(message);
        }

        private static void LogFailureOnce(string message)
        {
            if (_failureLogged) return;
            _failureLogged = true;
            RuntimeContext.Plugin?.Log.LogWarning(message);
        }
    }
}
