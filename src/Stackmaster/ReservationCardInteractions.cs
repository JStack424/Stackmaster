#nullable disable
using System;
using UnityEngine;

namespace Stackmaster
{
    /// <summary>Shared fail-closed selection and one-count release policy for both strip views.</summary>
    internal static class ReservationCardInteractions
    {
        private const string HammerPrefabName = "Hammer";
        private static bool _selectionFailureLogged;

        internal static bool ReleaseOne(string pieceKey, string visibleName)
        {
            var player = Player.m_localPlayer;
            if (player == null) return false;
            if (!ExpeditionReservations.TryReleaseOne(player, pieceKey))
            {
                RuntimeContext.ShowCenter("Stackmaster could not save that reservation release; it was left unchanged.");
                return false;
            }
            RuntimeContext.ShowTopLeft("Stackmaster: released 1 × " + visibleName + " reservation.");
            return true;
        }

        internal static void SelectFromInventory(string pieceKey)
        {
            try
            {
                var player = Player.m_localPlayer;
                Piece piece;
                if (!TryResolveCurrentPiece(player, pieceKey, requireActiveHammer: true, out piece)) return;
                // Player.SetSelectedPiece is Valheim's current PieceTable path. It resolves the available
                // category/index and sets up the ordinary placement ghost without equipping any tool.
                player.SetSelectedPiece(piece);
            }
            catch (Exception exception)
            {
                LogSelectionFailureOnce(exception);
            }
        }

        internal static bool SelectFromBuildMenu(string pieceKey, BuildUi expectedOwner)
        {
            try
            {
                var player = Player.m_localPlayer;
                var hud = Hud.instance;
                if (player == null || hud == null || expectedOwner == null ||
                    !ReferenceEquals(hud.m_buildUi, expectedOwner) || !Hud.IsPieceSelectionVisible()) return false;
                Piece piece;
                if (!TryResolveCurrentPiece(player, pieceKey, requireActiveHammer: true, out piece)) return false;
                // Preserve Valheim's exact BuildUi selection, sound, touch, menu-close, and placement
                // transition. The scoped bypass skips only Stackmaster's modifier-click reservation prefix.
                QuickGrabMaterialsAction.SelectReservationCard(expectedOwner, piece);
                return true;
            }
            catch (Exception exception)
            {
                LogSelectionFailureOnce(exception);
                return false;
            }
        }

        internal static bool TryResolveCurrentPiece(
            Player player,
            string pieceKey,
            bool requireActiveHammer,
            out Piece piece)
        {
            piece = null;
            if (player == null || !ReferenceEquals(player, Player.m_localPlayer) ||
                !ReservationStripView.TryPrefabName(pieceKey, out var prefabName)) return false;

            var pieceTable = player.GetBuildTool();
            if (pieceTable == null) return false;
            if (requireActiveHammer && !HasActiveHammer(player, pieceTable)) return false;

            foreach (var candidateObject in pieceTable.m_pieces ?? new System.Collections.Generic.List<GameObject>())
            {
                if (candidateObject == null || !string.Equals(Utils.GetPrefabName(candidateObject), prefabName, StringComparison.Ordinal))
                    continue;
                var candidate = candidateObject.GetComponent<Piece>();
                Vector2Int index;
                Piece.PieceCategory category;
                string resolvedKey;
                string ignoredName;
                if (candidate == null ||
                    !pieceTable.GetPieceIndex(candidate, out index, out category) ||
                    !pieceTable.IsPieceAvailable(candidate) ||
                    !player.IsPieceAvailable(candidate) ||
                    !ExpeditionReservations.TryGetStablePieceIdentity(candidate, out resolvedKey, out ignoredName) ||
                    !string.Equals(resolvedKey, pieceKey, StringComparison.Ordinal))
                {
                    return false;
                }
                piece = candidate;
                return true;
            }
            return false;
        }

        internal static bool HasActiveHammer(Player player)
        {
            if (player == null) return false;
            var pieceTable = player.GetBuildTool();
            return pieceTable != null && HasActiveHammer(player, pieceTable);
        }

        private static bool HasActiveHammer(Player player, PieceTable pieceTable)
        {
            var rightItem = player.RightItem;
            return rightItem != null && rightItem.m_dropPrefab != null && rightItem.m_shared != null &&
                   string.Equals(Utils.GetPrefabName(rightItem.m_dropPrefab), HammerPrefabName, StringComparison.Ordinal) &&
                   ReferenceEquals(rightItem.m_shared.m_buildPieces, pieceTable);
        }
        private static void LogSelectionFailureOnce(Exception exception)
        {
            if (_selectionFailureLogged || RuntimeContext.Plugin == null) return;
            _selectionFailureLogged = true;
            RuntimeContext.Plugin.Log.LogWarning(
                "A reservation card could not select its piece safely; reservations were left unchanged: " +
                exception.GetType().Name);
        }

    }
}
