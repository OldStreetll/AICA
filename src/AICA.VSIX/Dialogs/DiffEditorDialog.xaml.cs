using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Collections.Generic;
using System.Linq;

namespace AICA.VSIX.Dialogs
{
    /// <summary>
    /// Diff editor dialog that allows users to view differences and edit content before applying changes
    /// </summary>
    public partial class DiffEditorDialog : Window
    {
        public string ModifiedContent { get; private set; }
        public bool WasModified { get; private set; }

        private string _originalContent;
        private string _modifiedContent;
        private bool _isUpdatingScroll = false;
        private bool _isUpdatingText = false;

        public DiffEditorDialog(string filePath, string originalContent, string modifiedContent)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[AICA] DiffEditorDialog constructor called");

                // Initialize fields BEFORE InitializeComponent to prevent null reference in event handlers
                _originalContent = originalContent ?? string.Empty;
                _modifiedContent = modifiedContent ?? string.Empty;

                InitializeComponent();
                System.Diagnostics.Debug.WriteLine($"[AICA] InitializeComponent completed");

                // Set file path
                FilePathText.Text = filePath;

                // Calculate and display changes summary
                var originalLines = _originalContent.Split('\n').Length;
                var modifiedLines = _modifiedContent.Split('\n').Length;
                var diff = modifiedLines - originalLines;
                var diffText = diff > 0 ? $"+{diff}" : diff.ToString();
                ChangesSummaryText.Text = $"Lines: {originalLines} → {modifiedLines} ({diffText})";

                ModifiedContent = _modifiedContent;
                WasModified = false;

                System.Diagnostics.Debug.WriteLine($"[AICA] About to call RenderDiffView");
                // Render diff view
                RenderDiffView();
                System.Diagnostics.Debug.WriteLine($"[AICA] RenderDiffView completed");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AICA] DiffEditorDialog constructor exception: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[AICA] Stack trace: {ex.StackTrace}");
                throw;
            }
        }

        private void RenderDiffView()
        {
            var originalLines = _originalContent.Split('\n');
            var modifiedLines = _modifiedContent.Split('\n');

            // Compute line-by-line diff
            var diffResult = ComputeDiff(originalLines, modifiedLines);

            // Render original side
            var originalDoc = OriginalRichTextBox.Document;
            originalDoc.Blocks.Clear();

            foreach (var item in diffResult)
            {
                var para = new Paragraph { Margin = new Thickness(0), LineHeight = 1 };
                var run = new Run(item.OriginalLine ?? string.Empty);

                if (item.Type == DiffType.Deleted)
                {
                    para.Background = new SolidColorBrush(Color.FromRgb(255, 200, 200)); // Light red
                    run.Foreground = new SolidColorBrush(Color.FromRgb(139, 0, 0)); // Dark red
                }
                else if (item.Type == DiffType.Unchanged)
                {
                    run.Foreground = Brushes.Black;
                }

                para.Inlines.Add(run);
                originalDoc.Blocks.Add(para);
            }

            // Render modified side
            var modifiedDoc = ModifiedRichTextBox.Document;
            modifiedDoc.Blocks.Clear();

            foreach (var item in diffResult)
            {
                var para = new Paragraph { Margin = new Thickness(0), LineHeight = 1 };
                var run = new Run(item.ModifiedLine ?? string.Empty);

                if (item.Type == DiffType.Added)
                {
                    para.Background = new SolidColorBrush(Color.FromRgb(200, 255, 200)); // Light green
                    run.Foreground = new SolidColorBrush(Color.FromRgb(0, 100, 0)); // Dark green
                }
                else if (item.Type == DiffType.Unchanged)
                {
                    run.Foreground = Brushes.Black;
                }

                para.Inlines.Add(run);
                modifiedDoc.Blocks.Add(para);
            }
        }

        private List<DiffLine> ComputeDiff(string[] originalLines, string[] modifiedLines)
        {
            var result = new List<DiffLine>();

            // Simple line-by-line diff using longest common subsequence (LCS)
            var lcs = LongestCommonSubsequence(originalLines, modifiedLines);

            int i = 0, j = 0;
            foreach (var commonLine in lcs)
            {
                // Add deleted lines
                while (i < originalLines.Length && originalLines[i] != commonLine)
                {
                    result.Add(new DiffLine
                    {
                        Type = DiffType.Deleted,
                        OriginalLine = originalLines[i],
                        ModifiedLine = string.Empty
                    });
                    i++;
                }

                // Add added lines
                while (j < modifiedLines.Length && modifiedLines[j] != commonLine)
                {
                    result.Add(new DiffLine
                    {
                        Type = DiffType.Added,
                        OriginalLine = string.Empty,
                        ModifiedLine = modifiedLines[j]
                    });
                    j++;
                }

                // Add unchanged line
                result.Add(new DiffLine
                {
                    Type = DiffType.Unchanged,
                    OriginalLine = originalLines[i],
                    ModifiedLine = modifiedLines[j]
                });
                i++;
                j++;
            }

            // Add remaining deleted lines
            while (i < originalLines.Length)
            {
                result.Add(new DiffLine
                {
                    Type = DiffType.Deleted,
                    OriginalLine = originalLines[i],
                    ModifiedLine = string.Empty
                });
                i++;
            }

            // Add remaining added lines
            while (j < modifiedLines.Length)
            {
                result.Add(new DiffLine
                {
                    Type = DiffType.Added,
                    OriginalLine = string.Empty,
                    ModifiedLine = modifiedLines[j]
                });
                j++;
            }

            return result;
        }

        private List<string> LongestCommonSubsequence(string[] a, string[] b)
        {
            int m = a.Length;
            int n = b.Length;
            int[,] dp = new int[m + 1, n + 1];

            // Build LCS table
            for (int i = 1; i <= m; i++)
            {
                for (int j = 1; j <= n; j++)
                {
                    if (a[i - 1] == b[j - 1])
                        dp[i, j] = dp[i - 1, j - 1] + 1;
                    else
                        dp[i, j] = System.Math.Max(dp[i - 1, j], dp[i, j - 1]);
                }
            }

            // Backtrack to find LCS
            var lcs = new List<string>();
            int x = m, y = n;
            while (x > 0 && y > 0)
            {
                if (a[x - 1] == b[y - 1])
                {
                    lcs.Insert(0, a[x - 1]);
                    x--;
                    y--;
                }
                else if (dp[x - 1, y] > dp[x, y - 1])
                    x--;
                else
                    y--;
            }

            return lcs;
        }

        private void ModifiedRichTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                if (_isUpdatingText) return;

                // Skip if not fully initialized yet
                if (string.IsNullOrEmpty(_originalContent))
                    return;

                // Check if UI elements are initialized
                if (ModifiedRichTextBox == null || ChangesSummaryText == null)
                    return;

                WasModified = true;

                // Extract plain text from RichTextBox
                var textRange = new TextRange(ModifiedRichTextBox.Document.ContentStart, ModifiedRichTextBox.Document.ContentEnd);
                ModifiedContent = textRange.Text.TrimEnd('\r', '\n');

                // Update changes summary
                var originalLines = _originalContent.Split('\n').Length;
                var modifiedLines = ModifiedContent.Split('\n').Length;
                var diff = modifiedLines - originalLines;
                var diffText = diff > 0 ? $"+{diff}" : diff.ToString();
                ChangesSummaryText.Text = $"Lines: {originalLines} → {modifiedLines} ({diffText})";
            }
            catch (Exception ex)
            {
                // Log but don't crash
                System.Diagnostics.Debug.WriteLine($"[AICA] ModifiedRichTextBox_TextChanged error: {ex.Message}");
            }
        }

        private void OriginalRichTextBox_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (_isUpdatingScroll) return;

            _isUpdatingScroll = true;
            ModifiedRichTextBox.ScrollToVerticalOffset(e.VerticalOffset);
            ModifiedRichTextBox.ScrollToHorizontalOffset(e.HorizontalOffset);
            _isUpdatingScroll = false;
        }

        private void ModifiedRichTextBox_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (_isUpdatingScroll) return;

            _isUpdatingScroll = true;
            OriginalRichTextBox.ScrollToVerticalOffset(e.VerticalOffset);
            OriginalRichTextBox.ScrollToHorizontalOffset(e.HorizontalOffset);
            _isUpdatingScroll = false;
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private enum DiffType
        {
            Unchanged,
            Added,
            Deleted
        }

        private class DiffLine
        {
            public DiffType Type { get; set; }
            public string OriginalLine { get; set; }
            public string ModifiedLine { get; set; }
        }
    }
}
