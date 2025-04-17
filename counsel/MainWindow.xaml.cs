using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Counsel.Models;
using Microsoft.Win32; // Added for OpenFileDialog
using System.IO; // Added for file handling

namespace Counsel
{
    public partial class MainWindow : Window
    {
        private Counsel.Models.AppMode _currentMode = Counsel.Models.AppMode.None; // Default to Chat Only
        private bool _isLoaded = false;             // Flag to indicate if window is fully loaded
        private readonly double _canvasTargetWidth = 400.0; // Target width for the canvas sidebar content
        private readonly GridLength _canvasTargetGridWidth; // Target GridLength for the column
        private Storyboard _sidebarStoryboard;      // Storyboard for sidebar animations
        private readonly HttpClient _httpClient;
        private readonly string _apiBaseUrl = "https://localhost:7274"; // Updated to match backend port

        public MainWindow()
        {
            InitializeComponent();
            _canvasTargetGridWidth = new GridLength(_canvasTargetWidth);
            this.Loaded += MainWindow_Loaded;

            // Initialize HttpClient with SSL bypass for localhost (development only)
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true // Bypass SSL validation
            };
            _httpClient = new HttpClient(handler)
            {
                BaseAddress = new Uri(_apiBaseUrl)
            };
            _httpClient.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            _isLoaded = true;

            _sidebarStoryboard = new Storyboard();

            ChatOnlyModeButton.IsChecked = true;
            _currentMode = Counsel.Models.AppMode.None;

            CanvasColumn.Width = new GridLength(0);

            RightSidebar.Width = 0;
            RightSidebar.Opacity = 0;
            RightSidebar.Visibility = Visibility.Collapsed;

            UpdateContextDisplay();
            UpdatePlaceholderVisibility();
            UpdateInputPlaceholderForMode(_currentMode);

            if (CaseSelectorComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                ContextCaseText.Text = $"Case: {selectedItem.Content}";
            }
            else
            {
                ContextCaseText.Text = "Case: [Select a Case]";
            }

            AddMessageBubble($"[System] Started in {GetModeFriendlyName(_currentMode)} mode. Canvas is hidden.", false, "System");
        }

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

        private async void SendMessage(string message)
        {
            if (!_isLoaded || ConversationPanel == null || ConversationScrollViewer == null) return;

            ClearWelcomeMessageIfNeeded();
            AddMessageBubble(message, isFromUser: true);

            string agentName = GetAgentNameForTask();
            TextBlock thinkingIndicator = new TextBlock
            {
                Style = (Style)FindResource("ThinkingIndicatorText"),
                Text = $"Counsel is thinking... [{agentName} Active]"
            };
            ConversationPanel.Children.Add(thinkingIndicator);
            ConversationScrollViewer.ScrollToBottom();

            try
            {
                var request = new Counsel.Models.QueryRequest
                {
                    Query = message,
                    Mode = _currentMode,
                    ChatHistory = null
                };

                var response = await SendQueryAsync(request);

                if (ConversationPanel.Children.Contains(thinkingIndicator))
                {
                    ConversationPanel.Children.Remove(thinkingIndicator);
                }

                AddMessageBubble(response.Response, isFromUser: false, agentName: agentName);

                if (_currentMode != Counsel.Models.AppMode.None)
                {
                    UpdateCanvas(response.CanvasContent, GetCanvasTitle());
                }
            }
            catch (Exception ex)
            {
                if (ConversationPanel.Children.Contains(thinkingIndicator))
                {
                    ConversationPanel.Children.Remove(thinkingIndicator);
                }

                AddMessageBubble($"[System] Error: Unable to process your request. {ex.Message}", false, "System");
            }

            RealInputTextBox.Clear();
            SendButton.IsEnabled = false;
            UpdatePlaceholderVisibility();
            RealInputTextBox.Focus();
        }

        private async Task<Counsel.Models.QueryResponse> SendQueryAsync(Counsel.Models.QueryRequest request)
        {
            try
            {
                Console.WriteLine($"Sending request: {JsonSerializer.Serialize(request)}");
                var response = await _httpClient.PostAsJsonAsync("/api/counsel/query", request);
                response.EnsureSuccessStatusCode();
                var queryResponse = await response.Content.ReadFromJsonAsync<Counsel.Models.QueryResponse>();

                if (queryResponse == null)
                {
                    throw new Exception("Received an empty response from the server.");
                }

                return queryResponse;
            }
            catch (HttpRequestException ex)
            {
                throw new Exception($"Network error: {ex.Message}");
            }
            catch (JsonException ex)
            {
                throw new Exception($"Response parsing error: {ex.Message}");
            }
        }

        private void ClearWelcomeMessageIfNeeded()
        {
            if (ConversationPanel.Children.Count > 0 && ConversationPanel.Children[0] is Border initialBorder && initialBorder.Name == "InitialWelcomeMessage")
            {
                ConversationPanel.Children.RemoveAt(0);
            }
        }

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
                case Counsel.Models.AppMode.DeepResearch: return "Deep Research Agent";
                case Counsel.Models.AppMode.Paralegal: return "Paralegal Agent";
                case Counsel.Models.AppMode.CrossExamine: return "Cross Examine Agent";
                case Counsel.Models.AppMode.None: default: return "General Agent";
            }
        }

        private string GetCanvasTitle()
        {
            switch (_currentMode)
            {
                case Counsel.Models.AppMode.DeepResearch: return "Legal Research Brief";
                case Counsel.Models.AppMode.Paralegal: return "Evidence Summary";
                case Counsel.Models.AppMode.CrossExamine: return "Insight Sheet";
                case Counsel.Models.AppMode.None: default: return "Canvas";
            }
        }

        private void ModeButton_Checked(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded || sender == null) return;

            Counsel.Models.AppMode newMode = Counsel.Models.AppMode.None;
            if (sender == ChatOnlyModeButton) newMode = Counsel.Models.AppMode.None;
            else if (sender == DeepResearchModeButton) newMode = Counsel.Models.AppMode.DeepResearch;
            else if (sender == ParalegalModeButton) newMode = Counsel.Models.AppMode.Paralegal;
            else if (sender == CrossExamineModeButton) newMode = Counsel.Models.AppMode.CrossExamine;

            if (newMode != _currentMode)
            {
                UpdateMode(newMode);
            }
        }

        private void UpdateMode(Counsel.Models.AppMode newMode)
        {
            if (!_isLoaded || RightSidebar == null || CanvasColumn == null) return;

            _currentMode = newMode;
            string modeName = GetModeFriendlyName(_currentMode);
            string systemMessage = $"[System] Switched to {modeName} mode.";

            UpdateInputPlaceholderForMode(_currentMode);
            UpdateContextDisplay();

            if (_currentMode == Counsel.Models.AppMode.None)
            {
                AnimateSidebar(false);
                systemMessage += " Canvas hidden.";
                UpdateCanvas("", "");
            }
            else
            {
                UpdateCanvas($"[Enter query to view {GetCanvasTitle()}.]", GetCanvasTitle());
                AnimateSidebar(true);
                systemMessage += " Canvas shown.";
            }

            AddMessageBubble(systemMessage, false, "System");

            ChatOnlyModeButton.IsChecked = (_currentMode == Counsel.Models.AppMode.None);
            DeepResearchModeButton.IsChecked = (_currentMode == Counsel.Models.AppMode.DeepResearch);
            ParalegalModeButton.IsChecked = (_currentMode == Counsel.Models.AppMode.Paralegal);
            CrossExamineModeButton.IsChecked = (_currentMode == Counsel.Models.AppMode.CrossExamine);
        }

        private void AnimateSidebar(bool show)
        {
            if (_sidebarStoryboard == null || RightSidebar == null || CanvasColumn == null) return;

            _sidebarStoryboard.Stop(RightSidebar);
            _sidebarStoryboard.Children.Clear();
            _sidebarStoryboard.Completed -= SidebarStoryboard_Completed_Hide;

            Duration duration = new Duration(TimeSpan.FromSeconds(0.3));
            var easing = new CubicEase { EasingMode = EasingMode.EaseOut };

            DoubleAnimation widthAnimation = new DoubleAnimation
            {
                Duration = duration,
                EasingFunction = easing
            };
            Storyboard.SetTarget(widthAnimation, RightSidebar);
            Storyboard.SetTargetProperty(widthAnimation, new PropertyPath(FrameworkElement.WidthProperty));

            DoubleAnimation opacityAnimation = new DoubleAnimation
            {
                Duration = duration,
                EasingFunction = easing
            };
            Storyboard.SetTarget(opacityAnimation, RightSidebar);
            Storyboard.SetTargetProperty(opacityAnimation, new PropertyPath(UIElement.OpacityProperty));

            if (show)
            {
                CanvasColumn.Width = _canvasTargetGridWidth;
                RightSidebar.Visibility = Visibility.Visible;

                widthAnimation.From = 0;
                widthAnimation.To = _canvasTargetWidth;

                opacityAnimation.From = 0.0;
                opacityAnimation.To = 1.0;
            }
            else
            {
                widthAnimation.From = RightSidebar.ActualWidth;
                widthAnimation.To = 0;

                opacityAnimation.From = RightSidebar.Opacity;
                opacityAnimation.To = 0.0;

                _sidebarStoryboard.Completed += SidebarStoryboard_Completed_Hide;
            }

            _sidebarStoryboard.Children.Add(widthAnimation);
            _sidebarStoryboard.Children.Add(opacityAnimation);
            _sidebarStoryboard.Begin(RightSidebar, true);
        }

        private void SidebarStoryboard_Completed_Hide(object sender, EventArgs e)
        {
            if (RightSidebar == null || CanvasColumn == null) return;

            if (_currentMode == Counsel.Models.AppMode.None)
            {
                CanvasColumn.Width = new GridLength(0);
                RightSidebar.Visibility = Visibility.Collapsed;
            }

            if (_sidebarStoryboard != null)
            {
                _sidebarStoryboard.Completed -= SidebarStoryboard_Completed_Hide;
            }
        }

        private void UpdateInputPlaceholderForMode(Counsel.Models.AppMode mode)
        {
            if (InputPlaceholder == null) return;
            switch (mode)
            {
                case Counsel.Models.AppMode.DeepResearch: InputPlaceholder.Text = "Ask a legal research question..."; break;
                case Counsel.Models.AppMode.Paralegal: InputPlaceholder.Text = "Request document summaries or evidence extracts..."; break;
                case Counsel.Models.AppMode.CrossExamine: InputPlaceholder.Text = "Submit testimony notes for insights..."; break;
                case Counsel.Models.AppMode.None: default: InputPlaceholder.Text = "Ask Counsel..."; break;
            }
            UpdatePlaceholderVisibility();
        }

        private string GetModeFriendlyName(Counsel.Models.AppMode mode)
        {
            switch (mode)
            {
                case Counsel.Models.AppMode.DeepResearch: return "Deep Research";
                case Counsel.Models.AppMode.Paralegal: return "Paralegal";
                case Counsel.Models.AppMode.CrossExamine: return "Cross Examine";
                case Counsel.Models.AppMode.None: default: return "Chat Only";
            }
        }

        private void UpdateCanvas(string content, string title)
        {
            if (!_isLoaded || RightSidebarTitle == null || RightSidebarContent == null) return;
            RightSidebarTitle.Text = title;
            RightSidebarContent.Text = content;
        }

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
                if (_currentMode != Counsel.Models.AppMode.None)
                {
                    UpdateCanvas($"[Case switched to {selectedCase}. Enter query for {GetCanvasTitle()}.]", GetCanvasTitle());
                }
            }
        }

        private void UpdateContextDisplay()
        {
            if (!_isLoaded || ContextCaseText == null || ContextModeText == null) return;

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
        }

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
        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(RightSidebarContent.Text))
                {
                    Clipboard.SetText(RightSidebarContent.Text);
                    AddMessageBubble("[System] Canvas content copied to clipboard.", false, "System");
                }
                else
                {
                    MessageBox.Show("No content to copy.", "Clipboard", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error copying text: {ex.Message}", "Clipboard Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }




        private async void UploadDocument_Click(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;

            // Open file dialog to select a PDF
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "PDF Files (*.pdf)|*.pdf",
                Title = "Select a PDF Document to Upload"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                string filePath = openFileDialog.FileName;
                string fileName = Path.GetFileName(filePath);

                AddMessageBubble($"[System] Uploading document: {fileName}", false, "System");

                try
                {
                    // Read file content
                    byte[] fileBytes = File.ReadAllBytes(filePath);
                    using var content = new MultipartFormDataContent();
                    var fileContent = new ByteArrayContent(fileBytes);
                    fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
                    content.Add(fileContent, "file", fileName);

                    // Send to backend
                    var response = await _httpClient.PostAsync("/api/documents/upload", content);
                    response.EnsureSuccessStatusCode();
                    var responseMessage = await response.Content.ReadAsStringAsync();

                    AddMessageBubble($"[System] Document uploaded successfully: {responseMessage}", false, "System");
                }
                catch (Exception ex)
                {
                    AddMessageBubble($"[System] Error uploading document: {ex.Message}", false, "System");
                }
            }
        }

        private void AttachButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            AddMessageBubble("[System] Attach file action triggered.", false, "System");
            MessageBox.Show("Placeholder: Open file dialog to attach file to message.", "Attach File", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void AddTaskButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded || TaskPopup == null || EventTitleTextBox == null) return;
            EventTitleTextBox.Clear();
            EventDateTextBox.Clear();
            EventTimeTextBox.Clear();
            EventDescriptionTextBox.Clear();
            TaskPopup.IsOpen = true;
            EventTitleTextBox.Focus();
        }

        private async void ConfirmTaskButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded || TaskPopup == null || EventTitleTextBox == null || TaskComboBox == null) return;

            string title = EventTitleTextBox.Text.Trim();
            string date = EventDateTextBox.Text.Trim();
            string time = EventTimeTextBox.Text.Trim();
            string description = EventDescriptionTextBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(description))
            {
                MessageBox.Show("Please provide at least a title or description.", "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Construct natural language query
            string query = $"{title} on {date} at {time}. {description}".Trim();
            if (string.IsNullOrWhiteSpace(query))
            {
                query = description; // Fallback to description if other fields are empty
            }

            try
            {
                // Log the query for debugging
                Console.WriteLine($"Sending calendar query: {query}");

                // Send query to backend
                var request = new { Query = query };
                var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync("/api/calendar/generate", content);

                // Log the status code
                Console.WriteLine($"HTTP Status: {response.StatusCode}");

                // Read response content
                var responseContent = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"Calendar API response: {responseContent}");

                // Check status code
                if (!response.IsSuccessStatusCode)
                {
                    MessageBox.Show($"Server error: {response.StatusCode} - {responseContent}", "Server Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Deserialize response
                var calendarResponse = JsonSerializer.Deserialize<CalendarEventResponse>(responseContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true // Handle case differences
                });

                if (calendarResponse == null || string.IsNullOrEmpty(calendarResponse.IcsContent) || string.IsNullOrEmpty(calendarResponse.FileName))
                {
                    MessageBox.Show($"Invalid response from server: {responseContent}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Save ICS file
                var saveFileDialog = new Microsoft.Win32.SaveFileDialog
                {
                    FileName = calendarResponse.FileName,
                    Filter = "ICS Files (*.ics)|*.ics"
                };
                if (saveFileDialog.ShowDialog() == true)
                {
                    await File.WriteAllTextAsync(saveFileDialog.FileName, calendarResponse.IcsContent);
                    MessageBox.Show("Calendar event saved successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);

                    // Update UI only if file is saved
                    string eventTitle = calendarResponse.EventDetails?.Title ?? title;
                    TaskComboBox.Items.Insert(0, new ComboBoxItem { Content = $"[Scheduled] {eventTitle}" });
                    TaskComboBox.SelectedIndex = 0;
                    AddMessageBubble($"[System] Calendar event added: {eventTitle}", false, "System");
                }
            }
            catch (HttpRequestException ex)
            {
                // Log the full exception
                Console.WriteLine($"HTTP error: {ex.Message}, Status: {ex.StatusCode}");
                string errorMessage = ex.StatusCode == System.Net.HttpStatusCode.BadRequest
                    ? "Bad request error. Please check your input data."
                    : ex.Message;
                MessageBox.Show($"Failed to create event: {errorMessage}", "HTTP Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (JsonException ex)
            {
                // Log the JSON error
                Console.WriteLine($"JSON error: {ex.Message}");
                MessageBox.Show($"Error parsing server response: {ex.Message}", "JSON Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                // Log the general error
                Console.WriteLine($"General error: {ex.Message}");
                MessageBox.Show($"Error creating calendar event: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            TaskPopup.IsOpen = false;
        }

        private void CancelTaskButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded || TaskPopup == null) return;
            TaskPopup.IsOpen = false;
        }

        private class CalendarEventResponse
        {
            public string IcsContent { get; set; }
            public string FileName { get; set; }
            public EventDetailsResponse EventDetails { get; set; }
        }

        private class EventDetailsResponse
        {
            public string Title { get; set; }
            public DateTime StartDateTime { get; set; }
            public DateTime EndDateTime { get; set; }
            public string Description { get; set; }
        }
    }
}
