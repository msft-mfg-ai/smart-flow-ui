// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Extensions.AI;
using MinimalApi.Services.Profile.Prompts;

namespace MinimalApi.Agents;

/// <summary>
/// Chat service using Microsoft Agent Framework
/// </summary>
internal sealed class ChatService : IChatService
{
    private readonly ILogger<ChatService> _logger;
    private readonly IConfiguration _configuration;
    private readonly OpenAIClientFacade _openAIClientFacade;
    private readonly AzureBlobStorageService _blobStorageService;

    public ChatService(OpenAIClientFacade openAIClientFacade, AzureBlobStorageService blobStorageService, ILogger<ChatService> logger, IConfiguration configuration)
    {
        _openAIClientFacade = openAIClientFacade;
        _blobStorageService = blobStorageService;
        _logger = logger;
        _configuration = configuration;
    }

    public async IAsyncEnumerable<ChatChunkResponse> ReplyAsync(UserInformation user, ProfileDefinition profile, ChatRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();

        var chatClient = _openAIClientFacade.GetAgentFrameworkChatClient();
        var parameters = SKExtensions.CreateUserParameters(request, profile, user);

        // Resolve system message
        var systemMessagePrompt = string.Empty;
        if (!string.IsNullOrEmpty(profile.ChatSystemMessageFile))
        {
            systemMessagePrompt = PromptService.GetPromptByName(profile.ChatSystemMessageFile);
        }

        if (!string.IsNullOrEmpty(profile.ChatSystemMessage))
        {
            var bytes = Convert.FromBase64String(profile.ChatSystemMessage);
            systemMessagePrompt = Encoding.UTF8.GetString(bytes);
        }

        // Build chat history
        var chatHistory = SKExtensions.ConvertChatHistory(request.History, systemMessagePrompt);
        
        // Get user message
        var userMessage = request.LastUserQuestion;

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
        await foreach (var update in chatClient.GetStreamingResponseAsync(chatHistory, DefaultSettings.AIChatRequestSettings, cancellationToken))
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

        var requestTokenCount = SKExtensions.GetTokenCount(chatHistory);
        var result = SKExtensions.BuildChatSimpleResponse(profile, request, requestTokenCount, sb.ToString(), _configuration, _openAIClientFacade.GetKernelDeploymentName(), sw.ElapsedMilliseconds);
        yield return new ChatChunkResponse(string.Empty, result);
    }
}
