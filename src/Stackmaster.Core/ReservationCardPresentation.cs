using System;

namespace Stackmaster.Core
{
    /// <summary>Shared, deterministic card labels used by every reservation strip.</summary>
    public static class ReservationCardPresentation
    {
        public static string RemovalLabel(int count)
        {
            if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
            return count == 1 ? "X" : "-";
        }
    }
}
