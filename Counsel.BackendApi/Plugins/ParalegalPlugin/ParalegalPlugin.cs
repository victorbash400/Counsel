using Microsoft.SemanticKernel;
using System.ComponentModel;
using System.Threading.Tasks;
using System;

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
        [Description("Generates professional legal notes from document chunks")]
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

            var promptTemplate = @"Based on the query '{{query}}' and the following document excerpts, generate structured legal notes in a professional format:

INSTRUCTIONS:
1. Create a well-formatted document with clear sections and proper indentation
2. Include only sections that are relevant to the content (not all documents will have timelines, for example)
3. Format should be clean professional text that will be displayed as-is (NO markdown symbols or formatting)
4. Use CAPITALIZED HEADERS and indentation for structure instead of markdown
5. Use bullet points (•) where appropriate
6. Include the following sections ONLY IF RELEVANT:
   - CASE SUMMARY
   - TIMELINE OF EVENTS (only if chronology is important)
   - KEY FACTS & ARGUMENTS
   - LEGAL ISSUES
   - PRECEDENT CONSIDERATIONS
   - CRITICAL QUESTIONS & FOLLOW-UPS
   - NEXT STEPS

Example format:
--------------------
LEGAL MEMORANDUM
Re: [Brief subject based on query]

CASE SUMMARY
[Concise summary paragraph]

KEY FACTS & ARGUMENTS
• [Fact/argument 1]
• [Fact/argument 2]
...

[Other relevant sections as needed]
--------------------

Document excerpts to analyze:
{{chunks}}";

            // Manually render the prompt by replacing placeholders
            var renderedPrompt = promptTemplate
                .Replace("{{query}}", query)
                .Replace("{{chunks}}", string.Join("\n", chunks));

            // Pass the rendered prompt with no arguments, since all variables are replaced
            var result = await _kernel.InvokePromptAsync(renderedPrompt, new KernelArguments());
            var notes = result.GetValue<string>() ?? "Error generating notes.";
            Console.WriteLine($"GenerateDocNotesAsync result: {notes.Substring(0, Math.Min(50, notes.Length))}...");
            return notes;
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

            var promptTemplate = @"Summarize the following document excerpts in the style of a professional legal brief summary, focusing on aspects relevant to the query '{{query}}':
            
INSTRUCTIONS:
1. Write in a formal, concise legal style
2. Focus on the most pertinent facts and considerations
3. Organize information logically
4. Use proper legal terminology
5. Be objective and precise
6. Format as PLAIN TEXT with no markdown or special formatting symbols

Document excerpts:
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
        [Description("Extracts key entities from document chunks in a professional legal format")]
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

            var promptTemplate = @"From the following document excerpts, extract key entities relevant to the query '{{query}}' and present them in a professional legal index format:

INSTRUCTIONS:
1. Create a well-formatted document with clear categories
2. Only include categories that contain relevant information
3. Format as a professional legal reference document using PLAIN TEXT only (NO markdown)
4. Use appropriate legal terminology and citation formats
5. Use CAPITALIZED HEADERS and indentation for structure

Example format:
--------------------
CASE REFERENCE INDEX

PARTIES
• Smith, John (Plaintiff)
• Acme Corporation (Defendant)
...

KEY DATES
• January 15, 2024 - Complaint filed
• March 3, 2024 - Motion to dismiss submitted
...

ORGANIZATIONS
• [List relevant organizations]

DEFINED TERMS
• [List important defined terms]

MONETARY FIGURES
• [List relevant amounts and descriptions]

JURISDICTIONAL CONSIDERATIONS
• [List relevant jurisdictions]
--------------------

Document excerpts:
{{chunks}}";

            var renderedPrompt = promptTemplate
                .Replace("{{query}}", query)
                .Replace("{{chunks}}", string.Join("\n", chunks));

            var result = await _kernel.InvokePromptAsync(renderedPrompt, new KernelArguments());
            var keyInfo = result.GetValue<string>() ?? "Error extracting key information.";
            Console.WriteLine($"ExtractKeyInfoAsync result: {keyInfo.Substring(0, Math.Min(50, keyInfo.Length))}...");
            return keyInfo;
        }
    }
}