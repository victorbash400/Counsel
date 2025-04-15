using Microsoft.AspNetCore.Mvc;
using Counsel.BackendApi.Services;
using Counsel.BackendApi.Plugins;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.SemanticKernel.Embeddings;
using Azure;

namespace Counsel.BackendApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DocumentsController : ControllerBase
    {
        private readonly DocumentProcessingService _documentProcessingService;
        private readonly SearchClient _searchClient;
#pragma warning disable SKEXP0001
        private readonly ITextEmbeddingGenerationService _embeddingService;
#pragma warning restore SKEXP0001
        private readonly ParalegalPlugin _paralegalPlugin;
        private readonly ResearchPlugin _researchPlugin;
        private readonly ExaminePlugin _examinePlugin;

        public DocumentsController(
            DocumentProcessingService documentProcessingService,
            SearchClient searchClient,
#pragma warning disable SKEXP0001
            ITextEmbeddingGenerationService embeddingService,
#pragma warning restore SKEXP0001
            ParalegalPlugin paralegalPlugin,
            ResearchPlugin researchPlugin,
            ExaminePlugin examinePlugin)
        {
            _documentProcessingService = documentProcessingService;
            _searchClient = searchClient;
            _embeddingService = embeddingService;
            _paralegalPlugin = paralegalPlugin;
            _researchPlugin = researchPlugin;
            _examinePlugin = examinePlugin;
            Console.WriteLine("DocumentsController initialized with ParalegalPlugin, ResearchPlugin, and ExaminePlugin.");
        }

        [HttpPost("upload")]
        public async Task<IActionResult> Upload(IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                return BadRequest("No file uploaded.");
            }

            if (!file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest("Only PDF files are supported.");
            }

            try
            {
                Console.WriteLine($"Uploading file: {file.FileName}");
                using var stream = file.OpenReadStream();
                await _documentProcessingService.ProcessDocumentAsync(stream, file.FileName);
                Console.WriteLine("Upload successful.");
                return Ok("PDF processed and indexed successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Upload error: {ex.Message}");
                return StatusCode(500, $"Error processing PDF: {ex.Message}");
            }
        }

        [HttpGet("search")]
        public async Task<IActionResult> Search([FromQuery] string query)
        {
            if (string.IsNullOrEmpty(query))
            {
                return BadRequest("Query is required.");
            }

            try
            {
                Console.WriteLine($"Search query: {query}");
#pragma warning disable SKEXP0001
                var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(query);
#pragma warning restore SKEXP0001

                var vectorQuery = new VectorizedQuery(queryEmbedding.ToArray())
                {
                    KNearestNeighborsCount = 10,
                    Fields = { "Embedding" }
                };

                var options = new SearchOptions
                {
                    Size = 10,
                    VectorSearch = new VectorSearchOptions()
                };

                options.VectorSearch.Queries.Add(vectorQuery);

                var searchResults = await _searchClient.SearchAsync<SearchDocument>("*", options);
                var searchDocuments = new List<SearchResult<SearchDocument>>();

                await foreach (var result in searchResults.Value.GetResultsAsync())
                {
                    searchDocuments.Add(result);
                }

                Console.WriteLine($"Search found {searchDocuments.Count} results.");
                var documents = searchDocuments.Select(r => new
                {
                    Id = r.Document["Id"],
                    DocumentId = r.Document["DocumentId"],
                    Content = r.Document["Content"],
                    Score = r.Document["Score"]
                }).ToList();

                return Ok(documents);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Search error: {ex.Message}");
                return StatusCode(500, $"Error searching documents: {ex.Message}");
            }
        }

        [HttpPost("paralegal")]
        public async Task<IActionResult> Paralegal([FromQuery] string query, [FromQuery] string task)
        {
            if (string.IsNullOrEmpty(query) || string.IsNullOrEmpty(task))
            {
                return BadRequest("Query and task are required.");
            }

            var taskLower = task.ToLower();
            if (!new[] { "summarize", "extract", "notes" }.Contains(taskLower))
            {
                return BadRequest("Task must be 'summarize', 'extract', or 'notes'.");
            }

            try
            {
                Console.WriteLine($"Paralegal task: query='{query}', task='{taskLower}'");
#pragma warning disable SKEXP0001
                var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(query);
#pragma warning restore SKEXP0001

                var vectorQuery = new VectorizedQuery(queryEmbedding.ToArray())
                {
                    KNearestNeighborsCount = 10,
                    Fields = { "Embedding" }
                };

                var options = new SearchOptions
                {
                    Size = 10,
                    VectorSearch = new VectorSearchOptions()
                };

                options.VectorSearch.Queries.Add(vectorQuery);

                var searchResults = await _searchClient.SearchAsync<SearchDocument>("*", options);
                var searchDocuments = new List<SearchResult<SearchDocument>>();

                await foreach (var result in searchResults.Value.GetResultsAsync())
                {
                    searchDocuments.Add(result);
                }

                var contents = searchDocuments.Select(r => r.Document["Content"]?.ToString()).Where(c => !string.IsNullOrEmpty(c)).ToArray();
                Console.WriteLine($"Found {contents.Length} content chunks for paralegal task.");

                if (contents.Length == 0)
                {
                    Console.WriteLine("No content found for query.");
                    return Ok(new { ChatResponse = "No relevant content found.", CanvasContent = "", CanvasTitle = taskLower });
                }

                object responseData;
                switch (taskLower)
                {
                    case "summarize":
                        var summary = await _paralegalPlugin.SummarizeContextAsync(contents, query);
                        responseData = new { ChatResponse = "Summary generated.", CanvasContent = summary, CanvasTitle = "Document Summary" };
                        break;
                    case "extract":
                        var entities = await _paralegalPlugin.ExtractKeyInfoAsync(contents, query);
                        responseData = new { ChatResponse = "Entities extracted.", CanvasContent = entities, CanvasTitle = "Key Entities" };
                        break;
                    case "notes":
                        var notes = await _paralegalPlugin.GenerateDocNotesAsync(contents, query);
                        responseData = new { ChatResponse = "Notes generated.", CanvasContent = notes, CanvasTitle = "Structured Notes" };
                        break;
                    default:
                        throw new InvalidOperationException("Invalid task.");
                }

                Console.WriteLine("Paralegal task completed successfully.");
                return Ok(responseData);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Paralegal error: {ex.Message}\nStackTrace: {ex.StackTrace}");
                return StatusCode(500, $"Error processing paralegal task: {ex.Message}");
            }
        }

        [HttpPost("research")]
        public async Task<IActionResult> Research([FromQuery] string query, [FromQuery] string? caseContext = null)
        {
            if (string.IsNullOrEmpty(query))
            {
                return BadRequest("Query is required.");
            }

            try
            {
                Console.WriteLine($"Research task: query='{query}', caseContext='{caseContext ?? "null"}'");

                var researchResult = await _researchPlugin.PerformLegalResearchAsync(query);

                Console.WriteLine("Research task completed successfully.");

                if (string.IsNullOrEmpty(researchResult) || researchResult.Contains("An error occurred"))
                {
                    return StatusCode(500, researchResult ?? "An unknown error occurred during research.");
                }
                return Ok(researchResult);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Research error: {ex.Message}\nStackTrace: {ex.StackTrace}");
                return StatusCode(500, $"Error generating research brief: {ex.Message}");
            }
        }

        [HttpPost("examine")]
        public async Task<IActionResult> Examine([FromQuery] string query)
        {
            if (string.IsNullOrEmpty(query))
            {
                return BadRequest("Query is required.");
            }

            try
            {
                Console.WriteLine($"Examine task: query='{query}'");
                var examineResult = await _examinePlugin.FindRelevantPassagesAsync(query);

                Console.WriteLine("Examine task completed successfully.");

                if (string.IsNullOrEmpty(examineResult) || examineResult.Contains("An error occurred"))
                {
                    return StatusCode(500, examineResult ?? "An unknown error occurred during examination.");
                }

                return Ok(new
                {
                    ChatResponse = "Examination completed.",
                    CanvasContent = examineResult,
                    CanvasTitle = "Legal Analysis"
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Examine error: {ex.Message}\nStackTrace: {ex.StackTrace}");
                return StatusCode(500, $"Error generating examination: {ex.Message}");
            }
        }
    }
}