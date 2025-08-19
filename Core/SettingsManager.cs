using System;
using System.IO;
using System.Text.Json;
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
}