#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;

namespace Stackmaster
{
    internal sealed class OwnershipBatch
    {
        private readonly Dictionary<Container, ContainerHandle> _pending;
        private readonly Queue<Container> _requestOrder;
        private Container _waitingFor;
        private bool _grantReceived;

        internal OwnershipBatch(IEnumerable<ContainerHandle> handles, ISet<Container> unresolvedResponses)
        {
            var ordered = handles.Distinct().ToArray();
            _pending = ordered.ToDictionary(handle => handle.Container, handle => handle);
            _requestOrder = new Queue<Container>();
            SuccessfulContainerIds = new HashSet<string>(StringComparer.Ordinal);
            OwnerRejectedContainerIds = new HashSet<string>(StringComparer.Ordinal);
            FailedContainerIds = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var handle in ordered)
            {
                if (unresolvedResponses.Contains(handle.Container))
                {
                    FailedContainerIds[handle.Id] = "previous ownership response is still pending";
                    _pending.Remove(handle.Container);
                }
                else if (handle.NetworkView.IsOwner() && handle.Container.IsOwner())
                {
                    SuccessfulContainerIds.Add(handle.Id);
                    _pending.Remove(handle.Container);
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
        internal Container TimedOutRequestContainer { get; private set; }
        internal bool IsComplete => _pending.Count == 0;

        internal bool HandleResponse(Container container, bool granted)
        {
            if (_waitingFor != container || !_pending.ContainsKey(container))
            {
                return false;
            }

            if (!granted)
            {
                var handle = _pending[container];
                OwnerRejectedContainerIds.Add(handle.Id);
                FailedContainerIds[handle.Id] = "container owner reported busy or unavailable";
                _pending.Remove(container);
                _waitingFor = null;
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
            if (!_grantReceived || _waitingFor == null)
            {
                return;
            }

            ContainerHandle handle;
            if (!_pending.TryGetValue(_waitingFor, out handle))
            {
                _waitingFor = null;
                _grantReceived = false;
                RequestNext();
                return;
            }

            if (_waitingFor != null && handle.NetworkView != null && handle.NetworkView.IsValid() &&
                handle.NetworkView.GetZDO() != null &&
                handle.NetworkView.GetZDO().OwnerRevision != handle.ResourceOwnerRevision &&
                handle.NetworkView.IsOwner() && _waitingFor.IsOwner())
            {
                SuccessfulContainerIds.Add(handle.Id);
                _pending.Remove(_waitingFor);
                _waitingFor = null;
                _grantReceived = false;
                RequestNext();
            }
        }

        internal void Timeout()
        {
            TimedOutRequestContainer = _grantReceived ? null : _waitingFor;
            foreach (var handle in _pending.Values)
            {
                FailedContainerIds[handle.Id] = "ownership response timed out";
            }
            _pending.Clear();
            _requestOrder.Clear();
            _waitingFor = null;
            _grantReceived = false;
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
            while (_waitingFor == null && _requestOrder.Count > 0)
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
                    // StackAll initiates vanilla's owner-authorized RPC_RequestStack handshake.
                    // The matching response patch below suppresses the original broad transfer.
                    container.StackAll();
                }
                catch (Exception exception)
                {
                    // The send could theoretically fail after queuing the RPC. Fail closed by
                    // suppressing the next response rather than risking a delayed broad Stack All.
                    OwnershipCoordinator.SuppressLateResponse(container);
                    FailedContainerIds[handle.Id] = "ownership request failed: " + exception.GetType().Name;
                    _pending.Remove(container);
                    _waitingFor = null;
                    CancelRemaining("ownership batch cancelled after a required request failed");
                }
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
                if (batch.TimedOutRequestContainer != null)
                {
                    LateResponseSuppressions.Add(batch.TimedOutRequestContainer);
                }
                _active = null;
            }
        }

        internal static void SuppressLateResponse(Container container)
        {
            if (container != null)
            {
                LateResponseSuppressions.Add(container);
            }
        }

        internal static bool HandleResponse(Container container, bool granted)
        {
            if (_active != null && _active.HandleResponse(container, granted))
            {
                return true;
            }

            // A response can arrive after Stackmaster's bounded wait. Suppress that one
            // response permanently rather than ever letting vanilla Stack All run later.
            return LateResponseSuppressions.Remove(container);
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
