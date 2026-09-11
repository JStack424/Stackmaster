#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Stackmaster
{
    internal sealed class OwnershipBatch
    {
        private readonly Dictionary<Container, ContainerHandle> _pending;
        private readonly HashSet<Container> _granted = new HashSet<Container>();

        internal OwnershipBatch(IEnumerable<ContainerHandle> handles, Player player)
        {
            _pending = handles.Distinct().ToDictionary(handle => handle.Container, handle => handle);
            SuccessfulContainerIds = new HashSet<string>(StringComparer.Ordinal);
            FailedContainerIds = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var handle in _pending.Values.ToArray())
            {
                if (handle.NetworkView.IsOwner() && handle.Container.IsOwner())
                {
                    SuccessfulContainerIds.Add(handle.Id);
                    _pending.Remove(handle.Container);
                    continue;
                }

                try
                {
                    handle.NetworkView.InvokeRPC("RPC_RequestOpen", new object[] { player.GetPlayerID() });
                }
                catch (Exception exception)
                {
                    FailedContainerIds[handle.Id] = "ownership request failed: " + exception.GetType().Name;
                    _pending.Remove(handle.Container);
                }
            }
        }

        internal HashSet<string> SuccessfulContainerIds { get; }
        internal Dictionary<string, string> FailedContainerIds { get; }
        internal bool IsComplete => _pending.Count == 0;

        internal bool HandleResponse(Container container, bool granted)
        {
            ContainerHandle handle;
            if (!_pending.TryGetValue(container, out handle))
            {
                return false;
            }

            if (!granted)
            {
                FailedContainerIds[handle.Id] = "container owner denied access or reported it in use";
                _pending.Remove(container);
            }
            else
            {
                _granted.Add(container);
            }
            return true;
        }

        internal void Refresh()
        {
            foreach (var container in _granted.ToArray())
            {
                ContainerHandle handle;
                if (!_pending.TryGetValue(container, out handle))
                {
                    _granted.Remove(container);
                    continue;
                }

                if (container != null && handle.NetworkView != null && handle.NetworkView.IsValid() &&
                    handle.NetworkView.IsOwner() && container.IsOwner())
                {
                    SuccessfulContainerIds.Add(handle.Id);
                    _pending.Remove(container);
                    _granted.Remove(container);
                }
            }
        }

        internal void Timeout()
        {
            foreach (var handle in _pending.Values)
            {
                FailedContainerIds[handle.Id] = "ownership response timed out";
            }
            _pending.Clear();
            _granted.Clear();
        }
    }

    internal static class OwnershipCoordinator
    {
        private static OwnershipBatch _active;

        internal static OwnershipBatch Begin(IEnumerable<ContainerHandle> handles, Player player)
        {
            if (_active != null)
            {
                throw new InvalidOperationException("A Stackmaster ownership batch is already active.");
            }

            _active = new OwnershipBatch(handles, player);
            return _active;
        }

        internal static void End(OwnershipBatch batch)
        {
            if (ReferenceEquals(_active, batch))
            {
                _active = null;
            }
        }

        internal static bool HandleResponse(Container container, bool granted)
        {
            return _active != null && _active.HandleResponse(container, granted);
        }
    }

    [HarmonyPatch(typeof(Container), "RPC_OpenResponse", typeof(long), typeof(bool))]
    internal static class ContainerOpenResponsePatch
    {
        private static bool Prefix(Container __instance, bool granted)
        {
            return !OwnershipCoordinator.HandleResponse(__instance, granted);
        }
    }
}
