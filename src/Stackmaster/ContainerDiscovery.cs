#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Stackmaster.Core;
using UnityEngine;

namespace Stackmaster
{
    internal sealed class ContainerHandle
    {
        internal ContainerHandle(string id, Container container, ZNetView networkView, ContainerSnapshot snapshot)
        {
            Id = id;
            Container = container;
            NetworkView = networkView;
            Snapshot = snapshot;
        }

        internal string Id { get; }
        internal Container Container { get; }
        internal ZNetView NetworkView { get; }
        internal ContainerSnapshot Snapshot { get; }
    }

    internal sealed class DiscoveryResult
    {
        internal DiscoveryResult(IReadOnlyList<ContainerHandle> containers, bool truncated)
        {
            Containers = containers;
            Truncated = truncated;
        }

        internal IReadOnlyList<ContainerHandle> Containers { get; }
        internal bool Truncated { get; }
    }

    internal static class ContainerDiscovery
    {
        private const double SearchBudgetMilliseconds = 12.0;
        private static readonly FieldInfo NetworkViewField = AccessTools.Field(typeof(Container), "m_nview");
        private static readonly MethodInfo CheckAccessMethod = AccessTools.Method(typeof(Container), "CheckAccess", new[] { typeof(long) });
        private static readonly MethodInfo CheckForChangesMethod = AccessTools.Method(typeof(Container), "CheckForChanges");

        internal static DiscoveryResult Discover(Player player, Container target, CompatibilityCatalog catalog, float radius)
        {
            var stopwatch = Stopwatch.StartNew();
            var all = UnityEngine.Object.FindObjectsByType<Container>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            var candidates = all
                .Where(container => container != null && Vector3.Distance(player.transform.position, container.transform.position) <= radius)
                .Select(container => new
                {
                    Container = container,
                    Distance = (double)Vector3.Distance(player.transform.position, container.transform.position),
                    IsTarget = container == target
                })
                .OrderBy(value => value.IsTarget ? 0 : 1)
                .ThenBy(value => value.IsTarget ? 0d : value.Distance)
                .ThenBy(value => value.Container.GetInstanceID())
                .ToArray();

            var handles = new List<ContainerHandle>();
            var truncated = false;
            foreach (var candidate in candidates)
            {
                if (stopwatch.Elapsed.TotalMilliseconds >= SearchBudgetMilliseconds)
                {
                    truncated = true;
                    break;
                }

                var container = candidate.Container;
                var view = NetworkViewField.GetValue(container) as ZNetView;
                var zdo = view != null && view.IsValid() ? view.GetZDO() : null;
                var id = zdo != null ? zdo.m_uid.ToString() : "instance:" + container.GetInstanceID();
                var isVanilla = container.GetType() == typeof(Container) && container.GetType().Assembly == typeof(Container).Assembly;
                var refreshed = isVanilla && view != null && view.IsValid() && zdo != null && RefreshFromNetwork(container);
                var inventory = refreshed ? container.GetInventory() : null;
                var isKnown = refreshed && inventory != null;
                var inUse = isKnown && (container.IsInUse() || (container.m_wagon != null && container.m_wagon.InUse()));
                var accessible = isKnown && !inUse && CheckAccess(player, container);
                var capacity = inventory != null ? inventory.GetWidth() * inventory.GetHeight() : 0;
                var items = accessible
                    ? InventorySnapshots.CaptureInventory(id, inventory, catalog).Items
                    : Array.Empty<ItemStackSnapshot>();

                var snapshot = new ContainerSnapshot(
                    id,
                    candidate.Distance,
                    candidate.IsTarget,
                    isKnown,
                    accessible,
                    isVanilla,
                    inUse,
                    capacity,
                    items);
                handles.Add(new ContainerHandle(id, container, view, snapshot));
            }

            return new DiscoveryResult(handles, truncated);
        }

        internal static bool RefreshFromNetwork(Container container)
        {
            try
            {
                CheckForChangesMethod.Invoke(container, null);
                return true;
            }
            catch (TargetInvocationException exception)
            {
                RuntimeContext.Plugin.Log.LogWarning("Container refresh failed safely: " + (exception.InnerException?.Message ?? exception.Message));
                return false;
            }
        }

        internal static bool CheckAccess(Player player, Container container)
        {
            try
            {
                if (container.m_checkGuardStone && !PrivateArea.CheckAccess(container.transform.position, 0f, false, false))
                {
                    return false;
                }
                return (bool)CheckAccessMethod.Invoke(container, new object[] { player.GetPlayerID() });
            }
            catch (TargetInvocationException exception)
            {
                RuntimeContext.Plugin.Log.LogWarning("Container access check failed safely: " + (exception.InnerException?.Message ?? exception.Message));
                return false;
            }
        }
    }
}
