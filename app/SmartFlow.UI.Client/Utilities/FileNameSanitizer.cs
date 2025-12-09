// Copyright (c) Microsoft. All rights reserved.

namespace SmartFlow.UI.Client.Utilities;

/// <summary>
/// Provides methods to sanitize file names for safe storage and URL encoding.
/// </summary>
public static class FileNameSanitizer
{
    /// <summary>
    /// Characters that should be replaced in file names to avoid encoding issues.
    /// These characters can cause problems with URLs, blob storage, or file systems.
    /// </summary>
    private static readonly Dictionary<char, char> CharacterReplacements = new()
    {
        { ' ', '_' },      // Spaces -> underscores
        { '#', '_' },      // Hash can cause URL fragment issues
        { '%', '_' },      // Percent can cause double-encoding issues
        { '&', '_' },      // Ampersand can cause query string issues
        { '{', '_' },      // Curly braces
        { '}', '_' },
        { '\\', '_' },     // Backslash
        { '<', '_' },      // Less than
        { '>', '_' },      // Greater than
        { '*', '_' },      // Asterisk
        { '?', '_' },      // Question mark
        { '/', '_' },      // Forward slash (except for path separators)
        { '$', '_' },      // Dollar sign
        { '!', '_' },      // Exclamation mark
        { '\'', '_' },     // Single quote
        { '"', '_' },      // Double quote
        { ':', '_' },      // Colon (except for drive letters)
        { '@', '_' },      // At sign
        { '+', '_' },      // Plus sign (can be interpreted as space in URLs)
        { '`', '_' },      // Backtick
        { '|', '_' },      // Pipe
        { '=', '_' },      // Equals sign
    };

    /// <summary>
    /// Sanitizes a file name by replacing problematic characters with underscores.
    /// Preserves the file extension.
    /// </summary>
    /// <param name="fileName">The original file name to sanitize.</param>
    /// <returns>A sanitized file name safe for storage and URL encoding.</returns>
    public static string Sanitize(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return fileName;
        }

        // Separate the extension from the base name
        var extension = Path.GetExtension(fileName);
        var baseName = Path.GetFileNameWithoutExtension(fileName);

        // Replace problematic characters in the base name
        var sanitizedBaseName = new System.Text.StringBuilder(baseName.Length);
        
        foreach (var c in baseName)
        {
            if (CharacterReplacements.TryGetValue(c, out var replacement))
            {
                sanitizedBaseName.Append(replacement);
            }
            else
            {
                sanitizedBaseName.Append(c);
            }
        }

        // Remove consecutive underscores
        var result = sanitizedBaseName.ToString();
        while (result.Contains("__"))
        {
            result = result.Replace("__", "_");
        }

        // Trim leading/trailing underscores
        result = result.Trim('_');

        // If the entire base name was sanitized away, use a default name
        if (string.IsNullOrWhiteSpace(result))
        {
            result = "file";
        }

        return result + extension;
    }

    /// <summary>
    /// Sanitizes a file path by replacing problematic characters in the file name portion only.
    /// Preserves the directory structure.
    /// </summary>
    /// <param name="filePath">The file path to sanitize.</param>
    /// <returns>A sanitized file path with a safe file name.</returns>
    public static string SanitizePath(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return filePath;
        }

        // Normalize path separators
        filePath = filePath.Replace("\\", "/");

        // Find the last path separator
        var lastSeparatorIndex = filePath.LastIndexOf('/');
        
        if (lastSeparatorIndex == -1)
        {
            // No directory, just sanitize the file name
            return Sanitize(filePath);
        }

        // Separate directory and file name
        var directory = filePath.Substring(0, lastSeparatorIndex + 1);
        var fileName = filePath.Substring(lastSeparatorIndex + 1);

        // Sanitize only the file name portion
        return directory + Sanitize(fileName);
    }
}
