using CleanArchitecture.Application.Common.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace CleanArchitecture.Api.Controllers
{
    // Runtime control plane for the maintenance (stop/resume) switch. Lives outside the
    // /api surface and is intentionally always reachable (MaintenanceMiddleware exempts
    // /admin/maintenance) so an operator can resume the service after stopping it.
    // NOTE: this sample hosts no authentication — in a real deployment these endpoints MUST
    // be protected (auth + network policy) so only operators can flip the switch.
    [ApiController]
    [Route("admin/maintenance")]
    public class MaintenanceController : ControllerBase
    {
        private readonly IMaintenanceState _maintenance;

        public MaintenanceController(IMaintenanceState maintenance)
        {
            _maintenance = maintenance;
        }

        // GET /admin/maintenance -> current switch state.
        [HttpGet]
        public IActionResult GetStatus()
        {
            return Ok(new MaintenanceStatusResponse(_maintenance.IsStopped));
        }

        // POST /admin/maintenance/stop -> enter maintenance (API returns 503, batch pauses).
        [HttpPost("stop")]
        public IActionResult Stop()
        {
            _maintenance.Stop();
            return Ok(new MaintenanceStatusResponse(_maintenance.IsStopped));
        }

        // POST /admin/maintenance/resume -> leave maintenance (normal operation).
        [HttpPost("resume")]
        public IActionResult Resume()
        {
            _maintenance.Resume();
            return Ok(new MaintenanceStatusResponse(_maintenance.IsStopped));
        }
    }

    // Operational status payload (not the §4.5 data envelope — like /health, the control
    // plane returns a flat object).
    public record MaintenanceStatusResponse(bool Stopped);
}
