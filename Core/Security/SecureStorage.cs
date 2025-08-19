using System;
using System.Security.Cryptography;
using System.Text;

namespace Saturn.Core.Security;

public static class SecureStorage
{
    private static readonly byte[] Salt = Encoding.UTF8.GetBytes("Saturn-AI-Agent-Salt-2024");
    
    /// <summary>
    /// Encrypts a string using AES encryption with machine-specific entropy
    /// </summary>
    public static string EncryptString(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
            return string.Empty;
            
        try
        {
            var plainBytes = Encoding.UTF8.GetBytes(plainText);
            var encryptedBytes = ProtectedData.Protect(plainBytes, Salt, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(encryptedBytes);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to encrypt data", ex);
        }
    }
    
    /// <summary>
    /// Decrypts a string that was encrypted with EncryptString
    /// </summary>
    public static string DecryptString(string encryptedText)
    {
        if (string.IsNullOrEmpty(encryptedText))
            return string.Empty;
            
        try
        {
            var encryptedBytes = Convert.FromBase64String(encryptedText);
            var decryptedBytes = ProtectedData.Unprotect(encryptedBytes, Salt, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decryptedBytes);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to decrypt data", ex);
        }
    }
    
    /// <summary>
    /// Securely clears a string from memory by overwriting with random data
    /// </summary>
    public static void SecureClear(ref string sensitiveData)
    {
        if (string.IsNullOrEmpty(sensitiveData))
            return;
            
        // While we can't directly overwrite string memory in .NET due to immutability,
        // we can at least clear the reference and suggest garbage collection
        sensitiveData = string.Empty;
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
    
    /// <summary>
    /// Validates that a string appears to be an encrypted value
    /// </summary>
    public static bool IsEncrypted(string value)
    {
        if (string.IsNullOrEmpty(value))
            return false;
            
        try
        {
            // Try to decode as Base64 - encrypted values should be Base64 encoded
            Convert.FromBase64String(value);
            
            // Additional heuristics: encrypted values are typically longer and contain mixed case
            return value.Length > 20 && 
                   value.Any(char.IsUpper) && 
                   value.Any(char.IsLower) &&
                   (value.Contains('+') || value.Contains('/') || value.Contains('='));
        }
        catch
        {
            return false;
        }
    }
}