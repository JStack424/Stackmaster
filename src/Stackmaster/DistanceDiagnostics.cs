#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Stackmaster.Core;
using UnityEngine;

namespace Stackmaster
{
    // Local-log-only diagnostics for the two live distance transitions. Every query is read-only:
    // this component never requests ownership, invokes an RPC, changes a ZDO, or mutates inventory.
    internal static class DistanceDiagnosticTracker
    {
        private const int MaximumTrackedChests = 16;
        private const float ObservationIntervalSeconds = 0.5f;
        private static readonly Dictionary<string, TrackedChest> Tracked =
            new Dictionary<string, TrackedChest>(StringComparer.Ordinal);
        private static readonly BoundedDiagnosticTransitionTracker Transitions =
            new BoundedDiagnosticTransitionTracker(MaximumTrackedChests);
        private static float _nextObservationTime;
        private static bool _capacityLogged;
        private static bool _failureLogged;

        internal static void TrackRequiredHandles(IEnumerable<ContainerHandle> handles)
        {
            if (handles == null) return;
            try
            {
                foreach (var handle in handles.Where(item => item != null))
                {
                    if (Tracked.ContainsKey(handle.Id)) continue;
                    if (Tracked.Count >= MaximumTrackedChests)
                    {
                        if (!_capacityLogged && RuntimeContext.Plugin != null)
                        {
                            _capacityLogged = true;
                            RuntimeContext.Plugin.Log.LogWarning(
                                "Distance diagnostic registry reached its 16-container session limit; existing tracked sources remain active.");
                        }
                        break;
                    }
                    var zdo = handle.NetworkView != null && handle.NetworkView.IsValid()
                        ? handle.NetworkView.GetZDO()
                        : null;
                    var position = handle.Container != null
                        ? handle.Container.transform.position
                        : Vector3.zero;
                    Tracked.Add(handle.Id, new TrackedChest(
                        handle.Id,
                        handle.ResourceZdoId,
                        position,
                        handle.Container,
                        handle.NetworkView,
                        zdo));
                }
            }
            catch (Exception exception)
            {
                ReportFailureOnce(exception);
            }
        }

        internal static void ObserveCapture(
            Player player,
            StorageScope scope,
            IReadOnlyList<ContainerHandle> includedSources)
        {
            if (Tracked.Count == 0 || player == null || Time.realtimeSinceStartup < _nextObservationTime) return;
            _nextObservationTime = Time.realtimeSinceStartup + ObservationIntervalSeconds;
            try
            {
                var included = (includedSources ?? Array.Empty<ContainerHandle>())
                    .Where(handle => handle != null)
                    .GroupBy(handle => handle.Id, StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

                foreach (var tracked in Tracked.Values.ToArray())
                {
                    var currentContainer = ResolveSceneContainer(tracked);
                    var currentView = currentContainer != null ? currentContainer.GetComponent<ZNetView>() : null;
                    var viewValid = currentView != null && currentView.IsValid();
                    var sceneZdo = viewValid ? currentView.GetZDO() : null;
                    var managerZdo = ZDOMan.instance != null ? ZDOMan.instance.GetZDO(tracked.ZdoId) : null;
                    var currentZdo = managerZdo ?? sceneZdo;
                    ContainerHandle includedHandle;
                    var sourceIncluded = included.TryGetValue(tracked.Id, out includedHandle);
                    var sourceUnits = sourceIncluded && includedHandle.ResourceInventory != null
                        ? includedHandle.ResourceInventory.GetAllItems().Where(item => item != null).Sum(item => item.m_stack)
                        : 0;
                    var scopeContains = scope != null && scope.Contains(tracked.Position);
                    var state = new ContainerStreamDiagnosticState(
                        currentContainer != null && currentContainer.gameObject != null && currentContainer.gameObject.activeInHierarchy,
                        ReferenceEquals(currentContainer, tracked.OriginalContainer),
                        currentView != null,
                        viewValid,
                        ReferenceEquals(currentView, tracked.OriginalView),
                        sceneZdo != null,
                        managerZdo != null,
                        sceneZdo != null && managerZdo != null && ReferenceEquals(sceneZdo, managerZdo),
                        managerZdo != null && tracked.OriginalZdo != null && ReferenceEquals(managerZdo, tracked.OriginalZdo),
                        currentZdo != null ? currentZdo.GetOwner() : 0L,
                        currentZdo != null ? currentZdo.OwnerRevision : (ushort)0,
                        currentZdo != null ? currentZdo.DataRevision : 0U,
                        scopeContains,
                        sourceIncluded,
                        sourceUnits);
                    var decision = Transitions.Observe(tracked.Id, state.Fingerprint);
                    if (decision == DiagnosticEmissionDecision.SuppressDuplicate ||
                        decision == DiagnosticEmissionDecision.SuppressCapacity)
                    {
                        continue;
                    }

                    RuntimeContext.Plugin?.Log.LogWarning(
                        "Nearby-resource distance diagnostic transition: " +
                        "containerId=" + tracked.Id +
                        " transition=" + decision +
                        " distance=" + Vector3.Distance(player.transform.position, tracked.Position).ToString("0.0", CultureInfo.InvariantCulture) +
                        " scope=" + ScopeSummary(scope) +
                        " scopeContainsKnownPosition=" + state.ScopeContainsKnownPosition +
                        " sourceIncluded=" + state.SourceIncluded +
                        " sourceUnits=" + state.SourceUnits.ToString(CultureInfo.InvariantCulture) +
                        " containerLoaded=" + state.ContainerLoaded +
                        " capturedContainerRef=" + state.CapturedContainerReferenceMatches +
                        " nviewPresent=" + state.NetworkViewPresent +
                        " nviewValid=" + state.NetworkViewValid +
                        " capturedNviewRef=" + state.CapturedNetworkViewReferenceMatches +
                        " sceneZdo=" + state.SceneZdoPresent +
                        " managerZdo=" + state.ManagerZdoPresent +
                        " sceneManagerZdoRef=" + state.SceneAndManagerZdoReferenceMatch +
                        " capturedZdoRef=" + state.CapturedZdoReferenceMatches +
                        " owner=" + state.Owner.ToString(CultureInfo.InvariantCulture) +
                        " ownerRevision=" + state.OwnerRevision.ToString(CultureInfo.InvariantCulture) +
                        " dataRevision=" + state.DataRevision.ToString(CultureInfo.InvariantCulture) + ".");
                }
            }
            catch (Exception exception)
            {
                ReportFailureOnce(exception);
            }
        }

        internal static void ResetSession()
        {
            Tracked.Clear();
            Transitions.Reset();
            _nextObservationTime = 0f;
            _capacityLogged = false;
            _failureLogged = false;
        }

        private static Container ResolveSceneContainer(TrackedChest tracked)
        {
            if (tracked.OriginalContainer != null && tracked.OriginalContainer.gameObject != null &&
                tracked.OriginalContainer.gameObject.activeInHierarchy)
            {
                return tracked.OriginalContainer;
            }

            foreach (var candidate in UnityEngine.Object.FindObjectsByType<Container>(
                         FindObjectsInactive.Exclude,
                         FindObjectsSortMode.None))
            {
                if (candidate == null) continue;
                var view = candidate.GetComponent<ZNetView>();
                var zdo = view != null && view.IsValid() ? view.GetZDO() : null;
                if (zdo != null && zdo.m_uid.Equals(tracked.ZdoId)) return candidate;
            }
            return null;
        }

        private static string ScopeSummary(StorageScope scope)
        {
            if (scope == null || scope.Plan == null) return "unavailable";
            var zones = scope.Plan.ConnectedZones;
            if (scope.Kind != StorageScopeKind.WorkbenchMesh || zones.Count == 0)
            {
                return scope.Kind.ToString();
            }
            return scope.Kind + "(zones=" + zones.Count.ToString(CultureInfo.InvariantCulture) +
                   ",first=" + zones[0].Id + ",last=" + zones[zones.Count - 1].Id + ")";
        }

        private static void ReportFailureOnce(Exception exception)
        {
            if (_failureLogged) return;
            _failureLogged = true;
            try
            {
                RuntimeContext.Plugin?.Log.LogWarning(
                    "Distance diagnostics failed open without changing gameplay: " + exception.GetType().Name + ".");
            }
            catch
            {
                // Diagnostics must never block the HUD or an item-conserving action.
            }
        }

        private sealed class TrackedChest
        {
            internal TrackedChest(
                string id,
                ZDOID zdoId,
                Vector3 position,
                Container originalContainer,
                ZNetView originalView,
                ZDO originalZdo)
            {
                Id = id;
                ZdoId = zdoId;
                Position = position;
                OriginalContainer = originalContainer;
                OriginalView = originalView;
                OriginalZdo = originalZdo;
            }

            internal string Id { get; }
            internal ZDOID ZdoId { get; }
            internal Vector3 Position { get; }
            internal Container OriginalContainer { get; }
            internal ZNetView OriginalView { get; }
            internal ZDO OriginalZdo { get; }
        }
    }

    internal static class OwnershipDistanceDiagnosticLog
    {
        internal static void LogActionSelection(
            Player player,
            StorageScope scope,
            IEnumerable<ContainerHandle> handles)
        {
            if (handles == null) return;
            DistanceDiagnosticTracker.TrackRequiredHandles(handles);
            foreach (var handle in handles.Where(item => item != null))
            {
                LogSnapshot(
                    "action-selection",
                    handle,
                    player,
                    scope,
                    0f,
                    OwnershipLeaseManager.DescribeLifecycle(handle.Id),
                    null,
                    true,
                    null);
            }
        }

        internal static void LogImmediateResult(ContainerHandle handle, string reason)
        {
            if (handle == null) return;
            LogResult(
                handle,
                Time.realtimeSinceStartup,
                OwnershipLeaseManager.DescribeLifecycle(handle.Id),
                reason);
        }

        internal static void LogTransition(
            ContainerHandle handle,
            float startedAt,
            string lifecyclePath,
            BoundedDiagnosticTransitionTracker transitions,
            string phase)
        {
            if (handle == null || transitions == null) return;
            var state = Capture(handle);
            var decision = transitions.Observe(handle.Id, state.Fingerprint);
            if (decision == DiagnosticEmissionDecision.SuppressDuplicate ||
                decision == DiagnosticEmissionDecision.SuppressCapacity)
            {
                return;
            }
            LogSnapshot(phase, handle, Player.m_localPlayer, null,
                Time.realtimeSinceStartup - startedAt, lifecyclePath, state, false, decision.ToString());
        }

        internal static void LogResult(
            ContainerHandle handle,
            float startedAt,
            string lifecyclePath,
            string result)
        {
            if (handle == null) return;
            LogSnapshot("final", handle, Player.m_localPlayer, null,
                Time.realtimeSinceStartup - startedAt, lifecyclePath, Capture(handle), false, result);
        }

        private static OwnershipDiagnosticState Capture(ContainerHandle handle)
        {
            var container = handle.Container;
            var containerLoaded = container != null && container.gameObject != null && container.gameObject.activeInHierarchy;
            var currentView = containerLoaded ? container.GetComponent<ZNetView>() : null;
            var viewValid = currentView != null && currentView.IsValid();
            var sceneZdo = viewValid ? currentView.GetZDO() : null;
            var managerZdo = ZDOMan.instance != null ? ZDOMan.instance.GetZDO(handle.ResourceZdoId) : null;
            var currentZdo = managerZdo ?? sceneZdo;
            return new OwnershipDiagnosticState(
                currentZdo != null ? currentZdo.GetOwner() : 0L,
                currentZdo != null ? currentZdo.OwnerRevision : (ushort)0,
                currentZdo != null ? currentZdo.DataRevision : 0U,
                currentView != null && currentView.HasOwner(),
                currentView != null && currentView.IsOwner(),
                containerLoaded && container.IsOwner(),
                containerLoaded,
                containerLoaded && ReferenceEquals(container, handle.Container),
                viewValid,
                ReferenceEquals(currentView, handle.NetworkView),
                managerZdo != null,
                sceneZdo != null,
                currentZdo != null && string.Equals(currentZdo.m_uid.ToString(), handle.Id, StringComparison.Ordinal),
                currentZdo != null && handle.CapturedZdo != null && ReferenceEquals(currentZdo, handle.CapturedZdo),
                sceneZdo != null && managerZdo != null && ReferenceEquals(sceneZdo, managerZdo));
        }

        private static void LogSnapshot(
            string phase,
            ContainerHandle handle,
            Player player,
            StorageScope scope,
            float elapsed,
            string lifecyclePath,
            OwnershipDiagnosticState suppliedState,
            bool includeScope,
            string result)
        {
            try
            {
                var state = suppliedState ?? Capture(handle);
                var localSession = ZDOMan.instance != null ? ZDOMan.GetSessionID() : 0L;
                var distance = player != null && handle.Container != null
                    ? Vector3.Distance(player.transform.position, handle.Container.transform.position)
                    : handle.Snapshot != null ? (float)handle.Snapshot.Distance : -1f;
                RuntimeContext.Plugin?.Log.LogWarning(
                    "Nearby-resource ownership diagnostic: " +
                    "phase=" + phase +
                    " lifecycle=" + lifecyclePath +
                    " containerId=" + handle.Id +
                    " elapsedMs=" + (elapsed * 1000f).ToString("0", CultureInfo.InvariantCulture) +
                    " distance=" + distance.ToString("0.0", CultureInfo.InvariantCulture) +
                    " localSession=" + localSession.ToString(CultureInfo.InvariantCulture) +
                    " capturedOwner=" + handle.ResourceOwner.ToString(CultureInfo.InvariantCulture) +
                    " capturedOwnerRevision=" + handle.ResourceOwnerRevision.ToString(CultureInfo.InvariantCulture) +
                    " owner=" + state.Owner.ToString(CultureInfo.InvariantCulture) +
                    " ownerRevision=" + state.OwnerRevision.ToString(CultureInfo.InvariantCulture) +
                    " dataRevision=" + state.DataRevision.ToString(CultureInfo.InvariantCulture) +
                    " hasOwner=" + state.HasOwner +
                    " nviewIsOwner=" + state.NetworkViewIsOwner +
                    " containerIsOwner=" + state.ContainerIsOwner +
                    " containerLoaded=" + state.ContainerLoaded +
                    " capturedContainerRef=" + state.CapturedContainerReferenceMatches +
                    " nviewValid=" + state.NetworkViewValid +
                    " capturedNviewRef=" + state.CapturedNetworkViewReferenceMatches +
                    " managerZdo=" + state.ManagerZdoPresent +
                    " sceneZdo=" + state.SceneZdoPresent +
                    " zdoIdentity=" + state.ZdoIdentityMatches +
                    " capturedZdoRef=" + state.CapturedZdoReferenceMatches +
                    " sceneManagerZdoRef=" + state.SceneAndManagerZdoReferenceMatch +
                    (includeScope ? " scope=" + (scope != null ? scope.Description : "unavailable") : string.Empty) +
                    (!string.IsNullOrEmpty(result) ? " result=" + result : string.Empty) + ".");
            }
            catch (Exception exception)
            {
                try
                {
                    RuntimeContext.Plugin?.Log.LogWarning(
                        "Ownership diagnostics failed open without changing the action: " + exception.GetType().Name + ".");
                }
                catch
                {
                    // Diagnostics must never alter ownership or transaction behavior.
                }
            }
        }
    }
}
