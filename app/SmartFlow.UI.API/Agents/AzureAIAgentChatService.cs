// Copyright (c) Microsoft. All rights reserved.

using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Extensions.AI;
using Microsoft.Agents.AI;
using OpenAI.Chat;

namespace MinimalApi.Agents;

/// <summary>
/// Azure AI Agent Chat Service using Microsoft Agent Framework
/// Based on: https://github.com/microsoft/semantic-kernel/blob/main/dotnet/samples/AgentFrameworkMigration/AzureOpenAI/Step01_Basics/Program.cs
/// </summary>
public class AzureAIAgentChatService : IChatService
{
    private readonly ILogger<AzureAIAgentChatService> _logger;
    private readonly IConfiguration _configuration;
    private readonly OpenAIClientFacade _openAIClientFacade;
    private readonly IChatClient _chatClient;
    
    public AzureAIAgentChatService(
        OpenAIClientFacade openAIClientFacade, 
        AzureBlobStorageService blobStorageService, 
        ILogger<AzureAIAgentChatService> logger, 
        IConfiguration configuration)
    {
        _openAIClientFacade = openAIClientFacade;
        _logger = logger;
        _configuration = configuration;

        var azureAIFoundryProjectEndpoint = _configuration["AzureAIFoundryProjectEndpoint"];
        ArgumentNullException.ThrowIfNullOrEmpty(azureAIFoundryProjectEndpoint, "AzureAIFoundryProjectEndpoint");
        
        var deploymentName = _configuration["AzureAIFoundryDeploymentName"] ?? "gpt-4o";
        
        // Create chat client for Azure AI Foundry using Agent Framework pattern
        ChatClient nativeChatClient = new AzureOpenAIClient(
            new Uri(azureAIFoundryProjectEndpoint),
            new DefaultAzureCredential())
            .GetChatClient(deploymentName);
        
        _chatClient = nativeChatClient.AsIChatClient(); // Convert to Microsoft.Extensions.AI.IChatClient
    }

    public async IAsyncEnumerable<ChatChunkResponse> ReplyAsync(
        UserInformation user, 
        ProfileDefinition profile, 
        ChatRequest request, 
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var sb = new StringBuilder();
        var userMessage = request.LastUserQuestion;

        // Create AI Agent using Agent Framework
        // Note: For now, creating a simple agent. In production, you'd retrieve the agent by ID
        // if Azure AI Foundry supports persistent agents via this API
        var agent = _chatClient.CreateAIAgent(
            name: profile.Name,
            instructions: profile.ChatSystemMessage ?? "You are a helpful assistant.");

        // Create agent thread
        var thread = agent.GetNewThread();

        // TODO: Handle file uploads with Agent Framework
        // The Agent Framework file upload pattern may differ from the old API
        if (request.FileUploads.Any())
        {
            _logger.LogWarning("File uploads not yet implemented for Microsoft Agent Framework");
        }

        var sources = new List<SupportingContentRecord>();
        var fileReferences = new List<string>();

        // Create run options
        var runOptions = new ChatClientAgentRunOptions(new ChatOptions
        {
            MaxOutputTokens = 2048
        });

        // Stream the agent run
        await foreach (AgentRunResponseUpdate update in agent.RunStreamingAsync(
            userMessage,
            thread,
            runOptions,
            cancellationToken))
        {
            // Get the text content from the update
            var updateText = update.ToString();
            
            if (!string.IsNullOrEmpty(updateText))
            {
                sb.Append(updateText);
                yield return new ChatChunkResponse(updateText);
                await Task.Yield();
            }

            // TODO: Handle citations and file references
            // The Agent Framework may expose these differently
        }

        sw.Stop();

        // Note: Thread ID might not be available in the same way
        // Agent Framework uses in-memory threads by default
        var threadId = string.Empty;

        var contextData = new ResponseContext(
            profile.Name, 
            sources.ToArray(), 
            Array.Empty<ThoughtRecord>(), 
            request.ChatTurnId, 
            request.ChatId, 
            threadId, 
            null);

        var responseText = sb.ToString();
        foreach (var source in sources)
        {
            responseText = responseText.Replace(source.Content, $"[{source.Title}]");
        }

        var result = new ApproachResponse(
            Answer: responseText, 
            CitationBaseUrl: string.Empty, 
            contextData);
        
        yield return new ChatChunkResponse(string.Empty, result);
    }
}
