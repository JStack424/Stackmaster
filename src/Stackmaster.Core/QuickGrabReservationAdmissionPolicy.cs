using System;

namespace Stackmaster.Core
{
    /// <summary>
    /// Central admission rule for build-piece reservations. Decorative material stacks and piles
    /// remain valid Quick Grab material requests, but never become persistent build intent.
    /// The player-visible localized name is authoritative when localization resolved; raw and
    /// stable names are consulted only when it did not.
    /// </summary>
    public static class QuickGrabReservationAdmissionPolicy
    {
        public static bool IsMaterialOnly(
            string? localizedDisplayName,
            bool localizationResolved,
            string? rawDisplayName,
            string? stablePrefabName)
        {
            if (localizationResolved)
            {
                return ContainsMaterialStructureWord(localizedDisplayName);
            }

            return ContainsMaterialStructureWord(rawDisplayName) ||
                   ContainsMaterialStructureWord(stablePrefabName);
        }

        private static bool ContainsMaterialStructureWord(string? value)
            => !string.IsNullOrEmpty(value) &&
               (value!.IndexOf("stack", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("pile", StringComparison.OrdinalIgnoreCase) >= 0);
    }
}
