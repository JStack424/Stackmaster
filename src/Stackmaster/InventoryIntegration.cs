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
        private const string ProtectionOverlayName = "StackmasterProtectionOverlay";
        private static readonly FieldInfo GridElementsField = AccessTools.Field(typeof(InventoryGrid), "m_elements");
        private static readonly Dictionary<InventoryElement, ProtectionOverlay> ProtectionOverlays = new Dictionary<InventoryElement, ProtectionOverlay>();
        private static readonly Color ProtectedBorderColor = new Color(0.22f, 0.78f, 0.84f, 0.82f);
        private static GameObject _toggleAnchor;
        private static Toggle _toggle;
        private static bool _sortedThisOpen;
        private static bool _overlaysDisabled;
        private static bool _overlayFailureLogged;

        internal static void EnsureToggle(InventoryGui gui)
        {
            if (_toggle != null || gui == null || gui.m_pvp == null || gui.m_player == null)
            {
                return;
            }

            // Use a dedicated anchor on the visible player panel rather than inheriting the
            // vanilla PvP toggle's potentially hidden or layout-controlled parent.
            _toggleAnchor = new GameObject(ToggleAnchorName, typeof(RectTransform), typeof(LayoutElement));
            _toggleAnchor.transform.SetParent(gui.m_player, false);
            var anchorRect = (RectTransform)_toggleAnchor.transform;
            anchorRect.anchorMin = new Vector2(0f, 0f);
            anchorRect.anchorMax = new Vector2(0f, 0f);
            anchorRect.pivot = new Vector2(0f, 0f);
            anchorRect.anchoredPosition = new Vector2(12f, 4f);
            anchorRect.sizeDelta = new Vector2(Mathf.Max(150f, gui.m_pvp.GetComponent<RectTransform>().rect.width), 28f);
            _toggleAnchor.GetComponent<LayoutElement>().ignoreLayout = true;
            _toggleAnchor.transform.SetAsLastSibling();

            _toggle = Object.Instantiate(gui.m_pvp, _toggleAnchor.transform, false);
            _toggle.gameObject.name = ToggleName;
            _toggle.gameObject.SetActive(true);
            _toggle.group = null;
            _toggle.interactable = true;
            _toggle.onValueChanged.RemoveAllListeners();
            _toggle.SetIsOnWithoutNotify(RuntimeContext.Plugin.AutoSortEnabled.Value);
            _toggle.onValueChanged.AddListener(enabled => RuntimeContext.Plugin.AutoSortEnabled.Value = enabled);

            var toggleRect = _toggle.GetComponent<RectTransform>();
            toggleRect.anchorMin = Vector2.zero;
            toggleRect.anchorMax = Vector2.one;
            toggleRect.offsetMin = Vector2.zero;
            toggleRect.offsetMax = Vector2.zero;

            var label = _toggle.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
            {
                label.text = "Auto-sort";
                label.textWrappingMode = TextWrappingModes.NoWrap;
            }

            RuntimeContext.Plugin.AutoSortEnabled.SettingChanged -= OnAutoSortSettingChanged;
            RuntimeContext.Plugin.AutoSortEnabled.SettingChanged += OnAutoSortSettingChanged;
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

        internal static void Shutdown()
        {
            if (RuntimeContext.Plugin != null && RuntimeContext.Plugin.AutoSortEnabled != null)
            {
                RuntimeContext.Plugin.AutoSortEnabled.SettingChanged -= OnAutoSortSettingChanged;
            }
            if (_toggleAnchor != null)
            {
                Object.Destroy(_toggleAnchor);
                _toggleAnchor = null;
                _toggle = null;
            }
            else if (_toggle != null)
            {
                Object.Destroy(_toggle.gameObject);
                _toggle = null;
            }

            DestroyProtectionOverlays();
            _overlaysDisabled = false;
            _overlayFailureLogged = false;
            _sortedThisOpen = false;
        }

        private static void OnAutoSortSettingChanged(object sender, EventArgs args)
        {
            if (_toggle != null && _toggle.isOn != RuntimeContext.Plugin.AutoSortEnabled.Value)
            {
                _toggle.SetIsOnWithoutNotify(RuntimeContext.Plugin.AutoSortEnabled.Value);
            }
        }

        internal static void SortOpenedInventories(Container container)
        {
            if (_sortedThisOpen)
            {
                return;
            }
            _sortedThisOpen = true;

            if (!RuntimeContext.Plugin.AutoSortEnabled.Value)
            {
                return;
            }

            var player = Player.m_localPlayer;
            if (player == null)
            {
                return;
            }

            ProtectionState protection;
            if (!RuntimeContext.TryLoadProtection(player, out protection))
            {
                return;
            }
            string failure;
            if (!SortExecutor.Sort(player.GetInventory(), true, player, protection, out failure))
            {
                RuntimeContext.Plugin.Log.LogWarning("Player auto-sort skipped safely: " + failure);
            }

            if (container != null && container.GetType() == typeof(Container) && container.IsOwner() && container.GetInventory() != null)
            {
                if (!SortExecutor.Sort(container.GetInventory(), false, null, null, out failure))
                {
                    RuntimeContext.Plugin.Log.LogWarning("Opened-container auto-sort skipped safely: " + failure);
                }
            }
        }

        internal static void OnInventoryHidden()
        {
            _sortedThisOpen = false;
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
                targetLabel.alignment = TextAlignmentOptions.BottomLeft;
                targetLabel.textWrappingMode = TextWrappingModes.NoWrap;
                targetLabel.fontSize = Mathf.Max(12f, targetLabel.fontSize * 0.8f);
                targetLabel.color = Color.white;
                targetLabel.outlineColor = new Color32(0, 24, 31, 255);
                targetLabel.outlineWidth = 0.22f;
                targetLabel.raycastTarget = false;
                var targetRect = targetLabel.rectTransform;
                targetRect.anchorMin = Vector2.zero;
                targetRect.anchorMax = Vector2.zero;
                targetRect.pivot = Vector2.zero;
                targetRect.anchoredPosition = new Vector2(3f, 2f);
                targetRect.sizeDelta = new Vector2(26f, 18f);

                var lockIcon = new GameObject("Lock", typeof(RectTransform));
                lockIcon.transform.SetParent(root.transform, false);
                var lockRect = (RectTransform)lockIcon.transform;
                lockRect.anchorMin = Vector2.zero;
                lockRect.anchorMax = Vector2.zero;
                lockRect.pivot = Vector2.zero;
                lockRect.anchoredPosition = new Vector2(3f, 3f);
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
        }
    }

    [HarmonyPatch(typeof(InventoryGui), "Hide")]
    internal static class InventoryGuiHidePatch
    {
        private static void Postfix()
        {
            if (!RuntimeContext.Compatibility.IsCompatible) return;
            InventoryIntegration.OnInventoryHidden();
        }
    }

    [HarmonyPatch(typeof(InventoryGui), "Update")]
    internal static class InventoryGuiUpdatePatch
    {
        private static void Postfix()
        {
            if (!RuntimeContext.Compatibility.IsCompatible) return;
            InventoryIntegration.RefreshProtectionOverlays();
        }
    }

    [HarmonyPatch(typeof(InventoryGui), "Show", typeof(Container), typeof(int))]
    internal static class InventoryGuiShowPatch
    {
        private static void Postfix(InventoryGui __instance, Container container)
        {
            if (!RuntimeContext.Compatibility.IsCompatible) return;
            InventoryIntegration.EnsureToggle(__instance);
            InventoryIntegration.SortOpenedInventories(container);
            InventoryIntegration.RefreshProtectionOverlays();
        }
    }
}
