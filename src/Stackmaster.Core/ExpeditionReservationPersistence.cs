using System;
using System.Collections.Generic;

namespace Stackmaster.Core
{
    public enum ExpeditionReservationPersistenceResult
    {
        Added,
        Incremented,
        RecipeChanged,
        CapacityExceeded,
        PersistenceFailed
    }

    /// <summary>
    /// Builds a detached reservation candidate and exposes it only after the supplied durable
    /// writer succeeds. A failed or throwing writer leaves the caller's active state unchanged.
    /// </summary>
    public static class ExpeditionReservationPersistence
    {
        public static bool TryAddDurably(
            ExpeditionReservationState current,
            string pieceKey,
            string displayName,
            IReadOnlyList<ResourceRequirement> requirements,
            Func<string, bool> persist,
            out ExpeditionReservationState committed,
            out ExpeditionReservationPersistenceResult result)
        {
            if (current == null) throw new ArgumentNullException(nameof(current));
            if (requirements == null) throw new ArgumentNullException(nameof(requirements));
            if (persist == null) throw new ArgumentNullException(nameof(persist));

            committed = current;
            ExpeditionReservationState candidate;
            if (!ExpeditionReservationState.TryParse(current.Serialize(), out candidate))
            {
                result = ExpeditionReservationPersistenceResult.PersistenceFailed;
                return false;
            }

            var addResult = candidate.AddQuickGrabReservation(pieceKey, displayName, requirements);
            switch (addResult)
            {
                case ExpeditionReservationAddResult.RecipeChanged:
                    result = ExpeditionReservationPersistenceResult.RecipeChanged;
                    return false;
                case ExpeditionReservationAddResult.CapacityExceeded:
                    result = ExpeditionReservationPersistenceResult.CapacityExceeded;
                    return false;
                case ExpeditionReservationAddResult.Added:
                    result = ExpeditionReservationPersistenceResult.Added;
                    break;
                case ExpeditionReservationAddResult.Incremented:
                    result = ExpeditionReservationPersistenceResult.Incremented;
                    break;
                default:
                    result = ExpeditionReservationPersistenceResult.PersistenceFailed;
                    return false;
            }

            var payload = candidate.Serialize();
            try
            {
                if (!persist(payload))
                {
                    result = ExpeditionReservationPersistenceResult.PersistenceFailed;
                    return false;
                }
            }
            catch
            {
                result = ExpeditionReservationPersistenceResult.PersistenceFailed;
                return false;
            }

            committed = candidate;
            return true;
        }
    }
}
