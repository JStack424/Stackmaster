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
        internal ContainerHandle(
            string id,
            Container container,
            ZNetView networkView,
            ContainerSnapshot snapshot,
            Inventory resourceInventory,
            uint resourceDataRevision,
            ZDOID resourceZdoId,
            ushort resourceOwnerRevision,
            long resourceOwner,
            byte[] resourcePayload,
            bool resourceReadable)
        {
            Id = id;
            Container = container;
            NetworkView = networkView;
            Snapshot = snapshot;
            ResourceInventory = resourceInventory;
            ResourceDataRevision = resourceDataRevision;
            ResourceZdoId = resourceZdoId;
            ResourceOwnerRevision = resourceOwnerRevision;
            ResourceOwner = resourceOwner;
            ResourcePayload = resourcePayload;
            ResourceReadable = resourceReadable;
        }

        internal string Id { get; }
        internal Container Container { get; }
        internal ZNetView NetworkView { get; }
        internal ContainerSnapshot Snapshot { get; }
        // Detached inventory decoded from the ZDO. Availability reads this copy and therefore
        // never claims ownership or mutates the live Container inventory.
        internal Inventory ResourceInventory { get; }
        internal uint ResourceDataRevision { get; }
        internal ZDOID ResourceZdoId { get; }
        internal ushort ResourceOwnerRevision { get; }
        internal long ResourceOwner { get; }
        // Exact copy of the serialized inventory payload used to create ResourceInventory.
        // Display-cache reuse compares it byte-for-byte as well as checking ZDO revisions.
        internal byte[] ResourcePayload { get; }
        internal bool ResourceReadable { get; }
    }

    internal sealed class TargetDiscoveryDiagnostic
    {
        internal TargetDiscoveryDiagnostic(bool targetPresent, double distance, StorageScope scope, Vector3 targetPosition)
        {
            TargetPresent = targetPresent;
            Distance = distance;
            Scope = scope != null ? scope.Description : "unavailable";
            WithinScope = targetPresent && scope != null && scope.Contains(targetPosition);
        }

        internal bool TargetPresent { get; }
        internal double Distance { get; }
        internal string Scope { get; }
        internal bool WithinScope { get; }
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

        internal string Format(DiscoveryResult discovery)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "targetPresent={0} discovered={1} distance={2} scope={3} withinScope={4} searchTruncated={5} truncationReason={6} searchMs={7:0.00} objectScanMs={8:0.00} inspectionMs={9:0.00} budgetMs={10:0.00} candidates={11} inspected={12} minimumBeforeBudget={13} maximumNearby={14} type={15} vanilla={16} nview={17} nviewValid={18} zdo={19} refresh={20} inventory={21} inUse={22} access={23} refreshError={24} accessError={25}",
                TargetPresent,
                Discovered,
                double.IsPositiveInfinity(Distance) ? "n/a" : Distance.ToString("0.0", CultureInfo.InvariantCulture),
                Scope,
                WithinScope,
                discovery.Truncated,
                discovery.TruncationReason ?? "none",
                discovery.SearchMilliseconds,
                discovery.ObjectScanMilliseconds,
                discovery.InspectionMilliseconds,
                ContainerDiscovery.SearchBudgetMilliseconds,
                discovery.NearbyCandidates,
                discovery.InspectedNearby,
                ContainerDiscovery.MinimumNearbyContainersBeforeBudget,
                ContainerDiscovery.MaximumNearbyContainers,
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
            double objectScanMilliseconds,
            double inspectionMilliseconds,
            int nearbyCandidates,
            int inspectedNearby,
            string truncationReason,
            TargetDiscoveryDiagnostic targetDiagnostic,
            StorageScope scope,
            double membershipStabilityDistance)
        {
            Containers = containers;
            Truncated = truncated;
            SearchMilliseconds = searchMilliseconds;
            ObjectScanMilliseconds = objectScanMilliseconds;
            InspectionMilliseconds = inspectionMilliseconds;
            NearbyCandidates = nearbyCandidates;
            InspectedNearby = inspectedNearby;
            TruncationReason = truncationReason;
            TargetDiagnostic = targetDiagnostic;
            Scope = scope;
            MembershipStabilityDistance = membershipStabilityDistance;
        }

        internal IReadOnlyList<ContainerHandle> Containers { get; }
        internal bool Truncated { get; }
        internal double SearchMilliseconds { get; }
        internal double ObjectScanMilliseconds { get; }
        internal double InspectionMilliseconds { get; }
        internal int NearbyCandidates { get; }
        internal int InspectedNearby { get; }
        internal string TruncationReason { get; }
        internal TargetDiscoveryDiagnostic TargetDiagnostic { get; }
        internal StorageScope Scope { get; }
        // In fallback-radius scope, the shortest player movement that could change membership
        // for any container seen by this complete scene query. Workbench membership is position-
        // independent while its structural signature is unchanged.
        internal double MembershipStabilityDistance { get; }
    }

    internal static class ContainerDiscovery
    {
        // Container discovery runs only for an explicit storage action. Always inspect a useful
        // normal-base prefix, then enforce a relaxed interaction budget and a hard dense-base
        // backstop. The budget starts after Unity's global object query and distance ordering so
        // ordinary scenes cannot spend the entire allowance before the first nearby chest.
        internal const double SearchBudgetMilliseconds = 25.0;
        internal const int MinimumNearbyContainersBeforeBudget = 8;
        internal const int MaximumNearbyContainers = 128;
        private static readonly NearbyInspectionPolicy NearbyPolicy = new NearbyInspectionPolicy(
            MinimumNearbyContainersBeforeBudget,
            MaximumNearbyContainers,
            SearchBudgetMilliseconds);
        private static readonly FieldInfo NetworkViewField = AccessTools.Field(typeof(Container), "m_nview");
        private static readonly MethodInfo CheckAccessMethod = AccessTools.Method(typeof(Container), "CheckAccess", new[] { typeof(long) });
        private static readonly MethodInfo CheckForChangesMethod = AccessTools.Method(typeof(Container), "CheckForChanges");

        internal static DiscoveryResult Discover(
            Player player,
            Container target,
            CompatibilityCatalog catalog,
            StorageScope scope,
            bool requireComplete = false,
            bool resourceReadOnly = false)
        {
            var handles = new List<ContainerHandle>();
            scope = scope ?? StorageScopeProvider.Resolve(player);
            var targetDistance = target != null
                ? (double)Vector3.Distance(player.transform.position, target.transform.position)
                : double.PositiveInfinity;
            var targetDiagnostic = new TargetDiscoveryDiagnostic(
                target != null,
                targetDistance,
                scope,
                target != null ? target.transform.position : default(Vector3));

            // The explicit target is the user's requested action surface. Inspect it before
            // object discovery and outside the nearby-search budget so a busy scene can never
            // cause a valid targeted container to be omitted.
            if (target != null && scope.Contains(target.transform.position))
            {
                handles.Add(Inspect(player, target, catalog, targetDistance, true, targetDiagnostic, resourceReadOnly));
            }

            var totalStopwatch = Stopwatch.StartNew();
            var objectScanStopwatch = Stopwatch.StartNew();
            var allCandidates = UnityEngine.Object.FindObjectsByType<Container>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .Where(container => container != null && container != target)
                .Select(container => new
                {
                    Container = container,
                    Distance = (double)Vector3.Distance(player.transform.position, container.transform.position)
                })
                .ToArray();
            var membershipStabilityDistance = scope.Kind == StorageScopeKind.NearbyRadius
                ? allCandidates
                    .Select(candidate => Math.Abs(candidate.Distance - scope.Plan.FallbackRadius))
                    .DefaultIfEmpty(double.PositiveInfinity)
                    .Min()
                : double.PositiveInfinity;
            var nearby = allCandidates
                .Where(candidate => scope.Contains(candidate.Container.transform.position))
                .OrderBy(candidate => candidate.Distance)
                .ThenBy(candidate => candidate.Container.GetInstanceID())
                .ToArray();
            objectScanStopwatch.Stop();

            // Do not charge Unity's scene-wide object query and deterministic ordering against
            // nearby inventory inspection. On Joe's test machine that query alone could exceed
            // the previous 12 ms wall-clock limit even with only three nearby chests.
            var inspectionStopwatch = Stopwatch.StartNew();
            var inspectedNearby = 0;
            string truncationReason = null;
            foreach (var candidate in nearby)
            {
                var elapsedInspectionMilliseconds = inspectionStopwatch.Elapsed.TotalMilliseconds;
                if (!requireComplete && !scope.RequiresCompleteDiscovery &&
                    !NearbyPolicy.CanInspectNext(inspectedNearby, elapsedInspectionMilliseconds))
                {
                    truncationReason = NearbyPolicy.StopReason(inspectedNearby, elapsedInspectionMilliseconds);
                    break;
                }

                handles.Add(Inspect(player, candidate.Container, catalog, candidate.Distance, false, null, resourceReadOnly));
                inspectedNearby++;
            }

            inspectionStopwatch.Stop();
            totalStopwatch.Stop();
            var truncated = inspectedNearby < nearby.Length;
            if (truncated && string.IsNullOrEmpty(truncationReason))
            {
                truncationReason = "responsiveness limit reached";
            }
            return new DiscoveryResult(
                handles,
                truncated,
                totalStopwatch.Elapsed.TotalMilliseconds,
                objectScanStopwatch.Elapsed.TotalMilliseconds,
                inspectionStopwatch.Elapsed.TotalMilliseconds,
                nearby.Length,
                inspectedNearby,
                truncationReason,
                targetDiagnostic,
                scope,
                membershipStabilityDistance);
        }

        private static ContainerHandle Inspect(
            Player player,
            Container container,
            CompatibilityCatalog catalog,
            double distance,
            bool isTarget,
            TargetDiscoveryDiagnostic diagnostic,
            bool resourceReadOnly)
        {
            var view = NetworkViewField.GetValue(container) as ZNetView;
            var viewValid = view != null && view.IsValid();
            var zdo = viewValid ? view.GetZDO() : null;
            var id = zdo != null ? zdo.m_uid.ToString() : "instance:" + container.GetInstanceID();
            var observedType = container.GetType();
            var isVanilla = observedType == typeof(Container) && observedType.Assembly == typeof(Container).Assembly;
            string refreshFailure = null;
            // Resource-only discovery must not call CheckForChanges or GetInventory: both touch
            // the live Container state. It reads only a detached serialized ZDO snapshot below.
            var refreshed = !resourceReadOnly && isVanilla && viewValid && zdo != null &&
                TryRefreshFromNetwork(container, out refreshFailure);
            var inventory = refreshed ? container.GetInventory() : null;
            var isKnown = refreshed && inventory != null;
            var locallyOpenTarget = isTarget && StorageAction.IsLocalOpenTarget(container);
            var inUse = isKnown &&
                ((!locallyOpenTarget && container.IsInUse()) || (container.m_wagon != null && container.m_wagon.InUse()));
            string accessFailure = null;
            var accessCheckable = resourceReadOnly
                ? isVanilla && viewValid && zdo != null
                : isKnown;
            var accessGranted = accessCheckable && TryCheckAccess(player, container, out accessFailure);

            // Container.Load reads this same serialized ZDO field without checking ownership.
            // Decode into a detached Inventory so HUD/accounting reads never touch the live
            // inventory and can include accessible remote-owned (including currently open)
            // vanilla chests. Revision-before/after equality rejects a torn network snapshot.
            Inventory resourceInventory = null;
            uint resourceDataRevision = 0;
            byte[] resourcePayload = null;
            string resourceFailure = null;
            var resourceDecoded = resourceReadOnly && isVanilla && viewValid && zdo != null &&
                TryReadSerializedInventory(container, zdo, out resourceInventory, out resourceDataRevision, out resourcePayload, out resourceFailure);
            var readPlan = ResourceSnapshotPolicy.Evaluate(
                resourceReadOnly,
                isKnown,
                accessGranted,
                inUse,
                resourceDecoded);
            var accessible = readPlan.CaptureLiveInventory;
            var capacity = inventory != null ? inventory.GetWidth() * inventory.GetHeight() : 0;
            // A read-only remote/unopened container deliberately has no live Inventory. Never
            // pass that null reference into ordinary inventory capture; its detached inventory
            // is carried separately by ContainerHandle.ResourceInventory.
            var items = accessible && inventory != null
                ? InventorySnapshots.CaptureInventory(id, inventory, catalog).Items
                : Array.Empty<ItemStackSnapshot>();
            var resourceReadable = readPlan.UseDetachedInventory;
            if (!resourceDecoded && string.IsNullOrEmpty(refreshFailure)) refreshFailure = resourceFailure;

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
                diagnostic.AccessGranted = isKnown ? (bool?)accessGranted : null;
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
            return new ContainerHandle(
                id,
                container,
                view,
                snapshot,
                resourceReadable ? resourceInventory : null,
                resourceDataRevision,
                zdo != null ? zdo.m_uid : default(ZDOID),
                zdo != null ? zdo.OwnerRevision : (ushort)0,
                zdo != null ? zdo.GetOwner() : 0L,
                resourceReadable ? resourcePayload : null,
                resourceReadable);
        }

        private static bool TryReadSerializedInventory(
            Container container,
            ZDO zdo,
            out Inventory inventory,
            out uint dataRevision,
            out byte[] payload,
            out string failure)
        {
            inventory = null;
            dataRevision = 0;
            payload = null;
            failure = null;
            try
            {
                var before = zdo.DataRevision;
                var bytes = zdo.GetByteArray(ZDOVars.s_items, null);
                var after = zdo.DataRevision;
                if (before != after)
                {
                    failure = "container data revision changed while reading its serialized inventory";
                    return false;
                }

                // Temporary inventories suppress Changed callbacks and weight bookkeeping while
                // decoding. Serialized item rows still load completely.
                var snapshot = new Inventory(true);
                if (bytes != null)
                {
                    snapshot.Load(new ZPackage(bytes));
                }
                if (zdo.DataRevision != after)
                {
                    failure = "container data revision changed while decoding its serialized inventory";
                    return false;
                }

                // Inventory(true) resolves each serialized row's drop prefab but does not populate
                // ItemData.m_shared. Nearby resource accounting keys on shared metadata, so hydrate
                // every row from the resolved prefab before exposing this detached snapshot. Resolve
                // the whole inventory first: one incomplete row rejects the chest rather than
                // presenting a partial count.
                var hydrated = DetachedItemHydrator.TryHydrate(
                    snapshot.GetAllItems(),
                    item =>
                    {
                        var itemDrop = item.m_dropPrefab != null
                            ? item.m_dropPrefab.GetComponent<ItemDrop>()
                            : null;
                        return itemDrop != null && itemDrop.m_itemData != null
                            ? itemDrop.m_itemData.m_shared
                            : null;
                    },
                    (item, shared) => item.m_shared = shared);
                if (!hydrated)
                {
                    failure = "detached inventory item metadata could not be resolved";
                    return false;
                }

                inventory = snapshot;
                dataRevision = after;
                payload = bytes == null ? null : (byte[])bytes.Clone();
                return true;
            }
            catch (Exception exception)
            {
                failure = exception.Message;
                return false;
            }
        }

        internal static bool IsResourceHandleCurrent(Player player, StorageScope scope, ContainerHandle handle)
        {
            if (player == null || scope == null || handle == null || !handle.ResourceReadable ||
                handle.Container == null || handle.Container.GetType() != typeof(Container) ||
                handle.Container.gameObject == null || !handle.Container.gameObject.activeInHierarchy ||
                handle.NetworkView == null || !handle.NetworkView.IsValid() ||
                !ReferenceEquals(handle.NetworkView, NetworkViewField.GetValue(handle.Container)) ||
                !scope.Contains(handle.Container.transform.position))
            {
                return false;
            }

            try
            {
                var zdo = handle.NetworkView.GetZDO();
                if (zdo == null || zdo.m_uid != handle.ResourceZdoId ||
                    zdo.DataRevision != handle.ResourceDataRevision ||
                    zdo.OwnerRevision != handle.ResourceOwnerRevision ||
                    zdo.GetOwner() != handle.ResourceOwner ||
                    !TryCheckAccess(player, handle.Container, out _))
                {
                    return false;
                }

                var payload = zdo.GetByteArray(ZDOVars.s_items, null);
                return zdo.DataRevision == handle.ResourceDataRevision &&
                       DisplayCaptureEpochPolicy.PayloadMatches(handle.ResourcePayload, payload);
            }
            catch
            {
                return false;
            }
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
