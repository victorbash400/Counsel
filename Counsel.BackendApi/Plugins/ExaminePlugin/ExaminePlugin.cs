using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Embeddings;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using System.ComponentModel;

namespace Counsel.BackendApi.Plugins
{
    public class ExaminePlugin
    {
        private readonly ILogger<ExaminePlugin> _logger;
        private readonly SearchClient _searchClient;
        private readonly HttpClient _httpClient;
        private readonly string _braveApiKey;
        private readonly Kernel _kernel;
#pragma warning disable SKEXP0001
        private readonly ITextEmbeddingGenerationService _embeddingService;
#pragma warning restore SKEXP0001
        private readonly string _searchEndpoint;
        private readonly string _searchIndex;
        private const int MAX_DOC_RESULTS = 5; // Optimized for efficiency
        private const int MAX_WEB_RESULTS = 5; // Optimized for efficiency

        public ExaminePlugin(
            ILogger<ExaminePlugin> logger,
            SearchClient searchClient,
            IConfiguration config,
            HttpClient httpClient,
            Kernel kernel,
#pragma warning disable SKEXP0001
            ITextEmbeddingGenerationService embeddingService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _searchClient = searchClient ?? throw new ArgumentNullException(nameof(searchClient));
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));
            _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));

            if (config == null) throw new ArgumentNullException(nameof(config));

            _braveApiKey = config["BraveSearch:ApiKey"] ?? throw new ArgumentNullException("BraveSearch:ApiKey is missing");
            _searchEndpoint = config["AzureSearch:Endpoint"] ?? throw new ArgumentNullException("AzureSearch:Endpoint is missing");
            _searchIndex = config["AzureSearch:IndexName"] ?? throw new ArgumentNullException("AzureSearch:IndexName is missing");

            _logger.LogInformation("Initialized ExaminePlugin with optimized vector search and Brave Search integration");
        }

        [KernelFunction]
        [Description("Examines documents and web sources to build precise legal arguments with detailed passage analysis")]
        public async Task<string> FindRelevantPassagesAsync(
            [Description("The legal query to analyze, e.g., 'Assess breach justification in contract dispute'")] string query)
        {
            _logger.LogInformation("Starting optimized analysis for query: {Query}", query);

            try
            {
                var documentChunks = await VectorSearchDocsAsync(query);
                if (!documentChunks.Any())
                {
                    _logger.LogWarning("No relevant documents found for query: {Query}", query);
                }

                var webResults = await WebSearchAsync(query);
                var formattedWebContent = FormatWebResultsForLLM(webResults);

                var analysis = await GenerateLegalAnalysisAsync(query, documentChunks, formattedWebContent);

                _logger.LogInformation("Completed analysis for query: {Query}", query);
                return analysis;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing query: {Query}", ex.Message);
                return $"ERROR: {ex.Message}. Please refine your query or check document availability.";
            }
        }

        private async Task<List<DocumentChunk>> VectorSearchDocsAsync(string query)
        {
            try
            {
                _logger.LogInformation("Performing vector search for query: {Query}", query);

                var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(query);
                _logger.LogDebug("Query embedding generated, length: {Length}", queryEmbedding.Length);

                var searchOptions = new SearchOptions
                {
                    Size = MAX_DOC_RESULTS,
                    VectorSearch = new VectorSearchOptions
                    {
                        Queries = { new VectorizedQuery(queryEmbedding.ToArray()) { KNearestNeighborsCount = MAX_DOC_RESULTS, Fields = { "Embedding" } } }
                    }
                };

                _logger.LogDebug("Executing vector search with Size={Size}, K={K}, Fields={Fields}",
                    searchOptions.Size, MAX_DOC_RESULTS, "Embedding");

                var searchResults = await _searchClient.SearchAsync<SearchDocument>("*", searchOptions);
                var documentChunks = new List<DocumentChunk>();

                await foreach (var result in searchResults.Value.GetResultsAsync())
                {
                    var doc = new DocumentChunk
                    {
                        Content = result.Document["Content"]?.ToString() ?? string.Empty,
                        DocumentId = result.Document["DocumentId"]?.ToString() ?? string.Empty,
                        Score = result.Score ?? 0.0
                    };
                    if (!string.IsNullOrEmpty(doc.Content))
                    {
                        documentChunks.Add(doc);
                    }
                }

                _logger.LogInformation("Found {Count} document chunks with confidence score: {Score}",
                    documentChunks.Count, searchResults.Value.TotalCount > 0 ? documentChunks.Average(d => d.Score) : 0);
                return documentChunks.Take(MAX_DOC_RESULTS).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during vector search: {ErrorMessage}", ex.Message);
                return new List<DocumentChunk>();
            }
        }

        private async Task<List<WebResult>> WebSearchAsync(string query)
        {
            _logger.LogInformation("Initiating web search for query: {Query}", query);

            try
            {
                if (string.IsNullOrEmpty(_braveApiKey))
                {
                    _logger.LogWarning("Brave Search API key is missing, skipping web search");
                    return new List<WebResult>();
                }

                // Enhanced query for legal specificity, targeting recent authoritative sources
                string enhancedQuery = $"{query} site:*.gov site:*.edu site:*.org legal case law statute 2020..2025 -inurl:(signup login advertisement)";
                var requestUrl = $"https://api.search.brave.com/res/v1/web/search?q={Uri.EscapeDataString(enhancedQuery)}&count={MAX_WEB_RESULTS}";
                var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
                request.Headers.Add("Accept", "application/json");
                request.Headers.Add("X-Subscription-Token", _braveApiKey);

                var response = await _httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync();
                var searchResponse = JsonSerializer.Deserialize<BraveSearchResponse>(content);

                if (searchResponse?.Web?.Results == null || !searchResponse.Web.Results.Any())
                {
                    _logger.LogWarning("No web results found for query: {Query}", query);
                    return new List<WebResult>();
                }

                var results = searchResponse.Web.Results
                    .Select(r => new WebResult
                    {
                        Title = r.Title ?? string.Empty,
                        Description = r.Description ?? string.Empty,
                        Url = r.Url ?? string.Empty,
                        PublishedDate = ParseBraveAge(r.Age)
                    })
                    .Take(MAX_WEB_RESULTS)
                    .ToList();

                _logger.LogInformation("Web search returned {Count} results", results.Count);
                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Web search failed: {ErrorMessage}", ex.Message);
                return new List<WebResult>();
            }
        }

        private async Task<string> GenerateLegalAnalysisAsync(string query, List<DocumentChunk> documentChunks, string webContent)
        {
            _logger.LogInformation("Generating optimized legal analysis for query: {Query}", query);

            try
            {
                // Updated prompt for plain text formatted output with good readability
                var promptTemplate = @"# LEGAL ARGUMENT ANALYSIS

## QUERY
""{{query}}"" 

## INSTRUCTIONS
You are an expert legal assistant tasked with analyzing documents and web sources to construct precise legal arguments for a contract dispute. Produce a detailed yet concise analysis that:
1. Extracts key passages related to contract clauses, timelines, breaches, or remedies.
2. Explains passage relevance to the query, focusing on legal implications (e.g., materiality, waiver, damages).
3. Cross-checks document chunks for consistency (e.g., notice timing, performance details).
4. Integrates web-sourced case law or statutes (2020-2025) to support arguments.
5. Cites sources clearly:
   - Documents: [Doc ID: <DocumentId>, Score: <Score>]
   - Web: [<Title>, <URL>]
6. Identifies gaps needing clarification (e.g., missing contract terms).
7. Avoids judgments; presents objective findings.

## DOCUMENT CHUNKS
{{documentChunks}}

## WEB RESULTS
{{webContent}}

## OUTPUT FORMAT
Generate your response in plain text format with these formatting guidelines:
1. Use ASCII text formatting techniques for structure (e.g., underlines, indentation, spacing)
2. Create a professional, legal-style report with clear sections
3. Format all dates, amounts, legal terms consistently
4. Use ASCII symbols for structure (e.g., -, =, *, >) 
5. Create a clean header with border lines
6. Use indentation for quoted passages (4 spaces)
7. Format citations consistently with appropriate indentation
8. Use whitespace effectively to separate sections
9. Create an organized hierarchy with main sections and subsections
10. Include a metadata summary at the end

Follow this structure:
- Professional header with borders (====== or -------)
- Main title and query identification
- Date/timestamp
- Key Passages section with analysis
- Legal Authority section with web references
- Gaps & Ambiguities section
- Metadata summary

Example formatting:
===============================================================
LEGAL ANALYSIS REPORT
---------------------------------------------------------------
QUERY: ""{{query}}""
Generated: {{timestamp}}
===============================================================

KEY PASSAGES & ANALYSIS
---------------------------------------------------------------
[Section content with proper spacing and paragraph structure]

> Quoted passage from document with proper indentation
> Continued indented text

Source: [Doc ID: 12345, Score: 0.92]

Analysis:
This passage demonstrates [legal implication] because...

[Additional passages with consistent formatting]

LEGAL AUTHORITY & PRECEDENT
---------------------------------------------------------------
[Web references and case law structured in a readable format]

Reference: Smith v. Jones (2023)
Source: [Legal Database, URL]

[Analysis of applicable law]

GAPS & AMBIGUITIES
---------------------------------------------------------------
1. [First gap identified]
2. [Second gap identified]

METADATA
---------------------------------------------------------------
Documents Analyzed: {{documentCount}}
Average Confidence Score: {{averageScore}}
Analysis Timestamp: {{timestamp}}
===============================================================";

                var documentContent = new StringBuilder();
                foreach (var chunk in documentChunks)
                {
                    documentContent.AppendLine($"### Doc ID: {chunk.DocumentId}");
                    documentContent.AppendLine($"**Content**: {chunk.Content}");
                    documentContent.AppendLine($"**Score**: {chunk.Score:F2}");
                    documentContent.AppendLine();
                }

                var timestamp = DateTime.Now.ToString("MMMM d, yyyy 'at' h:mm tt");
                var avgScore = documentChunks.Any() ? documentChunks.Average(d => d.Score).ToString("F2") : "N/A";

                var renderedPrompt = promptTemplate
                    .Replace("{{query}}", query)
                    .Replace("{{documentChunks}}", documentContent.ToString())
                    .Replace("{{webContent}}", webContent)
                    .Replace("{{timestamp}}", timestamp)
                    .Replace("{{documentCount}}", documentChunks.Count.ToString())
                    .Replace("{{averageScore}}", avgScore);

                var result = await _kernel.InvokePromptAsync(renderedPrompt, new KernelArguments());
                var analysis = result.GetValue<string>() ?? "Error generating legal analysis.";

                _logger.LogInformation("Legal analysis generated successfully");
                return analysis;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating legal analysis: {ErrorMessage}", ex.Message);
                return @"===============================================================
ERROR GENERATING LEGAL ANALYSIS
===============================================================

" + ex.Message + @"

Please refine your query or check document availability.
===============================================================";
            }
        }

        private string FormatWebResultsForLLM(List<WebResult> webResults)
        {
            if (webResults == null || !webResults.Any())
                return "No web search results found.";

            var sb = new StringBuilder();
            sb.AppendLine("## Web Search Results");
            foreach (var result in webResults)
            {
                sb.AppendLine($"### {result.Title}");
                if (result.PublishedDate.HasValue)
                    sb.AppendLine($"**Published**: {result.PublishedDate.Value:MMMM d, yyyy}");
                sb.AppendLine($"**URL**: {result.Url}");
                sb.AppendLine($"{result.Description}");
                sb.AppendLine();
            }
            return sb.ToString();
        }

        private DateTime? ParseBraveAge(string age)
        {
            if (string.IsNullOrEmpty(age))
                return null;

            try
            {
                var now = DateTime.UtcNow;
                if (age.Contains("minute"))
                {
                    int minutes = int.Parse(age.Split(' ')[0]);
                    return now.AddMinutes(-minutes);
                }
                else if (age.Contains("hour"))
                {
                    int hours = int.Parse(age.Split(' ')[0]);
                    return now.AddHours(-hours);
                }
                else if (age.Contains("day"))
                {
                    int days = int.Parse(age.Split(' ')[0]);
                    return now.AddDays(-days);
                }
                else if (age.Contains("month"))
                {
                    int months = int.Parse(age.Split(' ')[0]);
                    return now.AddMonths(-months);
                }
                else if (age.Contains("year"))
                {
                    int years = int.Parse(age.Split(' ')[0]);
                    return now.AddYears(-years);
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        #region Data Transfer Objects
        public class DocumentChunk
        {
            public string Content { get; set; } = string.Empty;
            public string DocumentId { get; set; } = string.Empty;
            public double Score { get; set; }
        }

        public class WebResult
        {
            public string Title { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public string Url { get; set; } = string.Empty;
            public DateTime? PublishedDate { get; set; }
        }

        public class BraveSearchResponse
        {
            [JsonPropertyName("web")]
            public BraveWebResults Web { get; set; } = new BraveWebResults();
        }

        public class BraveWebResults
        {
            [JsonPropertyName("results")]
            public List<BraveWebResult> Results { get; set; } = new List<BraveWebResult>();
        }

        public class BraveWebResult
        {
            [JsonPropertyName("title")]
            public string Title { get; set; } = string.Empty;
            [JsonPropertyName("description")]
            public string Description { get; set; } = string.Empty;
            [JsonPropertyName("url")]
            public string Url { get; set; } = string.Empty;
            [JsonPropertyName("age")]
            public string Age { get; set; } = string.Empty;
        }
        #endregion
    }
}