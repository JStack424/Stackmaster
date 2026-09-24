using System;

namespace Stackmaster.Core
{
    public static class FailedDepositPolicy
    {
        public static int AttemptedQuantity(ItemStackSnapshot item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            if (item.IsQuickBar || item.IsEquipped) return 0;
            if (!item.IsProtected) return item.Quantity;
            return item.ReplenishmentTarget.HasValue
                ? Math.Max(0, item.Quantity - item.ReplenishmentTarget.Value)
                : 0;
        }

        public static int FailedRemainder(ItemStackSnapshot original, int survivingStackQuantity)
        {
            if (original == null) throw new ArgumentNullException(nameof(original));
            if (survivingStackQuantity < 0) throw new ArgumentOutOfRangeException(nameof(survivingStackQuantity));
            var attempted = AttemptedQuantity(original);
            if (attempted == 0 || survivingStackQuantity == 0) return 0;
            var intentionallyRetained = original.IsProtected && original.ReplenishmentTarget.HasValue
                ? original.ReplenishmentTarget.Value
                : 0;
            return Math.Min(attempted, Math.Max(0, survivingStackQuantity - intentionallyRetained));
        }
    }
}
