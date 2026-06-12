using System;
using System.Threading;
using System.Threading.Tasks;
using CleanArchitecture.Application.Common.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CleanArchitecture.Infrastructure.BackgroundServices
{
    // Sample periodic background batch that demonstrates the maintenance contract: at the
    // top of every tick it consults IMaintenanceState and skips its work while the service
    // is stopped, so a maintenance window pauses background processing without a redeploy.
    // Real workers follow the same shape — check the gate before each unit of work.
    public class DemoBatchWorker : BackgroundService
    {
        private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

        private readonly IMaintenanceState _maintenance;
        private readonly ILogger<DemoBatchWorker> _logger;

        // Number of ticks that actually performed work (i.e. were not skipped for
        // maintenance). Exposed so the skip/run behaviour is observable in tests.
        public int CompletedRuns { get; private set; }

        public DemoBatchWorker(IMaintenanceState maintenance, ILogger<DemoBatchWorker> logger)
        {
            _maintenance = maintenance;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                using (var timer = new PeriodicTimer(Interval))
                {
                    while (await timer.WaitForNextTickAsync(stoppingToken))
                    {
                        RunTick();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown when the stopping token is cancelled.
            }
        }

        // One unit of background work. Synchronous and public so it can be driven directly
        // and deterministically from tests without spinning the timer loop.
        public void RunTick()
        {
            if (_maintenance.IsStopped)
            {
                _logger.LogInformation("DemoBatchWorker tick skipped: maintenance mode is active");
                return;
            }

            // Stand-in for real batch work.
            CompletedRuns++;
            _logger.LogInformation("DemoBatchWorker tick completed (run {completed_runs})", CompletedRuns);
        }
    }
}
