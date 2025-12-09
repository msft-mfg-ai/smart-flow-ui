// Copyright (c) Microsoft. All rights reserved.
using System.Reflection;

namespace MinimalApi.Services.Profile.Prompts;

/// <summary>
/// Service for loading and rendering prompts
/// </summary>
public static class PromptService
{
    public static string ChatSystemPrompt = "RAGChatSystemPrompt";
    public static string ChatUserPrompt = "RAGChatUserPrompt";
    public static string RAGSearchSystemPrompt = "RAGSearchQuerySystemPrompt";
    public static string RAGSearchUserPrompt = "RAGSearchUserPrompt";

    public static string ChatSimpleSystemPrompt = "ChatSimpleSystemPrompt";
    public static string ChatSimpleUserPrompt = "ChatSimpleUserPrompt";

    /// <summary>
    /// Gets a prompt template by name from embedded resources
    /// </summary>
    public static string GetPromptByName(string prompt)
    {
        var resourceName = $"SmartFlow.UI.API.Services.Profile.Prompts.{prompt}.txt";
        var assembly = Assembly.GetExecutingAssembly();
        using (Stream stream = assembly.GetManifestResourceStream(resourceName))
        {
            if (stream == null)
                throw new ArgumentException($"The resource {resourceName} was not found.");

            using (StreamReader reader = new StreamReader(stream))
                return reader.ReadToEnd();
        }
    }

    /// <summary>
    /// Renders a prompt template with the provided parameters
    /// Uses simple string interpolation instead of Semantic Kernel's prompt template
    /// </summary>
    public static Task<string> RenderPromptAsync(string prompt, Dictionary<string, object?> parameters)
    {
        var result = prompt;
        
        foreach (var param in parameters)
        {
            var placeholder = "{{" + param.Key + "}}";
            var value = param.Value?.ToString() ?? "";
            result = result.Replace(placeholder, value);
            
            // Also support $variable syntax
            var dollarPlaceholder = "$" + param.Key;
            result = result.Replace(dollarPlaceholder, value);
        }
        
        return Task.FromResult(result);
    }
}
