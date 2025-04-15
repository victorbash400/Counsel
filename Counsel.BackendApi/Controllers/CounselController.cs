using Counsel.BackendApi.Models;
using Counsel.BackendApi.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Counsel.BackendApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CounselController : ControllerBase
    {
        private readonly DocumentProcessingService _documentProcessingService;
        private readonly SKOrchestratorService _orchestratorService;
        private readonly ILogger<CounselController> _logger;

        public CounselController(
            DocumentProcessingService documentProcessingService,
            SKOrchestratorService orchestratorService,
            ILogger<CounselController> logger)
        {
            _documentProcessingService = documentProcessingService ?? throw new ArgumentNullException(nameof(documentProcessingService));
            _orchestratorService = orchestratorService ?? throw new ArgumentNullException(nameof(orchestratorService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        [HttpPost("query")]
        [ProducesResponseType(typeof(QueryResponse), 200)]
        [ProducesResponseType(400)]
        public async Task<IActionResult> Query([FromBody] QueryRequest request)
        {
            if (request == null || string.IsNullOrEmpty(request.Query))
            {
                _logger.LogWarning("Invalid query request received.");
                return BadRequest("Query cannot be null or empty.");
            }

            try
            {
                _logger.LogInformation("Processing query: {Query}, Mode: {Mode}", request.Query, request.Mode);

                var response = await _orchestratorService.ProcessQueryAsync(request);
                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing query: {Query}", request.Query);
                return StatusCode(500, "An error occurred while processing the query.");
            }
        }

        [HttpPost("documents/upload")]
        [ProducesResponseType(200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(500)]
        public async Task<IActionResult> UploadDocument()
        {
            if (!Request.HasFormContentType)
            {
                _logger.LogWarning("Invalid form content type for document upload.");
                return BadRequest("Expected multipart form data.");
            }

            var file = Request.Form.Files.FirstOrDefault();
            if (file == null || file.Length == 0)
            {
                _logger.LogWarning("No file uploaded or file is empty.");
                return BadRequest("No file uploaded or file is empty.");
            }

            if (!file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Uploaded file is not a PDF: {FileName}", file.FileName);
                return BadRequest("Only PDF files are supported.");
            }

            try
            {
                _logger.LogInformation("Processing document upload: {FileName}", file.FileName);
                using var stream = file.OpenReadStream();
                await _documentProcessingService.ProcessDocumentAsync(stream, file.FileName);
                _logger.LogInformation("Document uploaded successfully: {FileName}", file.FileName);
                return Ok("Document uploaded successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing document upload: {FileName}", file.FileName);
                return StatusCode(500, "An error occurred while uploading the document.");
            }
        }
    }
}