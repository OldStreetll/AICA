using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace AICA.Core.Initialization
{
    /// <summary>
    /// Event args for step state changes.
    /// </summary>
    public class InitStepChangedEventArgs : EventArgs
    {
        public InitStepState Step { get; }
        public InitStepChangedEventArgs(InitStepState step) => Step = step;
    }

    /// <summary>
    /// Event args for initialization completion.
    /// </summary>
    public class InitCompletedEventArgs : EventArgs
    {
        public bool AllSucceeded { get; }
        public IReadOnlyList<InitStepState> Steps { get; }

        public InitCompletedEventArgs(bool allSucceeded, IReadOnlyList<InitStepState> steps)
        {
            AllSucceeded = allSucceeded;
            Steps = steps;
        }
    }

    /// <summary>
    /// Central coordinator for AICA initialization steps.
    /// Tracks step progress, fires events for UI consumption.
    /// Thread-safe: all state access is lock-protected.
    /// </summary>
    public class InitializationManager
    {
        private readonly object _lock = new object();
        private readonly Dictionary<InitStepId, InitStepState> _steps;
        private volatile bool _isRunning;
        private CancellationTokenSource _cts;

        /// <summary>Fired when initialization starts. Thread: caller (usually background).</summary>
        public event EventHandler InitStarted;

        /// <summary>Fired when any step's state changes. Thread: background.</summary>
        public event EventHandler<InitStepChangedEventArgs> StepChanged;

        /// <summary>Fired when all steps have completed/failed/skipped. Thread: background.</summary>
        public event EventHandler<InitCompletedEventArgs> InitCompleted;

        /// <summary>Whether initialization is currently running.</summary>
        public bool IsRunning => _isRunning;

        public InitializationManager()
        {
            _steps = new Dictionary<InitStepId, InitStepState>
            {
                [InitStepId.RulesInit] = new InitStepState(
                    InitStepId.RulesInit, "规则目录初始化", InitStepStatus.Pending),
                [InitStepId.SymbolIndexing] = new InitStepState(
                    InitStepId.SymbolIndexing, "符号索引", InitStepStatus.Pending),
                [InitStepId.GitNexusInstall] = new InitStepState(
                    InitStepId.GitNexusInstall, "GitNexus 依赖安装", InitStepStatus.Pending),
                [InitStepId.GitNexusAnalyze] = new InitStepState(
                    InitStepId.GitNexusAnalyze, "GitNexus 代码分析", InitStepStatus.Pending),
            };
        }

        /// <summary>
        /// Get a snapshot of all step states.
        /// </summary>
        public IReadOnlyList<InitStepState> GetSteps()
        {
            lock (_lock)
            {
                return _steps.Values.ToList();
            }
        }

        /// <summary>
        /// Update a step's state and fire StepChanged event.
        /// </summary>
        public void UpdateStep(InitStepId id, InitStepStatus status,
            string message = null, double? progress = null)
        {
            InitStepState newState;
            lock (_lock)
            {
                if (!_steps.TryGetValue(id, out var current)) return;
                newState = current.With(status: status,
                    statusMessage: message, progressPercent: progress);
                _steps[id] = newState;
            }

            StepChanged?.Invoke(this, new InitStepChangedEventArgs(newState));
            CheckCompletion();
        }

        /// <summary>
        /// Start initialization sequence. Called by SolutionEventListener.
        /// Cancels any previous run's background tasks first.
        /// </summary>
        public void Start()
        {
            _cts?.Cancel();
            _cts?.Dispose();

            _isRunning = true;
            _cts = new CancellationTokenSource();

            lock (_lock)
            {
                var ids = _steps.Keys.ToList();
                foreach (var id in ids)
                {
                    _steps[id] = new InitStepState(
                        id, _steps[id].DisplayName, InitStepStatus.Pending);
                }
            }

            InitStarted?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Cancel all running initialization (e.g., solution close).
        /// </summary>
        public void Cancel()
        {
            _cts?.Cancel();
            _isRunning = false;
        }

        /// <summary>
        /// Cancellation token for the current initialization run.
        /// </summary>
        public CancellationToken Token => _cts?.Token ?? CancellationToken.None;

        private void CheckCompletion()
        {
            bool shouldComplete = false;
            IReadOnlyList<InitStepState> snapshot;

            lock (_lock)
            {
                snapshot = _steps.Values.ToList();
                var allDone = snapshot.All(s =>
                    s.Status == InitStepStatus.Completed ||
                    s.Status == InitStepStatus.Failed ||
                    s.Status == InitStepStatus.Skipped);

                if (allDone && _isRunning)
                {
                    _isRunning = false;
                    shouldComplete = true;
                }
            }

            if (shouldComplete)
            {
                var allSucceeded = snapshot.All(s =>
                    s.Status == InitStepStatus.Completed ||
                    s.Status == InitStepStatus.Skipped);

                InitCompleted?.Invoke(this,
                    new InitCompletedEventArgs(allSucceeded, snapshot));
            }
        }
    }
}
