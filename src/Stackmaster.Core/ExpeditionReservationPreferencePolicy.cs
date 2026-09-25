using System.Globalization;

namespace Stackmaster.Core
{
    /// <summary>
    /// Local preference identity for expedition reservations. Player and world are both required
    /// parts of the key so the same character can keep independent plans in different worlds and
    /// different characters never share reservations in one world.
    /// </summary>
    public static class ExpeditionReservationPreferencePolicy
    {
        public const string Prefix = "com.jstack424.stackmaster/expedition-reservations/v1/";

        public static string Key(long playerId, long worldId)
            => Prefix + playerId.ToString(CultureInfo.InvariantCulture) + "/" +
               worldId.ToString(CultureInfo.InvariantCulture);
    }
}
