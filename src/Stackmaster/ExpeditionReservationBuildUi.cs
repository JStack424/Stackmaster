#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
        private static readonly FieldInfo TabContainerField = typeof(BuildUi).GetField(
            "m_tabContainer", BindingFlags.Instance | BindingFlags.NonPublic);
        private static ReservationStripView _view;
        private static BuildUi _owner;
        private static Player _player;
        private static RectTransform _menuRect;
        private static RectTransform _hostRect;
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
                if (!ReferenceEquals(_owner, buildUi) || !ReferenceEquals(_player, player) || _view == null ||
                    _menuRect == null || _hostRect == null)
                {
                    Attach(buildUi, player);
                }
                _view.Root.transform.SetAsLastSibling();
                RefreshVisible(player);
                UpdateGeometry(_menuRect, _hostRect);
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
                !ReferenceEquals(Hud.instance != null ? Hud.instance.m_buildUi : null, _owner))
            {
                return;
            }
            if (!_owner.gameObject.activeInHierarchy || !Hud.IsPieceSelectionVisible() ||
                !ReservationCardInteractions.HasActiveHammer(player))
            {
                Hide();
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
                _view.Render(records, SelectPiece, ReleaseOne);
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
            _menuRect = null;
            _hostRect = null;
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
            var menuRect = TabContainerField?.GetValue(buildUi) as RectTransform;
            if (menuRect == null) throw new InvalidOperationException("BuildUi tab container is unavailable.");
            var hostRect = buildUi.transform.parent as RectTransform;
            if (hostRect == null) throw new InvalidOperationException("BuildUi parent UI root is unavailable.");

            var stale = hostRect.Find(RootName);
            if (stale != null)
            {
                stale.gameObject.SetActive(false);
                Object.Destroy(stale.gameObject);
            }
            var legacy = buildUi.transform.Find(RootName);
            if (legacy != null)
            {
                legacy.gameObject.SetActive(false);
                Object.Destroy(legacy.gameObject);
            }
            var template = buildUi.GetComponentsInChildren<TMP_Text>(true)
                .FirstOrDefault(candidate => candidate != null && candidate.font != null);
            _view = new ReservationStripView(hostRect, RootName, template);
            _owner = buildUi;
            _player = player;
            _menuRect = menuRect;
            _hostRect = hostRect;
            _view.RootRect.anchorMin = _view.RootRect.anchorMax = hostRect.pivot;
            _view.RootRect.pivot = Vector2.zero;
            _view.RootRect.sizeDelta = new Vector2(menuRect.rect.width, BuildMenuReservationLayoutPlanner.Height);
            _view.Root.transform.SetAsLastSibling();
            _refreshRequested = true;
        }

        private static void UpdateGeometry(RectTransform menuRect, RectTransform hostRect)
        {
            if (_view == null || menuRect == null || hostRect == null) return;

            // BuildUi is the activation/input root, not a reliable visible-panel bound. m_tabContainer
            // is the top edge of the Categories / Materials / Recent / Favorites panel, so anchor to it and
            // express the result in BuildUi's parent where the row cannot be clipped by BuildUi.
            var corners = new Vector3[4];
            menuRect.GetWorldCorners(corners);
            var menuLeft = float.PositiveInfinity;
            var menuRight = float.NegativeInfinity;
            var menuTop = float.NegativeInfinity;
            foreach (var corner in corners)
            {
                var local = hostRect.InverseTransformPoint(corner);
                menuLeft = Mathf.Min(menuLeft, local.x);
                menuRight = Mathf.Max(menuRight, local.x);
                menuTop = Mathf.Max(menuTop, local.y);
            }

            var hostCorners = new Vector3[4];
            hostRect.GetWorldCorners(hostCorners);
            var scaleX = Mathf.Abs(hostRect.lossyScale.x);
            var scaleY = Mathf.Abs(hostRect.lossyScale.y);
            if (scaleX <= 0.0001f || scaleY <= 0.0001f)
                throw new InvalidOperationException("The build-menu canvas root had an invalid UI scale.");
            var safeArea = Screen.safeArea;
            var hostBounds = hostRect.rect;
            var safeLeft = hostBounds.xMin + (safeArea.xMin - hostCorners[0].x) / scaleX;
            var safeRight = hostBounds.xMin + (safeArea.xMax - hostCorners[0].x) / scaleX;
            var safeTop = hostBounds.yMin + (safeArea.yMax - hostCorners[0].y) / scaleY;
            var geometry = BuildMenuReservationLayoutPlanner.Plan(
                menuLeft, menuRight, menuTop, safeLeft, safeRight, safeTop);
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

        private static void SelectPiece(string pieceKey)
        {
            if (!ReservationCardInteractions.SelectFromBuildMenu(pieceKey, _owner)) return;
            // The row lives beside BuildUi on the canvas to avoid menu clipping, so hide it in
            // the same click frame while BuildUi.OnSelectPiece completes the normal menu close.
            Hide();
        }

        private static void ReleaseOne(string pieceKey, string visibleName)
        {
            try
            {
                ReservationCardInteractions.ReleaseOne(pieceKey, visibleName);
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
