#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using Stackmaster.Core;
using UnityEngine;

namespace Stackmaster
{
    /// <summary>
    /// Resolves Hammer-piece station access without changing any station radius. The connected
    /// canonical Workbench mesh is the only home boundary; non-Workbench stations are occupants,
    /// never graph nodes.
    /// </summary>
    internal static class WholeHomeCraftingStationService
    {
        internal static bool HasBuildStation(Player player, string requiredStationName)
        {
            if (player == null || string.IsNullOrEmpty(requiredStationName)) return false;

            // Preserve vanilla first. This also keeps exact outside-home behavior and avoids any
            // dependence on home topology when the ordinary station radius already qualifies.
            if (CraftingStation.HaveBuildStationInRange(requiredStationName, player.transform.position) != null)
            {
                return true;
            }

            var plugin = RuntimeContext.Plugin;
            if (plugin == null || plugin.WholeHomeCraftingStationsEnabled == null ||
                !plugin.WholeHomeCraftingStationsEnabled.Value)
            {
                return false;
            }

            var scope = StorageScopeProvider.Resolve(player);
            if (scope == null || scope.Kind != StorageScopeKind.WorkbenchMesh) return false;
            var stations = CraftingStation.Instances;
            if (stations == null) return false;

            try
            {
                var matching = new List<CraftingStation>();
                var identities = new HashSet<string>(StringComparer.Ordinal);
                foreach (var station in stations.OfType<CraftingStation>().ToArray())
                {
                    var validLoaded = IsValidLoadedStation(station);
                    if (!validLoaded || !string.Equals(station.m_name, requiredStationName, StringComparison.Ordinal))
                    {
                        continue;
                    }
                    var position = station.transform.position;
                    var zdo = station.GetComponent<ZNetView>().GetZDO();
                    if (!WholeHomeStationCoveragePolicy.Qualifies(
                            scope.Plan,
                            new ScopePoint(position.x, position.y, position.z),
                            validLoaded,
                            zdo != null && !zdo.m_uid.IsNone()))
                    {
                        continue;
                    }
                    if (zdo == null || !identities.Add(zdo.m_uid.ToString()))
                    {
                        // Ambiguous loaded identities fail closed rather than granting access.
                        return false;
                    }
                    matching.Add(station);
                }
                return matching.Count > 0;
            }
            catch (Exception exception)
            {
                RuntimeContext.Plugin?.Log.LogWarning("Whole-home crafting-station check failed safely: " + exception.GetType().Name);
                return false;
            }
        }

        private static bool IsValidLoadedStation(CraftingStation station)
        {
            if (station == null || !station.isActiveAndEnabled || station.gameObject == null ||
                !station.gameObject.activeInHierarchy)
            {
                return false;
            }
            var view = station.GetComponent<ZNetView>();
            var zdo = view != null && view.IsValid() ? view.GetZDO() : null;
            return zdo != null && !zdo.m_uid.IsNone();
        }
    }
}
