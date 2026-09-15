#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Stackmaster.Core;
using UnityEngine;

namespace Stackmaster
{
    internal sealed class AcquiredContainerOwnership
    {
        internal AcquiredContainerOwnership(
            ContainerHandle handle,
            long previousOwner,
            long acquiredSession,
            ushort acquiredOwnerRevision)
        {
            Handle = handle;
            PreviousOwner = previousOwner;
            AcquiredSession = acquiredSession;
            AcquiredOwnerRevision = acquiredOwnerRevision;
        }

        internal ContainerHandle Handle { get; }
        internal long PreviousOwner { get; }
        internal long AcquiredSession { get; }
        internal ushort AcquiredOwnerRevision { get; }
        internal string Id => Handle.Id;
    }

    internal sealed class OwnershipBatch
    {
        private readonly Dictionary<Container, ContainerHandle> _pending;
        private readonly Queue<Container> _requestOrder;
        private readonly long _requestSession;
        private Container _waitingFor;
        private ContainerHandle _waitingHandle;
        private bool _grantReceived;

        internal OwnershipBatch(IEnumerable<ContainerHandle> handles, ISet<Container> unresolvedResponses)
        {
            var ordered = handles.Distinct().ToArray();
            _pending = ordered.ToDictionary(handle => handle.Container, handle => handle);
            _requestOrder = new Queue<Container>();
            _requestSession = ZDOMan.instance != null ? ZDOMan.GetSessionID() : 0L;
            SuccessfulContainerIds = new HashSet<string>(StringComparer.Ordinal);
            OwnerRejectedContainerIds = new HashSet<string>(StringComparer.Ordinal);
            FailedContainerIds = new Dictionary<string, string>(StringComparer.Ordinal);
            AcquiredOwnerships = new List<AcquiredContainerOwnership>();

            foreach (var handle in ordered)
            {
                if (unresolvedResponses.Contains(handle.Container) ||
                    OwnershipLeaseManager.HasPotentialAcquisition(handle.Id))
                {
                    FailedContainerIds[handle.Id] = "previous ownership response is still pending";
                    RemovePendingHandle(handle);
                }
                else if (!handle.NetworkView.HasOwner())
                {
                    // UID 0 is ZRoutedRpc.Everybody, not an authoritative owner. Wait for
                    // vanilla's regular ownership pass instead of broadcasting a stack request.
                    FailedContainerIds[handle.Id] = "container is temporarily unowned";
                    RemovePendingHandle(handle);
                }
                else if (handle.NetworkView.IsOwner() && handle.Container.IsOwner())
                {
                    SuccessfulContainerIds.Add(handle.Id);
                    RemovePendingHandle(handle);
                }
                else
                {
                    _requestOrder.Enqueue(handle.Container);
                }
            }

            RequestNext();
        }

        internal HashSet<string> SuccessfulContainerIds { get; }
        internal HashSet<string> OwnerRejectedContainerIds { get; }
        internal Dictionary<string, string> FailedContainerIds { get; }
        internal List<AcquiredContainerOwnership> AcquiredOwnerships { get; }
        internal Container TimedOutRequestContainer { get; private set; }
        internal ContainerHandle TimedOutAcquisitionHandle { get; private set; }
        internal bool IsComplete => _pending.Count == 0;

        internal bool HandleResponse(Container container, bool granted)
        {
            if (!ReferenceEquals(_waitingFor, container) || _waitingHandle == null)
            {
                return false;
            }

            if (!granted)
            {
                var handle = _waitingHandle;
                OwnerRejectedContainerIds.Add(handle.Id);
                FailedContainerIds[handle.Id] = "container owner reported busy or unavailable";
                RemovePendingHandle(handle);
                _waitingFor = null;
                _waitingHandle = null;
                _grantReceived = false;
                CancelRemaining("ownership batch cancelled after a required container was unavailable");
            }
            else
            {
                _grantReceived = true;
            }
            return true;
        }

        internal void Refresh()
        {
            var handle = _waitingHandle;
            if (!_grantReceived || handle == null)
            {
                return;
            }
            var zdo = OwnershipLeaseManager.ResolveZdo(handle);
            var expectedOwnerRevision = OwnershipLeasePolicy.NextOwnerRevision(handle.ResourceOwnerRevision);
            if (zdo != null && zdo.OwnerRevision == expectedOwnerRevision &&
                zdo.GetOwner() == _requestSession)
            {
                SuccessfulContainerIds.Add(handle.Id);
                AcquiredOwnerships.Add(new AcquiredContainerOwnership(
                    handle,
                    handle.ResourceOwner,
                    _requestSession,
                    expectedOwnerRevision));
                RemovePendingHandle(handle);
                _waitingFor = null;
                _waitingHandle = null;
                _grantReceived = false;
                RequestNext();
            }
        }

        internal void Timeout()
        {
            // Keep the durable handle directly. Unity's destroyed-object null semantics must
            // not erase an in-flight request merely because its scene Container unloaded.
            TimedOutAcquisitionHandle = _waitingHandle;
            TimedOutRequestContainer = _grantReceived ? null : _waitingFor;
            foreach (var handle in _pending.Values)
            {
                FailedContainerIds[handle.Id] = "ownership response timed out";
            }
            _pending.Clear();
            _requestOrder.Clear();
            _waitingFor = null;
            _waitingHandle = null;
            _grantReceived = false;
        }

        private void RemovePendingHandle(ContainerHandle handle)
        {
            if (handle == null) return;
            var key = _pending.Keys.FirstOrDefault(container => ReferenceEquals(container, handle.Container));
            if (!ReferenceEquals(key, null)) _pending.Remove(key);
        }

        private void CancelRemaining(string reason)
        {
            foreach (var handle in _pending.Values)
            {
                FailedContainerIds[handle.Id] = reason;
            }
            _pending.Clear();
            _requestOrder.Clear();
        }

        private void RequestNext()
        {
            while (_waitingHandle == null && _requestOrder.Count > 0)
            {
                var container = _requestOrder.Dequeue();
                ContainerHandle handle;
                if (!_pending.TryGetValue(container, out handle))
                {
                    continue;
                }

                try
                {
                    _waitingFor = container;
                    _waitingHandle = handle;
                    // StackAll initiates vanilla's owner-authorized RPC_RequestStack handshake.
                    // The matching response patch below suppresses the original broad transfer.
                    container.StackAll();
                }
                catch (Exception exception)
                {
                    // The send could theoretically fail after queuing the RPC. Fail closed by
                    // suppressing the next response rather than risking a delayed broad Stack All.
                    OwnershipCoordinator.SuppressLateResponse(container);
                    OwnershipLeaseManager.WatchPotentialAcquisition(handle);
                    FailedContainerIds[handle.Id] = "ownership request failed: " + exception.GetType().Name;
                    RemovePendingHandle(handle);
                    _waitingFor = null;
                    _waitingHandle = null;
                    CancelRemaining("ownership batch cancelled after a required request failed");
                }
            }
        }
    }

    internal sealed class OwnershipLease
    {
        internal OwnershipLease(
            AcquiredContainerOwnership acquisition,
            float expiresAt,
            OwnershipLeasePurpose purpose)
        {
            Acquisition = acquisition;
            ExpiresAt = expiresAt;
            Purpose = purpose;
        }

        internal AcquiredContainerOwnership Acquisition { get; }
        internal float ExpiresAt { get; set; }
        internal OwnershipLeasePurpose Purpose { get; set; }
    }

    internal sealed class PendingOwnershipCleanup
    {
        internal PendingOwnershipCleanup(ContainerHandle handle, long requestSession)
        {
            Handle = handle;
            RequestSession = requestSession;
        }

        internal ContainerHandle Handle { get; }
        internal long RequestSession { get; }
    }

    internal static class OwnershipLeaseManager
    {
        internal const float RetryLeaseSeconds = 10f;
        internal const float BuildingLeaseSeconds = 30f;
        private static readonly Dictionary<string, OwnershipLease> Leases =
            new Dictionary<string, OwnershipLease>(StringComparer.Ordinal);
        private static readonly Dictionary<string, PendingOwnershipCleanup> Pending =
            new Dictionary<string, PendingOwnershipCleanup>(StringComparer.Ordinal);

        internal static void HoldForRetry(OwnershipBatch batch)
        {
            if (batch == null) return;
            var expiresAt = OwnershipLeaseRetentionPolicy.RenewedExpiry(
                Time.realtimeSinceStartup,
                RetryLeaseSeconds);
            foreach (var acquisition in batch.AcquiredOwnerships)
            {
                Leases[acquisition.Id] = new OwnershipLease(
                    acquisition,
                    expiresAt,
                    OwnershipLeasePurpose.Retry);
            }
        }

        internal static void RenewForSuccessfulBuild(IEnumerable<ContainerHandle> usedHandles)
        {
            if (usedHandles == null) return;
            var now = Time.realtimeSinceStartup;
            foreach (var usedHandle in usedHandles
                .Where(handle => handle != null)
                .GroupBy(handle => handle.Id, StringComparer.Ordinal)
                .Select(group => group.First()))
            {
                OwnershipLease lease;
                var demonstrablyAcquired = Leases.TryGetValue(usedHandle.Id, out lease) &&
                    LeaseStillMatchesExactAcquisition(lease, usedHandle);
                if (!OwnershipLeaseRetentionPolicy.ShouldRenewForSuccessfulBuild(
                        demonstrablyAcquired,
                        usedByPlacement: true,
                        placementSucceeded: true))
                {
                    continue;
                }

                lease.Purpose = OwnershipLeasePurpose.Building;
                lease.ExpiresAt = OwnershipLeaseRetentionPolicy.RenewedExpiry(now, BuildingLeaseSeconds);
            }
        }

        private static bool LeaseStillMatchesExactAcquisition(
            OwnershipLease lease,
            ContainerHandle usedHandle)
        {
            if (lease == null || usedHandle == null || ZDOMan.instance == null) return false;
            var acquisition = lease.Acquisition;
            var zdo = ResolveZdo(acquisition.Handle);
            return zdo != null &&
                   IdentityMatches(acquisition.Handle, zdo) &&
                   IdentityMatches(usedHandle, zdo) &&
                   ZDOMan.GetSessionID() == acquisition.AcquiredSession &&
                   zdo.GetOwner() == acquisition.AcquiredSession &&
                   zdo.OwnerRevision == acquisition.AcquiredOwnerRevision;
        }

        internal static void WatchPotentialAcquisition(ContainerHandle handle)
        {
            if (handle == null) return;
            Pending[handle.Id] = new PendingOwnershipCleanup(
                handle,
                ZDOMan.instance != null ? ZDOMan.GetSessionID() : 0L);
        }

        internal static bool HasPotentialAcquisition(string id)
            => id != null && Pending.ContainsKey(id);

        internal static bool HasUnresolvedCleanup => Pending.Count > 0 || Leases.Count > 0;

        internal static void DiscardEndedSessionState()
        {
            // RuntimeContext calls this only after observing a different ZNet instance. At that
            // point old transport responses cannot arrive and these identity-scoped records must
            // not be allowed to influence the new session.
            Pending.Clear();
            Leases.Clear();
        }

        internal static void CancelPotentialAcquisition(Container container)
        {
            if (container == null) return;
            foreach (var pending in Pending.Values.Where(item => item.Handle.Container == container).ToArray())
            {
                Pending.Remove(pending.Handle.Id);
            }
        }

        internal static void ObserveRemoteManualOpen(Container container, long requesterSession)
        {
            if (container == null) return;
            var currentView = container.GetComponent<ZNetView>();
            var currentZdo = currentView != null && currentView.IsValid() ? currentView.GetZDO() : null;
            var lease = Leases.Values.FirstOrDefault(item =>
                ReferenceEquals(item.Acquisition.Handle.Container, container) ||
                (currentZdo != null && IdentityMatches(item.Acquisition.Handle, currentZdo)));
            if (lease == null ||
                !OwnershipLeaseRetentionPolicy.ShouldYieldToManualOpen(
                    lease.Purpose,
                    requesterSession,
                    lease.Acquisition.AcquiredSession,
                    logicallyReserved: false))
            {
                return;
            }

            var zdo = ResolveZdo(lease.Acquisition.Handle);
            if (zdo == null) return;
            if (!IdentityMatches(lease.Acquisition.Handle, zdo))
            {
                Leases.Remove(lease.Acquisition.Id);
                return;
            }
            if (zdo.GetOwner() != requesterSession) return;

            // Vanilla has accepted this remote manual open and transferred directly to its
            // requester. Forget the lease immediately after that transfer; never set owner 0
            // around RPC_RequestOpen or manufacture an open response on vanilla's behalf.
            Leases.Remove(lease.Acquisition.Id);
            if (RuntimeContext.Plugin != null)
            {
                RuntimeContext.Plugin.Log.LogDebug(
                    "Yielded Stackmaster build lease for " + lease.Acquisition.Id +
                    " to a remote vanilla open request.");
            }
        }

        internal static void Update()
        {
            var now = Time.realtimeSinceStartup;
            foreach (var pending in Pending.Values.ToArray())
            {
                ObservePending(pending);
            }
            foreach (var lease in Leases.Values.Where(item =>
                OwnershipLeaseRetentionPolicy.IsExpired(now, item.ExpiresAt)).ToArray())
            {
                var container = lease.Acquisition.Handle.Container;
                if (container != null && StorageAction.IsLocalOpenTarget(container))
                {
                    lease.ExpiresAt = now + 1f;
                    continue;
                }
                Release(lease.Acquisition, "retry lease expired");
            }
        }

        private static void ObservePending(PendingOwnershipCleanup pending)
        {
            var handle = pending.Handle;
            if (ZDOMan.instance == null || ZDOMan.GetSessionID() != pending.RequestSession)
            {
                // A new/ended network session is authoritative: no later local acquisition can
                // belong to this request.
                Pending.Remove(handle.Id);
                return;
            }

            var zdo = ResolveZdo(handle);
            if (zdo == null)
            {
                // Scene objects may unload while their persistent ZDO remains in flight. Keep
                // the exact record until the same network session can resolve it conclusively.
                return;
            }

            var expectedOwnerRevision = OwnershipLeasePolicy.NextOwnerRevision(handle.ResourceOwnerRevision);
            if (zdo.OwnerRevision == expectedOwnerRevision && zdo.GetOwner() == pending.RequestSession)
            {
                Pending.Remove(handle.Id);
                ReleaseOrRetry(new AcquiredContainerOwnership(
                    handle,
                    handle.ResourceOwner,
                    pending.RequestSession,
                    expectedOwnerRevision),
                    "late ownership update after timed-out request");
                return;
            }

            if (zdo.OwnerRevision != handle.ResourceOwnerRevision)
            {
                // A different ownership transition won the race. Exact proof is gone, so do
                // not touch it; vanilla remains authoritative.
                Pending.Remove(handle.Id);
            }
        }

        private static bool IdentityMatches(ContainerHandle handle, ZDO zdo)
        {
            return handle != null && zdo != null &&
                   string.Equals(zdo.m_uid.ToString(), handle.Id, StringComparison.Ordinal);
        }

        internal static ZDO ResolveZdo(ContainerHandle handle)
        {
            if (handle == null || ZDOMan.instance == null) return null;
            var zdo = ZDOMan.instance.GetZDO(handle.ResourceZdoId);
            if (zdo != null && string.Equals(zdo.m_uid.ToString(), handle.Id, StringComparison.Ordinal)) return zdo;

            var view = handle.NetworkView;
            zdo = view != null && view.IsValid() ? view.GetZDO() : null;
            return zdo != null && string.Equals(zdo.m_uid.ToString(), handle.Id, StringComparison.Ordinal)
                ? zdo
                : null;
        }

        internal static void ReleaseBatch(OwnershipBatch batch, string reason)
        {
            if (batch == null) return;
            foreach (var acquisition in batch.AcquiredOwnerships.ToArray())
            {
                ReleaseOrRetry(acquisition, reason);
            }
        }

        internal static void ReleaseMatching(IEnumerable<ContainerHandle> handles, string reason)
        {
            if (handles == null) return;
            foreach (var id in handles.Where(handle => handle != null).Select(handle => handle.Id)
                .Distinct(StringComparer.Ordinal).ToArray())
            {
                OwnershipLease lease;
                if (!Leases.TryGetValue(id, out lease)) continue;
                ReleaseOrRetry(lease.Acquisition, reason);
            }
        }

        internal static void ReleaseAll(string reason)
        {
            foreach (var pending in Pending.Values.ToArray())
            {
                ObservePending(pending);
            }
            // Do not discard unresolved requests: a response or exact owner update can arrive
            // after disable/hot unload. The session-lifetime observer owns those records.
            foreach (var lease in Leases.Values.ToArray())
            {
                ReleaseOrRetry(lease.Acquisition, reason, true);
            }
        }

        private static void Release(AcquiredContainerOwnership acquisition, string reason)
        {
            ReleaseOrRetry(acquisition, reason);
        }

        private static void ReleaseOrRetry(
            AcquiredContainerOwnership acquisition,
            string reason,
            bool shutdownRelease = false)
        {
            OwnershipLease existing;
            var hasExisting = Leases.TryGetValue(acquisition.Id, out existing);
            if (hasExisting && !SameAcquisition(existing.Acquisition, acquisition))
            {
                // A delayed prior-session coroutine must never remove or overwrite a newer exact
                // lease that happens to use the same persistent container id.
                return;
            }

            if (TryRelinquish(acquisition, reason, shutdownRelease))
            {
                if (hasExisting) Leases.Remove(acquisition.Id);
            }
            else
            {
                // Retain the exact cleanup record on a transient failure. Update retries it;
                // stale identity/revision guards still prevent touching unrelated ownership.
                var purpose = hasExisting ? existing.Purpose : OwnershipLeasePurpose.Retry;
                Leases[acquisition.Id] = new OwnershipLease(
                    acquisition,
                    Time.realtimeSinceStartup + 1f,
                    purpose);
            }
        }

        private static bool SameAcquisition(
            AcquiredContainerOwnership left,
            AcquiredContainerOwnership right)
        {
            return left != null && right != null &&
                   string.Equals(left.Id, right.Id, StringComparison.Ordinal) &&
                   left.AcquiredSession == right.AcquiredSession &&
                   left.AcquiredOwnerRevision == right.AcquiredOwnerRevision;
        }

        private static bool TryRelinquish(
            AcquiredContainerOwnership acquisition,
            string reason,
            bool shutdownRelease)
        {
            try
            {
                var handle = acquisition.Handle;
                var container = handle != null ? handle.Container : null;
                if (!ReservationOwnershipCleanupPolicy.CanRelinquishOwnership(
                        NearbyResourceService.HasPendingReservationRelease(acquisition.Id)))
                {
                    // The exact local reservation must be cleared while this exact acquisition is
                    // still ours. This applies even to shutdownRelease; the safety patch retries
                    // reservations before ownership leases and keeps all identity/revision guards.
                    return false;
                }
                if (ZDOMan.instance == null || ZDOMan.GetSessionID() != acquisition.AcquiredSession)
                {
                    return true;
                }
                var zdo = ResolveZdo(handle);
                if (zdo == null)
                {
                    // Keep retrying through scene unload while this exact network session lives.
                    return false;
                }
                var identityMatches =
                    string.Equals(zdo.m_uid.ToString(), acquisition.Id, StringComparison.Ordinal);
                var decision = OwnershipLeasePolicy.Decide(
                    identityMatches,
                    zdo != null && zdo.GetOwner() == acquisition.AcquiredSession &&
                        ZDOMan.instance != null && ZDOMan.GetSessionID() == acquisition.AcquiredSession,
                    zdo != null && zdo.GetOwner() == acquisition.AcquiredSession,
                    zdo != null && zdo.OwnerRevision == acquisition.AcquiredOwnerRevision);
                if (!decision.ShouldRelease)
                {
                    // Identity, owner, or exact revision no longer proves this is our lease.
                    // Forget the record without touching the container and let vanilla recover.
                    return true;
                }
                if (!shutdownRelease && container != null &&
                    (StorageAction.IsLocalOpenTarget(container) || container.IsInUse()))
                {
                    // Never clear vanilla/local use state during normal play. Stackmaster
                    // reservations are released by their transaction owner before this handoff.
                    return false;
                }

                // Vanilla transfers a container by force-sending its latest ZDO before changing
                // owner. Send once more after the owner revision changes so a disconnect cannot
                // strand the new owner behind an unsent local revision.
                if (ZDOMan.instance != null) ZDOMan.instance.ForceSendZDO(zdo.m_uid);
                zdo.SetOwner(decision.TargetOwner);
                if (ZDOMan.instance != null) ZDOMan.instance.ForceSendZDO(zdo.m_uid);

                if (RuntimeContext.Plugin != null)
                {
                    RuntimeContext.Plugin.Log.LogDebug("Relinquished Stackmaster ownership of " + acquisition.Id +
                        " to " + decision.TargetOwner + " (" + reason + ").");
                }
                return true;
            }
            catch (Exception exception)
            {
                if (RuntimeContext.Plugin != null)
                {
                    RuntimeContext.Plugin.Log.LogError("Failed to relinquish Stackmaster-acquired container ownership (" +
                        reason + "): " + exception);
                }
                return false;
            }
        }

    }

    internal static class OwnershipCoordinator
    {
        private static readonly HashSet<Container> LateResponseSuppressions = new HashSet<Container>();
        private static OwnershipBatch _active;

        internal static OwnershipBatch Begin(IEnumerable<ContainerHandle> handles)
        {
            if (_active != null)
            {
                throw new InvalidOperationException("A Stackmaster ownership batch is already active.");
            }

            _active = new OwnershipBatch(handles, LateResponseSuppressions);
            return _active;
        }

        internal static void End(OwnershipBatch batch)
        {
            if (ReferenceEquals(_active, batch))
            {
                if (!ReferenceEquals(batch.TimedOutRequestContainer, null))
                {
                    LateResponseSuppressions.Add(batch.TimedOutRequestContainer);
                }
                if (batch.TimedOutAcquisitionHandle != null)
                {
                    OwnershipLeaseManager.WatchPotentialAcquisition(batch.TimedOutAcquisitionHandle);
                }
                _active = null;
            }
        }

        internal static void Shutdown(string reason)
        {
            var active = _active;
            var completedSafely = true;
            try
            {
                if (active != null)
                {
                    try
                    {
                        // Capture an exact grant that reached the ZDO just before shutdown before
                        // converting any still-unresolved request into session-lifetime cleanup.
                        active.Refresh();
                    }
                    catch (Exception exception)
                    {
                        completedSafely = false;
                        LogCleanupFailure("refreshing the active ownership batch", exception);
                    }

                    try
                    {
                        if (!active.IsComplete) active.Timeout();
                    }
                    catch (Exception exception)
                    {
                        completedSafely = false;
                        LogCleanupFailure("timing out the active ownership batch", exception);
                    }

                    try
                    {
                        OwnershipLeaseManager.ReleaseBatch(active, reason);
                    }
                    catch (Exception exception)
                    {
                        completedSafely = false;
                        LogCleanupFailure("releasing the active ownership batch", exception);
                    }
                }
            }
            finally
            {
                if (active != null)
                {
                    try
                    {
                        End(active);
                    }
                    catch (Exception exception)
                    {
                        completedSafely = false;
                        LogCleanupFailure("ending the active ownership batch", exception);
                        _active = null;
                    }
                }

                try
                {
                    OwnershipLeaseManager.ReleaseAll(reason);
                }
                catch (Exception exception)
                {
                    completedSafely = false;
                    LogCleanupFailure("releasing retained ownership cleanup", exception);
                }
            }

            if (!completedSafely)
            {
                throw new InvalidOperationException(
                    "One or more ownership cleanup stages failed; see the preceding cleanup errors.");
            }
        }

        private static void LogCleanupFailure(string operation, Exception exception)
        {
            if (RuntimeContext.Plugin != null)
            {
                RuntimeContext.Plugin.Log.LogError("Ownership cleanup failed while " + operation + ": " + exception);
            }
        }

        internal static void DiscardEndedSessionState()
        {
            if (_active != null)
            {
                throw new InvalidOperationException("Cannot rearm while a prior-session ownership batch is still active.");
            }

            OwnershipLeaseManager.DiscardEndedSessionState();
            LateResponseSuppressions.Clear();
        }

        internal static void SuppressLateResponse(Container container)
        {
            if (!ReferenceEquals(container, null))
            {
                LateResponseSuppressions.Add(container);
            }
        }

        internal static bool HasUnresolvedResponses => LateResponseSuppressions.Count > 0;
        internal static bool HasUnresolvedCleanup =>
            HasUnresolvedResponses || OwnershipLeaseManager.HasUnresolvedCleanup;

        internal static bool HandleResponse(Container container, bool granted)
        {
            if (_active != null && _active.HandleResponse(container, granted))
            {
                return true;
            }

            // A response can arrive after Stackmaster's bounded wait. Suppress that one
            // response permanently rather than ever letting vanilla Stack All run later.
            if (!LateResponseSuppressions.Remove(container)) return false;
            if (!granted) OwnershipLeaseManager.CancelPotentialAcquisition(container);
            return true;
        }
    }

    internal static class OwnershipLifecyclePatch
    {
        internal static void Prefix()
        {
            RuntimeContext.DisconnectSession();
        }
    }

    internal static class OwnershipSafetyUpdatePatch
    {
        private static void Postfix(ZNet __instance)
        {
            // Runs independently of the plugin component's enabled/compatibility state. It is
            // also retained under the session-lifetime Harmony id during hot unload. Cleanup for
            // the ended session is observed before a different ZNet instance may rearm features.
            NearbyResourceService.UpdatePendingReservationReleases();
            OwnershipLeaseManager.Update();
            RuntimeContext.TryRearmSession(__instance);
        }
    }

    [HarmonyPatch(typeof(Container), "RPC_RequestOpen", typeof(long), typeof(long))]
    internal static class ContainerOpenRequestLeasePatch
    {
        private static void Postfix(
            Container __instance,
            [HarmonyArgument(0)] long requesterSession)
        {
            // Vanilla's owner-side busy/access checks and transfer run first. An accepted remote
            // open already owns the chest at this point, so Stackmaster can invalidate its lease
            // without blocking the request or manufacturing a response.
            try
            {
                OwnershipLeaseManager.ObserveRemoteManualOpen(__instance, requesterSession);
            }
            catch (Exception exception)
            {
                if (RuntimeContext.Plugin != null)
                {
                    RuntimeContext.Plugin.Log.LogError(
                        "Failed to observe a remote manual chest open; guarded lease expiry remains active: " + exception);
                }
            }
        }
    }

    [HarmonyPatch(typeof(Container), "RPC_StackResponse", typeof(long), typeof(bool))]
    internal static class ContainerStackResponsePatch
    {
        private static bool Prefix(Container __instance, bool granted)
        {
            // Even after a session-fatal disable, a delayed response caused by Stackmaster
            // must remain suppressed or vanilla RPC_StackResponse would perform Stack All.
            // Unrelated responses still fall through normally.
            return !OwnershipCoordinator.HandleResponse(__instance, granted);
        }
    }
}
