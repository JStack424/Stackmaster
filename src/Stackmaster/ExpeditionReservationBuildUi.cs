#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Stackmaster.Core;
using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Stackmaster
{
    /// <summary>Optional mirror of the shared reservation strip above the active hammer build menu.</summary>
    internal static class ExpeditionReservationBuildUi
    {
        private const string RootName = "StackmasterBuildMenuReservations";
        private static ReservationStripView _view;
        private static BuildUi _owner;
        private static Player _player;
        private static string _digest;
        private static bool _refreshRequested = true;
        private static bool _disabled;
        private static bool _failureLogged;

        internal static void Tick()
        {
            if (_disabled) return;
            try
            {
                var player = Player.m_localPlayer;
                var hud = Hud.instance;
                var buildUi = hud != null ? hud.m_buildUi : null;
                if (player == null || buildUi == null || !ReservationCardInteractions.HasActiveHammer(player) ||
                    !Hud.IsPieceSelectionVisible() || !buildUi.gameObject.activeInHierarchy)
                {
                    Hide();
                    return;
                }
                if (!ReferenceEquals(_owner, buildUi) || !ReferenceEquals(_player, player) || _view == null)
                {
                    Attach(buildUi, player);
                }
                RefreshVisible(player);
                UpdateGeometry((RectTransform)buildUi.transform);
            }
            catch (Exception exception)
            {
                Disable("The optional build-menu reservation row stopped safely", exception);
            }
        }

        internal static void RequestRefresh()
        {
            _refreshRequested = true;
            RefreshVisible(Player.m_localPlayer);
        }

        internal static void RefreshVisible(Player player)
        {
            if (_disabled || _view == null || _owner == null || player == null ||
                !ReferenceEquals(player, Player.m_localPlayer) || !ReferenceEquals(player, _player) ||
                !ReferenceEquals(Hud.instance != null ? Hud.instance.m_buildUi : null, _owner) ||
                !_owner.gameObject.activeInHierarchy)
            {
                return;
            }
            IReadOnlyList<ExpeditionReservationRecord> records;
            if (!ExpeditionReservations.TryGetRecords(player, out records) || records.Count == 0)
            {
                _digest = string.Empty;
                _view.SetVisible(false);
                return;
            }
            var digest = Digest(records);
            if (_refreshRequested || !string.Equals(digest, _digest, StringComparison.Ordinal))
            {
                _view.Render(records, pieceKey => ReservationCardInteractions.SelectFromBuildMenu(pieceKey, _owner), ReleaseOne);
                _digest = digest;
                _refreshRequested = false;
            }
            _view.SetVisible(true);
        }

        internal static void RearmSession()
        {
            Destroy();
            _disabled = false;
            _failureLogged = false;
            _refreshRequested = true;
        }

        internal static void Destroy()
        {
            _view?.Destroy();
            _view = null;
            _owner = null;
            _player = null;
            _digest = null;
        }

        private static void Attach(BuildUi buildUi, Player player)
        {
            Destroy();
            var compatibility = CompatibilityGate.EvaluateBuildMenuReservationUi();
            if (!compatibility.IsCompatible)
            {
                Disable("The optional build-menu reservation row is unavailable: " + compatibility.Reason);
                return;
            }
            var menuRect = buildUi.transform as RectTransform;
            if (menuRect == null) throw new InvalidOperationException("BuildUi did not have a RectTransform.");
            var stale = menuRect.Find(RootName);
            if (stale != null)
            {
                stale.gameObject.SetActive(false);
                Object.Destroy(stale.gameObject);
            }
            var template = buildUi.GetComponentsInChildren<TMP_Text>(true)
                .FirstOrDefault(candidate => candidate != null && candidate.font != null);
            _view = new ReservationStripView(menuRect, RootName, template);
            _owner = buildUi;
            _player = player;
            _view.RootRect.anchorMin = _view.RootRect.anchorMax = menuRect.pivot;
            _view.RootRect.pivot = Vector2.zero;
            _view.RootRect.sizeDelta = new Vector2(menuRect.rect.width, BuildMenuReservationLayoutPlanner.Height);
            _view.Root.transform.SetAsLastSibling();
            _refreshRequested = true;
        }

        private static void UpdateGeometry(RectTransform menuRect)
        {
            if (_view == null || menuRect == null) return;
            var corners = new Vector3[4];
            menuRect.GetWorldCorners(corners);
            var scaleX = Mathf.Abs(menuRect.lossyScale.x);
            var scaleY = Mathf.Abs(menuRect.lossyScale.y);
            if (scaleX <= 0.0001f || scaleY <= 0.0001f)
                throw new InvalidOperationException("The build-menu transform had an invalid UI scale.");
            var rect = menuRect.rect;
            var safeLeft = rect.xMin + (Screen.safeArea.xMin - corners[0].x) / scaleX;
            var safeRight = rect.xMin + (Screen.safeArea.xMax - corners[0].x) / scaleX;
            var safeTop = rect.yMin + (Screen.safeArea.yMax - corners[0].y) / scaleY;
            var geometry = BuildMenuReservationLayoutPlanner.Plan(
                rect.xMin, rect.xMax, rect.yMax, safeLeft, safeRight, safeTop);
            if (!geometry.Visible)
            {
                _view.SetVisible(false);
                return;
            }
            _view.RootRect.anchoredPosition = new Vector2(geometry.Left, geometry.Bottom);
            _view.RootRect.sizeDelta = new Vector2(geometry.Width, geometry.Height);
            var inner = ReservationStripLayoutPlanner.Plan(
                geometry.Width, 1f, _view.PreferredLabelWidth);
            _view.ApplyInnerGeometry(geometry.Width, inner.LabelWidth, inner.ViewportLeft, inner.ViewportRight);
        }

        private static void ReleaseOne(string pieceKey, string visibleName)
        {
            try
            {
                ReservationCardInteractions.ReleaseOne(pieceKey, visibleName);
                _refreshRequested = true;
                RefreshVisible(Player.m_localPlayer);
            }
            catch (Exception exception)
            {
                Disable("A build-menu reservation card could not be used safely", exception);
            }
        }

        private static void Hide()
        {
            if (_view != null) _view.SetVisible(false);
            _digest = null;
            _refreshRequested = true;
        }

        private static string Digest(IEnumerable<ExpeditionReservationRecord> records)
        {
            var builder = new StringBuilder();
            foreach (var record in records.OrderBy(value => value.PieceKey, StringComparer.Ordinal))
                builder.Append(record.PieceKey).Append('\u001f').Append(record.DisplayName).Append('\u001f').Append(record.Count).Append('\u001e');
            return builder.ToString();
        }

        private static void Disable(string message, Exception exception = null)
        {
            Destroy();
            _disabled = true;
            if (_failureLogged || RuntimeContext.Plugin == null) return;
            _failureLogged = true;
            RuntimeContext.Plugin.Log.LogWarning(exception == null ? message : message + ": " + exception);
        }
    }
}
