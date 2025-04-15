using Counsel.BackendApi.Models;
using Counsel.BackendApi.Plugins;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using Microsoft.SemanticKernel.Embeddings;
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace Counsel.BackendApi.Services
{
    /// <summary>
    /// Service responsible for orchestrating calls to Semantic Kernel plugins
    /// based on the application mode, including dynamic task selection for Paralegal mode.
    /// </summary>
    public class SKOrchestratorService
    {
        // Dependencies injected via constructor
        private readonly Kernel _kernel;
        private readonly SearchClient _searchClient;
#pragma warning disable SKEXP0001 // Type is for evaluation purposes and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
        private readonly ITextEmbeddingGenerationService _embeddingService;
#pragma warning restore SKEXP0001 // Type is for evaluation purposes and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
        private readonly ILogger<SKOrchestratorService> _logger;
        private readonly ResearchPlugin _researchPlugin;
        private readonly ExaminePlugin _examinePlugin;
        private readonly ParalegalPlugin _paralegalPlugin;

        /// <summary>
        /// Initializes a new instance of the <see cref="SKOrchestratorService"/> class.
        /// </summary>
        public SKOrchestratorService(
            Kernel kernel,
            SearchClient searchClient,
#pragma warning disable SKEXP0001 // Type is for evaluation purposes and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
            ITextEmbeddingGenerationService embeddingService,
#pragma warning restore SKEXP0001 // Type is for evaluation purposes and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
            ILogger<SKOrchestratorService> logger,
            ResearchPlugin researchPlugin,
            ExaminePlugin examinePlugin,
            ParalegalPlugin paralegalPlugin
            )
        {
            // Store injected dependencies
            _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));
            _searchClient = searchClient ?? throw new ArgumentNullException(nameof(searchClient));
            _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _researchPlugin = researchPlugin ?? throw new ArgumentNullException(nameof(researchPlugin));
            _examinePlugin = examinePlugin ?? throw new ArgumentNullException(nameof(examinePlugin));
            _paralegalPlugin = paralegalPlugin ?? throw new ArgumentNullException(nameof(paralegalPlugin));
        }

        /// <summary>
        /// Processes a user query based on the specified application mode.
        /// </summary>
        public async Task<QueryResponse> ProcessQueryAsync(QueryRequest request)
        {
            // Validate the incoming request
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
            string chatResponse = "Processing your request...";
            string canvasContent = string.Empty;
            // Prepare KernelArguments - needed for intent detection and basic chat
            var arguments = new KernelArguments() { ["query"] = request.Query };

            try
            {
                // Route request based on the application mode
                switch (request.Mode)
                {
                    case AppMode.DeepResearch:
                        return await ExecuteWithFallbackAsync(
                            async () =>
                            {
                                _logger.LogInformation("Calling ResearchPlugin.PerformLegalResearchAsync directly");
                                var result = await _researchPlugin.PerformLegalResearchAsync(request.Query);
                                return new QueryResponse
                                {
                                    Response = $"Generated research brief based on your query about '{Shorten(request.Query)}'.",
                                    CanvasContent = result
                                };
                            },
                            "An error occurred during research.");

                    case AppMode.Paralegal:
                        return await ExecuteParalegalModeAsync(request);

                    case AppMode.CrossExamine:
                        return await ExecuteWithFallbackAsync(
                            async () =>
                            {
                                _logger.LogInformation("Calling ExaminePlugin.FindRelevantPassagesAsync directly");
                                var result = await _examinePlugin.FindRelevantPassagesAsync(request.Query);
                                return new QueryResponse
                                {
                                    Response = $"Found relevant passages based on your query about '{Shorten(request.Query)}'.",
                                    CanvasContent = result
                                };
                            },
                            "An error occurred during examination.");

                    case AppMode.None:
                    default:
                        return await ExecuteEnhancedChatAsync(request);
                }
            }
            catch (Exception ex)
            {
                // Log any top-level exceptions
                _logger.LogError(ex, "Unhandled error processing query: {Query} in Mode {Mode}. Exception: {ExceptionType} - {ExceptionMessage}",
                    request.Query, request.Mode, ex.GetType().Name, ex.Message);

                return await ExecuteWithFallbackAsync(
                    async () => await ExecuteEnhancedChatAsync(request, true),
                    "An unexpected error occurred. Falling back to basic mode."
                );
            }
        }

        /// <summary>
        /// Executes the paralegal mode with proper fallback handling
        /// </summary>
        private async Task<QueryResponse> ExecuteParalegalModeAsync(QueryRequest request)
        {
            _logger.LogInformation("Processing Paralegal request for query: {Query}", request.Query);

            // --- Step 1: Perform RAG to get document chunks ---
            string[] chunks;
            try
            {
#pragma warning disable SKEXP0001 // Suppress warning for embedding generation
                var embeddings = await _embeddingService.GenerateEmbeddingsAsync(new[] { request.Query });
#pragma warning restore SKEXP0001
                var vector = embeddings.First().ToArray();
                var searchOptions = new SearchOptions
                {
                    VectorSearch = new VectorSearchOptions { Queries = { new VectorizedQuery(vector) { KNearestNeighborsCount = 5, Fields = { "Embedding" } } } },
                    Size = 5
                };
                var searchResults = await _searchClient.SearchAsync<SearchDocument>(request.Query, searchOptions);
                chunks = searchResults.Value.GetResults()
                             .Select(r => r.Document["Content"]?.ToString() ?? string.Empty)
                             .Where(s => !string.IsNullOrEmpty(s))
                             .ToArray();
                _logger.LogInformation("RAG found {ChunkCount} relevant document chunks.", chunks.Length);
            }
            catch (Exception ragEx)
            {
                _logger.LogError(ragEx, "Error during RAG phase for Paralegal mode. Falling back to enhanced chat.");
                return await ExecuteEnhancedChatAsync(request, true);
            }

            // --- Step 2: Handle case where no documents are found ---
            if (chunks.Length == 0)
            {
                _logger.LogWarning("No relevant document chunks found for query: {Query}. Falling back to enhanced chat.", request.Query);
                return await ExecuteEnhancedChatAsync(request, true, "No relevant documents were found for your query. Here's what I can tell you:");
            }

            // --- Step 3: Determine Paralegal Task Intent using LLM ---
            string desiredTask = "notes"; // Default task
            try
            {
                var intentPrompt = @"Based on the user's query, determine the primary paralegal task implied.
The query is: '{{query}}'
Possible tasks are:
- notes: Generating structured notes, timelines, key points, or questions about the documents.
- summarize: Creating a concise summary of the documents.
- extract: Extracting specific entities like people, dates, organizations, terms, or amounts from the documents.

Respond ONLY with one word: 'notes', 'summarize', or 'extract'.";

                _logger.LogInformation("Invoking LLM to determine paralegal task for query: {Query}", request.Query);
                var intentResult = await _kernel.InvokePromptAsync(intentPrompt, new KernelArguments { ["query"] = request.Query });
                var intentResponse = intentResult?.GetValue<string>()?.Trim().ToLowerInvariant();

                if (intentResponse == "summarize" || intentResponse == "extract")
                {
                    desiredTask = intentResponse;
                }
                _logger.LogInformation("LLM determined paralegal task as: {DesiredTask}", desiredTask);
            }
            catch (Exception llmEx)
            {
                _logger.LogWarning(llmEx, "LLM call to determine paralegal task failed. Defaulting to 'notes'.");
                // Continue with the default task ('notes') if LLM fails
            }

            // --- Step 4: Execute the determined Paralegal Task ---
            _logger.LogInformation("Executing paralegal task '{DesiredTask}' directly.", desiredTask);
            try
            {
                string response;
                string content;

                switch (desiredTask)
                {
                    case "summarize":
                        content = await _paralegalPlugin.SummarizeContextAsync(chunks, request.Query);
                        response = $"Generated summary based on documents for '{Shorten(request.Query)}'.";
                        break;
                    case "extract":
                        content = await _paralegalPlugin.ExtractKeyInfoAsync(chunks, request.Query);
                        response = $"Extracted key information from documents for '{Shorten(request.Query)}'.";
                        break;
                    case "notes":
                    default:
                        content = await _paralegalPlugin.GenerateDocNotesAsync(chunks, request.Query);
                        response = $"Generated notes from documents for '{Shorten(request.Query)}'.";
                        break;
                }

                // Basic check if the plugin returned valid content
                if (string.IsNullOrWhiteSpace(content) || content == "{}" || content.Contains("No content to analyze"))
                {
                    _logger.LogWarning("Paralegal plugin task '{DesiredTask}' returned empty or placeholder content. Falling back to enhanced chat.", desiredTask);
                    return await ExecuteEnhancedChatAsync(request, true, "I found some documents, but couldn't extract meaningful content. Here's what I can tell you:");
                }

                return new QueryResponse { Response = response, CanvasContent = content };
            }
            catch (Exception pluginEx)
            {
                _logger.LogError(pluginEx, "Error executing paralegal plugin task '{DesiredTask}'. Falling back to enhanced chat.", desiredTask);
                return await ExecuteEnhancedChatAsync(request, true);
            }
        }

        /// <summary>
        /// Executes an enhanced chat response with context-awareness
        /// </summary>
        private async Task<QueryResponse> ExecuteEnhancedChatAsync(QueryRequest request, bool isFallback = false, string fallbackPrefix = null)
        {
            _logger.LogInformation("Executing enhanced chat for query: {Query}, isFallback: {IsFallback}", request.Query, isFallback);

            try
            {
                // Create a context-aware prompt that's better than the basic one
                var prompt = @"You are a knowledgeable legal assistant specializing in legal analysis, research, and document review.

USER QUERY: {{query}}

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

USER QUERY: {{query}}

Note: This is a fallback response because more specialized processing was not possible.

" + (fallbackPrefix != null ? fallbackPrefix + "\n\n" : "") + @"
Provide a helpful, concise, and accurate response to the user's query. Focus on delivering practical legal information while:
- Citing general legal principles or doctrines when relevant
- Being precise with terminology
- Maintaining a professional tone
- Acknowledging limitations of general advice vs. specific legal counsel when appropriate";
                }

                var arguments = new KernelArguments { ["query"] = request.Query };
                var result = await _kernel.InvokePromptAsync(prompt, arguments);
                var chatResponse = result?.GetValue<string>() ?? "I apologize, but I couldn't process that request effectively.";

                return new QueryResponse
                {
                    Response = chatResponse,
                    CanvasContent = string.Empty
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in enhanced chat mode. Falling back to absolute basic chat.");

                // Absolute basic fallback
                return new QueryResponse
                {
                    Response = $"I understand you're asking about: '{Shorten(request.Query)}'. However, I'm currently experiencing some technical issues. Please try again or rephrase your question.",
                    CanvasContent = string.Empty
                };
            }
        }

        /// <summary>
        /// Generic method to execute a function with fallback handling
        /// </summary>
        private async Task<QueryResponse> ExecuteWithFallbackAsync(Func<Task<QueryResponse>> mainAction, string errorMessage)
        {
            try
            {
                var response = await mainAction();

                // Validate response content
                if (string.IsNullOrWhiteSpace(response.CanvasContent) ||
                    response.CanvasContent.Contains("An error occurred") ||
                    response.CanvasContent == "{}")
                {
                    _logger.LogWarning("Action returned invalid or empty content: {Content}",
                        response.CanvasContent?.Substring(0, Math.Min(100, response.CanvasContent?.Length ?? 0)));

                    // Fall back to enhanced chat
                    return await ExecuteEnhancedChatAsync(new QueryRequest
                    {
                        Query = response.Response,
                        Mode = AppMode.None
                    }, true);
                }

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing action: {ErrorMessage}. Exception: {ExceptionType} - {ExceptionMessage}",
                    errorMessage, ex.GetType().Name, ex.Message);

                // Fall back to enhanced chat
                return await ExecuteEnhancedChatAsync(new QueryRequest
                {
                    Query = errorMessage,
                    Mode = AppMode.None
                }, true);
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
    }
}