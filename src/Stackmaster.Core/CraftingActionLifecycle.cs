#nullable enable

namespace Stackmaster.Core
{
    public enum CraftingActionPhase
    {
        Idle,
        Acquiring,
        Reserved,
        Crafting,
        Transferred
    }

    /// <summary>
    /// A generation-guarded lifecycle for one user-initiated craft. The token makes delayed
    /// ownership/coroutine callbacks from a canceled action or prior server session harmless.
    /// </summary>
    public sealed class CraftingActionLifecycle
    {
        private int _generation;

        public CraftingActionPhase Phase { get; private set; } = CraftingActionPhase.Idle;
        public int Generation => _generation;
        public bool IsActive => Phase != CraftingActionPhase.Idle && Phase != CraftingActionPhase.Transferred;

        public int Begin(bool needsOwnership)
        {
            _generation++;
            Phase = needsOwnership ? CraftingActionPhase.Acquiring : CraftingActionPhase.Reserved;
            return _generation;
        }

        public bool MarkReserved(int generation)
        {
            if (generation != _generation || Phase != CraftingActionPhase.Acquiring) return false;
            Phase = CraftingActionPhase.Reserved;
            return true;
        }

        public bool MarkCrafting(int generation)
        {
            if (generation != _generation || Phase != CraftingActionPhase.Reserved) return false;
            Phase = CraftingActionPhase.Crafting;
            return true;
        }

        public bool TransferToTransaction(int generation)
        {
            if (generation != _generation || Phase != CraftingActionPhase.Crafting) return false;
            Phase = CraftingActionPhase.Transferred;
            return true;
        }

        public void FinishTransferred(int generation)
        {
            if (generation == _generation && Phase == CraftingActionPhase.Transferred)
            {
                Phase = CraftingActionPhase.Idle;
            }
        }

        public void Cancel()
        {
            _generation++;
            Phase = CraftingActionPhase.Idle;
        }

        public bool Matches(int generation) => generation == _generation && IsActive;
    }
}
