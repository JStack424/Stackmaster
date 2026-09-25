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
    /// Building never mutates this state; only an explicit reservation-icon click releases it.
    /// </summary>
    internal static class ExpeditionReservations
    {
        private const string CloneSuffix = "(Clone)";
        private static ExpeditionReservationState _state = new ExpeditionReservationState();
        private static string _loadedKey;
        private static bool _storageHealthy = true;
        private static bool _writesHealthy = true;
        private static bool _failureLogged;

        internal static void Initialize()
        {
            _state = new ExpeditionReservationState();
            _loadedKey = null;
            _storageHealthy = true;
            _writesHealthy = true;
            _failureLogged = false;
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

                ExpeditionReservationState preview;
                if (!ExpeditionReservationState.TryParse(_state.Serialize(), out preview))
                {
                    failure = "reservation state could not be validated";
                    return false;
                }
                var result = preview.RecordSuccessfulQuickGrab(pieceKey, displayName, requirements);
                if (result == ExpeditionReservationAddResult.RecipeChanged)
                {
                    failure = "the reserved build-piece recipe changed";
                    return false;
                }
                if (result == ExpeditionReservationAddResult.CapacityExceeded)
                {
                    failure = "the safe reservation limit was reached";
                    return false;
                }
                if (result != ExpeditionReservationAddResult.Added &&
                    result != ExpeditionReservationAddResult.Incremented)
                {
                    return false;
                }
                try
                {
                    // Validate the post-add payload, not merely the current one. This keeps an
                    // oversized reservation from being discovered only after item transfer.
                    preview.Serialize();
                }
                catch (InvalidOperationException)
                {
                    failure = "the safe reservation storage limit was reached";
                    return false;
                }
                return true;
            }
            catch (Exception exception)
            {
                DisableStorage("Expedition reservations could not be validated and were disabled for this session: " + exception.GetType().Name);
                failure = "reservations are unavailable";
                return false;
            }
        }

        internal static bool RecordSuccessfulQuickGrab(
            Player player,
            string pieceKey,
            string displayName,
            IReadOnlyList<ResourceRequirement> requirements)
        {
            try
            {
                if (!_storageHealthy || !_writesHealthy || player == null || string.IsNullOrEmpty(pieceKey) ||
                    string.IsNullOrEmpty(displayName) || requirements == null || !EnsureLoaded(player))
                {
                    if (player != null && (string.IsNullOrEmpty(pieceKey) || string.IsNullOrEmpty(displayName)))
                    {
                        LogFailureOnce("A successful Quick Grab could not be reserved because its captured build-piece identity was unavailable.");
                    }
                    return false;
                }

                var result = _state.RecordSuccessfulQuickGrab(pieceKey, displayName, requirements);
                if (result == ExpeditionReservationAddResult.RecipeChanged)
                {
                    LogFailureOnce("A successful Quick Grab used a changed recipe for an already reserved piece; the existing reservation was preserved unchanged.");
                    return false;
                }
                if (result == ExpeditionReservationAddResult.CapacityExceeded)
                {
                    LogFailureOnce("A successful Quick Grab exceeded the safe reservation limit; existing reservations were preserved unchanged.");
                    return false;
                }

                Save();
                InventoryIntegration.RequestExpeditionRefresh();
                return _storageHealthy && _writesHealthy;
            }
            catch (Exception exception)
            {
                DisableStorage("Expedition reservations could not be updated and were disabled for this session: " + exception.GetType().Name);
                return false;
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

        internal static bool TryReleaseOne(Player player, string pieceKey)
        {
            try
            {
                if (!_storageHealthy || !_writesHealthy || player == null || string.IsNullOrEmpty(pieceKey) || !EnsureLoaded(player))
                {
                    return false;
                }
                // The settled interaction changes saved intent only. It deliberately neither
                // transfers inventory nor invokes Quick Stack. Release is durable-first: a failed
                // persistence write must not change the active in-session reservation count.
                var previousPayload = _state.Serialize();
                ExpeditionReservationState candidate;
                if (!ExpeditionReservationState.TryParse(previousPayload, out candidate))
                {
                    throw new InvalidOperationException("The active reservation state could not be cloned safely.");
                }
                if (!candidate.TryReleaseReservation(pieceKey, 1))
                {
                    return false;
                }

                try
                {
                    PlayerPrefs.SetString(_loadedKey, candidate.Serialize());
                    PlayerPrefs.Save();
                }
                catch (Exception exception)
                {
                    _writesHealthy = false;
                    try
                    {
                        // Restore the PlayerPrefs memory cache as well as the runtime state. A
                        // best-effort second flush prevents a partially completed first save from
                        // becoming the durable value later in the session.
                        PlayerPrefs.SetString(_loadedKey, previousPayload);
                        PlayerPrefs.Save();
                    }
                    catch (Exception rollbackException)
                    {
                        RuntimeContext.Plugin?.Log.LogError(
                            "Quick Grab reservation release rollback could not be flushed: " + rollbackException);
                    }
                    LogFailureOnce("Quick Grab reservation release could not be saved and was left unchanged: " + exception.GetType().Name);
                    return false;
                }

                _state = candidate;
                InventoryIntegration.RequestExpeditionRefresh();
                return true;
            }
            catch (Exception exception)
            {
                DisableStorage("An expedition reservation could not be released safely: " + exception.GetType().Name);
                return false;
            }
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
                        continue;
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
                var raw = PlayerPrefs.GetString(key, string.Empty);
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

        private static void Save()
        {
            if (!_storageHealthy || string.IsNullOrEmpty(_loadedKey))
            {
                throw new InvalidOperationException("Expedition reservation storage is unavailable.");
            }
            if (!_writesHealthy)
            {
                return;
            }

            try
            {
                PlayerPrefs.SetString(_loadedKey, _state.Serialize());
                PlayerPrefs.Save();
            }
            catch (Exception exception)
            {
                // The Quick Grab already committed. Keep its reservation in session memory and
                // stop further durable writes rather than erasing or rolling back unrelated state.
                _writesHealthy = false;
                LogFailureOnce("Expedition reservations could not be saved; the in-session reservation was retained but durable writes were disabled: " + exception.GetType().Name);
            }
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
