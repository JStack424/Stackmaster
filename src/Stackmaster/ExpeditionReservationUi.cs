#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using Stackmaster.Core;
using TMPro;
using UnityEngine;

namespace Stackmaster
{
    /// <summary>Optional inventory controller for the shared reservation-card strip.</summary>
    internal static class ExpeditionReservationUi
    {
        private const string RootName = "StackmasterExpeditionReservations";
        private static ReservationStripView _view;
        private static InventoryGui _gui;
        private static bool _disabled;
        private static bool _failureLogged;

        internal static void Ensure(InventoryGui gui)
        {
            if (_disabled || _view != null || gui == null || gui.m_player == null) return;
            try
            {
                var compatibility = CompatibilityGate.EvaluateExpeditionReservationUi();
                if (!compatibility.IsCompatible)
                {
                    Disable("Reservation cards are unavailable because the optional UI contract changed: " + compatibility.Reason);
                    return;
                }
                _gui = gui;
                var template = gui.m_player.GetComponentsInChildren<TMP_Text>(true)
                    .FirstOrDefault(candidate => candidate != null && candidate.font != null);
                _view = new ReservationStripView(gui.m_player, RootName, template);
                var rootRect = _view.RootRect;
                rootRect.anchorMin = rootRect.anchorMax = new Vector2(1f, 1f);
                rootRect.pivot = new Vector2(0f, 1f);
                rootRect.sizeDelta = new Vector2(120f, ReservationStripLayoutPlanner.RootHeight);
                UpdateGeometry(gui);
            }
            catch (Exception exception)
            {
                Disable("Reservation cards could not be created safely", exception);
            }
        }

        internal static void Refresh(Player player)
        {
            if (_disabled) return;
            try
            {
                var gui = InventoryGui.instance;
                if (gui == null || player == null || !InventoryGui.IsVisible())
                {
                    Hide();
                    return;
                }
                Ensure(gui);
                if (_view == null) return;
                IReadOnlyList<ExpeditionReservationRecord> records;
                if (!ExpeditionReservations.TryGetRecords(player, out records) || records.Count == 0)
                {
                    _view.SetVisible(false);
                    return;
                }
                UpdateGeometry(gui);
                _view.Render(records, ReservationCardInteractions.SelectFromInventory, ReleaseOne);
                _view.SetVisible(true);
            }
            catch (Exception exception)
            {
                Disable("Reservation cards could not be refreshed safely", exception);
            }
        }

        internal static void Hide()
        {
            if (_view != null) _view.SetVisible(false);
        }

        internal static void RearmSession()
        {
            Destroy();
            _disabled = false;
            _failureLogged = false;
        }

        internal static void Destroy()
        {
            _view?.Destroy();
            _view = null;
            _gui = null;
        }

        private static void UpdateGeometry(InventoryGui gui)
        {
            if (_view == null || gui == null || gui.m_player == null) return;
            var playerRect = gui.m_player as RectTransform;
            if (playerRect == null) return;
            var corners = new Vector3[4];
            playerRect.GetWorldCorners(corners);
            var scaleX = Mathf.Abs(playerRect.lossyScale.x);
            if (scaleX <= 0.0001f) return;
            var geometry = ReservationStripLayoutPlanner.Plan(
                Mathf.Max(0f, Screen.safeArea.xMax - corners[2].x), scaleX, _view.PreferredLabelWidth);
            _view.RootRect.anchoredPosition = new Vector2(geometry.RootOffsetX, geometry.RootOffsetY);
            _view.RootRect.sizeDelta = new Vector2(geometry.RootWidth, ReservationStripLayoutPlanner.RootHeight);
            _view.ApplyInnerGeometry(geometry.RootWidth, geometry.LabelWidth, geometry.ViewportLeft, geometry.ViewportRight);
        }

        private static void ReleaseOne(string pieceKey, string visibleName)
        {
            try
            {
                ReservationCardInteractions.ReleaseOne(pieceKey, visibleName);
                Refresh(Player.m_localPlayer);
            }
            catch (Exception exception)
            {
                Disable("A reservation card could not be used safely", exception);
            }
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
