using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation; // Needed for animations
using System.Windows.Threading;       // Needed for DispatcherTimer

namespace Counsel
{
    /// <summary>
    /// Enum defining the different operational modes of the application.
    /// 'None' represents the default Chat Only mode where the canvas is hidden.
    /// </summary>
    public enum AppMode
    {
        DeepResearch,
        Paralegal,
        CrossExamine,
        None // Represents Chat Only mode (Canvas hidden)
    }

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private AppMode _currentMode = AppMode.None; // Default to Chat Only
        private bool _isLoaded = false;             // Flag to indicate if window is fully loaded
        private readonly double _canvasTargetWidth = 400.0; // Target width for the canvas sidebar content
        private readonly GridLength _canvasTargetGridWidth; // Target GridLength for the column
        private Storyboard _sidebarStoryboard;      // Storyboard for sidebar animations

        public MainWindow()
        {
            InitializeComponent();
            // Store the target GridLength based on the double width
            _canvasTargetGridWidth = new GridLength(_canvasTargetWidth);
            this.Loaded += MainWindow_Loaded;
            // Event handlers are assigned in XAML (e.g., Click="...")
        }

        /// <summary>
        /// Handles the Window Loaded event. Sets initial state.
        /// </summary>
        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            _isLoaded = true;

            // Initialize Storyboard
            _sidebarStoryboard = new Storyboard();

            // --- Initial State Setup (Chat Only Mode) ---
            ChatOnlyModeButton.IsChecked = true; // Set the radio button
            _currentMode = AppMode.None;         // Set the internal mode

            // **MODIFIED**: Explicitly collapse the Canvas COLUMN on load
            CanvasColumn.Width = new GridLength(0);

            // Set the RightSidebar CONTENT properties for the hidden state (no animation needed here)
            RightSidebar.Width = 0;
            RightSidebar.Opacity = 0;
            RightSidebar.Visibility = Visibility.Collapsed; // Ensure content is collapsed too

            // Update UI elements based on initial state
            UpdateContextDisplay();
            UpdatePlaceholderVisibility();
            UpdateInputPlaceholderForMode(_currentMode); // Set initial placeholder

            // Set initial case context text from the ComboBox selection
            if (CaseSelectorComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                ContextCaseText.Text = $"Case: {selectedItem.Content}";
            }
            else
            {
                ContextCaseText.Text = "Case: [Select a Case]";
            }

            // Add initial system message confirming mode
            AddMessageBubble($"[System] Started in {GetModeFriendlyName(_currentMode)} mode. Canvas is hidden.", false, "System");
        }

        // --- Input Handling ---
        // (No changes in this section)
        private void RealInputTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            UpdatePlaceholderVisibility();
        }

        private void RealInputTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            UpdatePlaceholderVisibility();
        }

        private void RealInputTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdatePlaceholderVisibility();
            SendButton.IsEnabled = !string.IsNullOrWhiteSpace(RealInputTextBox.Text);
        }

        private void RealInputTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && !(Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift)))
            {
                if (SendButton.IsEnabled)
                {
                    SendMessage(RealInputTextBox.Text);
                }
                e.Handled = true;
            }
        }

        private void UpdatePlaceholderVisibility()
        {
            if (InputPlaceholder == null || RealInputTextBox == null) return;
            InputPlaceholder.Visibility = (string.IsNullOrWhiteSpace(RealInputTextBox.Text) && !RealInputTextBox.IsKeyboardFocused)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void SendButton_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(RealInputTextBox.Text))
            {
                SendMessage(RealInputTextBox.Text);
            }
        }

        private void SendMessage(string message)
        {
            if (!_isLoaded || ConversationPanel == null || ConversationScrollViewer == null) return;

            ClearWelcomeMessageIfNeeded();
            AddMessageBubble(message, isFromUser: true);
            SimulateResponse(message);

            RealInputTextBox.Clear();
            SendButton.IsEnabled = false;
            UpdatePlaceholderVisibility();
            RealInputTextBox.Focus();
        }

        private void ClearWelcomeMessageIfNeeded()
        {
            if (ConversationPanel.Children.Count > 0 && ConversationPanel.Children[0] is Border initialBorder && initialBorder.Name == "InitialWelcomeMessage")
            {
                ConversationPanel.Children.RemoveAt(0);
            }
        }

        // --- Conversation Display ---
        // (No changes in this section)
        private void AddMessageBubble(string message, bool isFromUser, string agentName = null)
        {
            if (!_isLoaded || ConversationPanel == null || ConversationScrollViewer == null)
            {
                Dispatcher.InvokeAsync(() => AddMessageBubble(message, isFromUser, agentName), DispatcherPriority.Loaded);
                return;
            }

            Border messageBubble = new Border();
            TextBlock textBlock = new TextBlock { Text = message };
            StackPanel contentStack = new StackPanel();

            if (isFromUser)
            {
                messageBubble.Style = (Style)FindResource("UserMessageBubble");
                textBlock.Style = (Style)FindResource("UserMessageText");
                contentStack.Children.Add(textBlock);
            }
            else
            {
                if (agentName == "System")
                {
                    messageBubble.Style = (Style)FindResource("SystemMessageBubble");
                    textBlock.Style = (Style)FindResource("SystemMessageText");
                    contentStack.Children.Add(textBlock);
                }
                else
                {
                    messageBubble.Style = (Style)FindResource("AgentMessageBubble");
                    textBlock.Style = (Style)FindResource("AgentMessageText");

                    if (!string.IsNullOrEmpty(agentName))
                    {
                        TextBlock agentNameBlock = new TextBlock
                        {
                            Style = (Style)FindResource("AgentNameText"),
                            Text = agentName
                        };
                        contentStack.Children.Add(agentNameBlock);
                    }
                    contentStack.Children.Add(textBlock);

                    Button copyButton = new Button
                    {
                        Style = (Style)FindResource("CopyButtonStyle"),
                        Tag = textBlock
                    };
                    copyButton.Click += CopyAgentTextButton_Click;
                    contentStack.Children.Add(copyButton);

                    messageBubble.MouseEnter += AgentBubble_MouseEnter;
                    messageBubble.MouseLeave += AgentBubble_MouseLeave;
                }
            }

            messageBubble.Child = contentStack;
            ConversationPanel.Children.Add(messageBubble);
            ConversationScrollViewer.ScrollToBottom();
        }

        private void SimulateResponse(string userMessage)
        {
            if (!_isLoaded || ConversationPanel == null || ConversationScrollViewer == null)
            {
                Dispatcher.InvokeAsync(() => SimulateResponse(userMessage), DispatcherPriority.Loaded);
                return;
            }

            string agentName = GetAgentNameForTask();
            TextBlock thinkingIndicator = new TextBlock
            {
                Style = (Style)FindResource("ThinkingIndicatorText"),
                Text = $"Counsel is thinking... [{agentName} Active]"
            };
            ConversationPanel.Children.Add(thinkingIndicator);
            ConversationScrollViewer.ScrollToBottom();

            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                if (ConversationPanel.Children.Contains(thinkingIndicator))
                {
                    ConversationPanel.Children.Remove(thinkingIndicator);
                }
                string response = GeneratePlaceholderResponse(agentName, userMessage);
                AddMessageBubble(response, isFromUser: false, agentName: agentName);

                if (_currentMode != AppMode.None)
                {
                    UpdateCanvas(GenerateCanvasContent(userMessage, agentName), GetCanvasTitle());
                }
            };
            timer.Start();
        }

        private string GeneratePlaceholderResponse(string agentName, string userMessage)
        {
            string caseContext = "[No Case Selected]";
            if (CaseSelectorComboBox.SelectedItem is ComboBoxItem selectedItem && CaseSelectorComboBox.SelectedIndex > -1)
            {
                string content = selectedItem.Content.ToString();
                if (!content.StartsWith("["))
                {
                    caseContext = content;
                }
            }
            string contextInfo = $" (Context: Case '{caseContext}', Mode: {_currentMode})";
            switch (agentName)
            {
                case "Deep Research Agent": return $"[Deep Research] Analyzing your query about '{Shorten(userMessage)}'.{contextInfo}";
                case "Paralegal Agent": return $"[Paralegal] Processing request related to '{Shorten(userMessage)}'.{contextInfo}";
                case "Cross Examine Agent": return $"[Cross Examine] Evaluating testimony regarding '{Shorten(userMessage)}'.{contextInfo}";
                case "General Agent": default: return $"[Response] Received your message: '{Shorten(userMessage)}'.{contextInfo}";
            }
        }

        private string Shorten(string text, int maxLength = 25)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            text = text.Replace(Environment.NewLine, " ").Trim();
            return text.Length <= maxLength ? text : text.Substring(0, maxLength) + "...";
        }

        private string GetAgentNameForTask()
        {
            switch (_currentMode)
            {
                case AppMode.DeepResearch: return "Deep Research Agent";
                case AppMode.Paralegal: return "Paralegal Agent";
                case AppMode.CrossExamine: return "Cross Examine Agent";
                case AppMode.None: default: return "General Agent";
            }
        }

        // --- Canvas Content Generation ---
        // (No changes in this section)
        private string GenerateCanvasContent(string userMessage, string agentName)
        {
            string caseContext = "[No Case Selected]";
            if (CaseSelectorComboBox.SelectedItem is ComboBoxItem selectedItem && CaseSelectorComboBox.SelectedIndex > -1)
            {
                string content = selectedItem.Content.ToString();
                if (!content.StartsWith("["))
                {
                    caseContext = content;
                }
            }
            switch (_currentMode)
            {
                case AppMode.DeepResearch: return $"───────────────────────────────\nISSUE: Whether '{Shorten(userMessage)}' applies in {caseContext}\n───────────────────────────────\n\n🔹 Summary:\n[Placeholder summary based on '{Shorten(userMessage)}'...]\n\n🔹 Statutory References:\n- [Placeholder Statute 1]\n- [Placeholder Statute 2]\n\n🔹 Relevant Cases:\n- *[Case Name 1]* – [Brief relevance]\n- *[Case Name 2]* – [Brief relevance]\n\n🔹 Commentary:\n[Placeholder analysis based on precedents...]";
                case AppMode.Paralegal: return $"───────────────────────────────\n🔍 Mentions of \"{Shorten(userMessage)}\"\n───────────────────────────────\n\n📁 File: [Placeholder Doc 1.pdf]\nPg X: “[Placeholder mention of {Shorten(userMessage)}…]”\n\n📁 File: [Placeholder Doc 2.docx]\nPg Y: “[Another reference to {Shorten(userMessage)}…]”\n\nSummary:\nMultiple documents reference '{Shorten(userMessage)}' in the context of {caseContext}.\n[Placeholder action item or note].";
                case AppMode.CrossExamine: return $"───────────────────────────────\n🎯 Cross-Examination Insight\n───────────────────────────────\n\n🔹 Flag: Potential Contradiction\nWitness statement regarding \"{Shorten(userMessage)}\" may conflict with [Placeholder Evidence Doc/Pg].\n\n🔹 Suggested Follow-up:\n“Could you elaborate on your testimony concerning '{Shorten(userMessage)}' in light of [Evidence]?”\n\n🔹 Note Reference:\n[Placeholder link/reference to testimony notes]";
                case AppMode.None: default: return "[Canvas not available in Chat Only mode]";
            }
        }

        private string GetCanvasTitle()
        {
            switch (_currentMode)
            {
                case AppMode.DeepResearch: return "Legal Research Brief";
                case AppMode.Paralegal: return "Evidence Summary";
                case AppMode.CrossExamine: return "Insight Sheet";
                case AppMode.None: default: return "Canvas";
            }
        }

        // --- Mode Switching & Animation ---

        private void ModeButton_Checked(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded || sender == null) return;

            AppMode newMode = AppMode.None;
            if (sender == ChatOnlyModeButton) newMode = AppMode.None;
            else if (sender == DeepResearchModeButton) newMode = AppMode.DeepResearch;
            else if (sender == ParalegalModeButton) newMode = AppMode.Paralegal;
            else if (sender == CrossExamineModeButton) newMode = AppMode.CrossExamine;

            if (newMode != _currentMode)
            {
                UpdateMode(newMode);
            }
        }

        private void UpdateMode(AppMode newMode)
        {
            if (!_isLoaded || RightSidebar == null || CanvasColumn == null) return; // Ensure elements are ready

            _currentMode = newMode;
            string modeName = GetModeFriendlyName(_currentMode);
            string systemMessage = $"[System] Switched to {modeName} mode.";

            UpdateInputPlaceholderForMode(_currentMode);
            UpdateContextDisplay();

            if (_currentMode == AppMode.None)
            {
                // Hide Canvas
                AnimateSidebar(false); // Animate content out, collapse column on complete
                systemMessage += " Canvas hidden.";
                UpdateCanvas("", ""); // Clear canvas content
            }
            else
            {
                // Show Canvas
                UpdateCanvas($"[Enter query to view {GetCanvasTitle()}.]", GetCanvasTitle()); // Set initial content
                AnimateSidebar(true); // Expand column, animate content in
                systemMessage += " Canvas shown.";
            }

            AddMessageBubble(systemMessage, false, "System");

            // Ensure RadioButtons reflect the current state
            ChatOnlyModeButton.IsChecked = (_currentMode == AppMode.None);
            DeepResearchModeButton.IsChecked = (_currentMode == AppMode.DeepResearch);
            ParalegalModeButton.IsChecked = (_currentMode == AppMode.Paralegal);
            CrossExamineModeButton.IsChecked = (_currentMode == AppMode.CrossExamine);
        }

        /// <summary>
        /// Animates the Right Sidebar CONTENT width/opacity and sets the COLUMN width.
        /// </summary>
        /// <param name="show">True to show the sidebar, false to hide it.</param>
        private void AnimateSidebar(bool show)
        {
            if (_sidebarStoryboard == null || RightSidebar == null || CanvasColumn == null) return;

            _sidebarStoryboard.Stop(RightSidebar); // Stop previous animation on the content
            _sidebarStoryboard.Children.Clear();
            // Clean up previous handler rigorously
            _sidebarStoryboard.Completed -= SidebarStoryboard_Completed_Hide;

            Duration duration = new Duration(TimeSpan.FromSeconds(0.3));
            var easing = new CubicEase { EasingMode = EasingMode.EaseOut };

            // Create Width Animation (for RightSidebar CONTENT)
            DoubleAnimation widthAnimation = new DoubleAnimation
            {
                Duration = duration,
                EasingFunction = easing
            };
            Storyboard.SetTarget(widthAnimation, RightSidebar);
            Storyboard.SetTargetProperty(widthAnimation, new PropertyPath(FrameworkElement.WidthProperty));

            // Create Opacity Animation (for RightSidebar CONTENT)
            DoubleAnimation opacityAnimation = new DoubleAnimation
            {
                Duration = duration,
                EasingFunction = easing
            };
            Storyboard.SetTarget(opacityAnimation, RightSidebar);
            Storyboard.SetTargetProperty(opacityAnimation, new PropertyPath(UIElement.OpacityProperty));

            if (show)
            {
                // --- SHOW ---
                // **MODIFIED**: Set COLUMN width immediately to allow expansion
                CanvasColumn.Width = _canvasTargetGridWidth;
                // Make CONTENT visible before animating in
                RightSidebar.Visibility = Visibility.Visible;

                // Animate CONTENT width from 0 to target
                widthAnimation.From = 0; // Start from 0 as it was collapsed
                widthAnimation.To = _canvasTargetWidth;

                // Animate CONTENT opacity from 0 to 1
                opacityAnimation.From = 0.0;
                opacityAnimation.To = 1.0;
            }
            else
            {
                // --- HIDE ---
                // Animate CONTENT width from current to 0
                widthAnimation.From = RightSidebar.ActualWidth;
                widthAnimation.To = 0;

                // Animate CONTENT opacity from current to 0
                opacityAnimation.From = RightSidebar.Opacity;
                opacityAnimation.To = 0.0;

                // **MODIFIED**: Attach completed handler to collapse COLUMN *after* animation
                _sidebarStoryboard.Completed += SidebarStoryboard_Completed_Hide;
            }

            _sidebarStoryboard.Children.Add(widthAnimation);
            _sidebarStoryboard.Children.Add(opacityAnimation);
            _sidebarStoryboard.Begin(RightSidebar, true); // Animate the RightSidebar content
        }

        /// <summary>
        /// Event handler for the completion of the sidebar hide animation.
        /// Sets content visibility to Collapsed and collapses the COLUMN.
        /// </summary>
        private void SidebarStoryboard_Completed_Hide(object sender, EventArgs e)
        {
            // Ensure elements are still valid
            if (RightSidebar == null || CanvasColumn == null) return;

            // Only collapse if we are still in the 'None' mode
            if (_currentMode == AppMode.None)
            {
                // **MODIFIED**: Collapse the COLUMN width
                CanvasColumn.Width = new GridLength(0);
                // Collapse the CONTENT visibility
                RightSidebar.Visibility = Visibility.Collapsed;
            }

            // Clean up the event handler
            if (_sidebarStoryboard != null)
            {
                _sidebarStoryboard.Completed -= SidebarStoryboard_Completed_Hide;
            }
        }


        private void UpdateInputPlaceholderForMode(AppMode mode)
        {
            if (InputPlaceholder == null) return;
            switch (mode)
            {
                case AppMode.DeepResearch: InputPlaceholder.Text = "Ask a legal research question..."; break;
                case AppMode.Paralegal: InputPlaceholder.Text = "Request document summaries or evidence extracts..."; break;
                case AppMode.CrossExamine: InputPlaceholder.Text = "Submit testimony notes for insights..."; break;
                case AppMode.None: default: InputPlaceholder.Text = "Ask Counsel..."; break;
            }
            UpdatePlaceholderVisibility();
        }

        private string GetModeFriendlyName(AppMode mode)
        {
            switch (mode)
            {
                case AppMode.DeepResearch: return "Deep Research";
                case AppMode.Paralegal: return "Paralegal";
                case AppMode.CrossExamine: return "Cross Examine";
                case AppMode.None: default: return "Chat Only";
            }
        }

        // --- Canvas Update ---
        // (No changes in this section)
        private void UpdateCanvas(string content, string title)
        {
            if (!_isLoaded || RightSidebarTitle == null || RightSidebarContent == null) return;
            RightSidebarTitle.Text = title;
            RightSidebarContent.Text = content;
        }

        // --- Other Event Handlers ---
        // (No changes in this section unless specified)

        private void CaseSelectorComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded || ContextCaseText == null || CaseSelectorComboBox == null) return;

            string selectedCase = "[No Case Selected]";
            bool isSpecialAction = false;

            if (CaseSelectorComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                string content = selectedItem.Content.ToString();
                if (content.StartsWith("["))
                {
                    isSpecialAction = true;
                    MessageBox.Show($"Placeholder: '{content}' action triggered.", "Case Selection", MessageBoxButton.OK, MessageBoxImage.Information);
                    object previousItem = null;
                    if (e.RemovedItems.Count > 0) previousItem = e.RemovedItems[0];
                    Dispatcher.BeginInvoke(new Action(() => {
                        CaseSelectorComboBox.SelectedItem = previousItem;
                    }), DispatcherPriority.ContextIdle);
                }
                else
                {
                    selectedCase = content;
                    AddMessageBubble($"[System] Switched to case: {selectedCase}", false, "System");
                }
            }

            if (!isSpecialAction)
            {
                UpdateContextDisplay();
                if (_currentMode != AppMode.None)
                {
                    UpdateCanvas($"[Case switched to {selectedCase}. Enter query for {GetCanvasTitle()}.]", GetCanvasTitle());
                }
            }
        }

        private void DocumentComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded || ContextFocusText == null || DocumentComboBox == null) return;

            bool documentSelected = false;
            string selectedDocument = "[No Document Selected]";

            if (DocumentComboBox.SelectedItem is ComboBoxItem selectedItem && DocumentComboBox.SelectedIndex > 0)
            {
                selectedDocument = selectedItem.Content.ToString();
                documentSelected = true;
                AddMessageBubble($"[System] Focused on document: {selectedDocument}", false, "System");
                if (_currentMode != AppMode.None)
                {
                    string canvasTitle = $"Document Summary: {Shorten(selectedDocument, 30)}";
                    UpdateCanvas($"Summary for {selectedDocument}:\n\n[Placeholder document summary...]", canvasTitle);
                }
            }

            UpdateContextDisplay();

            if (!documentSelected && _currentMode != AppMode.None)
            {
                UpdateCanvas($"[Select a document or enter query for {GetCanvasTitle()}.]", GetCanvasTitle());
            }
        }

        private void UpdateContextDisplay()
        {
            if (!_isLoaded || ContextCaseText == null || ContextModeText == null || ContextFocusText == null) return;

            string caseText = "[No Case Selected]";
            if (CaseSelectorComboBox.SelectedItem is ComboBoxItem selectedCaseItem)
            {
                string caseContent = selectedCaseItem.Content.ToString();
                if (!caseContent.StartsWith("["))
                {
                    caseText = caseContent;
                }
            }
            ContextCaseText.Text = $"Case: {caseText}";
            ContextModeText.Text = $"Mode: {GetModeFriendlyName(_currentMode)}";

            if (DocumentComboBox.SelectedItem is ComboBoxItem selectedDocItem && DocumentComboBox.SelectedIndex > 0)
            {
                ContextFocusText.Text = $"Focus: {selectedDocItem.Content}";
                ContextFocusText.Visibility = Visibility.Visible;
            }
            else
            {
                ContextFocusText.Visibility = Visibility.Collapsed;
            }
        }

        // --- UI Interaction Helpers (Copy Button, etc.) ---
        // (No changes in this section)
        private void AgentBubble_MouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is Border bubble && bubble.Child is StackPanel stackPanel)
            {
                foreach (var child in stackPanel.Children)
                {
                    if (child is Button copyButton && copyButton.Style == (Style)FindResource("CopyButtonStyle"))
                    {
                        copyButton.Visibility = Visibility.Visible;
                        break;
                    }
                }
            }
        }

        private void AgentBubble_MouseLeave(object sender, MouseEventArgs e)
        {
            if (sender is Border bubble && bubble.Child is StackPanel stackPanel)
            {
                foreach (var child in stackPanel.Children)
                {
                    if (child is Button copyButton && copyButton.Style == (Style)FindResource("CopyButtonStyle"))
                    {
                        copyButton.Visibility = Visibility.Hidden;
                        break;
                    }
                }
            }
        }

        private void CopyAgentTextButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is TextBlock textBlock)
            {
                try
                {
                    Clipboard.SetText(textBlock.Text);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error copying text: {ex.Message}", "Clipboard Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        // --- Placeholder actions for other buttons ---
        // (No changes in this section)
        private void UploadDocument_Click(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            AddMessageBubble("[System] Upload document action triggered.", false, "System");
            MessageBox.Show("Placeholder: Open file dialog to upload document.", "Upload Document", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void AttachButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            AddMessageBubble("[System] Attach file action triggered.", false, "System");
            MessageBox.Show("Placeholder: Open file dialog to attach file to message.", "Attach File", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // --- Task Popup Event Handlers ---
        // (No changes in this section)
        private void AddTaskButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded || TaskPopup == null || TaskInputTextBox == null) return;
            TaskInputTextBox.Clear();
            TaskPopup.IsOpen = true;
            TaskInputTextBox.Focus();
        }

        private void ConfirmTaskButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded || TaskPopup == null || TaskInputTextBox == null || TaskComboBox == null) return;
            if (!string.IsNullOrWhiteSpace(TaskInputTextBox.Text))
            {
                string task = TaskInputTextBox.Text.Trim();
                TaskComboBox.Items.Insert(0, new ComboBoxItem { Content = $"[New Task] {task}" });
                TaskComboBox.SelectedIndex = 0;
                AddMessageBubble($"[System] Reminder added: {task}", false, "System");
            }
            TaskPopup.IsOpen = false;
        }

        private void CancelTaskButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded || TaskPopup == null) return;
            TaskPopup.IsOpen = false;
        }
    }
}
