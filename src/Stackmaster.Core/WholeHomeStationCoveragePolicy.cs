using System;

namespace Stackmaster.Core
{
    /// <summary>Pure home-membership rule for one already name-matched crafting station.</summary>
    public static class WholeHomeStationCoveragePolicy
    {
        public static bool Qualifies(
            StorageScopePlan playerHome,
            ScopePoint stationPosition,
            bool isLoadedAndActive,
            bool hasStableIdentity)
            => playerHome != null &&
               playerHome.Kind == StorageScopeKind.WorkbenchMesh &&
               isLoadedAndActive &&
               hasStableIdentity &&
               playerHome.Contains(stationPosition);
    }
}
