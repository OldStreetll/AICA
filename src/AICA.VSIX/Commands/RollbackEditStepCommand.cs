using AICA.Core.Storage;
using AICA.ToolWindows;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AICA.Commands
{
    [Command(PackageGuids.CommandSetString, PackageIds.RollbackEditStepCommand)]
    internal sealed class RollbackEditStepCommand : BaseCommand<RollbackEditStepCommand>
    {
        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var sessionId = ChatToolWindowControl.CurrentSessionId;
            if (string.IsNullOrEmpty(sessionId))
            {
                await VS.MessageBox.ShowWarningAsync(
                    "AICA: Rollback Edit Step",
                    "No active AICA session. Please open the AICA chat first.");
                return;
            }

            var snapshotMgr = SnapshotManager.Instance;
            if (snapshotMgr == null)
            {
                await VS.MessageBox.ShowWarningAsync(
                    "AICA: Rollback Edit Step",
                    "Snapshot system not initialized.");
                return;
            }

            // Get all snapshots for the current session
            List<SnapshotInfo> snapshots;
            try
            {
                snapshots = await snapshotMgr.GetSnapshotsAsync(sessionId);
            }
            catch (Exception ex)
            {
                await VS.MessageBox.ShowErrorAsync(
                    "AICA: Rollback Edit Step",
                    $"Failed to retrieve snapshots: {ex.Message}");
                return;
            }

            if (snapshots == null || snapshots.Count == 0)
            {
                await VS.MessageBox.ShowAsync(
                    "AICA: Rollback Edit Step",
                    "No snapshots available for the current session.",
                    OLEMSGICON.OLEMSGICON_INFO,
                    OLEMSGBUTTON.OLEMSGBUTTON_OK);
                return;
            }

            // Group snapshots by step index
            var stepGroups = snapshots
                .GroupBy(s => s.StepIndex)
                .OrderByDescending(g => g.Key)
                .Select(g => new StepGroup
                {
                    StepIndex = g.Key,
                    Timestamp = g.Min(s => s.CreatedUtc),
                    Files = g.Select(s => s.OriginalFilePath).Distinct().ToList()
                })
                .ToList();

            // Ensure we're back on the UI thread after awaits (WPF Window requires it)
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            // Show QuickPick-style dialog
            var selected = ShowStepPickerDialog(stepGroups);
            if (selected == null)
                return; // user cancelled

            // Pre-check for post-snapshot modifications
            var warningLines = new List<string>();
            foreach (var filePath in selected.Files)
            {
                try
                {
                    if (!System.IO.File.Exists(filePath))
                    {
                        warningLines.Add($"  [DELETED] {System.IO.Path.GetFileName(filePath)} — will be re-created");
                    }
                    else
                    {
                        var fileInfo = new System.IO.FileInfo(filePath);
                        if (fileInfo.LastWriteTimeUtc > selected.Timestamp)
                        {
                            warningLines.Add($"  [MODIFIED] {System.IO.Path.GetFileName(filePath)} — changed after snapshot");
                        }
                    }
                }
                catch { /* skip check errors */ }
            }

            // Confirmation dialog
            var confirmSb = new StringBuilder();
            confirmSb.AppendLine($"Rollback Step {selected.StepIndex} ({selected.Timestamp:HH:mm:ss})\n");
            confirmSb.AppendLine("Files to restore:");
            foreach (var f in selected.Files)
                confirmSb.AppendLine($"  \u2022 {System.IO.Path.GetFileName(f)}");

            if (warningLines.Count > 0)
            {
                confirmSb.AppendLine("\nWarnings:");
                foreach (var w in warningLines)
                    confirmSb.AppendLine(w);
            }

            confirmSb.AppendLine("\nThis will overwrite the current file contents.");
            confirmSb.AppendLine("Git status will NOT be affected.");
            confirmSb.AppendLine("\nProceed?");

            var confirm = await VS.MessageBox.ShowAsync(
                "AICA: Confirm Rollback",
                confirmSb.ToString(),
                OLEMSGICON.OLEMSGICON_WARNING,
                OLEMSGBUTTON.OLEMSGBUTTON_YESNO);

            if (confirm != VSConstants.MessageBoxResult.IDYES)
                return;

            // Execute rollback
            RestoreResult result;
            try
            {
                result = await snapshotMgr.RestoreAsync(sessionId, selected.StepIndex);
            }
            catch (Exception ex)
            {
                await VS.MessageBox.ShowErrorAsync(
                    "AICA: Rollback Failed",
                    $"Rollback failed: {ex.Message}");
                return;
            }

            // Show result
            var resultSb = new StringBuilder();
            var restoredCount = result.Files.Count(f => f.Success);
            var failedCount = result.Files.Count(f => !f.Success);

            if (result.Success)
            {
                resultSb.AppendLine($"Successfully restored {restoredCount} file(s) from step {selected.StepIndex}.");
            }
            else
            {
                resultSb.AppendLine($"Partial rollback: {restoredCount} restored, {failedCount} failed.");
                foreach (var f in result.Files.Where(f => !f.Success))
                    resultSb.AppendLine($"  \u2717 {System.IO.Path.GetFileName(f.FilePath)}: {f.FailureReason}");
            }

            if (result.Warnings.Count > 0)
            {
                resultSb.AppendLine("\nWarnings:");
                foreach (var w in result.Warnings)
                    resultSb.AppendLine($"  \u2022 {w}");
            }

            resultSb.AppendLine("\nGit status was not affected by this rollback.");

            await VS.MessageBox.ShowAsync(
                "AICA: Rollback Result",
                resultSb.ToString(),
                result.Success ? OLEMSGICON.OLEMSGICON_INFO : OLEMSGICON.OLEMSGICON_WARNING,
                OLEMSGBUTTON.OLEMSGBUTTON_OK);

            await VS.StatusBar.ShowMessageAsync(
                result.Success
                    ? $"AICA: Rolled back step {selected.StepIndex} ({restoredCount} files)"
                    : $"AICA: Rollback partial \u2014 {failedCount} file(s) failed");
        }

        /// <summary>
        /// Shows a WPF ListBox dialog for selecting a snapshot step. Returns null if cancelled.
        /// </summary>
        private static StepGroup ShowStepPickerDialog(List<StepGroup> steps)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            StepGroup result = null;

            var window = new Window
            {
                Title = "AICA: Rollback Edit Step",
                Width = 500,
                Height = 380,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize,
                Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
                Foreground = Brushes.White
            };

            var panel = new StackPanel { Margin = new Thickness(16) };

            var header = new TextBlock
            {
                Text = "Select a step to rollback:",
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 12),
                Foreground = Brushes.White
            };
            panel.Children.Add(header);

            var listBox = new ListBox
            {
                Height = 240,
                Background = new SolidColorBrush(Color.FromRgb(37, 37, 38)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
                FontFamily = new FontFamily("Consolas")
            };

            foreach (var step in steps)
            {
                var fileNames = string.Join(", ",
                    step.Files.Select(f => System.IO.Path.GetFileName(f)));
                if (fileNames.Length > 60)
                    fileNames = fileNames.Substring(0, 57) + "...";

                var item = new ListBoxItem
                {
                    Content = $"Step {step.StepIndex}  |  {step.Timestamp:HH:mm:ss}  |  {fileNames}",
                    Tag = step,
                    Foreground = Brushes.White,
                    Padding = new Thickness(4, 6, 4, 6)
                };
                listBox.Items.Add(item);
            }

            if (listBox.Items.Count > 0)
                listBox.SelectedIndex = 0;

            panel.Children.Add(listBox);

            var hint = new TextBlock
            {
                Text = "This operation does NOT affect Git.",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150)),
                Margin = new Thickness(0, 8, 0, 8)
            };
            panel.Children.Add(hint);

            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var okButton = new Button
            {
                Content = "Rollback",
                Width = 90,
                Height = 28,
                Margin = new Thickness(0, 0, 8, 0),
                IsDefault = true
            };
            okButton.Click += (s, args) =>
            {
                if (listBox.SelectedItem is ListBoxItem sel)
                    result = sel.Tag as StepGroup;
                window.DialogResult = true;
                window.Close();
            };

            var cancelButton = new Button
            {
                Content = "Cancel",
                Width = 90,
                Height = 28,
                IsCancel = true
            };
            cancelButton.Click += (s, args) => window.Close();

            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);
            panel.Children.Add(buttonPanel);

            window.Content = panel;

            // Double-click to select
            listBox.MouseDoubleClick += (s, args) =>
            {
                if (listBox.SelectedItem is ListBoxItem sel)
                    result = sel.Tag as StepGroup;
                window.DialogResult = true;
                window.Close();
            };

            window.ShowDialog();
            return result;
        }

        private class StepGroup
        {
            public int StepIndex { get; set; }
            public DateTime Timestamp { get; set; }
            public List<string> Files { get; set; }
        }
    }
}
