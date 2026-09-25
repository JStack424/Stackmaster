#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using Stackmaster.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Stackmaster
{
    /// <summary>
    /// Optional, read-mostly inventory UI for persistent build-piece reservations. A click changes
    /// only the saved reservation count; it never starts Quick Stack or mutates an inventory.
    /// Every setup/refresh failure removes this optional surface without affecting reservation or
    /// item-state behavior.
    /// </summary>
    internal static class ExpeditionReservationUi
    {
        private const string RootName = "StackmasterExpeditionReservations";
        private const string PiecePrefix = "prefab:";
        private static readonly Color PanelColor = new Color(0.045f, 0.075f, 0.09f, 0.94f);
        private static readonly Color Orange = new Color(1f, 0.48f, 0.08f, 1f);
        private static GameObject _root;
        private static RectTransform _content;
        private static TMP_Text _label;
        private static InventoryGui _gui;
        private static bool _disabled;
        private static bool _failureLogged;

        internal static void Ensure(InventoryGui gui)
        {
            if (_disabled || _root != null || gui == null || gui.m_player == null) return;
            try
            {
                _gui = gui;
                _root = new GameObject(RootName, typeof(RectTransform), typeof(Image));
                _root.transform.SetParent(gui.m_player, false);
                _root.transform.SetAsLastSibling();
                var rootRect = (RectTransform)_root.transform;
                rootRect.anchorMin = new Vector2(0f, 1f);
                rootRect.anchorMax = new Vector2(1f, 1f);
                rootRect.pivot = new Vector2(0.5f, 0f);
                rootRect.anchoredPosition = new Vector2(0f, 8f);
                rootRect.sizeDelta = new Vector2(0f, 54f);
                var panel = _root.GetComponent<Image>();
                panel.color = PanelColor;
                panel.raycastTarget = false;
                var outline = _root.AddComponent<Outline>();
                outline.effectColor = new Color(1f, 0.48f, 0.08f, 0.72f);
                outline.effectDistance = new Vector2(1f, -1f);

                var labelObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
                labelObject.transform.SetParent(_root.transform, false);
                _label = labelObject.GetComponent<TextMeshProUGUI>();
                CopyTextStyle(gui, _label);
                _label.text = "Reserved";
                _label.fontSize = Mathf.Max(13f, _label.fontSize * 0.82f);
                _label.alignment = TextAlignmentOptions.MidlineLeft;
                _label.textWrappingMode = TextWrappingModes.NoWrap;
                _label.color = Orange;
                _label.raycastTarget = false;
                var labelRect = _label.rectTransform;
                labelRect.anchorMin = new Vector2(0f, 0f);
                labelRect.anchorMax = new Vector2(0f, 1f);
                labelRect.pivot = new Vector2(0f, 0.5f);
                labelRect.anchoredPosition = new Vector2(8f, 0f);
                labelRect.sizeDelta = new Vector2(70f, 0f);

                var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
                viewport.transform.SetParent(_root.transform, false);
                var viewportRect = (RectTransform)viewport.transform;
                viewportRect.anchorMin = Vector2.zero;
                viewportRect.anchorMax = Vector2.one;
                viewportRect.offsetMin = new Vector2(74f, 3f);
                viewportRect.offsetMax = new Vector2(-4f, -3f);

                var contentObject = new GameObject(
                    "Content",
                    typeof(RectTransform),
                    typeof(HorizontalLayoutGroup),
                    typeof(ContentSizeFitter));
                contentObject.transform.SetParent(viewport.transform, false);
                _content = (RectTransform)contentObject.transform;
                _content.anchorMin = new Vector2(0f, 0f);
                _content.anchorMax = new Vector2(0f, 1f);
                _content.pivot = new Vector2(0f, 0.5f);
                _content.anchoredPosition = Vector2.zero;
                _content.sizeDelta = new Vector2(0f, 0f);
                var layout = contentObject.GetComponent<HorizontalLayoutGroup>();
                layout.spacing = 4f;
                layout.childControlWidth = false;
                layout.childControlHeight = false;
                layout.childForceExpandWidth = false;
                layout.childForceExpandHeight = false;
                var fitter = contentObject.GetComponent<ContentSizeFitter>();
                fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
                fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;

                var scroll = _root.AddComponent<ScrollRect>();
                scroll.viewport = viewportRect;
                scroll.content = _content;
                scroll.horizontal = true;
                scroll.vertical = false;
                scroll.movementType = ScrollRect.MovementType.Clamped;
                scroll.inertia = false;
                scroll.scrollSensitivity = 24f;

                _root.SetActive(false);
            }
            catch (Exception exception)
            {
                Disable("Reservation icons could not be created safely", exception);
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
                if (_root == null || _content == null) return;

                IReadOnlyList<ExpeditionReservationRecord> records;
                if (!ExpeditionReservations.TryGetRecords(player, out records) || records.Count == 0)
                {
                    ClearButtons();
                    _root.SetActive(false);
                    return;
                }

                ClearButtons();
                foreach (var record in records.OrderBy(value => value.DisplayName, StringComparer.OrdinalIgnoreCase)
                             .ThenBy(value => value.PieceKey, StringComparer.Ordinal))
                {
                    CreateButton(record);
                }
                _root.SetActive(true);
                LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
            }
            catch (Exception exception)
            {
                Disable("Reservation icons could not be refreshed safely", exception);
            }
        }

        internal static void Hide()
        {
            if (_root != null) _root.SetActive(false);
        }

        internal static void RearmSession()
        {
            Destroy();
            _disabled = false;
            _failureLogged = false;
        }

        internal static void Destroy()
        {
            if (_root != null) Object.Destroy(_root);
            _root = null;
            _content = null;
            _label = null;
            _gui = null;
        }

        private static void CreateButton(ExpeditionReservationRecord record)
        {
            var buttonObject = new GameObject(
                "Reservation-" + SafeObjectName(record.PieceKey),
                typeof(RectTransform),
                typeof(LayoutElement),
                typeof(Image),
                typeof(Button));
            buttonObject.transform.SetParent(_content, false);
            var rect = (RectTransform)buttonObject.transform;
            rect.sizeDelta = new Vector2(46f, 46f);
            var layout = buttonObject.GetComponent<LayoutElement>();
            layout.preferredWidth = 46f;
            layout.preferredHeight = 46f;
            layout.minWidth = 46f;
            layout.minHeight = 46f;
            var background = buttonObject.GetComponent<Image>();
            background.color = new Color(0.08f, 0.115f, 0.13f, 0.98f);
            background.raycastTarget = true;
            var outline = buttonObject.AddComponent<Outline>();
            outline.effectColor = Orange;
            outline.effectDistance = new Vector2(2f, -2f);

            var iconObject = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            iconObject.transform.SetParent(buttonObject.transform, false);
            var icon = iconObject.GetComponent<Image>();
            icon.raycastTarget = false;
            var iconRect = icon.rectTransform;
            iconRect.anchorMin = new Vector2(0.5f, 0.5f);
            iconRect.anchorMax = new Vector2(0.5f, 0.5f);
            iconRect.pivot = new Vector2(0.5f, 0.5f);
            iconRect.anchoredPosition = Vector2.zero;
            iconRect.sizeDelta = new Vector2(38f, 38f);
            Sprite sprite;
            if (TryResolvePieceIcon(record.PieceKey, out sprite))
            {
                icon.sprite = sprite;
                icon.color = Color.white;
                icon.preserveAspect = true;
            }
            else
            {
                icon.color = new Color(1f, 0.48f, 0.08f, 0.22f);
                CreateText(buttonObject.transform, "Missing", "?", TextAlignmentOptions.Center, 24f,
                    new Vector2(0f, 0f), Vector2.one, Vector2.zero, Vector2.zero);
            }

            var count = CreateText(buttonObject.transform, "Count", record.Count.ToString(),
                TextAlignmentOptions.BottomRight, 15f,
                Vector2.zero, Vector2.one, new Vector2(2f, 1f), new Vector2(-3f, -2f));
            count.color = Orange;
            count.outlineColor = new Color32(45, 15, 0, 255);
            count.outlineWidth = 0.25f;

            var button = buttonObject.GetComponent<Button>();
            button.targetGraphic = background;
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 0.82f, 0.64f, 1f);
            colors.pressedColor = new Color(1f, 0.58f, 0.28f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(1f, 1f, 1f, 0.35f);
            colors.fadeDuration = 0.08f;
            button.colors = colors;

            var pieceKey = record.PieceKey;
            var visibleName = Localize(record.DisplayName);
            button.onClick.AddListener(() => ReleaseOne(pieceKey, visibleName));
        }

        private static void ReleaseOne(string pieceKey, string visibleName)
        {
            try
            {
                var player = Player.m_localPlayer;
                if (player == null) return;
                if (!ExpeditionReservations.TryReleaseOne(player, pieceKey))
                {
                    Refresh(player);
                    RuntimeContext.ShowCenter("Stackmaster could not save that reservation release; it was left unchanged.");
                    return;
                }
                InventoryIntegration.RequestExpeditionRefresh();
                Refresh(player);
                RuntimeContext.ShowTopLeft("Stackmaster: released 1 × " + visibleName + " reservation.");
            }
            catch (Exception exception)
            {
                Disable("A reservation icon could not be used safely", exception);
            }
        }

        private static bool TryResolvePieceIcon(string pieceKey, out Sprite sprite)
        {
            sprite = null;
            if (string.IsNullOrEmpty(pieceKey) || !pieceKey.StartsWith(PiecePrefix, StringComparison.Ordinal) ||
                ZNetScene.instance == null)
            {
                return false;
            }
            var prefabName = pieceKey.Substring(PiecePrefix.Length);
            if (string.IsNullOrEmpty(prefabName)) return false;
            var prefab = ZNetScene.instance.GetPrefab(prefabName);
            var piece = prefab != null ? prefab.GetComponent<Piece>() : null;
            sprite = piece != null ? piece.m_icon : null;
            return sprite != null;
        }

        private static TMP_Text CreateText(
            Transform parent,
            string name,
            string text,
            TextAlignmentOptions alignment,
            float fontSize,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 offsetMin,
            Vector2 offsetMax)
        {
            var gameObject = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            gameObject.transform.SetParent(parent, false);
            var label = gameObject.GetComponent<TextMeshProUGUI>();
            CopyTextStyle(_gui, label);
            label.text = text;
            label.alignment = alignment;
            label.fontSize = fontSize;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.color = Color.white;
            label.raycastTarget = false;
            var rect = label.rectTransform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            return label;
        }

        private static void CopyTextStyle(InventoryGui gui, TMP_Text target)
        {
            if (gui == null || target == null) return;
            var template = gui.m_player != null
                ? gui.m_player.GetComponentsInChildren<TMP_Text>(true)
                    .FirstOrDefault(candidate => candidate != target && candidate.font != null)
                : null;
            if (template == null) return;
            target.font = template.font;
            target.fontSharedMaterial = template.fontSharedMaterial;
        }

        private static string Localize(string text)
            => Localization.instance != null ? Localization.instance.Localize(text ?? string.Empty) : text ?? string.Empty;

        private static string SafeObjectName(string value)
            => new string((value ?? string.Empty).Where(character => char.IsLetterOrDigit(character) || character == '-' || character == '_').Take(48).ToArray());

        private static void ClearButtons()
        {
            if (_content == null) return;
            for (var index = _content.childCount - 1; index >= 0; index--)
            {
                Object.Destroy(_content.GetChild(index).gameObject);
            }
        }

        private static void Disable(string message, Exception exception)
        {
            Destroy();
            _disabled = true;
            if (_failureLogged || RuntimeContext.Plugin == null) return;
            _failureLogged = true;
            RuntimeContext.Plugin.Log.LogWarning(message + ": " + exception);
        }
    }
}
