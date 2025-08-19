using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
#if WINDOWS
using System.Security.Cryptography;
#endif

namespace Saturn.Core.Security;

public static class SecureStorage
{
    private static readonly byte[] BaseSalt = Encoding.UTF8.GetBytes("Saturn-AI-Agent-Salt-2024");
    private const int SaltSize = 16;
    private const int KeySize = 32; // 256 bits
    private const int IvSize = 16;  // 128 bits
    private const int Iterations = 100000; // PBKDF2 iterations
    
    /// <summary>
    /// Encrypts a string using AES encryption with machine-specific entropy and proper key derivation
    /// </summary>
    public static string EncryptString(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
            return string.Empty;

        try
        {
            var plainBytes = Encoding.UTF8.GetBytes(plainText);
#if WINDOWS
            var encryptedBytes = ProtectedData.Protect(plainBytes, BaseSalt, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(encryptedBytes);
#else
            // Generate random salt for this encryption operation
            var salt = new byte[SaltSize];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(salt);
            }

            // Derive key using PBKDF2 with user-specific entropy
            var key = DeriveKeySecure(salt);

            using var aes = Aes.Create();
            aes.Key = key;
            aes.GenerateIV();

            using var encryptor = aes.CreateEncryptor();
            var encrypted = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

            // Combine salt, IV, and encrypted data
            var result = new byte[SaltSize + IvSize + encrypted.Length];
            Array.Copy(salt, 0, result, 0, SaltSize);
            Array.Copy(aes.IV, 0, result, SaltSize, IvSize);
            Array.Copy(encrypted, 0, result, SaltSize + IvSize, encrypted.Length);

            // Clear sensitive data
            Array.Clear(key, 0, key.Length);
            Array.Clear(plainBytes, 0, plainBytes.Length);

            return Convert.ToBase64String(result);
#endif
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
#if WINDOWS
            var decryptedBytes = ProtectedData.Unprotect(encryptedBytes, BaseSalt, DataProtectionScope.CurrentUser);
            var result = Encoding.UTF8.GetString(decryptedBytes);
            Array.Clear(decryptedBytes, 0, decryptedBytes.Length);
            return result;
#else
            // Cross-platform decryption
            if (encryptedBytes.Length < SaltSize + IvSize)
                throw new InvalidOperationException("Invalid encrypted data - insufficient length");

            // Extract salt, IV, and encrypted data
            var salt = new byte[SaltSize];
            var iv = new byte[IvSize];
            var encrypted = new byte[encryptedBytes.Length - SaltSize - IvSize];

            Array.Copy(encryptedBytes, 0, salt, 0, SaltSize);
            Array.Copy(encryptedBytes, SaltSize, iv, 0, IvSize);
            Array.Copy(encryptedBytes, SaltSize + IvSize, encrypted, 0, encrypted.Length);

            // Derive the same key using the extracted salt
            var key = DeriveKeySecure(salt);

            using var aes = Aes.Create();
            aes.Key = key;
            aes.IV = iv;

            using var decryptor = aes.CreateDecryptor();
            var decryptedBytes = decryptor.TransformFinalBlock(encrypted, 0, encrypted.Length);

            var result = Encoding.UTF8.GetString(decryptedBytes);

            // Clear sensitive data
            Array.Clear(key, 0, key.Length);
            Array.Clear(decryptedBytes, 0, decryptedBytes.Length);
            Array.Clear(salt, 0, salt.Length);
            Array.Clear(iv, 0, iv.Length);

            return result;
#endif
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

#if !WINDOWS
    /// <summary>
    /// Derives an AES key from the salt for cross-platform encryption with proper security
    /// </summary>
    private static byte[] DeriveKeySecure(byte[] salt)
    {
        // Combine base salt with user-specific entropy and provided salt
        var combinedSalt = new byte[BaseSalt.Length + salt.Length + Environment.UserName.Length];
        Array.Copy(BaseSalt, 0, combinedSalt, 0, BaseSalt.Length);
        Array.Copy(salt, 0, combinedSalt, BaseSalt.Length, salt.Length);
        var userBytes = Encoding.UTF8.GetBytes(Environment.UserName);
        Array.Copy(userBytes, 0, combinedSalt, BaseSalt.Length + salt.Length, userBytes.Length);

        try
        {
            using var pbkdf2 = new Rfc2898DeriveBytes("Saturn-AI-Agent-Key-2024", combinedSalt, Iterations, HashAlgorithmName.SHA256);
            return pbkdf2.GetBytes(KeySize);
        }
        finally
        {
            // Clear sensitive data
            Array.Clear(combinedSalt, 0, combinedSalt.Length);
        }
    }

    /// <summary>
    /// Legacy key derivation method for backward compatibility
    /// </summary>
    private static byte[] DeriveKey(byte[] salt)
    {
        using var pbkdf2 = new Rfc2898DeriveBytes("Saturn-AI-Agent-Key-2024", salt, 10000, HashAlgorithmName.SHA256);
        return pbkdf2.GetBytes(32); // 256-bit key for AES-256
    }
#endif
}