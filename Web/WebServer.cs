using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Saturn.Agents;
using Saturn.Tools.Core;
using Saturn.Core;
using Saturn.Core.Configuration;
using System.Security.Cryptography;
using System.Linq;

namespace Saturn.Web
{
    public class WebServer
    {
        private readonly Agent _agent;
        private readonly ToolRegistry _toolRegistry;
        private readonly SettingsManager _settingsManager;
        private readonly IConfigurationService _configurationService;
        private readonly ILogger<WebServer> _logger;
        private WebApplication? _app;

        public WebServer(Agent agent, ToolRegistry toolRegistry, SettingsManager settingsManager, IConfigurationService configurationService, ILogger<WebServer> logger)
        {
            _agent = agent;
            _toolRegistry = toolRegistry;
            _settingsManager = settingsManager;
            _configurationService = configurationService;
            _logger = logger;
        }

        public async Task StartAsync(int port = 5173)
        {
            var builder = WebApplication.CreateBuilder();

            // Load centralized configuration
            var config = await _configurationService.GetConfigurationAsync();

            // CORS configuration
            if (config.Web.EnableCors)
            {
                builder.Services.AddCors(options =>
                {
                    options.AddPolicy("SaturnCors", policy =>
                    {
                        if (config.Web.CorsOrigins != null && config.Web.CorsOrigins.Count > 0)
                        {
                            policy.WithOrigins(config.Web.CorsOrigins.ToArray())
                                  .AllowAnyHeader()
                                  .AllowAnyMethod();
                        }
                        else
                        {
                            policy.AllowAnyOrigin()
                                  .AllowAnyHeader()
                                  .AllowAnyMethod();
                        }
                    });
                });
            }

            // SignalR configuration (from settings)
            builder.Services.AddSignalR(options =>
            {
                options.EnableDetailedErrors = config.Web.SignalR.EnableDetailedErrors;
                options.MaximumReceiveMessageSize = config.Web.SignalR.MaxMessageSize;
            });
            builder.Services.AddSingleton(_agent);
            builder.Services.AddSingleton(_toolRegistry);
            builder.Services.AddSingleton(_settingsManager);

            _app = builder.Build();

            // Use CORS if enabled
            if (config.Web.EnableCors)
            {
                _app.UseCors("SaturnCors");
            }

            // Serve local static assets from wwwroot (e.g., SignalR client)
            _app.UseStaticFiles();

            _app.MapHub<ChatHub>("/chathub");
            _app.MapGet("/", async context =>
            {
                context.Response.ContentType = "text/html";
                // Generate a per-request nonce for CSP and inline resources
                var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));

                // Apply strict CSP via header with nonce; no external CDNs allowed
                var csp = $"default-src 'self'; " +
                          $"script-src 'self' 'nonce-{nonce}'; " +
                          $"style-src 'self' 'nonce-{nonce}'; " +
                          $"connect-src 'self' ws: wss:; " +
                          $"img-src 'self' data:; font-src 'self'; object-src 'none'; frame-src 'none'; base-uri 'self'";
                context.Response.Headers["Content-Security-Policy"] = csp;

                await context.Response.WriteAsync(GetHtmlContent(nonce));
            });

            // Bind URLs
            _app.Urls.Add($"http://localhost:{port}");
            if (config.Web.EnableHttps)
            {
                try
                {
                    _app.Urls.Add($"https://localhost:{port}");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "HTTPS was enabled in configuration but could not be bound. Falling back to HTTP only.");
                }
            }

            await _app.StartAsync();
            _logger.LogInformation("Web UI available at {Urls}", string.Join(", ", _app.Urls));
        }

        public async Task StopAsync()
        {
            if (_app != null)
            {
                await _app.StopAsync();
                await _app.DisposeAsync();
            }
        }

        private string GetHtmlContent(string nonce)
        {
            return @"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Saturn AI Assistant</title>
    <!-- SECURITY FIX: Remove external CDN dependency to prevent supply chain attacks -->
    <!-- SignalR will be served from local resources or self-hosted CDN -->
    <style nonce=""" + nonce + @""">
        * {
            margin: 0;
            padding: 0;
            box-sizing: border-box;
        }
        
        body {
            font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
            background: linear-gradient(135deg, #1a1a2e 0%, #2d3748 50%, #3a4a5e 100%);
            color: #f0f0f0;
            height: 100vh;
            overflow: hidden;
        }
        
        .container {
            display: grid;
            grid-template-columns: 300px 1fr;
            grid-template-rows: 60px 1fr;
            height: 100vh;
            gap: 1px;
            background: #333;
        }

        /* Mobile-first responsive design */
        @media (max-width: 768px) {
            .container {
                grid-template-columns: 1fr;
                grid-template-rows: 60px 1fr 200px;
            }

            .sidebar {
                order: 3;
                height: 200px;
                overflow-y: auto;
            }

            .chat-area {
                order: 2;
            }

            .header {
                order: 1;
            }

            .header h1 {
                font-size: 1.2rem;
            }

            .header-actions {
                gap: 5px;
            }

            .btn {
                padding: 6px 12px;
                font-size: 0.8rem;
            }
        }

        @media (max-width: 480px) {
            .container {
                grid-template-rows: 50px 1fr 180px;
            }

            .header {
                padding: 0 10px;
            }

            .header h1 {
                font-size: 1rem;
            }

            .btn {
                padding: 4px 8px;
                font-size: 0.7rem;
            }

            .sidebar {
                height: 180px;
            }

            .config-panel {
                padding: 10px;
            }

            .form-group {
                margin-bottom: 10px;
            }
        }
        
        .header {
            grid-column: 1 / -1;
            background: linear-gradient(135deg, #2d3748 0%, #1a202c 100%);
            display: flex;
            align-items: center;
            justify-content: space-between;
            padding: 0 20px;
            box-shadow: 0 2px 8px rgba(0,0,0,0.15);
            backdrop-filter: blur(10px);
            position: relative;
            border-bottom: 1px solid #4a5568;
        }

        .header::before {
            content: '';
            position: absolute;
            top: 0;
            left: 0;
            right: 0;
            height: 1px;
            background: linear-gradient(90deg, transparent, #7dd3fc, transparent);
        }

        .header h1 {
            color: white;
            font-size: 1.5rem;
            font-weight: 700;
            background: linear-gradient(135deg, #7dd3fc 0%, #38bdf8 100%);
            -webkit-background-clip: text;
            -webkit-text-fill-color: transparent;
            background-clip: text;
            display: flex;
            align-items: center;
            gap: 10px;
        }

        .header h1::before {
            content: '🪐';
            font-size: 1.2em;
            filter: drop-shadow(0 0 8px rgba(125, 211, 252, 0.5));
        }
        
        .header-actions {
            display: flex;
            gap: 10px;
            align-items: center;
        }
        
        .btn {
            background: rgba(255,255,255,0.1);
            border: 1px solid rgba(255,255,255,0.2);
            color: white;
            padding: 8px 16px;
            border-radius: 6px;
            cursor: pointer;
            transition: all 0.2s;
            font-size: 0.9rem;
        }
        
        .btn:hover {
            background: rgba(255,255,255,0.2);
            transform: translateY(-1px);
        }
        
        .btn.primary {
            background: #38a169;
            border-color: #38a169;
        }
        
        .btn.primary:hover {
            background: #2f855a;
        }
        
        .sidebar {
            background: #2a3441;
            border-right: 1px solid #4a5568;
            overflow-y: auto;
        }
        
        .status-panel, .tools-panel, .config-panel {
            margin: 15px;
            background: #3a4a5a;
            border-radius: 8px;
            padding: 15px;
            border: 1px solid #5a6978;
        }
        
        .status-panel h3, .tools-panel h3, .config-panel h3 {
            color: #7dd3fc;
            margin-bottom: 10px;
            font-size: 1rem;
            border-bottom: 1px solid #5a6978;
            padding-bottom: 5px;
        }
        
        .status-item {
            display: flex;
            justify-content: space-between;
            margin: 8px 0;
            font-size: 0.85rem;
        }
        
        .status-label {
            color: #b5c2d0;
        }
        
        .status-value {
            color: #f5f7fa;
            font-weight: 500;
        }
        
        .status-value.active {
            color: #68d391;
        }
        
        .tools-list {
            max-height: 200px;
            overflow-y: auto;
        }
        
        .tool-item {
            background: #5a6978;
            margin: 5px 0;
            padding: 8px;
            border-radius: 4px;
            border-left: 3px solid #7dd3fc;
        }
        
        .tool-name {
            font-weight: 600;
            color: #f5f7fa;
            font-size: 0.85rem;
        }
        
        .tool-desc {
            color: #b5c2d0;
            font-size: 0.75rem;
            margin-top: 2px;
        }
        
        .config-form {
            display: flex;
            flex-direction: column;
            gap: 12px;
        }
        
        .form-group {
            display: flex;
            flex-direction: column;
            gap: 5px;
        }
        
        .form-label {
            color: #b5c2d0;
            font-size: 0.85rem;
            font-weight: 500;
        }
        
        .form-input, .form-select {
            background: #5a6978;
            border: 1px solid #8a9bac;
            color: #f5f7fa;
            padding: 8px;
            border-radius: 4px;
            font-size: 0.85rem;
        }
        
        .form-input:focus, .form-select:focus {
            outline: none;
            border-color: #7dd3fc;
            box-shadow: 0 0 0 2px rgba(125, 211, 252, 0.2);
        }
        
        .form-checkbox {
            display: flex;
            align-items: center;
            gap: 8px;
        }
        
        .form-checkbox input {
            accent-color: #7dd3fc;
        }

        .form-help {
            font-size: 0.8rem;
            color: #a0aec0;
            margin-top: 4px;
            line-height: 1.3;
        }
        
        .chat-area {
            background: #2a3441;
            display: flex;
            flex-direction: column;
        }
        
        .messages {
            flex: 1;
            padding: 20px;
            overflow-y: auto;
            background: linear-gradient(135deg, #2a3441 0%, #3a4a5a 100%);
        }
        
        .message {
            margin: 15px 0;
            max-width: 80%;
            animation: fadeIn 0.3s ease-in;
        }
        
        @keyframes fadeIn {
            from { opacity: 0; transform: translateY(10px); }
            to { opacity: 1; transform: translateY(0); }
        }
        
        .message.user {
            align-self: flex-end;
            margin-left: auto;
        }
        
        .message.assistant {
            align-self: flex-start;
        }
        
        .message-content {
            padding: 16px 20px;
            border-radius: 16px;
            position: relative;
            word-wrap: break-word;
            line-height: 1.6;
            box-shadow: 0 2px 8px rgba(0,0,0,0.1);
            transition: all 0.2s ease;
        }

        .message:hover .message-content {
            transform: translateY(-1px);
            box-shadow: 0 4px 12px rgba(0,0,0,0.15);
        }

        .message.user .message-content {
            background: linear-gradient(135deg, #3182ce 0%, #2c5282 100%);
            color: white;
            border-bottom-right-radius: 6px;
        }

        .message.user .message-content::after {
            content: '';
            position: absolute;
            bottom: 0;
            right: -8px;
            width: 0;
            height: 0;
            border: 8px solid transparent;
            border-left-color: #2c5282;
            border-bottom: none;
        }

        .message.assistant .message-content {
            background: linear-gradient(135deg, #3a4a5a 0%, #2d3748 100%);
            color: #f5f7fa;
            border: 1px solid #4a5568;
            border-bottom-left-radius: 6px;
        }

        .message.assistant .message-content::after {
            content: '';
            position: absolute;
            bottom: 0;
            left: -8px;
            width: 0;
            height: 0;
            border: 8px solid transparent;
            border-right-color: #3a4a5a;
            border-bottom: none;
        }
        
        .input-area {
            padding: 20px;
            background: #3a4a5a;
            border-top: 1px solid #5a6978;
        }
        
        .input-container {
            display: flex;
            gap: 10px;
            align-items: center;
        }
        
        .message-input {
            flex: 1;
            background: #5a6978;
            border: 1px solid #8a9bac;
            color: #f5f7fa;
            padding: 12px 16px;
            border-radius: 25px;
            outline: none;
            font-size: 1rem;
            transition: all 0.2s;
        }
        
        .message-input:focus {
            border-color: #7dd3fc;
            box-shadow: 0 0 0 3px rgba(125, 211, 252, 0.2);
        }
        
        .send-btn {
            background: linear-gradient(135deg, #38a169 0%, #2f855a 100%);
            border: none;
            color: white;
            padding: 12px 20px;
            border-radius: 25px;
            cursor: pointer;
            font-weight: 600;
            transition: all 0.2s;
            min-width: 80px;
            min-height: 44px; /* Touch-friendly minimum */
        }

        .send-btn:hover {
            transform: translateY(-2px);
            box-shadow: 0 4px 12px rgba(56, 161, 105, 0.3);
        }

        .send-btn:disabled {
            background: #718096;
            cursor: not-allowed;
            transform: none;
            box-shadow: none;
        }

        /* Mobile input improvements */
        @media (max-width: 768px) {
            .input-area {
                padding: 15px;
            }

            .input-container {
                gap: 8px;
            }

            .message-input {
                font-size: 16px; /* Prevents zoom on iOS */
                padding: 14px 16px;
            }

            .send-btn {
                padding: 14px 18px;
                min-height: 48px;
                min-width: 48px;
            }
        }

        @media (max-width: 480px) {
            .input-area {
                padding: 12px;
            }

            .message-input {
                padding: 12px 14px;
                font-size: 16px;
            }

            .send-btn {
                padding: 12px 16px;
                font-size: 0.9rem;
            }
        }
        
        .connection-status {
            padding: 10px;
            text-align: center;
            background: #2c5282;
            color: white;
            font-size: 0.9rem;
        }
        
        .connection-status.connected {
            background: #38a169;
        }
        
        .connection-status.disconnected {
            background: #e53e3e;
        }
        
        .typing-indicator {
            display: none;
            margin: 10px 0;
            padding: 10px 15px;
            background: rgba(125, 211, 252, 0.1);
            border-left: 3px solid #7dd3fc;
            border-radius: 4px;
            color: #7dd3fc;
            font-style: italic;
            animation: pulse 1.5s infinite;
        }

        .typing-indicator.show {
            display: block;
        }

        @keyframes pulse {
            0%, 100% { opacity: 0.7; }
            50% { opacity: 1; }
        }

        /* Loading states */
        .loading-spinner {
            display: inline-block;
            width: 16px;
            height: 16px;
            border: 2px solid #4a5568;
            border-radius: 50%;
            border-top-color: #7dd3fc;
            animation: spin 1s ease-in-out infinite;
        }

        @keyframes spin {
            to { transform: rotate(360deg); }
        }

        .loading-text {
            display: flex;
            align-items: center;
            gap: 8px;
            color: #a0aec0;
            font-style: italic;
        }

        /* Toast notifications */
        .toast {
            position: fixed;
            top: 20px;
            right: 20px;
            background: #2d3748;
            color: white;
            padding: 12px 16px;
            border-radius: 8px;
            box-shadow: 0 4px 12px rgba(0,0,0,0.3);
            z-index: 1000;
            transform: translateX(100%);
            transition: transform 0.3s ease;
        }

        .toast.show {
            transform: translateX(0);
        }

        .toast.success {
            background: #38a169;
            border-left: 4px solid #68d391;
        }

        .toast.error {
            background: #e53e3e;
            border-left: 4px solid #fc8181;
        }

        .toast.warning {
            background: #d69e2e;
            border-left: 4px solid #f6e05e;
        }
        
        .config-actions {
            display: flex;
            gap: 8px;
            margin-top: 15px;
        }
        
        .btn-small {
            padding: 6px 12px;
            font-size: 0.8rem;
            border-radius: 4px;
        }
        
        .btn-success {
            background: #38a169;
            border-color: #38a169;
        }
        
        .btn-success:hover {
            background: #2f855a;
        }
        
        .alert {
            padding: 10px;
            border-radius: 4px;
            margin: 10px 0;
            display: none;
        }
        
        .alert.success {
            background: rgba(56, 161, 105, 0.1);
            border: 1px solid #38a169;
            color: #68d391;
        }
        
        .alert.error {
            background: rgba(229, 62, 62, 0.1);
            border: 1px solid #e53e3e;
            color: #fc8181;
        }
        
        .hidden {
            display: none !important;
        }
        
        /* Scrollbar styling */
        ::-webkit-scrollbar {
            width: 6px;
        }
        
        ::-webkit-scrollbar-track {
            background: #2d3748;
        }
        
        ::-webkit-scrollbar-thumb {
            background: #4a5568;
            border-radius: 3px;
        }
        
        ::-webkit-scrollbar-thumb:hover {
            background: #718096;
        }

        /* Accessibility improvements */
        .sr-only {
            position: absolute;
            width: 1px;
            height: 1px;
            padding: 0;
            margin: -1px;
            overflow: hidden;
            clip: rect(0, 0, 0, 0);
            white-space: nowrap;
            border: 0;
        }

        /* Focus indicators */
        *:focus {
            outline: 2px solid #7dd3fc;
            outline-offset: 2px;
        }

        .btn:focus,
        .form-input:focus,
        .form-select:focus,
        .message-input:focus,
        .send-btn:focus {
            outline: 2px solid #7dd3fc;
            outline-offset: 2px;
            box-shadow: 0 0 0 4px rgba(125, 211, 252, 0.2);
        }

        /* High contrast mode support */
        @media (prefers-contrast: high) {
            .container {
                background: #000;
            }

            .header {
                background: #000;
                border-bottom: 2px solid #fff;
            }

            .sidebar {
                background: #000;
                border-right: 2px solid #fff;
            }

            .chat-area {
                background: #000;
            }

            .message {
                border: 1px solid #fff;
            }
        }

        /* Reduced motion support */
        @media (prefers-reduced-motion: reduce) {
            * {
                animation-duration: 0.01ms !important;
                animation-iteration-count: 1 !important;
                transition-duration: 0.01ms !important;
            }
        }
    </style>
</head>
<body>
    <div class=""container"">
        <div class=""header"">
            <h1>🪐 Saturn AI Assistant</h1>
            <div class=""header-actions"">
                <button id=""toggleConfig"" class=""btn"">⚙️ Settings</button>
                <button id=""clearChat"" class=""btn"">🗑️ Clear</button>
                <div class=""connection-status"" id=""connectionStatus"">Connecting...</div>
            </div>
        </div>
        
        <div class=""sidebar"" role=""complementary"" aria-label=""Tools and Configuration"">
            <div class=""status-panel"" role=""region"" aria-labelledby=""status-heading"">
                <h3 id=""status-heading"">🤖 Agent Status</h3>
                <div id=""agentStatus"" aria-live=""polite"">
                    <div class=""status-item"">
                        <span class=""status-label"">Status:</span>
                        <span class=""status-value"">Loading...</span>
                    </div>
                </div>
            </div>

            <div class=""tools-panel"" role=""region"" aria-labelledby=""tools-heading"">
                <h3 id=""tools-heading"">🔧 Available Tools</h3>
                <div id=""toolsList"" class=""tools-list"" role=""list"">
                    <div class=""tool-item"" role=""listitem"">
                        <div class=""tool-name"">Loading...</div>
                        <div class=""tool-desc"">Fetching available tools...</div>
                    </div>
                </div>
            </div>
            
            <div class=""config-panel"" id=""configPanel"" role=""region"" aria-labelledby=""config-heading"">
                <h3 id=""config-heading"">⚙️ Configuration</h3>
                <div class=""alert success"" id=""configSuccess"" role=""status"" aria-live=""polite""></div>
                <div class=""alert error"" id=""configError"" role=""alert"" aria-live=""assertive""></div>
                <form class=""config-form"" id=""configForm"" aria-labelledby=""config-heading"">
                    <div class=""form-group"">
                        <label class=""form-label"" for=""apiKey"">API Key:</label>
                        <input type=""password"" id=""apiKey"" class=""form-input""
                               placeholder=""Enter OpenRouter API key...""
                               aria-describedby=""apiKey-help""
                               autocomplete=""off"">
                        <div id=""apiKey-help"" class=""form-help"">Your API key is encrypted and stored securely</div>
                    </div>
                    <div class=""form-group"">
                        <label class=""form-label"" for=""model"">Model:</label>
                        <select id=""model"" class=""form-select"" aria-describedby=""model-help"">
                            <option value="""">Loading models...</option>
                        </select>
                        <div id=""model-help"" class=""form-help"">Choose the AI model for responses</div>
                    </div>
                    <div class=""form-group"">
                        <label class=""form-label"" for=""temperature"">Temperature:</label>
                        <input type=""number"" id=""temperature"" class=""form-input""
                               min=""0"" max=""2"" step=""0.1"" placeholder=""0.7""
                               aria-describedby=""temperature-help"">
                        <div id=""temperature-help"" class=""form-help"">Controls randomness (0.0 = deterministic, 2.0 = very random)</div>
                    </div>
                    <div class=""form-group"">
                        <label class=""form-label"" for=""maxTokens"">Max Tokens:</label>
                        <input type=""number"" id=""maxTokens"" class=""form-input""
                               min=""1"" max=""4000"" placeholder=""2000""
                               aria-describedby=""maxTokens-help"">
                        <div id=""maxTokens-help"" class=""form-help"">Maximum length of the response</div>
                    </div>
                    <div class=""form-group"">
                        <div class=""form-checkbox"">
                            <input type=""checkbox"" id=""enableStreaming"" aria-describedby=""streaming-help"">
                            <label class=""form-label"" for=""enableStreaming"">Enable Streaming</label>
                        </div>
                        <div id=""streaming-help"" class=""form-help"">Show responses as they are generated</div>
                    </div>
                    <div class=""form-group"">
                        <div class=""form-checkbox"">
                            <input type=""checkbox"" id=""requireApproval"" aria-describedby=""approval-help"">
                            <label class=""form-label"" for=""requireApproval"">Require Command Approval</label>
                        </div>
                        <div id=""approval-help"" class=""form-help"">Ask for confirmation before executing commands</div>
                    </div>
                    <div class=""config-actions"">
                        <button type=""submit"" class=""btn btn-small btn-success"">💾 Save</button>
                        <button type=""button"" id=""refreshConfig"" class=""btn btn-small"">🔄 Refresh</button>
                    </div>
                </form>
            </div>
        </div>
        
        <div class=""chat-area"" role=""main"" aria-label=""Chat Interface"">
            <div class=""messages"" id=""messages"" role=""log"" aria-live=""polite"" aria-label=""Chat Messages"">
                <div class=""message assistant"" role=""article"" aria-label=""Assistant message"">
                    <div class=""message-content"">
                        Welcome to Saturn AI Assistant! How can I help you today?
                    </div>
                </div>
            </div>
            <div class=""typing-indicator"" id=""typingIndicator"" aria-live=""polite"" aria-hidden=""true"">Assistant is typing...</div>
            <div class=""input-area"" role=""region"" aria-label=""Message Input"">
                <div class=""input-container"">
                    <label for=""messageInput"" class=""sr-only"">Type your message</label>
                    <input type=""text"" id=""messageInput"" class=""message-input""
                           placeholder=""Type your message...""
                           autocomplete=""off""
                           aria-describedby=""input-help""
                           maxlength=""10000"">
                    <div id=""input-help"" class=""sr-only"">Press Enter to send, Shift+Enter for new line</div>
                    <button id=""sendButton"" class=""send-btn"" aria-label=""Send message"" type=""button"">
                        <span aria-hidden=""true"">Send</span>
                    </button>
                </div>
            </div>
        </div>
    </div>

    <!-- Load SignalR client from local static asset -->
    <script src=""/lib/signalr/signalr.min.js""></script>
    <script nonce=""" + nonce + @""">
        let connection;
        let isStreaming = false;
        let currentAssistantMessage = null;
        let configVisible = false;
        
        // Initialize SignalR connection
        async function initializeConnection() {
            connection = new signalR.HubConnectionBuilder()
                .withUrl(""/chathub"")
                .build();
                
            // Connection events
            connection.onclose(async () => {
                updateConnectionStatus('disconnected');
                await new Promise(resolve => setTimeout(resolve, 5000));
                await startConnection();
            });
            
            // Message handlers
            connection.on(""Connected"", function (message) {
                updateConnectionStatus('connected');
                console.log(message);
            });
            
            connection.on(""UserMessage"", function (message) {
                addMessage('user', message);
            });
            
            connection.on(""AssistantMessage"", function (message) {
                addMessage('assistant', message);
            });
            
            connection.on(""AssistantChunk"", function (chunk) {
                if (!isStreaming) {
                    isStreaming = true;
                    currentAssistantMessage = addMessage('assistant', '');
                    showTypingIndicator(false);
                }
                if (currentAssistantMessage) {
                    currentAssistantMessage.textContent += chunk;
                    scrollToBottom();
                }
            });
            
            connection.on(""MessageComplete"", function () {
                isStreaming = false;
                currentAssistantMessage = null;
                enableInput(true);
            });
            
            connection.on(""Error"", function (error) {
                addMessage('assistant', '❌ Error: ' + error);
                enableInput(true);
                showTypingIndicator(false);
                isStreaming = false;
                currentAssistantMessage = null;
            });
            
            connection.on(""AgentStatus"", function (statusJson) {
                updateAgentStatus(JSON.parse(statusJson));
            });
            
            connection.on(""AvailableTools"", function (toolsJson) {
                updateToolsList(JSON.parse(toolsJson));
            });
            
            connection.on(""Configuration"", function (configJson) {
                updateConfigForm(JSON.parse(configJson));
            });
            
            connection.on(""ConfigurationUpdated"", function (message) {
                showConfigAlert('success', message);
            });
            
            await startConnection();
        }
        
        async function startConnection() {
            try {
                await connection.start();
                updateConnectionStatus('connected');
                console.log(""SignalR Connected"");
            } catch (err) {
                console.error('SignalR Connection Error:', err);
                updateConnectionStatus('disconnected');
                setTimeout(startConnection, 5000);
            }
        }
        
        function updateConnectionStatus(status) {
            const statusElement = document.getElementById('connectionStatus');
            statusElement.className = 'connection-status ' + status;
            statusElement.textContent = status === 'connected' ? 'Connected' : 
                                      status === 'disconnected' ? 'Disconnected' : 'Connecting...';
        }
        
        function addMessage(sender, content) {
            const messagesContainer = document.getElementById('messages');
            const messageDiv = document.createElement('div');
            messageDiv.className = 'message ' + sender;
            messageDiv.setAttribute('role', 'article');
            messageDiv.setAttribute('aria-label', `${sender} message`);

            const contentDiv = document.createElement('div');
            contentDiv.className = 'message-content';

            // SECURITY FIX: Improved XSS prevention with comprehensive sanitization
            const sanitizedContent = sanitizeHtml(content);
            contentDiv.textContent = sanitizedContent;

            messageDiv.appendChild(contentDiv);

            // Use requestAnimationFrame for smooth rendering
            requestAnimationFrame(() => {
                messagesContainer.appendChild(messageDiv);
                scrollToBottom();
            });

            // Limit message history for performance (keep last 100 messages)
            const maxMessages = 100;
            const messageElements = messagesContainer.querySelectorAll('.message');
            if (messageElements.length > maxMessages) {
                const messagesToRemove = messageElements.length - maxMessages;
                for (let i = 0; i < messagesToRemove; i++) {
                    messagesContainer.removeChild(messageElements[i]);
                }
            }

            return contentDiv;
        }
        
        function scrollToBottom() {
            const messages = document.getElementById('messages');
            // Use smooth scrolling with performance optimization
            if (messages.scrollHeight - messages.scrollTop - messages.clientHeight < 100) {
                messages.scrollTo({
                    top: messages.scrollHeight,
                    behavior: 'smooth'
                });
            } else {
                // Jump scroll if user has scrolled far up
                messages.scrollTop = messages.scrollHeight;
            }
        }
        
        function enableInput(enabled) {
            const input = document.getElementById('messageInput');
            const button = document.getElementById('sendButton');
            input.disabled = !enabled;
            button.disabled = !enabled;

            if (enabled) {
                button.innerHTML = 'Send';
                input.focus();
            }
        }
        
        function showTypingIndicator(show) {
            const indicator = document.getElementById('typingIndicator');
            if (show) {
                indicator.classList.add('show');
                indicator.setAttribute('aria-hidden', 'false');
            } else {
                indicator.classList.remove('show');
                indicator.setAttribute('aria-hidden', 'true');
            }
        }

        function showToast(message, type = 'info') {
            const toast = document.createElement('div');
            toast.className = `toast ${type}`;
            toast.textContent = message;
            toast.setAttribute('role', 'alert');

            document.body.appendChild(toast);

            // Trigger animation
            setTimeout(() => toast.classList.add('show'), 100);

            // Auto-remove after 5 seconds
            setTimeout(() => {
                toast.classList.remove('show');
                setTimeout(() => {
                    if (document.body.contains(toast)) {
                        document.body.removeChild(toast);
                    }
                }, 300);
            }, 5000);
        }
        
        async function sendMessage() {
            const input = document.getElementById('messageInput');
            const sendButton = document.getElementById('sendButton');
            const message = input.value.trim();

            if (!message || !connection) return;

            // Add user message to chat
            addMessage('user', message);
            input.value = '';

            // Update UI state
            enableInput(false);
            sendButton.innerHTML = '<span class=""loading-spinner""></span> Sending...';
            showTypingIndicator(true);

            try {
                await connection.invoke(""SendMessage"", message);
            } catch (err) {
                console.error('Send Error:', err);
                addMessage('assistant', '❌ Failed to send message: ' + err.message);
                showToast('Failed to send message. Please try again.', 'error');
                enableInput(true);
                showTypingIndicator(false);
                sendButton.innerHTML = 'Send';
            }
        }
        
        function updateAgentStatus(status) {
            const statusContainer = document.getElementById('agentStatus');
            statusContainer.innerHTML = Object.entries(status).map(([key, value]) => 
                '<div class=""status-item"">' +
                    '<span class=""status-label"">' + formatLabel(key) + ':</span>' +
                    '<span class=""status-value ' + (value === 'Active' ? 'active' : '') + '"">' + value + '</span>' +
                '</div>'
            ).join('');
        }
        
        function updateToolsList(tools) {
            const toolsContainer = document.getElementById('toolsList');
            if (tools.length === 0) {
                toolsContainer.innerHTML = '<div class=""tool-item""><div class=""tool-name"">No tools available</div></div>';
                return;
            }
            
            toolsContainer.innerHTML = tools.map(tool => 
                '<div class=""tool-item"">' +
                    '<div class=""tool-name"">' + tool.Name + '</div>' +
                    '<div class=""tool-desc"">' + tool.Description + '</div>' +
                '</div>'
            ).join('');
        }
        
        // SECURITY FIX: Add comprehensive XSS sanitization function
        function sanitizeHtml(content) {
            // Create a temporary element to parse and sanitize HTML
            const tempDiv = document.createElement('div');
            // Set textContent to prevent script execution
            tempDiv.textContent = content;
            // Return the safely escaped text content
            return tempDiv.innerHTML
                .replace(/&lt;script[^&]*&gt;.*?&lt;\/script&gt;/gi, '')
                .replace(/javascript:/gi, '')
                .replace(/on\w+\s*=/gi, '')
                .replace(/&lt;iframe[^&]*&gt;.*?&lt;\/iframe&gt;/gi, '')
                .replace(/&lt;object[^&]*&gt;.*?&lt;\/object&gt;/gi, '')
                .replace(/&lt;embed[^&]*&gt;.*?&lt;\/embed&gt;/gi, '');
        }

        function updateConfigForm(config) {
            // SECURITY FIX: Never expose API key in placeholder - use masked placeholder instead
            if (config.ApiKey && config.ApiKey !== 'null') {
                document.getElementById('apiKey').placeholder = '••••••••••••••••';
            }
            
            // Populate model dropdown
            const modelSelect = document.getElementById('model');
            modelSelect.innerHTML = config.AvailableModels.map(model =>
                '<option value=""' + model + '""' + (model === config.Model ? ' selected' : '') + '>' + model + '</option>'
            ).join('');
            
            document.getElementById('temperature').value = config.Temperature || '';
            document.getElementById('maxTokens').value = config.MaxTokens || '';
            document.getElementById('enableStreaming').checked = config.EnableStreaming || false;
            document.getElementById('requireApproval').checked = config.RequireCommandApproval || false;
        }
        
        function showConfigAlert(type, message) {
            const alertId = type === 'success' ? 'configSuccess' : 'configError';
            const alert = document.getElementById(alertId);
            alert.textContent = message;
            alert.style.display = 'block';
            
            setTimeout(() => {
                alert.style.display = 'none';
            }, 3000);
        }
        
        function formatLabel(key) {
            return key.replace(/([A-Z])/g, ' $1').replace(/^./, str => str.toUpperCase());
        }
        
        // Event listeners
        document.getElementById('messageInput').addEventListener('keypress', function(e) {
            if (e.key === 'Enter' && !e.shiftKey) {
                e.preventDefault();
                sendMessage();
            }
        });

        // Global keyboard shortcuts
        document.addEventListener('keydown', function(e) {
            // Ctrl/Cmd + Enter to send message from anywhere
            if ((e.ctrlKey || e.metaKey) && e.key === 'Enter') {
                e.preventDefault();
                sendMessage();
            }

            // Escape to focus input
            if (e.key === 'Escape') {
                document.getElementById('messageInput').focus();
            }

            // Ctrl/Cmd + L to clear chat
            if ((e.ctrlKey || e.metaKey) && e.key === 'l') {
                e.preventDefault();
                document.getElementById('clearChat').click();
            }
        });

        document.getElementById('sendButton').addEventListener('click', sendMessage);
        
        document.getElementById('clearChat').addEventListener('click', function() {
            const messages = document.getElementById('messages');
            messages.innerHTML = 
                '<div class=""message assistant"">' +
                    '<div class=""message-content"">' +
                        'Chat cleared. How can I help you?' +
                    '</div>' +
                '</div>';
        });
        
        document.getElementById('toggleConfig').addEventListener('click', function() {
            const configPanel = document.getElementById('configPanel');
            configVisible = !configVisible;
            configPanel.style.display = configVisible ? 'block' : 'none';
            this.textContent = configVisible ? '⚙️ Hide Settings' : '⚙️ Settings';
        });
        
        document.getElementById('configForm').addEventListener('submit', async function(e) {
            e.preventDefault();
            
            const configData = {
                apiKey: document.getElementById('apiKey').value,
                model: document.getElementById('model').value,
                temperature: parseFloat(document.getElementById('temperature').value) || undefined,
                maxTokens: parseInt(document.getElementById('maxTokens').value) || undefined,
                enableStreaming: document.getElementById('enableStreaming').checked,
                requireCommandApproval: document.getElementById('requireApproval').checked
            };
            
            // Remove empty values
            Object.keys(configData).forEach(key => {
                if (configData[key] === '' || configData[key] === undefined) {
                    delete configData[key];
                }
            });
            
            try {
                await connection.invoke('UpdateConfiguration', JSON.stringify(configData));
            } catch (err) {
                showConfigAlert('error', 'Failed to save configuration: ' + err.message);
            }
        });
        
        document.getElementById('refreshConfig').addEventListener('click', async function() {
            try {
                await connection.invoke('GetConfiguration');
            } catch (err) {
                showConfigAlert('error', 'Failed to refresh configuration: ' + err.message);
            }
        });
        
        // Initialize when page loads
        document.addEventListener('DOMContentLoaded', function() {
            initializeConnection();
            
            // Hide config panel by default
            document.getElementById('configPanel').style.display = 'none';
        });
    </script>
</body>
</html>";
        }
    }
}