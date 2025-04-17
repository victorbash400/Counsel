using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using Counsel.BackendApi.Models;
using Counsel.BackendApi.Plugins;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Embeddings;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic; // Added for List<T>

namespace Counsel.BackendApi.Services
{
    /// <summary>
    /// Service responsible for orchestrating calls to Semantic Kernel plugins
    /// based on the application mode, including dynamic task selection for Paralegal mode
    /// and conversational summaries for specialized modes.
    /// </summary>
    public class SKOrchestratorService
    {
        private readonly Kernel _kernel;
        private readonly SearchClient _searchClient;
#pragma warning disable SKEXP0001
        private readonly ITextEmbeddingGenerationService _embeddingService;
#pragma warning restore SKEXP0001
        private readonly ILogger<SKOrchestratorService> _logger;
        private readonly ResearchPlugin _researchPlugin;
        private readonly ExaminePlugin _examinePlugin;
        private readonly ParalegalPlugin _paralegalPlugin;
        private readonly ChatHistory _sessionHistory; // Added for session chat history
        private const int MaxHistoryMessages = 10; // Limit history size to prevent token bloat

        // Constants for conversational summary generation
        private const string ConversationalSummaryPrompt = @"You are Counsel, a helpful legal AI assistant communicating in a chat window.
You just completed a task for the user and generated the following detailed content (which will be shown separately in a canvas):
--- DETAILED CONTENT START ---
{{$pluginResult}}
--- DETAILED CONTENT END ---

The task performed was: {{$taskDescription}}
User's original query was: {{$userQuery}}

Now, write a brief, friendly, and conversational message for the chat window (max 2-3 sentences).
Briefly summarize what you did or what the detailed content covers. You could mention a key finding or ask a relevant clarifying question if appropriate.
Do NOT repeat large portions of the detailed content. Focus on a natural chat interaction.
Example: ""I've put together the research brief you asked for on contract law precedents. It covers the key cases and statutes. You can find the details in the canvas.""
Example: ""I've analyzed the deposition notes and extracted the key points regarding timelines. Check the canvas for the full insight sheet.""
Example: ""Okay, I've summarized the evidence document about the financial transactions. The full summary is available in the canvas.""
";

        /// <summary>
        /// Initializes a new instance of the <see cref="SKOrchestratorService"/> class.
        /// </summary>
        public SKOrchestratorService(
            Kernel kernel,
            SearchClient searchClient,
#pragma warning disable SKEXP0001
            ITextEmbeddingGenerationService embeddingService,
#pragma warning restore SKEXP0001
            ILogger<SKOrchestratorService> logger,
            ResearchPlugin researchPlugin,
            ExaminePlugin examinePlugin,
            ParalegalPlugin paralegalPlugin)
        {
            _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));
            _searchClient = searchClient ?? throw new ArgumentNullException(nameof(searchClient));
            _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _researchPlugin = researchPlugin ?? throw new ArgumentNullException(nameof(researchPlugin));
            _examinePlugin = examinePlugin ?? throw new ArgumentNullException(nameof(examinePlugin));
            _paralegalPlugin = paralegalPlugin ?? throw new ArgumentNullException(nameof(paralegalPlugin));
            _sessionHistory = new ChatHistory(); // Initialize session history
        }

        /// <summary>
        /// Processes a user query based on the specified application mode.
        /// </summary>
        public async Task<QueryResponse> ProcessQueryAsync(QueryRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Query))
            {
                _logger.LogWarning("Invalid query request received. Query cannot be null or empty.");
                return new QueryResponse
                {
                    Response = "Your query appears to be empty. Please provide a question or topic to explore.",
                    CanvasContent = string.Empty
                };
            }

            _logger.LogInformation("Processing query: '{Query}', Mode: {Mode}", request.Query, request.Mode);

            try
            {
                // Add user query to session history
                _sessionHistory.AddUserMessage(request.Query);

                QueryResponse response;
                switch (request.Mode)
                {
                    case AppMode.DeepResearch:
                        response = await ExecuteWithFallbackAsync(
                            async () => await ExecuteResearchModeAsync(request),
                            "An error occurred during research.",
                            request);
                        break;

                    case AppMode.Paralegal:
                        response = await ExecuteParalegalModeAsync(request);
                        break;

                    case AppMode.CrossExamine:
                        response = await ExecuteWithFallbackAsync(
                            async () => await ExecuteExamineModeAsync(request),
                            "An error occurred during examination.",
                            request);
                        break;

                    case AppMode.None:
                    default:
                        response = await ExecuteEnhancedChatAsync(request);
                        break;
                }

                // Add assistant response to session history
                _sessionHistory.AddAssistantMessage(response.Response);

                // Trim history to keep only the last MaxHistoryMessages messages
                while (_sessionHistory.Count > MaxHistoryMessages)
                {
                    _sessionHistory.RemoveAt(0);
                }

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error processing query: {Query} in Mode {Mode}. Exception: {ExceptionType} - {ExceptionMessage}",
                    request.Query, request.Mode, ex.GetType().Name, ex.Message);
                var fallbackResponse = await ExecuteEnhancedChatAsync(request, true, $"An unexpected error occurred while processing your request in {request.Mode} mode. ");
                _sessionHistory.AddAssistantMessage(fallbackResponse.Response);
                return fallbackResponse;
            }
        }

        /// <summary>
        /// Executes the Deep Research mode.
        /// </summary>
        private async Task<QueryResponse> ExecuteResearchModeAsync(QueryRequest request)
        {
            _logger.LogInformation("Calling ResearchPlugin.PerformLegalResearchAsync directly for query: {Query}", request.Query);
            string canvasContent = await _researchPlugin.PerformLegalResearchAsync(request.Query);

            if (string.IsNullOrWhiteSpace(canvasContent) || canvasContent.Contains("error", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("ResearchPlugin returned empty or error content for query: {Query}", request.Query);
                return await ExecuteEnhancedChatAsync(request, true, "I tried to perform the research, but encountered an issue retrieving the details. ");
            }

            string conversationalResponse = await GenerateConversationalSummaryAsync(canvasContent, "Legal Research Brief Generation", request.Query);

            return new QueryResponse
            {
                Response = conversationalResponse,
                CanvasContent = canvasContent
            };
        }

        /// <summary>
        /// Executes the Cross Examine mode.
        /// </summary>
        private async Task<QueryResponse> ExecuteExamineModeAsync(QueryRequest request)
        {
            _logger.LogInformation("Calling ExaminePlugin.FindRelevantPassagesAsync directly for query: {Query}", request.Query);
            string canvasContent = await _examinePlugin.FindRelevantPassagesAsync(request.Query);

            if (string.IsNullOrWhiteSpace(canvasContent) || canvasContent.Contains("error", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("ExaminePlugin returned empty or error content for query: {Query}", request.Query);
                return await ExecuteEnhancedChatAsync(request, true, "I attempted to analyze the text for relevant passages, but couldn't retrieve the specific insights. ");
            }

            string conversationalResponse = await GenerateConversationalSummaryAsync(canvasContent, "Finding Relevant Passages / Insights", request.Query);

            return new QueryResponse
            {
                Response = conversationalResponse,
                CanvasContent = canvasContent
            };
        }

        /// <summary>
        /// Executes the paralegal mode with RAG, intent detection, plugin execution, and conversational summary.
        /// </summary>
        private async Task<QueryResponse> ExecuteParalegalModeAsync(QueryRequest request)
        {
            _logger.LogInformation("Processing Paralegal request for query: {Query}", request.Query);

            // --- Step 1: Perform RAG to get document chunks ---
            string[] chunks;
            try
            {
#pragma warning disable SKEXP0001
                var embeddings = await _embeddingService.GenerateEmbeddingsAsync(new[] { request.Query });
#pragma warning restore SKEXP0001
                if (!embeddings.Any())
                {
                    _logger.LogWarning("No embeddings generated for query: {Query}. Falling back to enhanced chat.", request.Query);
                    return await ExecuteEnhancedChatAsync(request, true, "I couldn't generate embeddings to search for relevant documents. ");
                }

                var vector = embeddings.First().ToArray();
                var searchOptions = new SearchOptions
                {
                    VectorSearch = new VectorSearchOptions { Queries = { new VectorizedQuery(vector) { KNearestNeighborsCount = 5, Fields = { "Embedding" } } } },
                    Size = 5,
                    Select = { "Content" }
                };
                var searchResults = await _searchClient.SearchAsync<SearchDocument>(null, searchOptions);
                chunks = searchResults.Value.GetResults()
                             .Select(r => r.Document["Content"]?.ToString() ?? string.Empty)
                             .Where(s => !string.IsNullOrEmpty(s))
                             .ToArray();
                _logger.LogInformation("RAG found {ChunkCount} relevant document chunks.", chunks.Length);
            }
            catch (Exception ragEx)
            {
                _logger.LogError(ragEx, "Error during RAG phase for Paralegal mode. Falling back to enhanced chat.");
                return await ExecuteEnhancedChatAsync(request, true, "I encountered an issue while searching for relevant documents. ");
            }

            // --- Step 2: Handle case where no documents are found ---
            if (chunks.Length == 0)
            {
                _logger.LogWarning("No relevant document chunks found for query: {Query}. Falling back to enhanced chat.", request.Query);
                return await ExecuteEnhancedChatAsync(request, false, "I couldn't find any documents specifically matching your request. However, I can still try to help based on general knowledge. ");
            }

            // --- Step 3: Determine Paralegal Task Intent using LLM ---
            string desiredTask = "notes";
            string taskDescriptionForSummary = "Generating notes from documents";
            try
            {
                var intentPrompt = @"Based on the user's query and the recent conversation history, determine the primary paralegal task implied.
The query is: '{{query}}'
Recent conversation history:
{{history}}
Possible tasks are:
- notes: Generating structured notes, timelines, key points, or questions about the documents.
- summarize: Creating a concise summary of the documents.
- extract: Extracting specific entities like people, dates, organizations, terms, or amounts from the documents.

Respond ONLY with one word: 'notes', 'summarize', or 'extract'.";

                // Serialize recent history for the prompt
                string historyText = string.Join("\n", _sessionHistory.Select(m => $"{m.Role}: {m.Content}").TakeLast(5));
                _logger.LogInformation("Invoking LLM to determine paralegal task for query: {Query}", request.Query);
                var intentResult = await _kernel.InvokePromptAsync(intentPrompt, new KernelArguments
                {
                    ["query"] = request.Query,
                    ["history"] = historyText
                });
                var intentResponse = intentResult?.GetValue<string>()?.Trim().ToLowerInvariant();
                if (intentResponse == "summarize" || intentResponse == "extract")
                {
                    desiredTask = intentResponse;
                }
                _logger.LogInformation("LLM determined paralegal task as: {DesiredTask}", desiredTask);

                taskDescriptionForSummary = desiredTask switch
                {
                    "summarize" => "Summarizing relevant document sections",
                    "extract" => "Extracting key information from documents",
                    _ => "Generating notes and insights from documents"
                };
            }
            catch (Exception llmEx)
            {
                _logger.LogWarning(llmEx, "LLM call to determine paralegal task failed. Defaulting to 'notes'.");
            }

            // --- Step 4: Execute the determined Paralegal Task ---
            _logger.LogInformation("Executing paralegal task '{DesiredTask}' directly.", desiredTask);
            string canvasContent;
            try
            {
                switch (desiredTask)
                {
                    case "summarize":
                        canvasContent = await _paralegalPlugin.SummarizeContextAsync(chunks, request.Query);
                        break;
                    case "extract":
                        canvasContent = await _paralegalPlugin.ExtractKeyInfoAsync(chunks, request.Query);
                        break;
                    case "notes":
                    default:
                        canvasContent = await _paralegalPlugin.GenerateDocNotesAsync(chunks, request.Query);
                        break;
                }

                if (string.IsNullOrWhiteSpace(canvasContent) || canvasContent == "{}" || canvasContent.Contains("No content to analyze", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("Paralegal plugin task '{DesiredTask}' returned empty or placeholder content. Falling back.", desiredTask);
                    return await ExecuteEnhancedChatAsync(request, true, $"I found some relevant documents, but couldn't extract the specific information you requested in the desired format. ");
                }
            }
            catch (Exception pluginEx)
            {
                _logger.LogError(pluginEx, "Error executing paralegal plugin task '{DesiredTask}'. Falling back.", desiredTask);
                return await ExecuteEnhancedChatAsync(request, true, $"I encountered an error while trying to perform the '{desiredTask}' task on the documents. ");
            }

            // --- Step 5: Generate Conversational Summary ---
            string conversationalResponse = await GenerateConversationalSummaryAsync(canvasContent, taskDescriptionForSummary, request.Query);

            return new QueryResponse { Response = conversationalResponse, CanvasContent = canvasContent };
        }

        /// <summary>
        /// Executes an enhanced chat response (used for ChatOnly mode or fallbacks).
        /// </summary>
        private async Task<QueryResponse> ExecuteEnhancedChatAsync(QueryRequest request, bool isFallback = false, string fallbackPrefix = null)
        {
            _logger.LogInformation("Executing enhanced chat for query: {Query}, isFallback: {IsFallback}", request.Query, isFallback);
            try
            {
                // Define a simple prompt for short queries like "Hey"
                if (request.Query.Length < 5)
                {
                    return new QueryResponse
                    {
                        Response = "Hello! I'm your legal assistant. How can I help you today?",
                        CanvasContent = string.Empty
                    };
                }

                var prompt = @"You are a knowledgeable legal assistant specializing in legal analysis, research, and document review.

Recent conversation history:
{{history}}

USER QUERY: {{$query}}

Provide a helpful, concise, and accurate response to the user's query. Focus on delivering practical legal information while:
- Citing general legal principles or doctrines when relevant
- Being precise with terminology
- Maintaining a professional tone
- Acknowledging limitations of general advice vs. specific legal counsel when appropriate

If this is a hypothetical scenario, analyze it thoughtfully. If it requires specific legal expertise, acknowledge that while providing general information.";

                if (isFallback)
                {
                    _logger.LogInformation("Using fallback mode for enhanced chat");
                    prompt = @"You are a knowledgeable legal assistant specializing in legal analysis, research, and document review.

Recent conversation history:
{{history}}

USER QUERY: {{$query}}

Note: This is a fallback response because more specialized processing was not possible.

" + (fallbackPrefix != null ? fallbackPrefix + "\n\n" : "") + @"
Provide a helpful, concise, and accurate response to the user's query. Focus on delivering practical legal information while:
- Citing general legal principles or doctrines when relevant
- Being precise with terminology
- Maintaining a professional tone
- Acknowledging limitations of general advice vs. specific legal counsel when appropriate";
                }

                try
                {
                    // Use ChatHistory directly with IChatCompletionService
                    var chatClient = _kernel.GetRequiredService<IChatCompletionService>();
                    var chatHistory = new ChatHistory();
                    chatHistory.AddSystemMessage(@"You are a knowledgeable legal assistant specializing in legal analysis, research, and document review.");

                    // Add session history to chat
                    foreach (var message in _sessionHistory)
                    {
                        if (message.Role == AuthorRole.User)
                        {
                            chatHistory.AddUserMessage(message.Content);
                        }
                        else if (message.Role == AuthorRole.Assistant)
                        {
                            chatHistory.AddAssistantMessage(message.Content);
                        }
                    }

                    // Add the current query
                    chatHistory.AddUserMessage(request.Query);

                    var chatResult = await chatClient.GetChatMessageContentAsync(chatHistory);
                    var response = chatResult?.Content?.Trim();

                    if (string.IsNullOrEmpty(response))
                    {
                        throw new Exception("Empty response from chat completion service");
                    }

                    if (isFallback && !string.IsNullOrWhiteSpace(fallbackPrefix) && !response.Contains(fallbackPrefix.Trim()))
                    {
                        response = fallbackPrefix.Trim() + " " + response;
                    }

                    return new QueryResponse
                    {
                        Response = response,
                        CanvasContent = string.Empty
                    };
                }
                catch (Exception promptEx)
                {
                    _logger.LogWarning(promptEx, "Error using chat completion service. Providing basic fallback.");

                    // For very short queries like "Hey", provide a simple greeting
                    if (request.Query.Length < 5)
                    {
                        return new QueryResponse
                        {
                            Response = "Hello! I'm your legal assistant. How can I help you today?",
                            CanvasContent = string.Empty
                        };
                    }

                    return new QueryResponse
                    {
                        Response = $"I understand you're asking about: '{Shorten(request.Query)}'. However, I encountered an internal error trying to generate a response. Please try rephrasing or ask again later.",
                        CanvasContent = string.Empty
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in enhanced chat mode execution. Providing basic fallback.");

                // For very short queries like "Hey", provide a simple greeting
                if (request.Query.Length < 5)
                {
                    return new QueryResponse
                    {
                        Response = "Hello! I'm your legal assistant. How can I help you today?",
                        CanvasContent = string.Empty
                    };
                }

                return new QueryResponse
                {
                    Response = $"I understand you're asking about: '{Shorten(request.Query)}'. However, I encountered an internal error trying to generate a response. Please try rephrasing or ask again later.",
                    CanvasContent = string.Empty
                };
            }
        }

        /// <summary>
        /// Generates a brief, conversational summary of a plugin's detailed output for the chat window.
        /// </summary>
        private async Task<string> GenerateConversationalSummaryAsync(string pluginResult, string taskDescription, string userQuery)
        {
            if (string.IsNullOrWhiteSpace(pluginResult))
            {
                _logger.LogWarning("Plugin result was empty, cannot generate conversational summary for task: {TaskDescription}", taskDescription);
                return $"I've completed the {taskDescription.ToLowerInvariant()} based on your request about '{Shorten(userQuery)}'. The detailed results are in the canvas.";
            }

            try
            {
                _logger.LogInformation("Generating conversational summary for task: {TaskDescription}", taskDescription);
                var summaryArguments = new KernelArguments
                {
                    ["pluginResult"] = Shorten(pluginResult, 2000, false),
                    ["taskDescription"] = taskDescription,
                    ["userQuery"] = userQuery
                };

                var summaryResult = await _kernel.InvokePromptAsync(ConversationalSummaryPrompt, summaryArguments);
                string summary = summaryResult?.GetValue<string>()?.Trim();

                if (string.IsNullOrWhiteSpace(summary))
                {
                    _logger.LogWarning("Conversational summary generation returned empty result for task: {TaskDescription}. Using default message.", taskDescription);
                    summary = $"I have processed your request regarding '{Shorten(userQuery)}' and completed the {taskDescription.ToLowerInvariant()}. Please see the canvas for details.";
                }

                return summary;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating conversational summary for task: {TaskDescription}", taskDescription);
                return $"I've finished the {taskDescription.ToLowerInvariant()} related to your query '{Shorten(userQuery)}'. You can view the details in the canvas area.";
            }
        }

        /// <summary>
        /// Generic method to execute a main action (plugin call + summary generation) with fallback handling.
        /// </summary>
        private async Task<QueryResponse> ExecuteWithFallbackAsync(Func<Task<QueryResponse>> mainAction, string errorMessage, QueryRequest originalRequest)
        {
            try
            {
                var response = await mainAction();

                if (string.IsNullOrWhiteSpace(response.CanvasContent) ||
                    response.CanvasContent.Contains("An error occurred", StringComparison.OrdinalIgnoreCase) ||
                    response.CanvasContent == "{}")
                {
                    _logger.LogWarning("Main action '{ErrorMessage}' resulted in invalid or empty canvas content: {ContentPreview}",
                        errorMessage, Shorten(response.CanvasContent, 100));
                    return await ExecuteEnhancedChatAsync(originalRequest, true, $"I tried to {errorMessage.ToLowerInvariant().Replace("an error occurred during ", "")}, but couldn't retrieve the specific details. ");
                }

                if (string.IsNullOrWhiteSpace(response.Response))
                {
                    _logger.LogWarning("Main action '{ErrorMessage}' resulted in an empty chat response despite having canvas content.", errorMessage);
                    response.Response = $"I've processed your request for {errorMessage.ToLowerInvariant().Replace("an error occurred during ", "")}. The details are in the canvas.";
                }

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing action '{ErrorMessage}'. Exception: {ExceptionType} - {ExceptionMessage}",
                    errorMessage, ex.GetType().Name, ex.Message);
                return await ExecuteEnhancedChatAsync(originalRequest, true, $"An unexpected error occurred while trying to {errorMessage.ToLowerInvariant().Replace("an error occurred during ", "")}. ");
            }
        }

        /// <summary>
        /// Helper function to shorten text for logging or display purposes.
        /// </summary>
        private string Shorten(string text, int maxLength = 30)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            text = text.Replace(Environment.NewLine, " ").Trim();
            return text.Length <= maxLength ? text : text.Substring(0, maxLength) + "...";
        }

        private string Shorten(string text, int maxLength, bool keepNewlines)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            if (!keepNewlines) text = text.Replace(Environment.NewLine, " ").Trim();
            else text = text.Trim();
            return text.Length <= maxLength ? text : text.Substring(0, maxLength) + "...";
        }
    }
}