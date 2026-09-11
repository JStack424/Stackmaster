#nullable disable
using HarmonyLib;
using Stackmaster.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Stackmaster
{
    internal static class InventoryIntegration
    {
        private const string ToggleName = "StackmasterAutoSortToggle";
        private static Toggle _toggle;
        private static bool _sortedThisOpen;

        internal static void EnsureToggle(InventoryGui gui)
        {
            if (_toggle != null || gui == null || gui.m_pvp == null)
            {
                return;
            }

            _toggle = Object.Instantiate(gui.m_pvp, gui.m_pvp.transform.parent);
            _toggle.gameObject.name = ToggleName;
            _toggle.group = null;
            _toggle.onValueChanged.RemoveAllListeners();
            _toggle.SetIsOnWithoutNotify(RuntimeContext.Plugin.AutoSortEnabled.Value);
            _toggle.onValueChanged.AddListener(enabled => RuntimeContext.Plugin.AutoSortEnabled.Value = enabled);

            var label = _toggle.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
            {
                label.text = "Auto-sort";
            }

            _toggle.transform.SetSiblingIndex(gui.m_pvp.transform.GetSiblingIndex() + 1);
            if (_toggle.transform.parent.GetComponent<LayoutGroup>() == null)
            {
                var rect = _toggle.GetComponent<RectTransform>();
                rect.anchoredPosition += new Vector2(0f, -28f);
            }

            RuntimeContext.Plugin.AutoSortEnabled.SettingChanged += OnAutoSortSettingChanged;
        }

        internal static void Shutdown()
        {
            if (RuntimeContext.Plugin != null && RuntimeContext.Plugin.AutoSortEnabled != null)
            {
                RuntimeContext.Plugin.AutoSortEnabled.SettingChanged -= OnAutoSortSettingChanged;
            }
            if (_toggle != null)
            {
                Object.Destroy(_toggle.gameObject);
                _toggle = null;
            }
            _sortedThisOpen = false;
        }

        private static void OnAutoSortSettingChanged(object sender, System.EventArgs args)
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

    [HarmonyPatch(typeof(InventoryGui), "Show", typeof(Container), typeof(int))]
    internal static class InventoryGuiShowPatch
    {
        private static void Postfix(InventoryGui __instance, Container container)
        {
            if (!RuntimeContext.Compatibility.IsCompatible) return;
            InventoryIntegration.EnsureToggle(__instance);
            InventoryIntegration.SortOpenedInventories(container);
        }
    }
}
