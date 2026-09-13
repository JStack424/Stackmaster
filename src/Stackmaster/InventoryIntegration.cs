#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Stackmaster.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Stackmaster
{
    internal static class InventoryIntegration
    {
        private const string ToggleName = "StackmasterAutoSortToggle";
        private const string ToggleAnchorName = "StackmasterAutoSortAnchor";
        private const string ChestToggleName = "StackmasterChestAutoSortToggle";
        private const string ChestToggleAnchorName = "StackmasterChestAutoSortAnchor";
        private const string ProtectionOverlayName = "StackmasterProtectionOverlay";
        private static readonly FieldInfo GridElementsField = AccessTools.Field(typeof(InventoryGrid), "m_elements");
        private static readonly Dictionary<InventoryElement, ProtectionOverlay> ProtectionOverlays = new Dictionary<InventoryElement, ProtectionOverlay>();
        private static readonly Color ProtectedBorderColor = new Color(0.22f, 0.78f, 0.84f, 0.82f);
        private static GameObject _toggleAnchor;
        private static GameObject _toggleCheckmark;
        private static Toggle _toggle;
        private static GameObject _chestToggleAnchor;
        private static GameObject _chestToggleCheckmark;
        private static Toggle _chestToggle;
        private static string _boundChestPreferenceKey;
        private static Container _boundChest;
        private static Inventory _observedPlayerInventory;
        private static readonly Action InventoryChangedHandler = OnObservedInventoryChanged;
        private static bool _overlayRefreshPending;
        private static bool _sortedThisOpen;
        private static bool _sortingChestOnClose;
        private static bool _overlaysDisabled;
        private static bool _overlayFailureLogged;

        internal static void EnsureToggle(InventoryGui gui)
        {
            if (_toggle != null || gui == null || gui.m_player == null)
            {
                return;
            }

            // Build a dedicated control instead of cloning Valheim's PvP toggle. Cloning the
            // PvP object also clones its localization behavior, which can restore "Enable PvP"
            // after Stackmaster changes the visible text.
            _toggleAnchor = new GameObject(
                ToggleAnchorName,
                typeof(RectTransform),
                typeof(LayoutElement),
                typeof(Toggle));
            _toggleAnchor.transform.SetParent(gui.m_player, false);
            _toggleAnchor.transform.SetAsLastSibling();
            var anchorRect = (RectTransform)_toggleAnchor.transform;
            anchorRect.anchorMin = new Vector2(0f, 0f);
            anchorRect.anchorMax = new Vector2(0f, 0f);
            anchorRect.pivot = new Vector2(0f, 0f);
            // Straddle the panel's lower edge instead of covering the bottom inventory row.
            anchorRect.anchoredPosition = new Vector2(12f, -24f);
            anchorRect.sizeDelta = new Vector2(164f, 28f);
            _toggleAnchor.GetComponent<LayoutElement>().ignoreLayout = true;

            // A transparent full-width graphic makes both the box and its label clickable.
            var hitArea = CreateImage(_toggleAnchor.transform, "HitArea", Color.clear);
            var hitRect = hitArea.rectTransform;
            hitRect.anchorMin = Vector2.zero;
            hitRect.anchorMax = Vector2.one;
            hitRect.offsetMin = Vector2.zero;
            hitRect.offsetMax = Vector2.zero;
            hitArea.raycastTarget = true;

            var box = CreateImage(_toggleAnchor.transform, "Box", new Color(0.055f, 0.09f, 0.105f, 0.94f));
            var boxRect = box.rectTransform;
            boxRect.anchorMin = new Vector2(0f, 0.5f);
            boxRect.anchorMax = new Vector2(0f, 0.5f);
            boxRect.pivot = new Vector2(0f, 0.5f);
            boxRect.anchoredPosition = Vector2.zero;
            boxRect.sizeDelta = new Vector2(20f, 20f);
            var boxOutline = box.gameObject.AddComponent<Outline>();
            boxOutline.effectColor = new Color(0.22f, 0.78f, 0.84f, 0.9f);
            boxOutline.effectDistance = new Vector2(1f, -1f);

            // Draw a symmetric X from UI images rather than relying on a font glyph that may not
            // exist in every Valheim font asset. A symmetric mark cannot appear sideways.
            _toggleCheckmark = new GameObject("CheckedX", typeof(RectTransform));
            _toggleCheckmark.transform.SetParent(_toggleAnchor.transform, false);
            var checkRect = (RectTransform)_toggleCheckmark.transform;
            checkRect.anchorMin = new Vector2(0f, 0.5f);
            checkRect.anchorMax = new Vector2(0f, 0.5f);
            checkRect.pivot = new Vector2(0f, 0.5f);
            checkRect.anchoredPosition = Vector2.zero;
            checkRect.sizeDelta = new Vector2(20f, 20f);
            CreateCheckedXStroke(checkRect, "ForwardStroke", 45f);
            CreateCheckedXStroke(checkRect, "BackStroke", -45f);

            var labelObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(_toggleAnchor.transform, false);
            var label = labelObject.GetComponent<TextMeshProUGUI>();
            CopyTextStyle(gui, label);
            label.text = "Auto-sort";
            label.fontSize = Mathf.Max(14f, label.fontSize);
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.color = Color.white;
            label.raycastTarget = false;
            var labelRect = label.rectTransform;
            labelRect.anchorMin = new Vector2(0f, 0f);
            labelRect.anchorMax = new Vector2(1f, 1f);
            labelRect.offsetMin = new Vector2(28f, 0f);
            labelRect.offsetMax = Vector2.zero;

            _toggle = _toggleAnchor.GetComponent<Toggle>();
            _toggle.gameObject.name = ToggleName;
            _toggle.group = null;
            _toggle.interactable = true;
            _toggle.transition = Selectable.Transition.ColorTint;
            _toggle.targetGraphic = hitArea;
            _toggle.graphic = null;
            var colors = _toggle.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.82f, 1f, 1f, 1f);
            colors.pressedColor = new Color(0.62f, 0.9f, 0.92f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(1f, 1f, 1f, 0.35f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            _toggle.colors = colors;
            _toggle.SetIsOnWithoutNotify(RuntimeContext.Plugin.AutoSortEnabled.Value);
            _toggleCheckmark.SetActive(RuntimeContext.Plugin.AutoSortEnabled.Value);
            _toggle.onValueChanged.AddListener(enabled =>
            {
                _toggleCheckmark.SetActive(enabled);
                RuntimeContext.Plugin.AutoSortEnabled.Value = enabled;
            });
            _toggleAnchor.SetActive(true);

            RuntimeContext.Plugin.AutoSortEnabled.SettingChanged -= OnAutoSortSettingChanged;
            RuntimeContext.Plugin.AutoSortEnabled.SettingChanged += OnAutoSortSettingChanged;
        }

        internal static void EnsureChestToggle(InventoryGui gui)
        {
            if (_chestToggle != null || gui == null || gui.m_container == null)
            {
                return;
            }

            _chestToggleAnchor = new GameObject(
                ChestToggleAnchorName,
                typeof(RectTransform),
                typeof(LayoutElement),
                typeof(Toggle));
            _chestToggleAnchor.transform.SetParent(gui.m_container, false);
            _chestToggleAnchor.transform.SetAsLastSibling();
            var anchorRect = (RectTransform)_chestToggleAnchor.transform;
            anchorRect.anchorMin = new Vector2(0f, 0f);
            anchorRect.anchorMax = new Vector2(0f, 0f);
            anchorRect.pivot = new Vector2(0f, 0f);
            anchorRect.anchoredPosition = new Vector2(12f, -24f);
            anchorRect.sizeDelta = new Vector2(210f, 28f);
            _chestToggleAnchor.GetComponent<LayoutElement>().ignoreLayout = true;

            var hitArea = CreateImage(_chestToggleAnchor.transform, "HitArea", Color.clear);
            var hitRect = hitArea.rectTransform;
            hitRect.anchorMin = Vector2.zero;
            hitRect.anchorMax = Vector2.one;
            hitRect.offsetMin = Vector2.zero;
            hitRect.offsetMax = Vector2.zero;
            hitArea.raycastTarget = true;

            var box = CreateImage(_chestToggleAnchor.transform, "Box", new Color(0.055f, 0.09f, 0.105f, 0.94f));
            var boxRect = box.rectTransform;
            boxRect.anchorMin = new Vector2(0f, 0.5f);
            boxRect.anchorMax = new Vector2(0f, 0.5f);
            boxRect.pivot = new Vector2(0f, 0.5f);
            boxRect.anchoredPosition = Vector2.zero;
            boxRect.sizeDelta = new Vector2(20f, 20f);
            var boxOutline = box.gameObject.AddComponent<Outline>();
            boxOutline.effectColor = new Color(0.22f, 0.78f, 0.84f, 0.9f);
            boxOutline.effectDistance = new Vector2(1f, -1f);

            _chestToggleCheckmark = new GameObject("CheckedX", typeof(RectTransform));
            _chestToggleCheckmark.transform.SetParent(_chestToggleAnchor.transform, false);
            var checkRect = (RectTransform)_chestToggleCheckmark.transform;
            checkRect.anchorMin = new Vector2(0f, 0.5f);
            checkRect.anchorMax = new Vector2(0f, 0.5f);
            checkRect.pivot = new Vector2(0f, 0.5f);
            checkRect.anchoredPosition = Vector2.zero;
            checkRect.sizeDelta = new Vector2(20f, 20f);
            CreateCheckedXStroke(checkRect, "ForwardStroke", 45f);
            CreateCheckedXStroke(checkRect, "BackStroke", -45f);

            var labelObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(_chestToggleAnchor.transform, false);
            var label = labelObject.GetComponent<TextMeshProUGUI>();
            CopyTextStyle(gui, label);
            label.text = "Auto-sort chest";
            label.fontSize = Mathf.Max(14f, label.fontSize);
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.color = Color.white;
            label.raycastTarget = false;
            var labelRect = label.rectTransform;
            labelRect.anchorMin = new Vector2(0f, 0f);
            labelRect.anchorMax = new Vector2(1f, 1f);
            labelRect.offsetMin = new Vector2(28f, 0f);
            labelRect.offsetMax = Vector2.zero;

            _chestToggle = _chestToggleAnchor.GetComponent<Toggle>();
            _chestToggle.gameObject.name = ChestToggleName;
            _chestToggle.group = null;
            _chestToggle.interactable = true;
            _chestToggle.transition = Selectable.Transition.ColorTint;
            _chestToggle.targetGraphic = hitArea;
            _chestToggle.graphic = null;
            var colors = _chestToggle.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.82f, 1f, 1f, 1f);
            colors.pressedColor = new Color(0.62f, 0.9f, 0.92f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(1f, 1f, 1f, 0.35f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            _chestToggle.colors = colors;
            _chestToggle.SetIsOnWithoutNotify(true);
            _chestToggleCheckmark.SetActive(true);
            _chestToggle.onValueChanged.AddListener(OnChestToggleChanged);
            _chestToggleAnchor.SetActive(false);
        }

        internal static void BindChestToggle(Container container)
        {
            _boundChest = null;
            _boundChestPreferenceKey = null;
            if (_chestToggleAnchor == null || _chestToggle == null)
            {
                return;
            }

            _chestToggleAnchor.SetActive(false);
            string key;
            bool enabled;
            if (!ChestSortPreferences.TryGet(container, out key, out enabled))
            {
                return;
            }

            _boundChest = container;
            _boundChestPreferenceKey = key;
            _chestToggle.interactable = true;
            _chestToggle.SetIsOnWithoutNotify(enabled);
            _chestToggleCheckmark.SetActive(enabled);
            _chestToggleAnchor.SetActive(true);
        }

        private static void OnChestToggleChanged(bool enabled)
        {
            if (_chestToggleCheckmark != null)
            {
                _chestToggleCheckmark.SetActive(enabled);
            }
            if (_boundChest == null || string.IsNullOrEmpty(_boundChestPreferenceKey))
            {
                return;
            }

            if (!ChestSortPreferences.TrySet(_boundChestPreferenceKey, enabled))
            {
                _chestToggle.SetIsOnWithoutNotify(false);
                _chestToggleCheckmark.SetActive(false);
                _chestToggle.interactable = false;
            }
        }

        private static void UnbindChestToggle()
        {
            _boundChest = null;
            _boundChestPreferenceKey = null;
            if (_chestToggleAnchor != null)
            {
                _chestToggleAnchor.SetActive(false);
            }
        }

        private static void CopyTextStyle(InventoryGui gui, TMP_Text target)
        {
            var template = gui.m_pvp != null ? gui.m_pvp.GetComponentInChildren<TMP_Text>(true) : null;
            if (template == null && gui.m_player != null)
            {
                template = gui.m_player
                    .GetComponentsInChildren<TMP_Text>(true)
                    .FirstOrDefault(candidate => candidate != target && candidate.font != null);
            }
            if (template == null || target == null)
            {
                return;
            }

            target.font = template.font;
            target.fontSharedMaterial = template.fontSharedMaterial;
            target.fontSize = template.fontSize;
        }

        private static void CreateCheckedXStroke(Transform parent, string name, float rotation)
        {
            var stroke = CreateImage(parent, name, new Color(0.25f, 0.9f, 0.94f, 1f));
            var rect = stroke.rectTransform;
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 0f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(10f, 10f);
            rect.sizeDelta = new Vector2(3f, 14f);
            rect.localRotation = Quaternion.Euler(0f, 0f, rotation);
        }

        internal static void RefreshProtectionOverlays()
        {
            if (_overlaysDisabled)
            {
                return;
            }
            if (!RuntimeContext.Compatibility.IsCompatible || !InventoryGui.IsVisible())
            {
                HideProtectionOverlays();
                return;
            }

            try
            {
                var gui = InventoryGui.instance;
                var player = Player.m_localPlayer;
                var grid = gui != null ? gui.m_playerGrid : null;
                if (player == null || grid == null || grid.GetInventory() != player.GetInventory())
                {
                    HideProtectionOverlays();
                    return;
                }

                if (GridElementsField == null)
                {
                    throw new MissingFieldException(typeof(InventoryGrid).FullName, "m_elements");
                }
                var elements = GridElementsField.GetValue(grid) as List<InventoryElement>;
                if (elements == null)
                {
                    throw new InvalidOperationException("The player inventory grid elements are unavailable.");
                }

                ProtectionState state;
                if (!RuntimeContext.TryLoadProtection(player, out state))
                {
                    HideProtectionOverlays();
                    return;
                }
                var resolution = InventorySnapshots.ResolveProtection(player, state);

                var currentElements = new HashSet<InventoryElement>(elements.Where(element => element != null));
                foreach (var stale in ProtectionOverlays.Keys.Where(element => element == null || !currentElements.Contains(element)).ToArray())
                {
                    ProtectionOverlays[stale].Destroy();
                    ProtectionOverlays.Remove(stale);
                }

                foreach (var element in currentElements)
                {
                    ProtectionOverlay overlay;
                    if (!ProtectionOverlays.TryGetValue(element, out overlay))
                    {
                        overlay = ProtectionOverlay.Create(element);
                        ProtectionOverlays.Add(element, overlay);
                    }

                    ProtectionRecord record;
                    overlay.Apply(resolution.TryGet(new Slot(element.Position.x, element.Position.y), out record) ? record : null);
                }
            }
            catch (Exception exception)
            {
                HideProtectionOverlays();
                _overlaysDisabled = true;
                if (!_overlayFailureLogged && RuntimeContext.Plugin != null)
                {
                    _overlayFailureLogged = true;
                    RuntimeContext.Plugin.Log.LogWarning("Protected-item indicators could not be shown safely: " + exception);
                }
            }
        }

        internal static void OnCompatibilityDisabled()
        {
            OnInventoryHidden();
            if (_toggleAnchor != null)
            {
                _toggleAnchor.SetActive(false);
            }
        }

        internal static void Shutdown()
        {
            _overlayRefreshPending = false;
            UnbindPlayerInventory();
            UnbindChestToggle();
            if (RuntimeContext.Plugin != null && RuntimeContext.Plugin.AutoSortEnabled != null)
            {
                RuntimeContext.Plugin.AutoSortEnabled.SettingChanged -= OnAutoSortSettingChanged;
            }
            if (_toggleAnchor != null)
            {
                Object.Destroy(_toggleAnchor);
                _toggleAnchor = null;
                _toggleCheckmark = null;
                _toggle = null;
            }
            else if (_toggle != null)
            {
                Object.Destroy(_toggle.gameObject);
                _toggle = null;
            }
            _toggleCheckmark = null;
            if (_chestToggleAnchor != null)
            {
                Object.Destroy(_chestToggleAnchor);
                _chestToggleAnchor = null;
                _chestToggleCheckmark = null;
                _chestToggle = null;
            }
            else if (_chestToggle != null)
            {
                Object.Destroy(_chestToggle.gameObject);
                _chestToggle = null;
            }
            _chestToggleCheckmark = null;
            _boundChestPreferenceKey = null;
            _boundChest = null;

            DestroyProtectionOverlays();
            _overlaysDisabled = false;
            _overlayFailureLogged = false;
            _sortedThisOpen = false;
            _sortingChestOnClose = false;
        }

        private static void OnAutoSortSettingChanged(object sender, EventArgs args)
        {
            var enabled = RuntimeContext.Plugin.AutoSortEnabled.Value;
            if (_toggle != null && _toggle.isOn != enabled)
            {
                _toggle.SetIsOnWithoutNotify(enabled);
            }
            if (_toggleCheckmark != null)
            {
                _toggleCheckmark.SetActive(enabled);
            }
        }

        internal static void BindPlayerInventory(Player player)
        {
            var inventory = player != null ? player.GetInventory() : null;
            if (ReferenceEquals(_observedPlayerInventory, inventory))
            {
                return;
            }

            UnbindPlayerInventory();
            _observedPlayerInventory = inventory;
            if (_observedPlayerInventory != null)
            {
                _observedPlayerInventory.m_onChanged += InventoryChangedHandler;
            }
        }

        private static void UnbindPlayerInventory()
        {
            if (_observedPlayerInventory != null)
            {
                _observedPlayerInventory.m_onChanged -= InventoryChangedHandler;
                _observedPlayerInventory = null;
            }
        }

        private static void OnObservedInventoryChanged()
        {
            // Inventory.m_onChanged can run before InventoryGrid rebuilds its element positions.
            // Defer the actual overlay walk until the matching player grid has finished UpdateGui.
            _overlayRefreshPending = true;
        }

        internal static void RequestProtectionOverlayRefresh()
        {
            _overlayRefreshPending = true;
        }

        internal static void FlushPendingProtectionOverlayRefresh(InventoryGrid grid, Inventory inventory)
        {
            if (!_overlayRefreshPending || grid == null || inventory == null)
            {
                return;
            }

            var gui = InventoryGui.instance;
            var player = Player.m_localPlayer;
            if (gui == null || player == null || !ReferenceEquals(grid, gui.m_playerGrid) ||
                !ReferenceEquals(inventory, player.GetInventory()))
            {
                return;
            }

            _overlayRefreshPending = false;
            RefreshProtectionOverlays();
        }

        internal static void SortOpenedInventories(Container container)
        {
            if (_sortedThisOpen)
            {
                return;
            }
            _sortedThisOpen = true;

            var player = Player.m_localPlayer;
            string failure;
            if (RuntimeContext.Plugin.AutoSortEnabled.Value && player != null)
            {
                ProtectionState protection;
                if (RuntimeContext.TryLoadProtection(player, out protection) &&
                    !SortExecutor.Sort(player.GetInventory(), true, player, protection, out failure))
                {
                    RuntimeContext.Plugin.Log.LogWarning("Player auto-sort skipped safely: " + failure);
                }
            }

            string preferenceKey;
            bool chestAutoSortEnabled;
            if (container != null && container.GetType() == typeof(Container) && container.IsOwner() &&
                container.GetInventory() != null &&
                ChestSortPreferences.TryGet(container, out preferenceKey, out chestAutoSortEnabled) &&
                chestAutoSortEnabled)
            {
                if (!SortExecutor.Sort(container.GetInventory(), false, null, null, out failure))
                {
                    RuntimeContext.Plugin.Log.LogWarning("Opened-container auto-sort skipped safely: " + failure);
                }
            }
        }

        internal static void SortClosingChest()
        {
            if (_sortingChestOnClose || _boundChest == null)
            {
                return;
            }

            _sortingChestOnClose = true;
            try
            {
                var container = _boundChest;
                string preferenceKey;
                bool enabled;
                if (container == null || container.GetType() != typeof(Container) || !container.IsOwner() ||
                    container.GetInventory() == null ||
                    !ChestSortPreferences.TryGet(container, out preferenceKey, out enabled) || !enabled)
                {
                    return;
                }

                string failure;
                if (!SortExecutor.Sort(container.GetInventory(), false, null, null, out failure))
                {
                    RuntimeContext.Plugin.Log.LogWarning("Closing-container auto-sort skipped safely: " + failure);
                }
            }
            catch (Exception exception)
            {
                RuntimeContext.Plugin?.Log.LogWarning("Closing-container auto-sort skipped safely: " + exception);
            }
            finally
            {
                _sortingChestOnClose = false;
            }
        }

        internal static void OnInventoryHidden()
        {
            _sortedThisOpen = false;
            _overlayRefreshPending = false;
            UnbindPlayerInventory();
            UnbindChestToggle();
            HideProtectionOverlays();
        }

        private static void HideProtectionOverlays()
        {
            foreach (var overlay in ProtectionOverlays.Values)
            {
                overlay.Apply(null);
            }
        }

        private static void DestroyProtectionOverlays()
        {
            foreach (var overlay in ProtectionOverlays.Values)
            {
                overlay.Destroy();
            }
            ProtectionOverlays.Clear();
        }

        private static Image CreateImage(Transform parent, string name, Color color)
        {
            var gameObject = new GameObject(name, typeof(RectTransform), typeof(Image));
            gameObject.transform.SetParent(parent, false);
            var image = gameObject.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static void ConfigureEdge(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 size)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = size;
        }

        private sealed class ProtectionOverlay
        {
            private readonly GameObject _root;
            private readonly TMP_Text _targetLabel;
            private readonly GameObject _lockIcon;

            private ProtectionOverlay(GameObject root, TMP_Text targetLabel, GameObject lockIcon)
            {
                _root = root;
                _targetLabel = targetLabel;
                _lockIcon = lockIcon;
            }

            internal static ProtectionOverlay Create(InventoryElement element)
            {
                var slotRoot = element.GetElementRectTransform();
                if (slotRoot == null || element.m_amount == null)
                {
                    throw new InvalidOperationException("An inventory item slot does not expose the expected UI elements.");
                }

                var previous = slotRoot.Find(ProtectionOverlayName);
                if (previous != null)
                {
                    Object.Destroy(previous.gameObject);
                }

                var root = new GameObject(ProtectionOverlayName, typeof(RectTransform));
                root.transform.SetParent(slotRoot, false);
                root.transform.SetAsLastSibling();
                var rootRect = (RectTransform)root.transform;
                rootRect.anchorMin = Vector2.zero;
                rootRect.anchorMax = Vector2.one;
                rootRect.offsetMin = Vector2.zero;
                rootRect.offsetMax = Vector2.zero;

                ConfigureEdge(CreateImage(root.transform, "Top", ProtectedBorderColor).rectTransform,
                    new Vector2(0f, 1f), Vector2.one, new Vector2(0.5f, 1f), new Vector2(0f, 2f));
                ConfigureEdge(CreateImage(root.transform, "Bottom", ProtectedBorderColor).rectTransform,
                    Vector2.zero, new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 2f));
                ConfigureEdge(CreateImage(root.transform, "Left", ProtectedBorderColor).rectTransform,
                    Vector2.zero, new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(2f, 0f));
                ConfigureEdge(CreateImage(root.transform, "Right", ProtectedBorderColor).rectTransform,
                    new Vector2(1f, 0f), Vector2.one, new Vector2(1f, 0.5f), new Vector2(2f, 0f));

                var targetLabel = Object.Instantiate(element.m_amount, root.transform, false);
                targetLabel.gameObject.name = "Target";
                targetLabel.gameObject.SetActive(true);
                targetLabel.text = string.Empty;
                targetLabel.alignment = TextAlignmentOptions.TopLeft;
                targetLabel.textWrappingMode = TextWrappingModes.NoWrap;
                targetLabel.fontSize = Mathf.Max(12f, targetLabel.fontSize * 0.8f);
                targetLabel.color = ProtectedBorderColor;
                targetLabel.outlineColor = new Color32(0, 24, 31, 255);
                targetLabel.outlineWidth = 0.22f;
                targetLabel.raycastTarget = false;
                var targetRect = targetLabel.rectTransform;
                targetRect.anchorMin = new Vector2(0f, 1f);
                targetRect.anchorMax = new Vector2(0f, 1f);
                targetRect.pivot = new Vector2(0f, 1f);
                targetRect.anchoredPosition = new Vector2(3f, -2f);
                targetRect.sizeDelta = new Vector2(26f, 18f);

                var lockIcon = new GameObject("Lock", typeof(RectTransform));
                lockIcon.transform.SetParent(root.transform, false);
                var lockRect = (RectTransform)lockIcon.transform;
                lockRect.anchorMin = new Vector2(0f, 1f);
                lockRect.anchorMax = new Vector2(0f, 1f);
                lockRect.pivot = new Vector2(0f, 1f);
                lockRect.anchoredPosition = new Vector2(3f, -3f);
                lockRect.sizeDelta = new Vector2(12f, 14f);

                var lockBody = CreateImage(lockIcon.transform, "Body", ProtectedBorderColor).rectTransform;
                lockBody.anchorMin = Vector2.zero;
                lockBody.anchorMax = Vector2.zero;
                lockBody.pivot = Vector2.zero;
                lockBody.anchoredPosition = Vector2.zero;
                lockBody.sizeDelta = new Vector2(12f, 8f);
                ConfigureEdge(CreateImage(lockIcon.transform, "ShackleTop", ProtectedBorderColor).rectTransform,
                    new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(8f, 2f));
                var shackleTop = lockIcon.transform.Find("ShackleTop").GetComponent<RectTransform>();
                shackleTop.anchoredPosition = new Vector2(0f, 12f);
                var shackleLeft = CreateImage(lockIcon.transform, "ShackleLeft", ProtectedBorderColor).rectTransform;
                shackleLeft.anchorMin = Vector2.zero;
                shackleLeft.anchorMax = Vector2.zero;
                shackleLeft.pivot = Vector2.zero;
                shackleLeft.anchoredPosition = new Vector2(2f, 7f);
                shackleLeft.sizeDelta = new Vector2(2f, 6f);
                var shackleRight = CreateImage(lockIcon.transform, "ShackleRight", ProtectedBorderColor).rectTransform;
                shackleRight.anchorMin = Vector2.zero;
                shackleRight.anchorMax = Vector2.zero;
                shackleRight.pivot = Vector2.zero;
                shackleRight.anchoredPosition = new Vector2(8f, 7f);
                shackleRight.sizeDelta = new Vector2(2f, 6f);

                root.SetActive(false);
                return new ProtectionOverlay(root, targetLabel, lockIcon);
            }

            internal void Apply(ProtectionRecord record)
            {
                if (_root == null)
                {
                    return;
                }

                var visible = record != null;
                _root.SetActive(visible);
                if (!visible)
                {
                    return;
                }

                var hasTarget = record.TargetQuantity.HasValue;
                _targetLabel.gameObject.SetActive(hasTarget);
                _targetLabel.text = hasTarget ? record.TargetQuantity.Value.ToString() : string.Empty;
                _lockIcon.SetActive(!hasTarget);
            }

            internal void Destroy()
            {
                if (_root != null)
                {
                    Object.Destroy(_root);
                }
            }
        }
    }

    [HarmonyPatch(typeof(InventoryGui), "Awake")]
    internal static class InventoryGuiAwakePatch
    {
        private static void Postfix(InventoryGui __instance)
        {
            if (!RuntimeContext.Compatibility.IsCompatible) return;
            InventoryIntegration.EnsureToggle(__instance);
            InventoryIntegration.EnsureChestToggle(__instance);
        }
    }

    [HarmonyPatch(typeof(InventoryGui), "Hide")]
    internal static class InventoryGuiHidePatch
    {
        private static void Prefix()
        {
            if (!RuntimeContext.Compatibility.IsCompatible) return;
            InventoryIntegration.SortClosingChest();
        }

        private static void Postfix()
        {
            if (!RuntimeContext.Compatibility.IsCompatible) return;
            InventoryIntegration.OnInventoryHidden();
        }
    }

    [HarmonyPatch(typeof(InventoryGrid), "UpdateInventory", typeof(Inventory), typeof(Player), typeof(ItemDrop.ItemData))]
    internal static class InventoryGridUpdateInventoryPatch
    {
        private static void Postfix(InventoryGrid __instance, Inventory inventory)
        {
            if (!RuntimeContext.Compatibility.IsCompatible) return;
            InventoryIntegration.FlushPendingProtectionOverlayRefresh(__instance, inventory);
        }
    }

    [HarmonyPatch(typeof(InventoryGui), "Show", typeof(Container), typeof(int))]
    internal static class InventoryGuiShowPatch
    {
        private static void Postfix(InventoryGui __instance, Container container)
        {
            if (!RuntimeContext.Compatibility.IsCompatible) return;
            InventoryIntegration.EnsureToggle(__instance);
            InventoryIntegration.EnsureChestToggle(__instance);
            InventoryIntegration.BindPlayerInventory(Player.m_localPlayer);
            InventoryIntegration.BindChestToggle(container);
            InventoryIntegration.SortOpenedInventories(container);
            InventoryIntegration.RequestProtectionOverlayRefresh();
        }
    }
}
