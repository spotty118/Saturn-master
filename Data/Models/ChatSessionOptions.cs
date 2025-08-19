namespace Saturn.Data.Models
{
    /// <summary>
    /// Configuration options for creating chat sessions
    /// </summary>
    public class ChatSessionOptions
    {
        public string ChatType { get; set; } = "main";
        public string? ParentSessionId { get; set; }
        public string? AgentName { get; set; }
        public string? Model { get; set; }
        public string? SystemPrompt { get; set; }
        public double? Temperature { get; set; }
        public int? MaxTokens { get; set; }
    }
}
