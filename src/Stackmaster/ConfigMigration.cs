#nullable disable
using System;
using BepInEx.Configuration;

namespace Stackmaster
{
    /// <summary>
    /// Narrow migrations for settings whose BepInEx section/key identity changed.
    /// The two released storage permissions default to enabled, so a false value
    /// from either the legacy or current key is the only non-default choice that
    /// must win while the legacy key is retired.
    /// </summary>
    internal static class ConfigMigration
    {
        internal static ConfigEntry<bool> BindRenamedDefaultEnabledBoolean(
            ConfigFile config,
            string section,
            string legacyKey,
            string currentKey,
            string currentDescription)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            var originalSaveOnConfigSet = config.SaveOnConfigSet;
            config.SaveOnConfigSet = false;
            try
            {
                // BepInEx 5.4.23.5 keeps unknown file values as internal orphaned
                // entries. Binding the old definition is its public, typed way to
                // hydrate that value; Remove then prevents the obsolete option from
                // being written or exposed after this synchronous migration.
                var legacy = config.Bind(section, legacyKey, true,
                    "Legacy Stackmaster setting; migrated automatically and removed from the saved configuration.");
                var current = config.Bind(section, currentKey, true, currentDescription);

                // Both keys have a canonical default of true. A stored false is the
                // only deviation from default, so false-safe conjunction preserves
                // old false choices, existing new false choices, missing/default
                // behavior, and repeated-launch idempotence without private API use.
                current.Value = legacy.Value && current.Value;

                if (!config.Remove(legacy.Definition))
                {
                    throw new InvalidOperationException("Could not retire legacy Stackmaster configuration key: " + legacyKey);
                }

                config.Save();
                return current;
            }
            finally
            {
                config.SaveOnConfigSet = originalSaveOnConfigSet;
            }
        }
    }
}
