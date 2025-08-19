using System;

namespace Saturn.Core.Constants
{
    /// <summary>
    /// Application-wide constants to eliminate magic numbers and improve maintainability
    /// </summary>
    public static class ApplicationConstants
    {
        /// <summary>
        /// Default network and server configuration
        /// </summary>
        public static class Network
        {
            public const int DefaultWebPort = 5173;
            public const int MinValidPort = 1024;
            public const int MaxValidPort = 65535;
            public const int DefaultTimeoutSeconds = 30;
        }

        /// <summary>
        /// File and I/O operation limits
        /// </summary>
        public static class FileOperations
        {
            public const long MaxFileSize = 50 * 1024 * 1024; // 50MB
            public const int MaxOutputLength = 1048576; // 1MB
            public const int DefaultMaxResults = 1000;
            public const int DefaultMaxFiles = 100;
        }

        /// <summary>
        /// Security and encryption settings
        /// </summary>
        public static class Security
        {
            public const int SaltSize = 16;
            public const int KeySize = 32; // 256 bits
            public const int IvSize = 16;  // 128 bits
            public const int Pbkdf2Iterations = 100000;
            public const int MinApiKeyLength = 20;
        }

        /// <summary>
        /// Database and history management
        /// </summary>
        public static class Database
        {
            public const int DefaultMaxHistorySize = 1000;
            public const int DefaultSessionLimit = 100;
            public const int DefaultMaxHistoryMessages = 20;
        }

        /// <summary>
        /// Agent and AI configuration
        /// </summary>
        public static class Agent
        {
            public const string DefaultModel = "anthropic/claude-sonnet-4";
            public const double DefaultTemperature = 0.7;
            public const int DefaultMaxTokens = 4096;
            public const double DefaultTopP = 1.0;
        }

        /// <summary>
        /// Tool execution limits
        /// </summary>
        public static class Tools
        {
            public const int DefaultCommandTimeoutSeconds = 30;
            public const int MaxCommandTimeoutSeconds = 300; // 5 minutes
            public const int DefaultMaxDepth = 10;
            public const int LineNumberPadding = 6;
        }

        /// <summary>
        /// Validation and input limits
        /// </summary>
        public static class Validation
        {
            public const int MaxInputLength = 1000000; // 1MB
            public const int MaxPathLength = 260; // Windows MAX_PATH
            public const int MaxUsernameLength = 256;
        }
    }
}
