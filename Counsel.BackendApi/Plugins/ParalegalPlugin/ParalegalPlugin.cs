using Microsoft.SemanticKernel;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Text.Json;

namespace Counsel.BackendApi.Plugins
{
    public class ParalegalPlugin
    {
        private readonly Kernel _kernel;

        public ParalegalPlugin(Kernel kernel)
        {
            _kernel = kernel;
            Console.WriteLine("ParalegalPlugin initialized with Kernel.");
        }

        [KernelFunction]
        [Description("Generates structured notes (timeline, points, questions) from document chunks")]
        public async Task<string> GenerateDocNotesAsync(
            [Description("List of document contents to analyze")] string[] chunks,
            [Description("User query for context")] string query)
        {
            Console.WriteLine($"GenerateDocNotesAsync called with query: {query}, chunks: {chunks?.Length ?? 0}");
            if (chunks == null || chunks.Length == 0)
            {
                Console.WriteLine("No content to analyze.");
                return "No content to analyze.";
            }

            var promptTemplate = @"Based on the query '{{query}}' and the following document excerpts, generate structured notes for a legal professional in JSON format:
            {
              ""timeline"": [],
              ""keyPoints"": [],
              ""questions"": []
            }
            - timeline: List of events in chronological order (e.g., {""date"": ""2024-01-01"", ""event"": ""...""}).
            - keyPoints: Bulleted list of key arguments or facts (e.g., {""point"": ""..."", ""source"": ""...""}).
            - questions: List of unanswered questions or follow-ups (e.g., ""..."").
            Excerpts:
            {{chunks}}";

            // Manually render the prompt by replacing placeholders
            var renderedPrompt = promptTemplate
                .Replace("{{query}}", query)
                .Replace("{{chunks}}", string.Join("\n", chunks));

            // Pass the rendered prompt with no arguments, since all variables are replaced
            var result = await _kernel.InvokePromptAsync(renderedPrompt, new KernelArguments());
            var json = result.GetValue<string>() ?? "{}";
            try
            {
                JsonSerializer.Deserialize<object>(json); // Validate JSON
                Console.WriteLine($"GenerateDocNotesAsync result: {json.Substring(0, Math.Min(50, json.Length))}...");
                return json;
            }
            catch
            {
                Console.WriteLine("GenerateDocNotesAsync: Invalid JSON, returning empty object.");
                return "{}";
            }
        }

        [KernelFunction]
        [Description("Summarizes a list of document chunks into a concise summary")]
        public async Task<string> SummarizeContextAsync(
            [Description("List of document contents to summarize")] string[] chunks,
            [Description("User query for context")] string query)
        {
            Console.WriteLine($"SummarizeContextAsync called with query: {query}, chunks: {chunks?.Length ?? 0}");
            if (chunks == null || chunks.Length == 0)
            {
                Console.WriteLine("No content to summarize.");
                return "No content to summarize.";
            }

            var promptTemplate = @"Summarize the following document excerpts into a concise paragraph, focusing on aspects relevant to the query '{{query}}':
            {{chunks}}";

            var renderedPrompt = promptTemplate
                .Replace("{{query}}", query)
                .Replace("{{chunks}}", string.Join("\n", chunks));

            var result = await _kernel.InvokePromptAsync(renderedPrompt, new KernelArguments());
            var summary = result.GetValue<string>() ?? "Error generating summary.";
            Console.WriteLine($"SummarizeContextAsync result: {summary.Substring(0, Math.Min(50, summary.Length))}...");
            return summary;
        }

        [KernelFunction]
        [Description("Extracts key entities (people, dates, organizations, terms, amounts) from document chunks")]
        public async Task<string> ExtractKeyInfoAsync(
            [Description("List of document contents to analyze")] string[] chunks,
            [Description("User query for context")] string query)
        {
            Console.WriteLine($"ExtractKeyInfoAsync called with query: {query}, chunks: {chunks?.Length ?? 0}");
            if (chunks == null || chunks.Length == 0)
            {
                Console.WriteLine("No content to analyze.");
                return "No content to analyze.";
            }

            var promptTemplate = @"From the following document excerpts, extract key entities (people, dates, organizations, defined terms, monetary amounts) relevant to the query '{{query}}'. Return as a JSON object with lists for each entity type:
            {
              ""people"": [],
              ""dates"": [],
              ""organizations"": [],
              ""terms"": [],
              ""amounts"": []
            }
            Excerpts:
            {{chunks}}";

            var renderedPrompt = promptTemplate
                .Replace("{{query}}", query)
                .Replace("{{chunks}}", string.Join("\n", chunks));

            var result = await _kernel.InvokePromptAsync(renderedPrompt, new KernelArguments());
            var json = result.GetValue<string>() ?? "{}";
            try
            {
                JsonSerializer.Deserialize<object>(json); // Validate JSON
                Console.WriteLine($"ExtractKeyInfoAsync result: {json.Substring(0, Math.Min(50, json.Length))}...");
                return json;
            }
            catch
            {
                Console.WriteLine("ExtractKeyInfoAsync: Invalid JSON, returning empty object.");
                return "{}";
            }
        }
    }
}