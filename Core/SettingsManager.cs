using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Saturn.Core.Security;

namespace Saturn.Core;

public class SettingsManager
{
    private readonly string _settingsPath;
    private readonly Dictionary<string, object> _settings;
    private readonly object _lock = new();
    
    public SettingsManager(string? workspacePath = null)
    {
        var saturnDir = workspacePath != null 
            ? Path.Combine(workspacePath, ".saturn")
            : Path.Combine(Environment.CurrentDirectory, ".saturn");
        
        if (!Directory.Exists(saturnDir))
        {
            Directory.CreateDirectory(saturnDir);
        }
        
        _settingsPath = Path.Combine(saturnDir, "settings.json");
        _settings = LoadSettings();
    }
    
    private Dictionary<string, object> LoadSettings()
    {
        if (!File.Exists(_settingsPath))
        {
            return new Dictionary<string, object>();
        }
        
        try
        {
            var json = File.ReadAllText(_settingsPath);
            return JsonSerializer.Deserialize<Dictionary<string, object>>(json) 
                   ?? new Dictionary<string, object>();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Failed to load settings: {ex.Message}");
            return new Dictionary<string, object>();
        }
    }
    
    private void SaveSettings()
    {
        try
        {
            var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions 
            { 
                WriteIndented = true 
            });
            File.WriteAllText(_settingsPath, json);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to save settings", ex);
        }
    }
    
    /// <summary>
    /// Sets a secure value (will be encrypted)
    /// </summary>
    public void SetSecureValue(string key, string value)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Key cannot be null or empty", nameof(key));
        
        lock (_lock)
        {
            var encryptedValue = SecureStorage.EncryptString(value ?? string.Empty);
            _settings[$"secure_{key}"] = encryptedValue;
            SaveSettings();
        }
    }
    
    /// <summary>
    /// Gets a secure value (will be decrypted)
    /// </summary>
    public string? GetSecureValue(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return null;
        
        lock (_lock)
        {
            var secureKey = $"secure_{key}";
            if (!_settings.TryGetValue(secureKey, out var encryptedValue))
                return null;
            
            if (encryptedValue is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.String)
            {
                var encryptedString = jsonElement.GetString();
                return string.IsNullOrEmpty(encryptedString) 
                    ? null 
                    : SecureStorage.DecryptString(encryptedString);
            }
            
            var encryptedStr = encryptedValue?.ToString();
            return string.IsNullOrEmpty(encryptedStr) 
                ? null 
                : SecureStorage.DecryptString(encryptedStr);
        }
    }
    
    /// <summary>
    /// Sets a regular (non-encrypted) value
    /// </summary>
    public void SetValue(string key, object value)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Key cannot be null or empty", nameof(key));
        
        lock (_lock)
        {
            _settings[key] = value;
            SaveSettings();
        }
    }
    
    /// <summary>
    /// Gets a regular (non-encrypted) value
    /// </summary>
    public T? GetValue<T>(string key, T? defaultValue = default)
    {
        if (string.IsNullOrWhiteSpace(key))
            return defaultValue;
        
        lock (_lock)
        {
            if (!_settings.TryGetValue(key, out var value))
                return defaultValue;
            
            if (value is JsonElement jsonElement)
            {
                return JsonSerializer.Deserialize<T>(jsonElement.GetRawText());
            }
            
            if (value is T directValue)
                return directValue;
            
            try
            {
                return (T)Convert.ChangeType(value, typeof(T));
            }
            catch
            {
                return defaultValue;
            }
        }
    }
    
    /// <summary>
    /// Removes a setting
    /// </summary>
    public bool RemoveValue(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return false;
        
        lock (_lock)
        {
            var removed = _settings.Remove(key) || _settings.Remove($"secure_{key}");
            if (removed)
            {
                SaveSettings();
            }
            return removed;
        }
    }
    
    /// <summary>
    /// Checks if a setting exists
    /// </summary>
    public bool HasValue(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return false;
        
        lock (_lock)
        {
            return _settings.ContainsKey(key) || _settings.ContainsKey($"secure_{key}");
        }
    }
    
    /// <summary>
    /// Migrates plain text API keys to encrypted storage
    /// </summary>
    public void MigratePlainTextApiKey(string plainTextKey)
    {
        if (string.IsNullOrEmpty(plainTextKey))
            return;
        
        // Remove from environment variables for security
        Environment.SetEnvironmentVariable("OPENROUTER_API_KEY", null);
        
        // Store securely
        SetSecureValue("openrouter_api_key", plainTextKey);
        
        Console.WriteLine("API key has been migrated to secure encrypted storage.");
    }
    
    /// <summary>
    /// Gets the OpenRouter API key from secure storage
    /// </summary>
    public string? GetOpenRouterApiKey()
    {
        return GetSecureValue("openrouter_api_key");
    }
    
    /// <summary>
    /// Sets the OpenRouter API key in secure storage
    /// </summary>
    public void SetOpenRouterApiKey(string apiKey)
    {
        SetSecureValue("openrouter_api_key", apiKey);
    }

    // Async helpers for compatibility with web/config components
    public Task<string> GetApiKeyAsync()
    {
        return Task.FromResult(GetOpenRouterApiKey() ?? string.Empty);
    }

    public Task SetApiKeyAsync(string apiKey)
    {
        SetOpenRouterApiKey(apiKey);
        return Task.CompletedTask;
    }

    public Task SetModelAsync(string model)
    {
        SetValue("default_model", model);
        return Task.CompletedTask;
    }

    public Task<SaturnSettings> LoadSettingsAsync()
    {
        var s = new SaturnSettings();
        lock (_lock)
        {
            s.OpenRouterApiKey = GetOpenRouterApiKey();
            s.DefaultModel = GetValue<string>("default_model", GetValue<string>("DefaultModel", null));
            s.Temperature = GetValue<double?>("temperature", GetValue<double?>("Temperature", null));
            s.MaxTokens = GetValue<int?>("max_tokens", GetValue<int?>("MaxTokens", null));
            s.EnableStreaming = GetValue<bool?>("enable_streaming", GetValue<bool?>("EnableStreaming", null));
            s.RequireCommandApproval = GetValue<bool?>("require_command_approval", GetValue<bool?>("RequireCommandApproval", null));
        }
        return Task.FromResult(s);
    }

    public Task SaveSettingsAsync(SaturnSettings settings)
    {
        if (settings == null) return Task.CompletedTask;

        lock (_lock)
        {
            if (!string.IsNullOrWhiteSpace(settings.DefaultModel))
                _settings["default_model"] = settings.DefaultModel!;
            if (settings.Temperature.HasValue)
                _settings["temperature"] = settings.Temperature.Value;
            if (settings.MaxTokens.HasValue)
                _settings["max_tokens"] = settings.MaxTokens.Value;
            if (settings.EnableStreaming.HasValue)
                _settings["enable_streaming"] = settings.EnableStreaming.Value;
            if (settings.RequireCommandApproval.HasValue)
                _settings["require_command_approval"] = settings.RequireCommandApproval.Value;

            SaveSettings();
        }

        if (!string.IsNullOrWhiteSpace(settings.OpenRouterApiKey))
        {
            SetOpenRouterApiKey(settings.OpenRouterApiKey!);
        }

        return Task.CompletedTask;
    }
// Backward-compatible async helpers wrapping existing sync APIs

    // Secure value async wrappers
    public Task<string?> GetSecureValueAsync(string key)
    {
        return Task.FromResult(GetSecureValue(key));
    }

    public Task SetSecureValueAsync(string key, string value)
    {
        SetSecureValue(key, value);
        return Task.CompletedTask;
    }

    // Regular value async wrappers (generic and common string overloads)
    public Task<T?> GetValueAsync<T>(string key, T? defaultValue = default)
    {
        return Task.FromResult(GetValue<T>(key, defaultValue));
    }

    public Task<string?> GetValueAsync(string key)
    {
        return Task.FromResult(GetValue<string>(key, null));
    }

    public Task SetValueAsync(string key, object value)
    {
        SetValue(key, value);
        return Task.CompletedTask;
    }

    public Task SetValueAsync(string key, string value)
    {
        SetValue(key, value);
        return Task.CompletedTask;
    }

    // OpenRouter API key async wrappers
    public Task<string?> GetOpenRouterApiKeyAsync()
    {
        return Task.FromResult(GetOpenRouterApiKey());
    }

    public Task SetOpenRouterApiKeyAsync(string apiKey)
    {
        SetOpenRouterApiKey(apiKey);
        return Task.CompletedTask;
    }

    // Settings DTO mapping helpers (sync)
    public SaturnSettings GetSettings()
    {
        var s = new SaturnSettings();
        lock (_lock)
        {
            s.OpenRouterApiKey = GetOpenRouterApiKey();
            s.DefaultModel = GetValue<string>("default_model", GetValue<string>("DefaultModel", null));
            s.Temperature = GetValue<double?>("temperature", GetValue<double?>("Temperature", null));
            s.MaxTokens = GetValue<int?>("max_tokens", GetValue<int?>("MaxTokens", null));
            s.EnableStreaming = GetValue<bool?>("enable_streaming", GetValue<bool?>("EnableStreaming", null));
            s.RequireCommandApproval = GetValue<bool?>("require_command_approval", GetValue<bool?>("RequireCommandApproval", null));
        }
        return s;
    }

    public void UpdateSettings(SaturnSettings settings)
    {
        if (settings == null) return;

        lock (_lock)
        {
            if (!string.IsNullOrWhiteSpace(settings.DefaultModel))
                _settings["default_model"] = settings.DefaultModel!;
            if (settings.Temperature.HasValue)
                _settings["temperature"] = settings.Temperature.Value;
            if (settings.MaxTokens.HasValue)
                _settings["max_tokens"] = settings.MaxTokens.Value;
            if (settings.EnableStreaming.HasValue)
                _settings["enable_streaming"] = settings.EnableStreaming.Value;
            if (settings.RequireCommandApproval.HasValue)
                _settings["require_command_approval"] = settings.RequireCommandApproval.Value;

            SaveSettings();
        }

        if (!string.IsNullOrWhiteSpace(settings.OpenRouterApiKey))
        {
            SetOpenRouterApiKey(settings.OpenRouterApiKey!);
        }
    }

    // Settings DTO mapping helpers (async)
    public Task<SaturnSettings> GetSettingsAsync()
    {
        return Task.FromResult(GetSettings());
    }

    public Task UpdateSettingsAsync(SaturnSettings settings)
    {
        UpdateSettings(settings);
        return Task.CompletedTask;
    }
}

public class SaturnSettings
{
    public string? OpenRouterApiKey { get; set; }
    public string? DefaultModel { get; set; }
    public double? Temperature { get; set; }
    public int? MaxTokens { get; set; }
    public bool? EnableStreaming { get; set; }
    public bool? RequireCommandApproval { get; set; }
}