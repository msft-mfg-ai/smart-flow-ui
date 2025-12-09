// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Extensions.AI;
using MinimalApi.Services.Profile.Prompts;

namespace MinimalApi.Agents;

/// <summary>
/// RAG Chat service using Microsoft Agent Framework
/// </summary>
internal sealed class RAGChatService : IChatService
{
    private readonly ILogger<RAGChatService> _logger;
    private readonly IConfiguration _configuration;
    private readonly OpenAIClientFacade _openAIClientFacade;

    public RAGChatService(OpenAIClientFacade openAIClientFacade,
                          ILogger<RAGChatService> logger,
                          IConfiguration configuration)
    {
        _openAIClientFacade = openAIClientFacade;
        _logger = logger;
        _configuration = configuration;
    }

    public async IAsyncEnumerable<ChatChunkResponse> ReplyAsync(UserInformation user, ProfileDefinition profile, ChatRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile.RAGSettings, "profile.RAGSettings");
        var sw = Stopwatch.StartNew();

        var chatClient = _openAIClientFacade.GetAgentFrameworkChatClient();
        var parameters = SKExtensions.CreateUserParameters(request, profile, user);
        var vectorSearchSettings = VectorSearchExtensions.CreateVectorSearchSettings(profile);

        // Resolve system message
        var systemMessagePrompt = ResolveSystemMessage(profile);

        // Build chat history
        var chatHistory = SKExtensions.ConvertChatHistory(request.History, systemMessagePrompt);

        // Execute RAG search to get sources
        var ragPlugin = new Assistants.Hub.API.Assistants.RAG.RAGRetrivalPlugins(
            _openAIClientFacade.GetSearchClientFactory(),
            _openAIClientFacade.GetEmbeddingsClient());

        var sources = new List<SupportingContentRecord>();
        var functionCallResults = new List<ExecutionStepResult>();
        
        try
        {
            var knowledgeSources = await ragPlugin.GetKnowledgeSourcesAsync(
                vectorSearchSettings, 
                request.LastUserQuestion);
            
            // Add sources to context
            var sourcesList = knowledgeSources.ToList();
            sources.AddRange(sourcesList.Select(x => new SupportingContentRecord(x.FilePath, x.Content)));
            
            // Add function call result for diagnostics
            var searchResult = $"Search Query: {request.LastUserQuestion}\n{System.Text.Json.JsonSerializer.Serialize(sourcesList, new System.Text.Json.JsonSerializerOptions { WriteIndented = true })}";
            functionCallResults.Add(new ExecutionStepResult("get_knowledge_articles", searchResult, sources));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving knowledge sources");
            functionCallResults.Add(new ExecutionStepResult("get_knowledge_articles", $"Error: {ex.Message}"));
        }

        // Build user message with sources context
        var userMessage = await ResolveUserMessageAsync(profile, parameters, sources);

        // Add user message with file uploads
        if (request.FileUploads.Any())
        {
            var userMessages = ChatHistoryExtensions.CreateUserMessageWithUploads(userMessage, request.FileUploads);
            foreach (var msg in userMessages)
            {
                chatHistory.Add(msg);
            }
        }
        else
        {
            chatHistory.Add(new ChatMessage(ChatRole.User, userMessage));
        }

        var sb = new StringBuilder();
        
        // Stream chat completion using Agent Framework
        await foreach (var update in chatClient.GetStreamingResponseAsync(chatHistory, DefaultSettings.AIChatWithToolsRequestSettings, cancellationToken))
        {
            foreach (var content in update.Contents)
            {
                if (content is TextContent textContent && !string.IsNullOrEmpty(textContent.Text))
                {
                    sb.Append(textContent.Text);
                    yield return new ChatChunkResponse(textContent.Text);
                    await Task.Yield();
                }
            }
        }
        
        sw.Stop();

        var result = SKExtensions.BuildStreamingResponse(
            profile, 
            request, 
            chatHistory, 
            sb.ToString(), 
            _configuration, 
            _openAIClientFacade.GetKernelDeploymentName(), 
            sw.ElapsedMilliseconds,
            sources,
            functionCallResults);

        _logger.LogInformation($"Chat Complete - Profile: {result.Context.Profile}, ChatId: {result.Context.ChatId}, ChatMessageId: {result.Context.MessageId}, ModelDeploymentName: {result.Context.Diagnostics?.ModelDeploymentName}, TotalTokens: {result.Context.Diagnostics?.AnswerDiagnostics?.TotalTokens}");

        yield return new ChatChunkResponse(string.Empty, result);
    }

    private string ResolveSystemMessage(ProfileDefinition profile)
    {
        ArgumentNullException.ThrowIfNull(profile.RAGSettings, "profile.RAGSettings");

        var systemMessagePrompt = string.Empty;
        if (!string.IsNullOrEmpty(profile.RAGSettings.ChatSystemMessageFile))
            systemMessagePrompt = PromptService.GetPromptByName(profile.RAGSettings.ChatSystemMessageFile);

        if (!string.IsNullOrEmpty(profile.RAGSettings.ChatSystemMessage))
        {
            var bytes = Convert.FromBase64String(profile.RAGSettings.ChatSystemMessage);
            systemMessagePrompt = Encoding.UTF8.GetString(bytes);
        }
        return systemMessagePrompt;
    }

    private async Task<string> ResolveUserMessageAsync(ProfileDefinition profile, Dictionary<string, object?> parameters, List<SupportingContentRecord> sources)
    {
        ArgumentNullException.ThrowIfNull(profile.RAGSettings, "profile.RAGSettings");

        var userMessage = string.Empty;
        if (!string.IsNullOrEmpty(profile.RAGSettings.ChatUserMessage))
        {
            var bytes = Convert.FromBase64String(profile.RAGSettings.ChatUserMessage);
            userMessage = Encoding.UTF8.GetString(bytes);
        }
        else
        {
            // Build user message with sources
            var question = parameters[ContextVariableOptions.Question]?.ToString() ?? "";
            var sourcesText = sources.Any() 
                ? string.Join("\n\n", sources.Select(s => $"Source: {s.Title}\n{s.Content}"))
                : "No relevant sources found.";
            
            userMessage = $"Question: {question}\n\nRelevant Sources:\n{sourcesText}";
        }

        return userMessage;
    }
}
