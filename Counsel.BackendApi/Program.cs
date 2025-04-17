using Microsoft.SemanticKernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Azure.AI.OpenAI;
using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Microsoft.SemanticKernel.Embeddings;
using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Serialization;

// Note: Ensure 'Ical.Net' NuGet package is installed (`dotnet add package Ical.Net`)

// Assuming your services and plugins are in these namespaces
using Counsel.BackendApi.Services;
using Counsel.BackendApi.Plugins;
using Counsel.BackendApi.Models;

namespace Counsel.BackendApi
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);
            var services = builder.Services;
            var configuration = builder.Configuration;

            // --- Standard Service Configuration ---
            services.AddControllers()
                .AddJsonOptions(options =>
                {
                    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
                });
            services.AddEndpointsApiExplorer();
            services.AddSwaggerGen();
            services.AddLogging(logging =>
            {
                logging.AddConsole();
                logging.SetMinimumLevel(LogLevel.Debug); // Debug level for testing
            });
            services.AddHttpClient();
            services.AddSingleton<DocumentProcessingService>();

            // --- Add CORS Policy ---
            services.AddCors(options =>
            {
                options.AddPolicy("AllowWpfApp", policy =>
                {
                    policy.WithOrigins("http://localhost", "https://localhost") // Specific origins for WPF app
                          .AllowAnyHeader()
                          .AllowAnyMethod();
                });
            });
            services.AddSingleton<DateResolutionService>();

            // --- Azure Client Configuration ---
            services.AddSingleton(sp => { /* ... Azure OpenAI Client setup ... */
                var endpoint = configuration["AzureOpenAI:Chat:Endpoint"];
                var apiKey = configuration["AzureOpenAI:Chat:ApiKey"];
                if (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(apiKey))
                    throw new InvalidOperationException("Azure OpenAI Client configuration missing (Endpoint/ApiKey).");
                if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri))
                    throw new InvalidOperationException($"Invalid Azure OpenAI endpoint URI: '{endpoint}'.");
                var actualApiKey = configuration["AzureOpenAI:Chat:ApiKey"];
                if (string.IsNullOrEmpty(actualApiKey))
                    throw new InvalidOperationException("Azure OpenAI Chat API Key is missing.");
                return new AzureOpenAIClient(endpointUri, new AzureKeyCredential(actualApiKey));
            });
            services.AddSingleton(sp => { /* ... Azure Search Client setup ... */
                var endpoint = configuration["AzureSearch:Endpoint"];
                var apiKey = configuration["AzureSearch:ApiKey"];
                var indexName = configuration["AzureSearch:IndexName"];
                var logger = sp.GetRequiredService<ILogger<Program>>();
                if (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(indexName))
                    throw new InvalidOperationException("Azure AI Search configuration missing (Endpoint/ApiKey/IndexName).");
                if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var serviceEndpointUri))
                    throw new InvalidOperationException($"Invalid Azure AI Search endpoint URI: '{endpoint}'.");
                var credential = new AzureKeyCredential(apiKey);
                var indexClient = new SearchIndexClient(serviceEndpointUri, credential);
                EnsureSearchIndexExists(indexClient, indexName, logger);
                return new SearchClient(serviceEndpointUri, indexName, credential);
            });

            // --- Semantic Kernel Configuration ---
            services.AddSingleton(sp => { /* ... Kernel setup ... */
                var logger = sp.GetRequiredService<ILogger<Kernel>>();
                var kernelBuilder = Kernel.CreateBuilder();
                // Chat Completion
                var chatDeploymentName = configuration["AzureOpenAI:Chat:DeploymentName"];
                var chatEndpoint = configuration["AzureOpenAI:Chat:Endpoint"];
                var chatApiKey = configuration["AzureOpenAI:Chat:ApiKey"];
                if (string.IsNullOrEmpty(chatDeploymentName) || string.IsNullOrEmpty(chatEndpoint) || string.IsNullOrEmpty(chatApiKey))
                    throw new InvalidOperationException("Azure OpenAI Chat configuration missing.");
                kernelBuilder.AddAzureOpenAIChatCompletion(chatDeploymentName, chatEndpoint, chatApiKey);
                // Embedding Generation
                var embeddingDeploymentName = configuration["AzureOpenAI:Embedding:DeploymentName"];
                var embeddingEndpoint = configuration["AzureOpenAI:Embedding:Endpoint"];
                var embeddingApiKey = configuration["AzureOpenAI:Embedding:ApiKey"];
                if (string.IsNullOrEmpty(embeddingDeploymentName) || string.IsNullOrEmpty(embeddingEndpoint) || string.IsNullOrEmpty(embeddingApiKey))
                    throw new InvalidOperationException("Azure OpenAI Embedding configuration missing.");
#pragma warning disable SKEXP0010, SKEXP0011
                kernelBuilder.AddAzureOpenAITextEmbeddingGeneration(embeddingDeploymentName, embeddingEndpoint, embeddingApiKey);
#pragma warning restore SKEXP0010, SKEXP0011
                var kernel = kernelBuilder.Build();
                return kernel;
            });
#pragma warning disable SKEXP0001, SKEXP0010
            services.AddSingleton<ITextEmbeddingGenerationService>(sp =>
                sp.GetRequiredService<Kernel>().GetRequiredService<ITextEmbeddingGenerationService>());
#pragma warning restore SKEXP0001, SKEXP0010

            // --- Register Plugins ---
            services.AddSingleton<ParalegalPlugin>(sp => new ParalegalPlugin(sp.GetRequiredService<Kernel>()));
            services.AddSingleton<ResearchPlugin>(sp => { /* ... ResearchPlugin dependencies ... */
                var logger = sp.GetRequiredService<ILogger<ResearchPlugin>>();
                var kernel = sp.GetRequiredService<Kernel>();
                var searchClient = sp.GetRequiredService<SearchClient>();
                var config = sp.GetRequiredService<IConfiguration>();
                var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient();
#pragma warning disable SKEXP0001
                var embeddingService = sp.GetRequiredService<ITextEmbeddingGenerationService>();
#pragma warning restore SKEXP0001
                return new ResearchPlugin(logger, searchClient, config, httpClient, kernel, embeddingService);
            });
            services.AddSingleton<ExaminePlugin>(sp => { /* ... ExaminePlugin dependencies ... */
                var logger = sp.GetRequiredService<ILogger<ExaminePlugin>>();
                var kernel = sp.GetRequiredService<Kernel>();
                var searchClient = sp.GetRequiredService<SearchClient>();
                var config = sp.GetRequiredService<IConfiguration>();
                var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient();
#pragma warning disable SKEXP0001
                var embeddingService = sp.GetRequiredService<ITextEmbeddingGenerationService>();
#pragma warning restore SKEXP0001
                return new ExaminePlugin(logger, searchClient, config, httpClient, kernel, embeddingService);
            });

            // --- Register Orchestrator Service ---
            services.AddSingleton<SKOrchestratorService>(sp =>
            {
                var kernel = sp.GetRequiredService<Kernel>();
                var searchClient = sp.GetRequiredService<SearchClient>();
#pragma warning disable SKEXP0001
                var embeddingService = sp.GetRequiredService<ITextEmbeddingGenerationService>();
#pragma warning restore SKEXP0001
                var logger = sp.GetRequiredService<ILogger<SKOrchestratorService>>();
                var researchPlugin = sp.GetRequiredService<ResearchPlugin>();
                var examinePlugin = sp.GetRequiredService<ExaminePlugin>();
                var paralegalPlugin = sp.GetRequiredService<ParalegalPlugin>();

                if (!kernel.Plugins.Contains("ParalegalPlugin"))
                {
                    logger.LogInformation("Adding ParalegalPlugin to the Kernel instance for SKOrchestratorService (optional step).");
                    kernel.Plugins.AddFromObject(paralegalPlugin, "ParalegalPlugin");
                }

                return new SKOrchestratorService(
                    kernel,
                    searchClient,
                    embeddingService,
                    logger,
                    researchPlugin,
                    examinePlugin,
                    paralegalPlugin
                );
            });

            // --- Build and Configure Application ---
            var app = builder.Build();

            // Optional: Verify SearchClient resolution
            try
            {
                app.Services.GetRequiredService<SearchClient>();
                app.Services.GetRequiredService<ILogger<Program>>().LogInformation("SearchClient resolved successfully.");
            }
            catch (Exception ex)
            {
                app.Services.GetRequiredService<ILogger<Program>>().LogError(ex, "Failed to resolve SearchClient.");
            }

            // Configure the HTTP request pipeline
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Error");
                app.UseHsts();
            }
            app.UseHttpsRedirection();
            app.UseCors("AllowWpfApp"); // Apply CORS policy
            app.MapControllers();
            app.Run();
        }

        // Helper method to ensure the Azure Search index exists
        private static void EnsureSearchIndexExists(SearchIndexClient indexClient, string indexName, ILogger logger)
        {
            try
            {
                logger.LogInformation("Checking for Azure AI Search index '{IndexName}'...", indexName);
                indexClient.GetIndex(indexName);
                logger.LogInformation("Azure AI Search index '{IndexName}' already exists.", indexName);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                logger.LogWarning("Azure AI Search index '{IndexName}' not found. Creating index...", indexName);
                var index = new SearchIndex(indexName)
                {
                    Fields = {
                        new SimpleField("Id", SearchFieldDataType.String) { IsKey = true, IsFilterable = true, IsSortable = true },
                        new SearchableField("DocumentId") { IsFilterable = true, IsSortable = true, IsFacetable = true },
                        new SearchableField("ChunkId") { IsFilterable = true, IsSortable = true },
                        new SearchableField("Content") { IsFilterable = false, IsSortable = false },
                        new SimpleField("PageNumber", SearchFieldDataType.Int32) { IsFilterable = true, IsSortable = true, IsFacetable = true },
                        new VectorSearchField("Embedding", 1536, "default-vector-profile")
                    },
                    VectorSearch = new VectorSearch
                    {
                        Profiles = { new VectorSearchProfile("default-vector-profile", "default-hnsw-algorithm") },
                        Algorithms = { new HnswAlgorithmConfiguration("default-hnsw-algorithm") }
                    }
                };
                indexClient.CreateIndex(index);
                logger.LogInformation("Azure AI Search index '{IndexName}' created successfully.", indexName);
            }
            catch (Exception ex)
            {
                logger.LogCritical(ex, "Failed to check or create Azure AI Search index '{IndexName}'.", indexName);
                throw;
            }
        }
    }
}