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
    }
}
