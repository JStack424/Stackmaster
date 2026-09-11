#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
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

    internal sealed class TargetDiscoveryDiagnostic
    {
        internal TargetDiscoveryDiagnostic(bool targetPresent, double distance, float radius)
        {
            TargetPresent = targetPresent;
            Distance = distance;
            Radius = radius;
            WithinRadius = targetPresent && distance <= radius;
        }

        internal bool TargetPresent { get; }
        internal double Distance { get; }
        internal float Radius { get; }
        internal bool WithinRadius { get; }
        internal bool Discovered { get; set; }
        internal string ObservedType { get; set; }
        internal bool? IsVanilla { get; set; }
        internal bool? HasNetworkView { get; set; }
        internal bool? NetworkViewValid { get; set; }
        internal bool? HasZdo { get; set; }
        internal bool? Refreshed { get; set; }
        internal bool? HasInventory { get; set; }
        internal bool? InUse { get; set; }
        internal bool? AccessGranted { get; set; }
        internal string RefreshFailure { get; set; }
        internal string AccessFailure { get; set; }

        internal string Format(bool searchTruncated, double searchMilliseconds, double searchBudgetMilliseconds)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "targetPresent={0} discovered={1} distance={2} radius={3:0.0} withinRadius={4} searchTruncated={5} searchMs={6:0.00} budgetMs={7:0.00} type={8} vanilla={9} nview={10} nviewValid={11} zdo={12} refresh={13} inventory={14} inUse={15} access={16} refreshError={17} accessError={18}",
                TargetPresent,
                Discovered,
                double.IsPositiveInfinity(Distance) ? "n/a" : Distance.ToString("0.0", CultureInfo.InvariantCulture),
                Radius,
                WithinRadius,
                searchTruncated,
                searchMilliseconds,
                searchBudgetMilliseconds,
                ObservedType ?? "n/a",
                FormatNullable(IsVanilla),
                FormatNullable(HasNetworkView),
                FormatNullable(NetworkViewValid),
                FormatNullable(HasZdo),
                FormatNullable(Refreshed),
                FormatNullable(HasInventory),
                FormatNullable(InUse),
                FormatNullable(AccessGranted),
                Sanitize(RefreshFailure),
                Sanitize(AccessFailure));
        }

        private static string FormatNullable(bool? value)
        {
            return value.HasValue ? value.Value.ToString() : "n/a";
        }

        private static string Sanitize(string value)
        {
            if (string.IsNullOrEmpty(value)) return "none";
            var oneLine = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
            return oneLine.Length <= 160 ? oneLine : oneLine.Substring(0, 160) + "…";
        }
    }

    internal sealed class DiscoveryResult
    {
        internal DiscoveryResult(
            IReadOnlyList<ContainerHandle> containers,
            bool truncated,
            double searchMilliseconds,
            TargetDiscoveryDiagnostic targetDiagnostic)
        {
            Containers = containers;
            Truncated = truncated;
            SearchMilliseconds = searchMilliseconds;
            TargetDiagnostic = targetDiagnostic;
        }

        internal IReadOnlyList<ContainerHandle> Containers { get; }
        internal bool Truncated { get; }
        internal double SearchMilliseconds { get; }
        internal TargetDiscoveryDiagnostic TargetDiagnostic { get; }
    }

    internal static class ContainerDiscovery
    {
        internal const double SearchBudgetMilliseconds = 12.0;
        private static readonly FieldInfo NetworkViewField = AccessTools.Field(typeof(Container), "m_nview");
        private static readonly MethodInfo CheckAccessMethod = AccessTools.Method(typeof(Container), "CheckAccess", new[] { typeof(long) });
        private static readonly MethodInfo CheckForChangesMethod = AccessTools.Method(typeof(Container), "CheckForChanges");

        internal static DiscoveryResult Discover(Player player, Container target, CompatibilityCatalog catalog, float radius)
        {
            var handles = new List<ContainerHandle>();
            var targetDistance = target != null
                ? (double)Vector3.Distance(player.transform.position, target.transform.position)
                : double.PositiveInfinity;
            var targetDiagnostic = new TargetDiscoveryDiagnostic(target != null, targetDistance, radius);

            // The explicit target is the user's requested action surface. Inspect it before
            // object discovery and outside the nearby-search budget so a busy scene can never
            // cause a valid targeted container to be omitted.
            if (target != null && targetDistance <= radius)
            {
                handles.Add(Inspect(player, target, catalog, targetDistance, true, targetDiagnostic));
            }

            // The 12 ms limit applies only to discovering and inspecting the remaining nearby
            // containers. Target validation above is intentionally never truncated by it.
            var stopwatch = Stopwatch.StartNew();
            var nearby = UnityEngine.Object.FindObjectsByType<Container>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .Where(container => container != null && container != target)
                .Select(container => new
                {
                    Container = container,
                    Distance = (double)Vector3.Distance(player.transform.position, container.transform.position)
                })
                .Where(candidate => candidate.Distance <= radius)
                .OrderBy(candidate => candidate.Distance)
                .ThenBy(candidate => candidate.Container.GetInstanceID())
                .ToArray();

            var inspectedNearby = 0;
            foreach (var candidate in nearby)
            {
                if (stopwatch.Elapsed.TotalMilliseconds >= SearchBudgetMilliseconds)
                {
                    break;
                }

                handles.Add(Inspect(player, candidate.Container, catalog, candidate.Distance, false, null));
                inspectedNearby++;
            }

            stopwatch.Stop();
            return new DiscoveryResult(handles, inspectedNearby < nearby.Length, stopwatch.Elapsed.TotalMilliseconds, targetDiagnostic);
        }

        private static ContainerHandle Inspect(
            Player player,
            Container container,
            CompatibilityCatalog catalog,
            double distance,
            bool isTarget,
            TargetDiscoveryDiagnostic diagnostic)
        {
            var view = NetworkViewField.GetValue(container) as ZNetView;
            var viewValid = view != null && view.IsValid();
            var zdo = viewValid ? view.GetZDO() : null;
            var id = zdo != null ? zdo.m_uid.ToString() : "instance:" + container.GetInstanceID();
            var observedType = container.GetType();
            var isVanilla = observedType == typeof(Container) && observedType.Assembly == typeof(Container).Assembly;
            string refreshFailure = null;
            var refreshed = isVanilla && viewValid && zdo != null && TryRefreshFromNetwork(container, out refreshFailure);
            var inventory = refreshed ? container.GetInventory() : null;
            var isKnown = refreshed && inventory != null;
            var inUse = isKnown && (container.IsInUse() || (container.m_wagon != null && container.m_wagon.InUse()));
            string accessFailure = null;
            var accessible = isKnown && !inUse && TryCheckAccess(player, container, out accessFailure);
            var capacity = inventory != null ? inventory.GetWidth() * inventory.GetHeight() : 0;
            var items = accessible
                ? InventorySnapshots.CaptureInventory(id, inventory, catalog).Items
                : Array.Empty<ItemStackSnapshot>();

            if (diagnostic != null)
            {
                diagnostic.Discovered = true;
                diagnostic.ObservedType = observedType.FullName;
                diagnostic.IsVanilla = isVanilla;
                diagnostic.HasNetworkView = view != null;
                diagnostic.NetworkViewValid = viewValid;
                diagnostic.HasZdo = zdo != null;
                diagnostic.Refreshed = refreshed;
                diagnostic.HasInventory = inventory != null;
                diagnostic.InUse = isKnown ? (bool?)inUse : null;
                diagnostic.AccessGranted = isKnown && !inUse ? (bool?)accessible : null;
                diagnostic.RefreshFailure = refreshFailure;
                diagnostic.AccessFailure = accessFailure;
            }

            var snapshot = new ContainerSnapshot(
                id,
                distance,
                isTarget,
                isKnown,
                accessible,
                isVanilla,
                inUse,
                capacity,
                items);
            return new ContainerHandle(id, container, view, snapshot);
        }

        internal static bool RefreshFromNetwork(Container container)
        {
            string failure;
            var refreshed = TryRefreshFromNetwork(container, out failure);
            if (!refreshed && RuntimeContext.Plugin != null)
            {
                RuntimeContext.Plugin.Log.LogWarning("Container refresh failed safely: " + failure);
            }
            return refreshed;
        }

        private static bool TryRefreshFromNetwork(Container container, out string failure)
        {
            failure = null;
            try
            {
                CheckForChangesMethod.Invoke(container, null);
                return true;
            }
            catch (Exception exception)
            {
                var invocation = exception as TargetInvocationException;
                failure = invocation != null
                    ? invocation.InnerException?.Message ?? invocation.Message
                    : exception.Message;
                return false;
            }
        }

        internal static bool CheckAccess(Player player, Container container)
        {
            string failure;
            var accessible = TryCheckAccess(player, container, out failure);
            if (!accessible && !string.IsNullOrEmpty(failure) && RuntimeContext.Plugin != null)
            {
                RuntimeContext.Plugin.Log.LogWarning("Container access check failed safely: " + failure);
            }
            return accessible;
        }

        private static bool TryCheckAccess(Player player, Container container, out string failure)
        {
            failure = null;
            try
            {
                if (container.m_checkGuardStone && !PrivateArea.CheckAccess(container.transform.position, 0f, false, false))
                {
                    return false;
                }
                return (bool)CheckAccessMethod.Invoke(container, new object[] { player.GetPlayerID() });
            }
            catch (Exception exception)
            {
                var invocation = exception as TargetInvocationException;
                failure = invocation != null
                    ? invocation.InnerException?.Message ?? invocation.Message
                    : exception.Message;
                return false;
            }
        }
    }
}
