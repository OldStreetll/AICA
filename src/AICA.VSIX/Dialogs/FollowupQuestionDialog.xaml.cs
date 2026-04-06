using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using AICA.Core.Agent;

namespace AICA.VSIX.Dialogs
{
    /// <summary>
    /// Dialog for asking the user a followup question with multiple choice options
    /// </summary>
    public partial class FollowupQuestionDialog : Window
    {
        private readonly List<QuestionOption> _options;
        private readonly bool _allowCustomInput;
        private readonly List<RadioButton> _optionRadios = new List<RadioButton>();

        public FollowupQuestionResult Result { get; private set; }

        public FollowupQuestionDialog(string question, List<QuestionOption> options, bool allowCustomInput)
        {
            InitializeComponent();

            _options = options ?? throw new ArgumentNullException(nameof(options));
            _allowCustomInput = allowCustomInput;

            // Set question text
            QuestionText.Text = question;

            // Create radio buttons for each option
            bool isFirst = true;
            foreach (var option in options)
            {
                var radio = new RadioButton
                {
                    Content = option.Label,
                    Tag = option,
                    IsChecked = isFirst,
                    Margin = new Thickness(0, 8, 0, 0)
                };

                // Add tooltip if description is provided
                if (!string.IsNullOrWhiteSpace(option.Description))
                {
                    radio.ToolTip = option.Description;
                }

                _optionRadios.Add(radio);
                OptionsPanel.Children.Add(radio);

                // Add description text if provided
                if (!string.IsNullOrWhiteSpace(option.Description))
                {
                    var descText = new TextBlock
                    {
                        Text = option.Description,
                        Margin = new Thickness(20, 2, 0, 0),
                        FontSize = 11,
                        Opacity = 0.7,
                        TextWrapping = TextWrapping.Wrap
                    };
                    OptionsPanel.Children.Add(descText);
                }

                isFirst = false;
            }

            // Show custom input if allowed
            if (allowCustomInput)
            {
                CustomInputPanel.Visibility = Visibility.Visible;
            }

            // Set initial result to cancelled
            Result = FollowupQuestionResult.Canceled();
        }

        private void CustomInputRadio_Checked(object sender, RoutedEventArgs e)
        {
            CustomInputBox.IsEnabled = true;
            CustomInputBox.Focus();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            // Check if custom input is selected
            if (_allowCustomInput && CustomInputRadio.IsChecked == true)
            {
                var customInput = CustomInputBox.Text?.Trim();
                if (string.IsNullOrWhiteSpace(customInput))
                {
                    System.Windows.MessageBox.Show(
                        "Please enter a value or select a predefined option.",
                        "AICA",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                Result = FollowupQuestionResult.FromCustomInput(customInput);
            }
            else
            {
                // Find the selected option
                QuestionOption selectedOption = null;
                foreach (var radio in _optionRadios)
                {
                    if (radio.IsChecked == true)
                    {
                        selectedOption = radio.Tag as QuestionOption;
                        break;
                    }
                }

                if (selectedOption == null)
                {
                    System.Windows.MessageBox.Show(
                        "Please select an option.",
                        "AICA",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                Result = FollowupQuestionResult.FromOption(selectedOption.Value);
            }

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Result = FollowupQuestionResult.Canceled();
            DialogResult = false;
            Close();
        }
    }
}
