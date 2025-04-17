using System.Collections.Generic;

namespace Counsel.Models
{
    /// <summary>
    /// Enum defining the different operational modes of the application.
    /// IMPORTANT: This order MUST match Counsel.BackendApi.Models.AppMode in the backend.
    /// </summary>
    public enum AppMode
    {
        None,         // Represents Chat Only mode (Value = 0)
        DeepResearch, // Value = 1
        Paralegal,    // Value = 2
        CrossExamine  // Value = 3
    }

    /// <summary>
    /// Represents a query request sent to the backend API.
    /// Matches Counsel.BackendApi.Models.QueryRequest.
    /// </summary>
    public class QueryRequest
    {
        public string Query { get; set; } = string.Empty;
        public AppMode Mode { get; set; } // Uses the AppMode enum defined above
        public List<string>? ChatHistory { get; set; }
    }

    /// <summary>
    /// Represents a response from the backend API.
    /// Matches Counsel.BackendApi.Models.QueryResponse.
    /// </summary>
    public class QueryResponse
    {
        public string Response { get; set; } = string.Empty;
        public string CanvasContent { get; set; } = string.Empty;
    }

    // Add any other classes or structs belonging to the Counsel.Models namespace here
    // if they exist in your original file.
}