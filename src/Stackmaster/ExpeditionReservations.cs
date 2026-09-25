#nullable disable
using System;
using System.Collections.Generic;
using Stackmaster.Core;
using UnityEngine;

namespace Stackmaster
{
    /// <summary>
    /// Persistent local expedition reservations, scoped to one character and one world. This
    /// foundation records only a successfully committed Quick Grab. It does not hook building,
    /// inventory-icon removal, sorting, or Quick Stack material precedence.
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

        internal static bool RecordSuccessfulQuickGrab(
            Player player,
            string pieceKey,
            string displayName,
            IReadOnlyList<ResourceRequirement> requirements)
        {
            try
            {
                if (!_storageHealthy || player == null || string.IsNullOrEmpty(pieceKey) ||
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
                return _storageHealthy;
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
