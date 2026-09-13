using System;
using System.Globalization;

namespace Stackmaster.Core
{
    public static class ChestSortPreferencePolicy
    {
        public const string KeyPrefix = "com.jstack424.stackmaster/chest-auto-sort/v1";

        public static bool TryCreateKey(long playerId, long worldId, string chestId, out string key)
        {
            key = string.Empty;
            if (playerId == 0L || worldId == 0L || string.IsNullOrWhiteSpace(chestId))
            {
                return false;
            }

            key = KeyPrefix + "/" +
                  playerId.ToString(CultureInfo.InvariantCulture) + "/" +
                  worldId.ToString(CultureInfo.InvariantCulture) + "/" + chestId.Trim();
            return true;
        }

        public static bool TryInterpretStoredValue(bool hasStoredValue, int storedValue, out bool enabled)
        {
            if (!hasStoredValue)
            {
                enabled = true;
                return true;
            }

            if (storedValue == 0)
            {
                enabled = false;
                return true;
            }

            if (storedValue == 1)
            {
                enabled = true;
                return true;
            }

            enabled = false;
            return false;
        }
    }
}
