using System;
using System.Linq;
using System.Windows.Controls;
using AICA.Core.Initialization;

namespace AICA.ToolWindows
{
    /// <summary>
    /// ViewModel for a single init step in the progress overlay.
    /// </summary>
    public class InitStepViewModel
    {
        public string DisplayName { get; }
        public string StatusIcon { get; }
        public string StatusMessage { get; }

        public InitStepViewModel(InitStepState state)
        {
            DisplayName = state.DisplayName;
            StatusMessage = state.StatusMessage ?? "";
            switch (state.Status)
            {
                case InitStepStatus.Pending:   StatusIcon = "\u25CB"; break;  // ○
                case InitStepStatus.Running:   StatusIcon = "\u25CC"; break;  // ◌
                case InitStepStatus.Completed: StatusIcon = "\u2713"; break;  // ✓
                case InitStepStatus.Failed:    StatusIcon = "\u26A0"; break;  // ⚠
                case InitStepStatus.Skipped:   StatusIcon = "\u2014"; break;  // —
                default:                       StatusIcon = "?"; break;
            }
        }
    }

    /// <summary>
    /// Initialization progress overlay shown during AICA startup.
    /// Displays a progress bar and step checklist.
    /// </summary>
    public partial class InitProgressOverlay : UserControl
    {
        private InitializationManager _manager;

        public InitProgressOverlay()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Bind to an InitializationManager and start tracking progress.
        /// Must be called on UI thread.
        /// </summary>
        public void Bind(InitializationManager manager)
        {
            if (_manager != null)
            {
                _manager.StepChanged -= OnStepChanged;
                _manager.InitCompleted -= OnInitCompleted;
            }

            _manager = manager;
            _manager.StepChanged += OnStepChanged;
            _manager.InitCompleted += OnInitCompleted;

            RefreshUI();
        }

        /// <summary>
        /// Unbind from the InitializationManager and stop tracking.
        /// </summary>
        public void Unbind()
        {
            if (_manager != null)
            {
                _manager.StepChanged -= OnStepChanged;
                _manager.InitCompleted -= OnInitCompleted;
                _manager = null;
            }
        }

        private void OnStepChanged(object sender, InitStepChangedEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(RefreshUI));
        }

        private void OnInitCompleted(object sender, InitCompletedEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                RefreshUI();
                HintText.Text = e.AllSucceeded
                    ? "初始化完成!"
                    : "初始化完成 (部分步骤失败，不影响基本功能)";
            }));
        }

        private void RefreshUI()
        {
            if (_manager == null) return;

            var steps = _manager.GetSteps();
            StepList.ItemsSource = steps.Select(s => new InitStepViewModel(s)).ToList();

            var total = steps.Count;
            var done = steps.Count(s =>
                s.Status == InitStepStatus.Completed ||
                s.Status == InitStepStatus.Failed ||
                s.Status == InitStepStatus.Skipped);

            if (done < total && steps.Any(s => s.Status == InitStepStatus.Running))
            {
                OverallProgress.IsIndeterminate = true;
            }
            else
            {
                OverallProgress.IsIndeterminate = false;
                OverallProgress.Value = total > 0 ? (done * 100.0 / total) : 0;
            }
        }
    }
}
