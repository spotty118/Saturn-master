using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Saturn.Agents;
using Saturn.Tools.Core;
using Saturn.Core;

namespace Saturn.Web
{
    public class WebServer
    {
        private readonly Agent _agent;
        private readonly ToolRegistry _toolRegistry;
        private readonly SettingsManager _settingsManager;
        private WebApplication? _app;

        public WebServer(Agent agent, ToolRegistry toolRegistry, SettingsManager settingsManager)
        {
            _agent = agent;
            _toolRegistry = toolRegistry;
            _settingsManager = settingsManager;
        }

        public async Task StartAsync(int port = 5173)
        {
            var builder = WebApplication.CreateBuilder();
            
            builder.Services.AddSignalR();
            builder.Services.AddSingleton(_agent);
            builder.Services.AddSingleton(_toolRegistry);
            builder.Services.AddSingleton(_settingsManager);

            _app = builder.Build();

            _app.MapHub<ChatHub>("/chathub");
            _app.MapGet("/", async context =>
            {
                context.Response.ContentType = "text/html";
                await context.Response.WriteAsync(GetHtmlContent());
            });

            _app.Urls.Add($"http://localhost:{port}");
            await _app.StartAsync();
            Console.WriteLine($"Web UI available at http://localhost:{port}");
        }

        public async Task StopAsync()
        {
            if (_app != null)
            {
                await _app.StopAsync();
                await _app.DisposeAsync();
            }
        }

        private string GetHtmlContent()
        {
            return @"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <meta http-equiv=""Content-Security-Policy"" content=""default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; connect-src 'self'; img-src 'self' data:; font-src 'self'; object-src 'none'; frame-src 'none'; base-uri 'self';"">
    <title>Saturn AI Assistant</title>
    <!-- SECURITY FIX: Remove external CDN dependency to prevent supply chain attacks -->
    <!-- SignalR will be served from local resources or self-hosted CDN -->
    <style>
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
        
        .header {
            grid-column: 1 / -1;
            background: linear-gradient(90deg, #2c5282 0%, #3182ce 100%);
            display: flex;
            align-items: center;
            justify-content: space-between;
            padding: 0 20px;
            box-shadow: 0 2px 10px rgba(0,0,0,0.3);
        }
        
        .header h1 {
            color: white;
            font-size: 1.5rem;
            font-weight: 600;
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
            padding: 12px 16px;
            border-radius: 12px;
            position: relative;
            word-wrap: break-word;
        }
        
        .message.user .message-content {
            background: linear-gradient(135deg, #3182ce 0%, #2c5282 100%);
            color: white;
            border-bottom-right-radius: 4px;
        }
        
        .message.assistant .message-content {
            background: #3a4a5a;
            color: #f5f7fa;
            border: 1px solid #5a6978;
            border-bottom-left-radius: 4px;
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
            color: #a0aec0;
            font-style: italic;
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
        
        <div class=""sidebar"">
            <div class=""status-panel"">
                <h3>🤖 Agent Status</h3>
                <div id=""agentStatus"">
                    <div class=""status-item"">
                        <span class=""status-label"">Status:</span>
                        <span class=""status-value"">Loading...</span>
                    </div>
                </div>
            </div>
            
            <div class=""tools-panel"">
                <h3>🔧 Available Tools</h3>
                <div id=""toolsList"" class=""tools-list"">
                    <div class=""tool-item"">
                        <div class=""tool-name"">Loading...</div>
                        <div class=""tool-desc"">Fetching available tools...</div>
                    </div>
                </div>
            </div>
            
            <div class=""config-panel"" id=""configPanel"">
                <h3>⚙️ Configuration</h3>
                <div class=""alert success"" id=""configSuccess""></div>
                <div class=""alert error"" id=""configError""></div>
                <form class=""config-form"" id=""configForm"">
                    <div class=""form-group"">
                        <label class=""form-label"">API Key:</label>
                        <input type=""password"" id=""apiKey"" class=""form-input"" placeholder=""Enter OpenRouter API key..."">
                    </div>
                    <div class=""form-group"">
                        <label class=""form-label"">Model:</label>
                        <select id=""model"" class=""form-select"">
                            <option value="""">Loading models...</option>
                        </select>
                    </div>
                    <div class=""form-group"">
                        <label class=""form-label"">Temperature:</label>
                        <input type=""number"" id=""temperature"" class=""form-input"" min=""0"" max=""2"" step=""0.1"" placeholder=""0.7"">
                    </div>
                    <div class=""form-group"">
                        <label class=""form-label"">Max Tokens:</label>
                        <input type=""number"" id=""maxTokens"" class=""form-input"" min=""1"" max=""4000"" placeholder=""2000"">
                    </div>
                    <div class=""form-group"">
                        <div class=""form-checkbox"">
                            <input type=""checkbox"" id=""enableStreaming"">
                            <label class=""form-label"">Enable Streaming</label>
                        </div>
                    </div>
                    <div class=""form-group"">
                        <div class=""form-checkbox"">
                            <input type=""checkbox"" id=""requireApproval"">
                            <label class=""form-label"">Require Command Approval</label>
                        </div>
                    </div>
                    <div class=""config-actions"">
                        <button type=""submit"" class=""btn btn-small btn-success"">💾 Save</button>
                        <button type=""button"" id=""refreshConfig"" class=""btn btn-small"">🔄 Refresh</button>
                    </div>
                </form>
            </div>
        </div>
        
        <div class=""chat-area"">
            <div class=""messages"" id=""messages"">
                <div class=""message assistant"">
                    <div class=""message-content"">
                        Welcome to Saturn AI Assistant! How can I help you today?
                    </div>
                </div>
            </div>
            <div class=""typing-indicator"" id=""typingIndicator"">Assistant is typing...</div>
            <div class=""input-area"">
                <div class=""input-container"">
                    <input type=""text"" id=""messageInput"" class=""message-input"" placeholder=""Type your message..."" autocomplete=""off"">
                    <button id=""sendButton"" class=""send-btn"">Send</button>
                </div>
            </div>
        </div>
    </div>

    <script>
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
                addMessage('assistant', `❌ Error: ${error}`);
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
            statusElement.className = `connection-status ${status}`;
            statusElement.textContent = status === 'connected' ? 'Connected' : 
                                      status === 'disconnected' ? 'Disconnected' : 'Connecting...';
        }
        
        function addMessage(sender, content) {
            const messagesContainer = document.getElementById('messages');
            const messageDiv = document.createElement('div');
            messageDiv.className = `message ${sender}`;
            
            const contentDiv = document.createElement('div');
            contentDiv.className = 'message-content';
            
            // SECURITY FIX: Improved XSS prevention with comprehensive sanitization
            const sanitizedContent = sanitizeHtml(content);
            contentDiv.textContent = sanitizedContent;
            
            messageDiv.appendChild(contentDiv);
            messagesContainer.appendChild(messageDiv);
            
            scrollToBottom();
            return contentDiv;
        }
        
        function scrollToBottom() {
            const messages = document.getElementById('messages');
            messages.scrollTop = messages.scrollHeight;
        }
        
        function enableInput(enabled) {
            const input = document.getElementById('messageInput');
            const button = document.getElementById('sendButton');
            input.disabled = !enabled;
            button.disabled = !enabled;
        }
        
        function showTypingIndicator(show) {
            const indicator = document.getElementById('typingIndicator');
            indicator.style.display = show ? 'block' : 'none';
        }
        
        async function sendMessage() {
            const input = document.getElementById('messageInput');
            const message = input.value.trim();
            
            if (!message || !connection) return;
            
            input.value = '';
            enableInput(false);
            showTypingIndicator(true);
            
            try {
                await connection.invoke(""SendMessage"", message);
            } catch (err) {
                console.error('Send Error:', err);
                addMessage('assistant', `❌ Failed to send message: ${err.message}`);
                enableInput(true);
                showTypingIndicator(false);
            }
        }
        
        function updateAgentStatus(status) {
            const statusContainer = document.getElementById('agentStatus');
            statusContainer.innerHTML = Object.entries(status).map(([key, value]) => 
                `<div class=""status-item"">
                    <span class=""status-label"">${formatLabel(key)}:</span>
                    <span class=""status-value ${value === 'Active' ? 'active' : ''}"">${value}</span>
                </div>`
            ).join('');
        }
        
        function updateToolsList(tools) {
            const toolsContainer = document.getElementById('toolsList');
            if (tools.length === 0) {
                toolsContainer.innerHTML = '<div class=""tool-item""><div class=""tool-name"">No tools available</div></div>';
                return;
            }
            
            toolsContainer.innerHTML = tools.map(tool => 
                `<div class=""tool-item"">
                    <div class=""tool-name"">${tool.Name}</div>
                    <div class=""tool-desc"">${tool.Description}</div>
                </div>`
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
                `<option value=""${model}"" ${model === config.Model ? 'selected' : ''}>${model}</option>`
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
        
        document.getElementById('sendButton').addEventListener('click', sendMessage);
        
        document.getElementById('clearChat').addEventListener('click', function() {
            const messages = document.getElementById('messages');
            messages.innerHTML = `
                <div class=""message assistant"">
                    <div class=""message-content"">
                        Chat cleared. How can I help you?
                    </div>
                </div>
            `;
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
                showConfigAlert('error', `Failed to save configuration: ${err.message}`);
            }
        });
        
        document.getElementById('refreshConfig').addEventListener('click', async function() {
            try {
                await connection.invoke('GetConfiguration');
            } catch (err) {
                showConfigAlert('error', `Failed to refresh configuration: ${err.message}`);
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