using System.Collections.Generic;

namespace Counsel.BackendApi.Models
{
    public class QueryRequest
    {
        public string Query { get; set; } = string.Empty;
        public AppMode Mode { get; set; }
        public List<string>? ChatHistory { get; set; }
    }
}