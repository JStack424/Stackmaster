using System;

namespace Stackmaster.Core
{
    /// <summary>
    /// The quantity in one player slot which serves persistent expedition reservations after a
    /// Quick Stack plan. Runtime integrations can use these immutable assignments to decorate the
    /// exact carried stacks without inferring reservation state from item totals.
    /// </summary>
    public sealed class ReservationSlotAllocation
    {
        public ReservationSlotAllocation(
            int playerSlot,
            string compatibilityKey,
            string itemName,
            int quality,
            int quantity)
        {
            if (playerSlot < 0) throw new ArgumentOutOfRangeException(nameof(playerSlot));
            if (string.IsNullOrWhiteSpace(compatibilityKey)) throw new ArgumentException("A compatibility key is required.", nameof(compatibilityKey));
            if (string.IsNullOrWhiteSpace(itemName)) throw new ArgumentException("A resource item name is required.", nameof(itemName));
            if (quality <= 0) throw new ArgumentOutOfRangeException(nameof(quality));
            if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));

            PlayerSlot = playerSlot;
            CompatibilityKey = compatibilityKey;
            ItemName = itemName;
            Quality = quality;
            Quantity = quantity;
        }

        public int PlayerSlot { get; }
        public string CompatibilityKey { get; }
        public string ItemName { get; }
        public int Quality { get; }
        public int Quantity { get; }
    }

    public sealed class ReservationReplenishmentShortage
    {
        public ReservationReplenishmentShortage(string itemName, int quality, int missingQuantity)
        {
            if (string.IsNullOrWhiteSpace(itemName)) throw new ArgumentException("A resource item name is required.", nameof(itemName));
            if (quality == 0 || quality < -1) throw new ArgumentOutOfRangeException(nameof(quality));
            if (missingQuantity <= 0) throw new ArgumentOutOfRangeException(nameof(missingQuantity));
            ItemName = itemName;
            Quality = quality;
            MissingQuantity = missingQuantity;
        }

        public string ItemName { get; }
        public int Quality { get; }
        public int MissingQuantity { get; }
    }

    public enum ReplenishmentReason
    {
        None,
        ExplicitProtectedTarget,
        ExpeditionReservation
    }
}
