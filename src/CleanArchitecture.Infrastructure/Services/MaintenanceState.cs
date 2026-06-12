using CleanArchitecture.Application.Common.Interfaces;

namespace CleanArchitecture.Infrastructure.Services
{
    // In-memory, process-wide maintenance switch. Registered as a singleton so the API
    // middleware, the admin endpoints and the background workers all observe one shared
    // flag. State is intentionally not persisted: a restart falls back to the configured
    // default (Maintenance:Enabled), which is the desired behaviour for a planned-
    // maintenance toggle.
    public class MaintenanceState : IMaintenanceState
    {
        // volatile: Stop/Resume are rare writes from admin requests, while IsStopped is read
        // on every API request and every background tick. volatile keeps the hot-path reads
        // lock-free while still observing the latest written value.
        private volatile bool _stopped;

        public MaintenanceState(bool stopped)
        {
            _stopped = stopped;
        }

        public bool IsStopped => _stopped;

        public void Stop() => _stopped = true;

        public void Resume() => _stopped = false;
    }
}
