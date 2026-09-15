#nullable enable

namespace Stackmaster.Core
{
    public enum SessionLifecyclePhase
    {
        Operational,
        AwaitingReconnect,
        PermanentlyDisabled,
        ShutDown
    }

    /// <summary>
    /// Separates a recoverable world-session boundary from a process-lifetime safety disable.
    /// Runtime cleanup stays in the plugin assembly; this state machine makes the rearm contract
    /// explicit and independently testable.
    /// </summary>
    public sealed class SessionLifecycleState
    {
        public SessionLifecycleState(bool initiallyCompatible)
        {
            Phase = initiallyCompatible
                ? SessionLifecyclePhase.Operational
                : SessionLifecyclePhase.PermanentlyDisabled;
        }

        public SessionLifecyclePhase Phase { get; private set; }
        public bool IsOperational => Phase == SessionLifecyclePhase.Operational;
        public bool CanAttemptRearm => Phase == SessionLifecyclePhase.AwaitingReconnect;
        public bool IsPermanentlyDisabled => Phase == SessionLifecyclePhase.PermanentlyDisabled ||
                                             Phase == SessionLifecyclePhase.ShutDown;

        public void BeginDisconnect()
        {
            if (Phase == SessionLifecyclePhase.Operational)
            {
                Phase = SessionLifecyclePhase.AwaitingReconnect;
            }
        }

        public bool TryRearm(bool isDifferentNetworkSession, bool cleanupCompletedSafely)
        {
            if (Phase != SessionLifecyclePhase.AwaitingReconnect ||
                !isDifferentNetworkSession ||
                !cleanupCompletedSafely)
            {
                return false;
            }

            Phase = SessionLifecyclePhase.Operational;
            return true;
        }

        public void DisablePermanently()
        {
            if (Phase != SessionLifecyclePhase.ShutDown)
            {
                Phase = SessionLifecyclePhase.PermanentlyDisabled;
            }
        }

        public void ShutDown()
        {
            Phase = SessionLifecyclePhase.ShutDown;
        }
    }
}
