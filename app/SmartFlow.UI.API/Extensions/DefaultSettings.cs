using Microsoft.Extensions.AI;

namespace MinimalApi.Extensions;

/// <summary>
/// Default chat options for Microsoft Agent Framework
/// </summary>
public static class DefaultSettings
{
    /// <summary>
    /// Chat options with tool/function calling enabled
    /// </summary>
    public static ChatOptions AIChatWithToolsRequestSettings = new ChatOptions
    {
        Temperature = 0.0f,
        MaxOutputTokens = 2024,
        TopP = 1.0f
    };

    /// <summary>
    /// Basic chat options without tool calling
    /// </summary>
    public static ChatOptions AIChatRequestSettings = new ChatOptions
    {
        Temperature = 0.0f,
        MaxOutputTokens = 2024,
        TopP = 1.0f
    };

    public static string CosmosDbDatabaseName = "ChatHistory";
    public static string CosmosDbCollectionName = "ChatTurn";
    public static string CosmosDBUserDocumentsCollectionName = "UserDocuments";
}
