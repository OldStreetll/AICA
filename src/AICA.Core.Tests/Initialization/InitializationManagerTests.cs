using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AICA.Core.Initialization;
using Xunit;

namespace AICA.Core.Tests.Initialization
{
    public class InitializationManagerTests
    {
        [Fact]
        public void Start_SetsIsRunningTrue()
        {
            var mgr = new InitializationManager();
            Assert.False(mgr.IsRunning);

            mgr.Start();
            Assert.True(mgr.IsRunning);
        }

        [Fact]
        public void Start_ResetsAllStepsToPending()
        {
            var mgr = new InitializationManager();
            mgr.Start();

            // Mark one step as completed
            mgr.UpdateStep(InitStepId.RulesInit, InitStepStatus.Completed, "done");

            // Restart
            mgr.Start();
            var steps = mgr.GetSteps();
            Assert.All(steps, s => Assert.Equal(InitStepStatus.Pending, s.Status));
        }

        [Fact]
        public void Start_CancelsPreviousCts()
        {
            var mgr = new InitializationManager();
            mgr.Start();
            var token1 = mgr.Token;

            mgr.Start();
            Assert.True(token1.IsCancellationRequested);
            Assert.False(mgr.Token.IsCancellationRequested);
        }

        [Fact]
        public void Start_FiresInitStartedEvent()
        {
            var mgr = new InitializationManager();
            var fired = false;
            mgr.InitStarted += (s, e) => fired = true;

            mgr.Start();
            Assert.True(fired);
        }

        [Fact]
        public void Cancel_SetsIsRunningFalse()
        {
            var mgr = new InitializationManager();
            mgr.Start();
            Assert.True(mgr.IsRunning);

            mgr.Cancel();
            Assert.False(mgr.IsRunning);
        }

        [Fact]
        public void Cancel_CancelsCancellationToken()
        {
            var mgr = new InitializationManager();
            mgr.Start();
            var token = mgr.Token;

            mgr.Cancel();
            Assert.True(token.IsCancellationRequested);
        }

        [Fact]
        public void GetSteps_Returns4Steps()
        {
            var mgr = new InitializationManager();
            var steps = mgr.GetSteps();
            Assert.Equal(4, steps.Count);

            var ids = steps.Select(s => s.Id).ToList();
            Assert.Contains(InitStepId.RulesInit, ids);
            Assert.Contains(InitStepId.SymbolIndexing, ids);
            Assert.Contains(InitStepId.GitNexusInstall, ids);
            Assert.Contains(InitStepId.GitNexusAnalyze, ids);
        }

        [Fact]
        public void UpdateStep_ChangesStatusAndMessage()
        {
            var mgr = new InitializationManager();
            mgr.Start();

            mgr.UpdateStep(InitStepId.RulesInit, InitStepStatus.Running, "Initializing...");
            var step = mgr.GetSteps().First(s => s.Id == InitStepId.RulesInit);

            Assert.Equal(InitStepStatus.Running, step.Status);
            Assert.Equal("Initializing...", step.StatusMessage);
        }

        [Fact]
        public void UpdateStep_FiresStepChangedEvent()
        {
            var mgr = new InitializationManager();
            mgr.Start();

            InitStepState changedStep = null;
            mgr.StepChanged += (s, e) => changedStep = e.Step;

            mgr.UpdateStep(InitStepId.SymbolIndexing, InitStepStatus.Running);
            Assert.NotNull(changedStep);
            Assert.Equal(InitStepId.SymbolIndexing, changedStep.Id);
            Assert.Equal(InitStepStatus.Running, changedStep.Status);
        }

        [Fact]
        public void AllStepsCompleted_FiresInitCompleted()
        {
            var mgr = new InitializationManager();
            mgr.Start();

            InitCompletedEventArgs completedArgs = null;
            mgr.InitCompleted += (s, e) => completedArgs = e;

            mgr.UpdateStep(InitStepId.RulesInit, InitStepStatus.Completed);
            Assert.Null(completedArgs); // Not all done yet

            mgr.UpdateStep(InitStepId.SymbolIndexing, InitStepStatus.Completed);
            Assert.Null(completedArgs);

            mgr.UpdateStep(InitStepId.GitNexusInstall, InitStepStatus.Completed);
            Assert.Null(completedArgs);

            mgr.UpdateStep(InitStepId.GitNexusAnalyze, InitStepStatus.Completed);
            Assert.NotNull(completedArgs);
            Assert.True(completedArgs.AllSucceeded);
            Assert.False(mgr.IsRunning);
        }

        [Fact]
        public void FailedSteps_AllSucceededIsFalse()
        {
            var mgr = new InitializationManager();
            mgr.Start();

            InitCompletedEventArgs completedArgs = null;
            mgr.InitCompleted += (s, e) => completedArgs = e;

            mgr.UpdateStep(InitStepId.RulesInit, InitStepStatus.Completed);
            mgr.UpdateStep(InitStepId.SymbolIndexing, InitStepStatus.Completed);
            mgr.UpdateStep(InitStepId.GitNexusInstall, InitStepStatus.Failed, "npm error");
            mgr.UpdateStep(InitStepId.GitNexusAnalyze, InitStepStatus.Skipped, "Install failed");

            Assert.NotNull(completedArgs);
            Assert.False(completedArgs.AllSucceeded);
        }

        [Fact]
        public void SkippedSteps_CountAsSuccess()
        {
            var mgr = new InitializationManager();
            mgr.Start();

            InitCompletedEventArgs completedArgs = null;
            mgr.InitCompleted += (s, e) => completedArgs = e;

            mgr.UpdateStep(InitStepId.RulesInit, InitStepStatus.Completed);
            mgr.UpdateStep(InitStepId.SymbolIndexing, InitStepStatus.Completed);
            mgr.UpdateStep(InitStepId.GitNexusInstall, InitStepStatus.Skipped, "No .git");
            mgr.UpdateStep(InitStepId.GitNexusAnalyze, InitStepStatus.Skipped, "No .git");

            Assert.NotNull(completedArgs);
            Assert.True(completedArgs.AllSucceeded);
        }

        [Fact]
        public async Task InitCompleted_DoesNotFireTwice_ConcurrentUpdates()
        {
            var mgr = new InitializationManager();
            mgr.Start();

            var fireCount = 0;
            mgr.InitCompleted += (s, e) => Interlocked.Increment(ref fireCount);

            // Complete first 3 steps
            mgr.UpdateStep(InitStepId.RulesInit, InitStepStatus.Completed);
            mgr.UpdateStep(InitStepId.SymbolIndexing, InitStepStatus.Completed);
            mgr.UpdateStep(InitStepId.GitNexusInstall, InitStepStatus.Completed);

            // Complete last step from multiple threads simultaneously
            var barrier = new Barrier(10);
            var tasks = new List<Task>();
            for (int i = 0; i < 10; i++)
            {
                tasks.Add(Task.Run(() =>
                {
                    barrier.SignalAndWait();
                    mgr.UpdateStep(InitStepId.GitNexusAnalyze, InitStepStatus.Completed);
                }));
            }
            await Task.WhenAll(tasks);

            Assert.Equal(1, fireCount);
        }

        [Fact]
        public void NotStarted_InitCompletedDoesNotFire()
        {
            var mgr = new InitializationManager();
            // Don't call Start()

            var fired = false;
            mgr.InitCompleted += (s, e) => fired = true;

            mgr.UpdateStep(InitStepId.RulesInit, InitStepStatus.Completed);
            mgr.UpdateStep(InitStepId.SymbolIndexing, InitStepStatus.Completed);
            mgr.UpdateStep(InitStepId.GitNexusInstall, InitStepStatus.Completed);
            mgr.UpdateStep(InitStepId.GitNexusAnalyze, InitStepStatus.Completed);

            Assert.False(fired);
        }
    }

    public class InitStepStateTests
    {
        [Fact]
        public void With_CreatesNewInstanceWithUpdatedFields()
        {
            var original = new InitStepState(
                InitStepId.RulesInit, "Rules", InitStepStatus.Pending);

            var updated = original.With(
                status: InitStepStatus.Running,
                statusMessage: "Working...");

            Assert.Equal(InitStepStatus.Pending, original.Status);
            Assert.Null(original.StatusMessage);

            Assert.Equal(InitStepStatus.Running, updated.Status);
            Assert.Equal("Working...", updated.StatusMessage);
            Assert.Equal(InitStepId.RulesInit, updated.Id);
            Assert.Equal("Rules", updated.DisplayName);
        }

        [Fact]
        public void With_PreservesUnchangedFields()
        {
            var original = new InitStepState(
                InitStepId.SymbolIndexing, "Indexing", InitStepStatus.Running,
                "50 files", 50.0);

            var updated = original.With(status: InitStepStatus.Completed);

            Assert.Equal("50 files", updated.StatusMessage);
            Assert.Equal(50.0, updated.ProgressPercent);
        }
    }
}
