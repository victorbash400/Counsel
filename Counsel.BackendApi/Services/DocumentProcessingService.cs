using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Embeddings;
using UglyToad.PdfPig;

namespace Counsel.BackendApi.Services
{
    public class DocumentProcessingService
    {
        private readonly SearchClient _searchClient;
        private readonly Kernel _kernel;
#pragma warning disable SKEXP0001 // Suppress warning for experimental SK embedding service
        private readonly ITextEmbeddingGenerationService _embeddingService;
#pragma warning restore SKEXP0001

        public DocumentProcessingService(
            SearchClient searchClient,
            Kernel kernel,
#pragma warning disable SKEXP0001 // Suppress warning for experimental SK embedding service
            ITextEmbeddingGenerationService embeddingService)
#pragma warning restore SKEXP0001
        {
            _searchClient = searchClient;
            _kernel = kernel;
            _embeddingService = embeddingService;
        }

        public async Task ProcessDocumentAsync(Stream pdfStream, string fileName)
        {
            // Extract text from PDF
            string text = await ExtractTextFromPdfAsync(pdfStream);

            // Chunk text
            var chunks = ChunkText(text);

            // Generate embeddings and index
            var documents = new List<SearchDocument>();
            for (int i = 0; i < chunks.Count; i++)
            {
                var chunk = chunks[i];
                var embedding = await GenerateEmbeddingAsync(chunk);
                var document = new SearchDocument
                {
                    ["Id"] = Guid.NewGuid().ToString(),
                    ["DocumentId"] = fileName + "_" + i,
                    ["Content"] = chunk,
                    ["Embedding"] = embedding
                };
                documents.Add(document);
            }

            // Index documents
            await _searchClient.IndexDocumentsAsync(IndexDocumentsBatch.Upload(documents));
        }

        private async Task<string> ExtractTextFromPdfAsync(Stream pdfStream)
        {
            using var document = PdfDocument.Open(pdfStream);
            var text = string.Join(" ", document.GetPages().Select(page => page.Text));
            return text;
        }

        private List<string> ChunkText(string text)
        {
            // Simple chunking by word count (~200 words per chunk)
            var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var chunks = new List<string>();
            int chunkSize = 200;
            for (int i = 0; i < words.Length; i += chunkSize)
            {
                var chunkWords = words.Skip(i).Take(chunkSize);
                chunks.Add(string.Join(" ", chunkWords));
            }
            return chunks;
        }

        private async Task<float[]> GenerateEmbeddingAsync(string text)
        {
#pragma warning disable SKEXP0010 // Suppress warning for experimental SK embedding method
            var embeddings = await _embeddingService.GenerateEmbeddingAsync(text);
#pragma warning restore SKEXP0010
            return embeddings.ToArray();
        }
    }
}