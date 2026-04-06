using System;
using System.Threading.Tasks;
using System.Windows;

namespace AICA.VSIX.Dialogs
{
    /// <summary>
    /// Non-modal confirmation dialog
    /// </summary>
    public partial class NonModalConfirmDialog : Window
    {
        private TaskCompletionSource<bool> _taskCompletionSource;

        public NonModalConfirmDialog(string message)
        {
            InitializeComponent();
            MessageText.Text = message;
            _taskCompletionSource = new TaskCompletionSource<bool>();
        }

        /// <summary>
        /// Show the dialog and return a task that completes when user clicks a button
        /// </summary>
        public Task<bool> ShowDialogAsync()
        {
            Show();
            return _taskCompletionSource.Task;
        }

        private void YesButton_Click(object sender, RoutedEventArgs e)
        {
            _taskCompletionSource.TrySetResult(true);
            Close();
        }

        private void NoButton_Click(object sender, RoutedEventArgs e)
        {
            _taskCompletionSource.TrySetResult(false);
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            // If window is closed without clicking a button, return false
            _taskCompletionSource.TrySetResult(false);
        }
    }
}
