#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using Stackmaster.Core;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Stackmaster
{
    /// <summary>One shared reservation-card presenter for inventory and hammer-menu strips.</summary>
    internal sealed class ReservationStripView
    {
        private const string PiecePrefix = "prefab:";
        private static readonly Color PanelColor = new Color(0.045f, 0.075f, 0.09f, 0.94f);
        private static readonly Color Orange = new Color(1f, 0.48f, 0.08f, 1f);
        private readonly TMP_Text _textTemplate;
        private readonly RectTransform _content;
        private readonly TMP_Text _label;

        internal ReservationStripView(Transform parent, string rootName, TMP_Text textTemplate)
        {
            if (parent == null) throw new ArgumentNullException(nameof(parent));
            _textTemplate = textTemplate;
            Root = new GameObject(rootName, typeof(RectTransform), typeof(Image));
            Root.transform.SetParent(parent, false);
            Root.transform.SetAsLastSibling();
            RootRect = (RectTransform)Root.transform;
            var panel = Root.GetComponent<Image>();
            panel.color = PanelColor;
            panel.raycastTarget = false;
            var outline = Root.AddComponent<Outline>();
            outline.effectColor = new Color(1f, 0.48f, 0.08f, 0.72f);
            outline.effectDistance = new Vector2(1f, -1f);

            _label = CreateText(Root.transform, "Label", "Reserved", TextAlignmentOptions.MidlineLeft, 13f);
            _label.color = Orange;
            var labelRect = _label.rectTransform;
            labelRect.anchorMin = new Vector2(0f, 0f);
            labelRect.anchorMax = new Vector2(0f, 1f);
            labelRect.pivot = new Vector2(0f, 0.5f);
            labelRect.anchoredPosition = new Vector2(ReservationStripLayoutPlanner.LabelLeftInset, 0f);
            labelRect.sizeDelta = Vector2.zero;

            var viewportObject = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewportObject.transform.SetParent(Root.transform, false);
            Viewport = (RectTransform)viewportObject.transform;
            Viewport.anchorMin = Vector2.zero;
            Viewport.anchorMax = Vector2.one;

            var contentObject = new GameObject("Content", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
            contentObject.transform.SetParent(Viewport, false);
            _content = (RectTransform)contentObject.transform;
            _content.anchorMin = new Vector2(0f, 0f);
            _content.anchorMax = new Vector2(0f, 1f);
            _content.pivot = new Vector2(0f, 0.5f);
            _content.anchoredPosition = Vector2.zero;
            _content.sizeDelta = Vector2.zero;
            var layout = contentObject.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 4f;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            var fitter = contentObject.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;

            var scroll = Root.AddComponent<ScrollRect>();
            scroll.viewport = Viewport;
            scroll.content = _content;
            scroll.horizontal = true;
            scroll.vertical = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.inertia = false;
            scroll.scrollSensitivity = 24f;
            Root.SetActive(false);
        }

        internal GameObject Root { get; }
        internal RectTransform RootRect { get; }
        internal RectTransform Viewport { get; }

        internal float PreferredLabelWidth
        {
            get
            {
                var preferred = _label.GetPreferredValues(_label.text ?? string.Empty, float.PositiveInfinity,
                    ReservationStripLayoutPlanner.RootHeight).x;
                if (float.IsNaN(preferred) || float.IsInfinity(preferred) || preferred < 0f)
                    throw new InvalidOperationException("The reservation label did not produce a finite width.");
                return preferred;
            }
        }

        internal void ApplyInnerGeometry(float rootWidth, float labelWidth, float viewportLeft, float viewportRight)
        {
            _label.rectTransform.sizeDelta = new Vector2(labelWidth, 0f);
            Viewport.offsetMin = new Vector2(viewportLeft, 3f);
            Viewport.offsetMax = new Vector2(viewportRight - rootWidth, -3f);
        }

        internal void Render(
            IEnumerable<ExpeditionReservationRecord> records,
            Action<string> select,
            Action<string, string> release)
        {
            ClearCards();
            foreach (var record in records.OrderBy(value => value.DisplayName, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(value => value.PieceKey, StringComparer.Ordinal))
            {
                CreateCard(record, select, release);
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
        }

        internal void SetVisible(bool visible) => Root.SetActive(visible);

        internal void Destroy()
        {
            if (Root != null) Object.Destroy(Root);
        }

        private void CreateCard(
            ExpeditionReservationRecord record,
            Action<string> select,
            Action<string, string> release)
        {
            var cardObject = new GameObject("Reservation-" + SafeObjectName(record.PieceKey),
                typeof(RectTransform), typeof(LayoutElement), typeof(Image), typeof(Button));
            cardObject.transform.SetParent(_content, false);
            var cardRect = (RectTransform)cardObject.transform;
            cardRect.sizeDelta = new Vector2(46f, 46f);
            var layout = cardObject.GetComponent<LayoutElement>();
            layout.preferredWidth = layout.minWidth = 46f;
            layout.preferredHeight = layout.minHeight = 46f;
            var background = cardObject.GetComponent<Image>();
            background.color = new Color(0.08f, 0.115f, 0.13f, 0.98f);
            background.raycastTarget = true;
            var outline = cardObject.AddComponent<Outline>();
            outline.effectColor = Orange;
            outline.effectDistance = new Vector2(2f, -2f);

            var iconObject = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            iconObject.transform.SetParent(cardObject.transform, false);
            var icon = iconObject.GetComponent<Image>();
            icon.raycastTarget = false;
            var iconRect = icon.rectTransform;
            iconRect.anchorMin = iconRect.anchorMax = iconRect.pivot = new Vector2(0.5f, 0.5f);
            iconRect.anchoredPosition = new Vector2(-4f, 1f);
            iconRect.sizeDelta = new Vector2(30f, 30f);
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
                CreateText(cardObject.transform, "Missing", "?", TextAlignmentOptions.Center, 22f);
            }

            var count = CreateText(cardObject.transform, "Count", record.Count.ToString(), TextAlignmentOptions.BottomLeft, 14f);
            count.color = Orange;
            count.outlineColor = new Color32(45, 15, 0, 255);
            count.outlineWidth = 0.25f;
            var countRect = count.rectTransform;
            countRect.anchorMin = Vector2.zero;
            countRect.anchorMax = Vector2.one;
            countRect.offsetMin = new Vector2(3f, 1f);
            countRect.offsetMax = new Vector2(-18f, -2f);

            var card = cardObject.GetComponent<Button>();
            ConfigureButton(card, background);
            card.navigation = new Navigation { mode = Navigation.Mode.None };
            var pieceKey = record.PieceKey;
            card.onClick.AddListener(() => select(pieceKey));

            var removeObject = new GameObject("Remove", typeof(RectTransform), typeof(Image), typeof(Button), typeof(ReservationRemovalClickHandler));
            removeObject.transform.SetParent(cardObject.transform, false);
            var removeRect = (RectTransform)removeObject.transform;
            removeRect.anchorMin = removeRect.anchorMax = removeRect.pivot = new Vector2(1f, 1f);
            removeRect.anchoredPosition = new Vector2(-1f, -1f);
            removeRect.sizeDelta = new Vector2(17f, 17f);
            var removeBackground = removeObject.GetComponent<Image>();
            removeBackground.color = new Color(0.28f, 0.08f, 0.03f, 0.98f);
            removeBackground.raycastTarget = true;
            var remove = removeObject.GetComponent<Button>();
            ConfigureButton(remove, removeBackground);
            remove.navigation = new Navigation { mode = Navigation.Mode.None };
            var removeLabel = CreateText(removeObject.transform, "RemovalLabel",
                ReservationCardPresentation.RemovalLabel(record.Count), TextAlignmentOptions.Center, 13f);
            removeLabel.rectTransform.anchorMin = Vector2.zero;
            removeLabel.rectTransform.anchorMax = Vector2.one;
            removeLabel.rectTransform.offsetMin = Vector2.zero;
            removeLabel.rectTransform.offsetMax = Vector2.zero;
            var visibleName = Localize(record.DisplayName);
            removeObject.GetComponent<ReservationRemovalClickHandler>()
                .Configure(() => release(pieceKey, visibleName));
        }

        private static void ConfigureButton(Button button, Graphic target)
        {
            button.targetGraphic = target;
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 0.82f, 0.64f, 1f);
            colors.pressedColor = new Color(1f, 0.58f, 0.28f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(1f, 1f, 1f, 0.35f);
            colors.fadeDuration = 0.08f;
            button.colors = colors;
        }

        private TMP_Text CreateText(Transform parent, string name, string text, TextAlignmentOptions alignment, float size)
        {
            var gameObject = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            gameObject.transform.SetParent(parent, false);
            var label = gameObject.GetComponent<TextMeshProUGUI>();
            if (_textTemplate != null && _textTemplate.font != null)
            {
                label.font = _textTemplate.font;
                label.fontSharedMaterial = _textTemplate.fontSharedMaterial;
            }
            label.text = text;
            label.alignment = alignment;
            label.fontSize = size;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.color = Color.white;
            label.raycastTarget = false;
            return label;
        }

        private static bool TryResolvePieceIcon(string pieceKey, out Sprite sprite)
        {
            sprite = null;
            if (!TryPrefabName(pieceKey, out var prefabName) || ZNetScene.instance == null) return false;
            var prefab = ZNetScene.instance.GetPrefab(prefabName);
            var piece = prefab != null ? prefab.GetComponent<Piece>() : null;
            sprite = piece != null ? piece.m_icon : null;
            return sprite != null;
        }

        internal static bool TryPrefabName(string pieceKey, out string prefabName)
        {
            prefabName = null;
            if (string.IsNullOrEmpty(pieceKey) || !pieceKey.StartsWith(PiecePrefix, StringComparison.Ordinal)) return false;
            prefabName = pieceKey.Substring(PiecePrefix.Length);
            return !string.IsNullOrWhiteSpace(prefabName);
        }

        private static string Localize(string text)
            => Localization.instance != null ? Localization.instance.Localize(text ?? string.Empty) : text ?? string.Empty;

        private static string SafeObjectName(string value)
            => new string((value ?? string.Empty).Where(character => char.IsLetterOrDigit(character) || character == '-' || character == '_').Take(48).ToArray());

        private void ClearCards()
        {
            for (var index = _content.childCount - 1; index >= 0; index--)
                Object.Destroy(_content.GetChild(index).gameObject);
        }
    }

    internal sealed class ReservationRemovalClickHandler : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler
    {
        private Action _leftClick;

        internal void Configure(Action leftClick) => _leftClick = leftClick;

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData != null && eventData.button == PointerEventData.InputButton.Left) eventData.Use();
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData != null && eventData.button == PointerEventData.InputButton.Left) eventData.Use();
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData == null || eventData.button != PointerEventData.InputButton.Left) return;
            eventData.Use();
            _leftClick?.Invoke();
        }
    }
}
