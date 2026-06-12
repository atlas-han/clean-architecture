using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Infrastructure.BackgroundServices;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CleanArchitecture.Api.IntegrationTests
{
    // Drives the demo worker's per-tick logic directly (no timer) to prove it honours the
    // maintenance gate deterministically.
    public class DemoBatchWorkerTests
    {
        private class FakeMaintenanceState : IMaintenanceState
        {
            public bool IsStopped { get; set; }
            public void Stop() => IsStopped = true;
            public void Resume() => IsStopped = false;
        }

        [Fact]
        public void RunTick_SkipsWork_WhileStopped()
        {
            var gate = new FakeMaintenanceState { IsStopped = true };
            var worker = new DemoBatchWorker(gate, NullLogger<DemoBatchWorker>.Instance);

            worker.RunTick();
            worker.RunTick();

            Assert.Equal(0, worker.CompletedRuns);
        }

        [Fact]
        public void RunTick_PerformsWork_WhileResumed()
        {
            var gate = new FakeMaintenanceState { IsStopped = false };
            var worker = new DemoBatchWorker(gate, NullLogger<DemoBatchWorker>.Instance);

            worker.RunTick();
            worker.RunTick();

            Assert.Equal(2, worker.CompletedRuns);
        }

        [Fact]
        public void RunTick_ResumesWork_AfterStopThenResume()
        {
            var gate = new FakeMaintenanceState { IsStopped = true };
            var worker = new DemoBatchWorker(gate, NullLogger<DemoBatchWorker>.Instance);

            worker.RunTick();              // skipped while stopped
            gate.Resume();
            worker.RunTick();              // runs after resume

            Assert.Equal(1, worker.CompletedRuns);
        }
    }
}
