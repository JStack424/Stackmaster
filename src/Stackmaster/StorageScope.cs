#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using Stackmaster.Core;
using UnityEngine;

namespace Stackmaster
{
    internal sealed class StorageScope
    {
        internal StorageScope(StorageScopePlan plan)
        {
            Plan = plan;
        }

        internal StorageScopePlan Plan { get; }
        internal StorageScopeKind Kind => Plan.Kind;
        internal bool RequiresCompleteDiscovery => Kind == StorageScopeKind.WorkbenchMesh;
        internal string Description => Kind == StorageScopeKind.WorkbenchMesh ? "workbench mesh" :
            Kind == StorageScopeKind.NearbyRadius ? "nearby radius" : "unavailable";
        internal string Signature => Kind == StorageScopeKind.WorkbenchMesh
            ? Kind + ":" + string.Join("|", Plan.ConnectedZones.Select(zone =>
                zone.Id + "@" + zone.Center.X.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "," +
                zone.Center.Y.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "," +
                zone.Center.Z.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "," +
                zone.BuildRange.ToString("R", System.Globalization.CultureInfo.InvariantCulture)))
            : Kind + ":" + Plan.PlayerPosition.X.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "," +
                Plan.PlayerPosition.Y.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "," +
                Plan.PlayerPosition.Z.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ":" +
                Plan.FallbackRadius.ToString("R", System.Globalization.CultureInfo.InvariantCulture);

        internal bool Contains(Vector3 point)
        {
            return Plan.Contains(new ScopePoint(point.x, point.y, point.z));
        }
    }

    internal static class StorageScopeProvider
    {
        internal const string WorkbenchPrefabName = "piece_workbench";
        private static bool _failureLogged;

        internal static StorageScope Resolve(Player player)
        {
            var radius = RuntimeContext.Plugin != null ? RuntimeContext.Plugin.NearbyStorageRadius.Value : 20f;
            var playerPosition = player != null
                ? new ScopePoint(player.transform.position.x, player.transform.position.y, player.transform.position.z)
                : new ScopePoint(0, 0, 0);
            if (player == null || ZNetScene.instance == null)
            {
                LogFailureOnce("Storage scope could not be resolved safely; chest-powered features were skipped.");
                return new StorageScope(StorageScopePolicy.Unavailable(playerPosition, radius));
            }

            try
            {
                var workbenchPrefab = ZNetScene.instance.GetPrefab(WorkbenchPrefabName);
                if (workbenchPrefab == null)
                {
                    LogFailureOnce("The verified vanilla workbench prefab is unavailable; chest-powered features were skipped.");
                    return new StorageScope(StorageScopePolicy.Unavailable(playerPosition, radius));
                }
                var workbenchPrefabHash = ZNetScene.instance.GetPrefabHash(workbenchPrefab);
                var stations = CraftingStation.Instances;
                if (stations == null)
                {
                    LogFailureOnce("Loaded crafting stations could not be enumerated; chest-powered features were skipped.");
                    return new StorageScope(StorageScopePolicy.Unavailable(playerPosition, radius));
                }

                var zones = new List<WorkbenchZone>();
                foreach (var station in stations.OfType<CraftingStation>().ToArray())
                {
                    if (station == null || station.GetType() != typeof(CraftingStation) || !station.isActiveAndEnabled ||
                        station.gameObject == null || !station.gameObject.activeInHierarchy)
                    {
                        continue;
                    }
                    var view = station.GetComponent<ZNetView>();
                    var zdo = view != null && view.IsValid() ? view.GetZDO() : null;
                    if (zdo == null || zdo.m_uid.IsNone() || zdo.GetPrefab() != workbenchPrefabHash)
                    {
                        continue;
                    }
                    var buildRange = station.GetStationBuildRange();
                    if (float.IsNaN(buildRange) || float.IsInfinity(buildRange) || buildRange <= 0f)
                    {
                        continue;
                    }
                    var position = station.transform.position;
                    zones.Add(new WorkbenchZone(
                        zdo.m_uid.ToString(),
                        new ScopePoint(position.x, position.y, position.z),
                        buildRange));
                }

                return new StorageScope(StorageScopePolicy.Resolve(playerPosition, radius, zones));
            }
            catch (Exception exception)
            {
                LogFailureOnce("Storage scope resolution failed safely: " + exception.GetType().Name);
                return new StorageScope(StorageScopePolicy.Unavailable(playerPosition, radius));
            }
        }

        internal static void Reset()
        {
            _failureLogged = false;
        }

        private static void LogFailureOnce(string message)
        {
            if (_failureLogged) return;
            _failureLogged = true;
            RuntimeContext.Plugin?.Log.LogWarning(message);
        }
    }
}
