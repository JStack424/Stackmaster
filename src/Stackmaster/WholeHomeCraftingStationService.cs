#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using Stackmaster.Core;
using UnityEngine;

namespace Stackmaster
{
    internal static class WholeHomeCraftingStationService
    {
        internal static bool HasBuildStation(Player player, CraftingStation requiredStation)
        {
            if (player == null || requiredStation == null) return false;
            if (CraftingStation.HaveBuildStationInRange(requiredStation.m_name, player.transform.position) != null)
            {
                return true;
            }
            var plugin = RuntimeContext.Plugin;
            if (plugin == null || !plugin.WholeHomeCraftingStationsEnabled.Value) return false;
            return HasBuildStation(player, requiredStation, player.transform.position, false);
        }

        internal static bool HasBuildStationForPlacement(Player player, CraftingStation requiredStation, Vector3 placementPosition)
        {
            if (player == null || requiredStation == null) return false;

            // Never narrow a placement that vanilla authorized from the player's own position.
            if (CraftingStation.HaveBuildStationInRange(requiredStation.m_name, player.transform.position) != null)
            {
                return true;
            }
            var plugin = RuntimeContext.Plugin;
            if (plugin == null || !plugin.WholeHomeCraftingStationsEnabled.Value) return false;
            return HasBuildStation(player, requiredStation, placementPosition, true);
        }

        private static bool HasBuildStation(
            Player player,
            CraftingStation requiredStation,
            Vector3 requiredPosition,
            bool requirePositionInPlayerMesh)
        {
            if (string.IsNullOrEmpty(requiredStation.m_name) || requiredStation.gameObject == null) return false;

            var expectedPrefabName = Utils.GetPrefabName(requiredStation.gameObject);
            if (string.IsNullOrEmpty(expectedPrefabName)) return false;
            var expectedPrefabHash = expectedPrefabName.GetStableHashCode();

            var scope = StorageScopeProvider.Resolve(player);
            if (scope == null || scope.Kind != StorageScopeKind.WorkbenchMesh) return false;
            if (requirePositionInPlayerMesh && !scope.Contains(requiredPosition)) return false;

            var stations = CraftingStation.Instances;
            if (stations == null) return false;

            var seen = new HashSet<ZDOID>();
            var found = false;
            foreach (var station in stations.OfType<CraftingStation>().ToArray())
            {
                if (station == null || station.gameObject == null || !station.isActiveAndEnabled ||
                    !station.gameObject.activeInHierarchy || station.GetType() != requiredStation.GetType() ||
                    !string.Equals(station.m_name, requiredStation.m_name, StringComparison.Ordinal))
                {
                    continue;
                }

                var view = station.GetComponent<ZNetView>();
                var zdo = view != null && view.IsValid() ? view.GetZDO() : null;
                if (zdo == null || zdo.m_uid.IsNone() || zdo.GetPrefab() != expectedPrefabHash) continue;
                if (!seen.Add(zdo.m_uid)) return false;

                var stationPosition = station.transform.position;
                if (WholeHomeStationCoveragePolicy.Qualifies(
                        scope.Plan,
                        new ScopePoint(stationPosition.x, stationPosition.y, stationPosition.z),
                        true,
                        true))
                {
                    found = true;
                }
            }
            return found;
        }
    }

    internal static class WholeHomePlacementGuardContext
    {
        private static Player _player;
        private static Piece _piece;

        internal static void Begin(Player player, Piece piece, bool noPlacementCost)
        {
            Clear();
            var plugin = RuntimeContext.Plugin;
            if (plugin == null || !RuntimeContext.Compatibility.IsCompatible ||
                !plugin.WholeHomeCraftingStationsEnabled.Value || noPlacementCost ||
                player == null || piece == null || piece.m_craftingStation == null ||
                (ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey(piece.FreeBuildKey())) ||
                CraftingStation.HaveBuildStationInRange(piece.m_craftingStation.m_name, player.transform.position) != null)
            {
                return;
            }

            _player = player;
            _piece = piece;
        }

        internal static void ValidateRefreshedGhost(
            Player player,
            GameObject placementGhost,
            ref Player.PlacementStatus placementStatus)
        {
            if (!ReferenceEquals(player, _player) || _piece == null || placementStatus != Player.PlacementStatus.Valid)
            {
                return;
            }

            var valid = false;
            if (placementGhost != null && placementGhost.activeInHierarchy)
            {
                var ghostPiece = placementGhost.GetComponent<Piece>();
                string expectedKey;
                string expectedName;
                string ghostKey;
                string ghostName;
                valid = ghostPiece != null &&
                        ExpeditionReservations.TryGetStablePieceIdentity(_piece, out expectedKey, out expectedName) &&
                        ExpeditionReservations.TryGetStablePieceIdentity(ghostPiece, out ghostKey, out ghostName) &&
                        string.Equals(expectedKey, ghostKey, StringComparison.Ordinal) &&
                        WholeHomeCraftingStationService.HasBuildStationForPlacement(
                            player,
                            _piece.m_craftingStation,
                            placementGhost.transform.position);
            }

            if (valid) return;
            placementStatus = Player.PlacementStatus.Invalid;
            RuntimeContext.ShowCenter("$msg_missingstation");
        }

        internal static void Clear()
        {
            _player = null;
            _piece = null;
        }
    }

    internal static class WholeHomePlacementGhostPatch
    {
        private static void Postfix(
            Player __instance,
            GameObject ___m_placementGhost,
            ref Player.PlacementStatus ___m_placementStatus)
        {
            WholeHomePlacementGuardContext.ValidateRefreshedGhost(
                __instance,
                ___m_placementGhost,
                ref ___m_placementStatus);
        }
    }
}
