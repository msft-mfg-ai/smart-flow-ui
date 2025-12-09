using Microsoft.Extensions.AI;
using TiktokenSharp;

namespace MinimalApi.Extensions;

/// <summary>
/// Extension methods for chat operations, migrated to Microsoft Agent Framework
/// </summary>
public static class SKExtensions
{
    /// <summary>
    /// Creates a dictionary of user parameters from a chat request
    /// </summary>
    public static Dictionary<string, object?> CreateUserParameters(ChatRequest request, ProfileDefinition profile, UserInformation user)
    {
        var parameters = new Dictionary<string, object?>
        {
            [ContextVariableOptions.Profile] = profile,
            [ContextVariableOptions.UserId] = user.UserId,
            [ContextVariableOptions.SessionId] = user.SessionId
        };

        if (request.SelectedUserCollectionFiles != null && request.SelectedUserCollectionFiles.Any())
        {
            parameters[ContextVariableOptions.SelectedDocuments] = request.SelectedUserCollectionFiles;
        }

        if (request.History.LastOrDefault()?.User is { } userQuestion)
        {
            parameters[ContextVariableOptions.Question] = $"{userQuestion}";
        }
        else
        {
            throw new InvalidOperationException("User question is null");
        }

        if (request.UserSelectionModel != null && request.UserSelectionModel.Options.Any())
        {
            foreach (var item in request.UserSelectionModel.Options)
            {
                parameters[item.Name] = item.SelectedValue;
            }
        }

        parameters[ContextVariableOptions.ChatTurns] = request.History;
        return parameters;
    }

    /// <summary>
    /// Converts chat history to a list of ChatMessages for Agent Framework
    /// </summary>
    public static IList<ChatMessage> ConvertChatHistory(ChatTurn[] history, string? systemMessage = null)
    {
        var messages = new List<ChatMessage>();
        
        if (!string.IsNullOrEmpty(systemMessage))
        {
            messages.Add(new ChatMessage(ChatRole.System, systemMessage));
        }

        foreach (var chatTurn in history.SkipLast(1))
        {
            messages.Add(new ChatMessage(ChatRole.User, chatTurn.User));
            if (chatTurn.Assistant != null)
            {
                messages.Add(new ChatMessage(ChatRole.Assistant, chatTurn.Assistant));
            }
        }

        return messages;
    }

    public static bool IsChatProfile(this Dictionary<string, string> options, List<ProfileDefinition> profiles)
    {
        var profile = options.GetChatProfile(profiles);
        var selected = profiles.FirstOrDefault(x => x.Name == profile.Name);
        return selected?.Approach.ToUpper() == "CHAT";
    }

    public static bool IsEndpointAssistantProfile(this Dictionary<string, string> options, List<ProfileDefinition> profiles)
    {
        var profile = options.GetChatProfile(profiles);
        var selected = profiles.FirstOrDefault(x => x.Name == profile.Name);
        return selected?.Approach.ToUpper() == "ENDPOINTASSISTANT";
    }

    public static bool IsEndpointAssistantV2Profile(this Dictionary<string, string> options, List<ProfileDefinition> profiles)
    {
        var profile = options.GetChatProfile(profiles);
        var selected = profiles.FirstOrDefault(x => x.Name == profile.Name);
        return selected?.Approach.ToUpper() == "ENDPOINTASSISTANTV2";
    }

    public static bool IsEndpointAssistantTaskProfile(this Dictionary<string, string> options, List<ProfileDefinition> profiles)
    {
        var profile = options.GetChatProfile(profiles);
        var selected = profiles.FirstOrDefault(x => x.Name == profile.Name);
        return selected?.Approach.ToUpper() == "ENDPOINTASSISTANTTASK";
    }

    public static bool IsAzureAIAgentChatProfile(this Dictionary<string, string> options, List<ProfileDefinition> profiles)
    {
        var profile = options.GetChatProfile(profiles);
        var selected = profiles.FirstOrDefault(x => x.Name == profile.Name);
        return selected?.Approach.ToUpper() == "AZUREAIAGENTCHATPROFILE";
    }

    public static bool IsImangeChatProfile(this Dictionary<string, string> options, List<ProfileDefinition> profiles)
    {
        var profile = options.GetChatProfile(profiles);
        var selected = profiles.FirstOrDefault(x => x.Name == profile.Name);
        return selected?.Approach.ToUpper() == "IMAGECHAT";
    }

    public static ProfileDefinition GetChatProfile(this Dictionary<string, string> options, List<ProfileDefinition> profiles)
    {
        var defaultProfile = profiles.First();
        var value = options.GetValueOrDefault("PROFILE", defaultProfile.Name);
        return profiles.FirstOrDefault(x => x.Name == value) ?? defaultProfile;
    }

    /// <summary>
    /// Builds a streaming response for Agent Framework
    /// </summary>
    public static ApproachResponse BuildStreamingResponse(
        ProfileDefinition profile, 
        ChatRequest request, 
        IList<ChatMessage> chatHistory, 
        string answer, 
        IConfiguration configuration, 
        string modelDeploymentName, 
        long workflowDurationMilliseconds,
        IEnumerable<SupportingContentRecord>? dataSources = null,
        IEnumerable<ExecutionStepResult>? functionCallResults = null)
    {
        var requestTokenCount = GetTokenCount(chatHistory);

        var sources = dataSources?.ToList() ?? new List<SupportingContentRecord>();

        var completionTokens = GetTokenCount(answer);
        var totalTokens = completionTokens + requestTokenCount;
        var chatDiagnostics = new CompletionsDiagnostics(completionTokens, requestTokenCount, totalTokens, 0);
        var diagnostics = new Diagnostics(chatDiagnostics, modelDeploymentName, workflowDurationMilliseconds);

        var systemMessage = chatHistory.FirstOrDefault(x => x.Role == ChatRole.System)?.Text ?? "";
        var thoughts = GetThoughtProcess(systemMessage, answer, functionCallResults);
        
        var contextData = new ResponseContext(
            profile.Name, 
            sources.Distinct().ToArray(),
            thoughts.Select(x => new ThoughtRecord(x.Name, x.Result)).ToArray(), 
            request.ChatTurnId, 
            request.ChatId, 
            string.Empty,
            diagnostics);

        return new ApproachResponse(
            Answer: NormalizeResponseText(answer),
            CitationBaseUrl: profile.Id,
            contextData);
    }

    /// <summary>
    /// Builds a simple chat response for Agent Framework
    /// </summary>
    public static ApproachResponse BuildChatSimpleResponse(
        ProfileDefinition profile, 
        ChatRequest request, 
        int requestTokenCount, 
        string answer, 
        IConfiguration configuration, 
        string modelDeploymentName, 
        long workflowDurationMilliseconds)
    {
        var completionTokens = GetTokenCount(answer);
        var totalTokens = completionTokens + requestTokenCount;
        var chatDiagnostics = new CompletionsDiagnostics(completionTokens, requestTokenCount, totalTokens, 0);
        var diagnostics = new Diagnostics(chatDiagnostics, modelDeploymentName, workflowDurationMilliseconds);

        var contextData = new ResponseContext(profile.Name, null, Array.Empty<ThoughtRecord>(), request.ChatTurnId, request.ChatId, string.Empty, diagnostics);

        return new ApproachResponse(
            Answer: NormalizeResponseText(answer),
            CitationBaseUrl: string.Empty,
            contextData);
    }

    private static string NormalizeResponseText(string text)
    {
        text = text.StartsWith("null,") ? text[5..] : text;
        text = text.Replace("\r", "\n")
            .Replace("\\n\\r", "\n")
            .Replace("\\n", "\n");

        return text;
    }

    /// <summary>
    /// Gets token count for a list of chat messages
    /// </summary>
    public static int GetTokenCount(IList<ChatMessage> chatHistory)
    {
        string requestContent = string.Join("", chatHistory.Select(x => x.Text ?? ""));
        var tikToken = TikToken.EncodingForModel("gpt-3.5-turbo");
        return tikToken.Encode(requestContent).Count;
    }

    /// <summary>
    /// Gets token count for a string
    /// </summary>
    public static int GetTokenCount(string text)
    {
        var tikToken = TikToken.EncodingForModel("gpt-3.5-turbo");
        return tikToken.Encode(text).Count;
    }

    /// <summary>
    /// Gets the thought process for diagnostics
    /// </summary>
    public static IEnumerable<ExecutionStepResult> GetThoughtProcess(
        string systemPrompt, 
        string answer, 
        IEnumerable<ExecutionStepResult>? functionCallResults = null)
    {
        if (functionCallResults != null)
        {
            foreach (var item in functionCallResults)
            {
                yield return item;
            }
        }
        yield return new ExecutionStepResult("chat_completion", $"{systemPrompt} \n {answer}");
    }
}

/// <summary>
/// Extension methods for vector search settings
/// </summary>
public static class VectorSearchExtensions
{
    /// <summary>
    /// Creates VectorSearchSettings from profile RAGSettings
    /// </summary>
    public static VectorSearchSettings CreateVectorSearchSettings(ProfileDefinition profile)
    {
        ArgumentNullException.ThrowIfNull(profile?.RAGSettings, "profile.RAGSettings");
        
        return new VectorSearchSettings(
            profile.RAGSettings.DocumentRetrievalIndexName,
            profile.RAGSettings.DocumentRetrievalDocumentCount,
            profile.RAGSettings.DocumentRetrievalSchema,
            profile.RAGSettings.DocumentRetrievalEmbeddingsDeployment,
            profile.RAGSettings.DocumentRetrievalMaxSourceTokens,
            profile.RAGSettings.KNearestNeighborsCount,
            profile.RAGSettings.Exhaustive,
            profile.RAGSettings.UseSemanticRanker,
            profile.RAGSettings.SemanticConfigurationName,
            profile.RAGSettings.StorageContianer,
            profile.RAGSettings.CitationUseSourcePage);
    }
}

