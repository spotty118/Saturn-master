using Saturn.Data.Models;
using Saturn.OpenRouter.Models.Api.Chat;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Saturn.Data
{
    /// <summary>
    /// Interface for chat history repository operations
    /// </summary>
    public interface IChatHistoryRepository : IDisposable
    {
        Task<ChatSession> CreateSessionAsync(
            string title, 
            ChatSessionOptions? options = null,
            CancellationToken cancellationToken = default);

        Task<ChatMessage> SaveMessageAsync(
            string sessionId, 
            Message message, 
            string? agentName = null, 
            int? sequenceNumber = null, 
            CancellationToken cancellationToken = default);

        Task<List<ChatMessage>> SaveMessageBatchAsync(
            string sessionId, 
            List<Message> messages,
            string? agentName = null, 
            CancellationToken cancellationToken = default);

        Task<ToolCallRecord> SaveToolCallAsync(
            string messageId, 
            string sessionId, 
            string toolName, 
            string arguments, 
            string? agentName = null,
            CancellationToken cancellationToken = default);

        Task UpdateToolCallResultAsync(
            string toolCallId, 
            string? result, 
            string? error, 
            int durationMs,
            CancellationToken cancellationToken = default);

        Task<List<ChatSession>> GetSessionsAsync(
            string? chatType = null, 
            int limit = 100);

        Task<ChatSession?> GetSessionAsync(string sessionId);

        Task<List<ChatMessage>> GetMessagesAsync(string sessionId);

        Task<List<ToolCallRecord>> GetToolCallsAsync(
            string sessionId, 
            int limit = 100);

        Task UpdateSessionAsync(ChatSession session);

        Task DeleteSessionAsync(string sessionId);

        Task<bool> SessionExistsAsync(string sessionId);
    }
}
