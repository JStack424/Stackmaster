using System;
using System.Globalization;

namespace Stackmaster.Core
{
    public static class QuickStackSummaryFormatter
    {
        public static string Format(int depositedUnits, int replenishedUnits, int leftBehindUnits)
        {
            if (depositedUnits < 0) throw new ArgumentOutOfRangeException(nameof(depositedUnits));
            if (replenishedUnits < 0) throw new ArgumentOutOfRangeException(nameof(replenishedUnits));
            if (leftBehindUnits < 0) throw new ArgumentOutOfRangeException(nameof(leftBehindUnits));

            return "Stackmaster:\n" +
                   "• " + depositedUnits.ToString(CultureInfo.InvariantCulture) + " deposited\n" +
                   "• " + replenishedUnits.ToString(CultureInfo.InvariantCulture) + " replenished\n" +
                   "• " + leftBehindUnits.ToString(CultureInfo.InvariantCulture) + " left behind";
        }

        public static string FormatReservationWithoutMaterials(string pieceName, int reservedCount)
        {
            if (string.IsNullOrWhiteSpace(pieceName)) throw new ArgumentException("A piece name is required.", nameof(pieceName));
            if (reservedCount <= 0) throw new ArgumentOutOfRangeException(nameof(reservedCount));
            return "Stackmaster:\n• Reserved " + pieceName.Trim() + " ×" +
                   reservedCount.ToString(CultureInfo.InvariantCulture) + " — materials not gathered.";
        }
    }
}
