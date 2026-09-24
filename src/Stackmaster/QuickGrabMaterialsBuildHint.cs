#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Stackmaster
{
    internal static class QuickGrabMaterialsBuildHintPatch
    {
        internal static void Postfix(KeyHints __instance)
        {
            try
            {
                QuickGrabMaterialsBuildHint.Update(__instance);
            }
            catch (Exception exception)
            {
                QuickGrabMaterialsBuildHint.FailForOwner(__instance);
                NearbyHudFailOpen.ReportOnce("quick-grab build-menu hint", exception);
            }
        }
    }

    internal static class QuickGrabMaterialsBuildHint
    {
        internal const string ObjectName = "StackmasterQuickGrabMaterialsHint";
        internal const string ActionText = "Quick Grab Materials";

        private static KeyHints _owner;
        private static KeyHints _failedOwner;
        private static GameObject _hint;
        private static string _lastShortcutText;

        internal static void Update(KeyHints owner)
        {
            if (!RuntimeContext.Compatibility.IsCompatible || RuntimeContext.Plugin == null || owner == null)
            {
                Detach();
                return;
            }
            if (ReferenceEquals(_failedOwner, owner)) return;
            if (_failedOwner != null) _failedOwner = null;

            if (!ReferenceEquals(_owner, owner) || _hint == null)
            {
                Attach(owner);
            }
            if (_hint == null) return;

            var shortcutText = FormatShortcut(RuntimeContext.Plugin.StorageActionShortcut.Value.Modifiers);
            if (!string.Equals(_lastShortcutText, shortcutText, StringComparison.Ordinal))
            {
                ApplyText(_hint, shortcutText);
                _lastShortcutText = shortcutText;
            }
        }

        internal static void FailForOwner(KeyHints owner)
        {
            Detach();
            _failedOwner = owner;
        }

        internal static void Detach()
        {
            try
            {
                if (_owner != null)
                {
                    var current = _owner.m_buildMenuHintsKB ?? Array.Empty<GameObject>();
                    _owner.m_buildMenuHintsKB = current
                        .Where(item => item == null ||
                                       (!ReferenceEquals(item, _hint) &&
                                        !string.Equals(item.name, ObjectName, StringComparison.Ordinal)))
                        .ToArray();
                }
                if (_hint != null)
                {
                    Object.Destroy(_hint);
                }
            }
            finally
            {
                _owner = null;
                _failedOwner = null;
                _hint = null;
                _lastShortcutText = null;
            }
        }

        private static void Attach(KeyHints owner)
        {
            Detach();
            var current = owner.m_buildMenuHintsKB ?? Array.Empty<GameObject>();
            var staleHints = current
                .Where(item => item != null && string.Equals(item.name, ObjectName, StringComparison.Ordinal))
                .ToArray();
            var vanillaAndThirdPartyHints = current
                .Where(item => item == null || !string.Equals(item.name, ObjectName, StringComparison.Ordinal))
                .ToArray();
            var template = vanillaAndThirdPartyHints.FirstOrDefault(item => item != null);
            if (template == null)
            {
                owner.m_buildMenuHintsKB = vanillaAndThirdPartyHints;
                foreach (var staleHint in staleHints)
                {
                    Object.Destroy(staleHint);
                }
                RuntimeContext.Plugin?.Log.LogWarning(
                    "Quick Grab Materials hint was skipped because Valheim exposed no keyboard build-menu hint template.");
                _failedOwner = owner;
                return;
            }

            var hint = Object.Instantiate(template, template.transform.parent, false);
            _owner = owner;
            _hint = hint;
            hint.name = ObjectName;
            hint.transform.SetAsLastSibling();
            hint.SetActive(false);

            owner.m_buildMenuHintsKB = vanillaAndThirdPartyHints.Concat(new[] { hint }).ToArray();
            foreach (var staleHint in staleHints)
            {
                Object.Destroy(staleHint);
            }
        }

        private static void ApplyText(GameObject hint, string shortcutText)
        {
            var labels = hint.GetComponentsInChildren<TMP_Text>(true);
            if (labels.Length == 0)
            {
                throw new InvalidOperationException("the cloned vanilla hint contains no TMP text");
            }

            foreach (var label in labels)
            {
                Localization.instance?.RemoveTextFromCache(label);
            }

            if (labels.Length == 1)
            {
                labels[0].text = ActionText + " <mspace=0.6em>" + shortcutText + "</mspace>";
                return;
            }

            var keyLabel = labels.FirstOrDefault(IsKeyLabel) ?? labels[labels.Length - 1];
            var actionLabel = labels.FirstOrDefault(label => !ReferenceEquals(label, keyLabel)) ?? labels[0];
            actionLabel.text = ActionText;
            keyLabel.text = shortcutText;
            foreach (var label in labels)
            {
                if (!ReferenceEquals(label, actionLabel) && !ReferenceEquals(label, keyLabel))
                {
                    label.text = string.Empty;
                }
            }
        }

        private static bool IsKeyLabel(TMP_Text label)
        {
            var objectName = label == null || label.gameObject == null ? string.Empty : label.gameObject.name;
            var parentName = label == null || label.transform.parent == null
                ? string.Empty
                : label.transform.parent.gameObject.name;
            var text = label == null ? string.Empty : label.text ?? string.Empty;
            return objectName.IndexOf("key", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   parentName.IndexOf("key", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   text.IndexOf("$KEY_", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   text.IndexOf("<sprite", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string FormatShortcut(IEnumerable<KeyCode> modifiers)
        {
            var keys = (modifiers ?? Array.Empty<KeyCode>())
                .Select(FormatKey)
                .Where(text => !string.IsNullOrEmpty(text))
                .ToList();
            keys.Add("Click");
            return string.Join(" + ", keys);
        }

        private static string FormatKey(KeyCode key)
        {
            switch (key)
            {
                case KeyCode.LeftAlt: return "Left Alt";
                case KeyCode.RightAlt: return "Right Alt";
                case KeyCode.LeftControl: return "Left Ctrl";
                case KeyCode.RightControl: return "Right Ctrl";
                case KeyCode.LeftShift: return "Left Shift";
                case KeyCode.RightShift: return "Right Shift";
                case KeyCode.LeftCommand: return "Left Command";
                case KeyCode.RightCommand: return "Right Command";
                case KeyCode.LeftWindows: return "Left Windows";
                case KeyCode.RightWindows: return "Right Windows";
            }

            var raw = key.ToString();
            if (raw.StartsWith("Alpha", StringComparison.Ordinal) && raw.Length == 6)
            {
                return raw.Substring(5);
            }

            var formatted = new StringBuilder(raw.Length + 4);
            for (var index = 0; index < raw.Length; index++)
            {
                var character = raw[index];
                if (index > 0 && char.IsUpper(character) && !char.IsUpper(raw[index - 1]))
                {
                    formatted.Append(' ');
                }
                formatted.Append(character);
            }
            return formatted.ToString();
        }
    }
}
