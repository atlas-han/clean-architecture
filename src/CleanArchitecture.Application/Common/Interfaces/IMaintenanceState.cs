namespace CleanArchitecture.Application.Common.Interfaces
{
    // Cross-cutting maintenance (stop/resume) switch. The API middleware consults it to
    // short-circuit traffic with 503 during a maintenance window, and background workers
    // consult it to skip their work — both without a redeploy. Implemented as an in-memory
    // singleton in Infrastructure and toggled at runtime through the /admin/maintenance
    // endpoints (the configured Maintenance:Enabled value provides the startup default).
    public interface IMaintenanceState
    {
        // True while the service is in a maintenance (stopped) window.
        bool IsStopped { get; }

        // Enter the maintenance window: the API returns 503 and background work is skipped.
        void Stop();

        // Leave the maintenance window: normal operation resumes.
        void Resume();
    }
}
