// Copyright (c) Microsoft. All rights reserved.
using MinimalApi.Agents;
using Microsoft.AspNetCore.Mvc;

namespace MinimalApi.Extensions;

internal static class WebApiChatExtensions
{
    internal static WebApplication MapChatApi(this WebApplication app)
    {
        var api = app.MapGroup("api");

        // Process chat turn
        api.MapPost("chat/streaming", OnPostChatStreamingAsync);
        api.MapPost("chat", OnPostChatAsync);

        // Process chat turn history
        api.MapGet("chat/history", OnGetHistoryAsync);
        api.MapGet("chat/history-v2", OnGetHistoryV2Async);
        api.MapGet("chat/history/{chatId}", OnGetChatHistorySessionAsync);

        // Process chat turn rating
        api.MapPost("chat/rating", OnPostChatRatingAsync);

        return app;
    }

    private static async Task<ApproachResponse> OnPostChatAsync(HttpContext context, ChatRequest request, ChatService chatService, RAGChatService ragChatService, IChatHistoryService chatHistoryService, EndpointChatService endpointChatService, EndpointChatServiceV2 endpointChatServiceV2, EndpointTaskService endpointTaskService, [FromServices] AzureAIAgentChatService? azureAIAgentChatService, ImageGenerationChatAgent imageGenerationChatAgent, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ApproachResponse response = null;
        var resultChunks = OnPostChatStreamingAsync(context, request, chatService, ragChatService, azureAIAgentChatService, chatHistoryService, endpointChatService, endpointChatServiceV2, endpointTaskService, imageGenerationChatAgent, cancellationToken);
        await foreach (var chunk in resultChunks)
        {
            if (chunk.FinalResult != null)
            {
                response = chunk.FinalResult;
            }
        }

        return response;
    }

    private static async IAsyncEnumerable<ChatChunkResponse> OnPostChatStreamingAsync(HttpContext context, ChatRequest request, ChatService chatService, RAGChatService ragChatService, [FromServices] AzureAIAgentChatService? azureAIAgentChatService, IChatHistoryService chatHistoryService, EndpointChatService endpointChatService, EndpointChatServiceV2 endpointChatServiceV2, EndpointTaskService endpointTaskService, ImageGenerationChatAgent imageGenerationChatAgent, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var profileService = context.RequestServices.GetRequiredService<ProfileService>();
        var profileInfo = await profileService.GetProfileDataAsync();
        var userInfo = await context.GetUserInfoAsync(profileInfo);

        var profile = request.OptionFlags.GetChatProfile(profileInfo.Profiles);
        if (!userInfo.HasAccess(profile))
        {
            throw new UnauthorizedAccessException("User does not have access to this profile");
        }

        //if (profile.Approach == ProfileApproach.UserDocumentChat.ToString())
        //{
        //    ArgumentNullException.ThrowIfNull(profile.RAGSettings, "Profile RAGSettings is null");

        //    var selectedDocument = request.SelectedUserCollectionFiles.FirstOrDefault();
        //    var documents = await documentService.GetDocumentUploadsAsync(userInfo, null);
        //    var document = documents.FirstOrDefault(d => d.SourceName == selectedDocument);

        //    ArgumentNullException.ThrowIfNull(document, "Document is null");
        //    profile.RAGSettings.DocumentRetrievalIndexName = document.RetrivalIndexName;
        //}

        var chat = await ResolveChatServiceAsync(request, chatService, ragChatService, endpointChatService, endpointChatServiceV2, endpointTaskService, azureAIAgentChatService, imageGenerationChatAgent, profileService);
        await foreach (var chunk in chat.ReplyAsync(userInfo, profile, request).WithCancellation(cancellationToken))
        {
            yield return chunk;
            if (chunk.FinalResult != null)
            {
                try
                {
                    await chatHistoryService.RecordChatMessageAsync(userInfo, request, chunk.FinalResult);
                }
                catch (Exception ex)
                {
                    var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("WebApiChatExtensions");
                    logger.LogError(ex, "Failed to record chat message for user {UserId}, chat {ChatId}, turn {ChatTurnId}", 
                        userInfo.UserId, request.ChatId, request.ChatTurnId);
                }
            }
        }
    }

    private static async Task<IChatService> ResolveChatServiceAsync(
        ChatRequest request,
        ChatService chatService,
        RAGChatService ragChatService,
        EndpointChatService endpointChatService,
        EndpointChatServiceV2 endpointChatServiceV2,
        EndpointTaskService endpointTaskService,
        [FromServices] AzureAIAgentChatService? azureAIAgentChatService,
        ImageGenerationChatAgent imageGenerationChatAgent,
        ProfileService profileService)
    {
        var profileInfo = await profileService.GetProfileDataAsync();

        if (request.OptionFlags.IsChatProfile(profileInfo.Profiles))
            return chatService;

        if (request.OptionFlags.IsEndpointAssistantProfile(profileInfo.Profiles))
            return endpointChatService;

        if (request.OptionFlags.IsEndpointAssistantV2Profile(profileInfo.Profiles))
            return endpointChatServiceV2;

        if (request.OptionFlags.IsEndpointAssistantTaskProfile(profileInfo.Profiles))
            return endpointTaskService;

        if (request.OptionFlags.IsAzureAIAgentChatProfile(profileInfo.Profiles))
        {
            if (azureAIAgentChatService == null)
                throw new InvalidOperationException("AzureAIAgentChatService is not configured. Please set AzureAIFoundryProjectEndpoint in configuration.");
            return azureAIAgentChatService;
        }

        if (request.OptionFlags.IsImangeChatProfile(profileInfo.Profiles))
            return imageGenerationChatAgent;

        return ragChatService;
    }

    private static async Task<IEnumerable<ChatHistoryResponse>> OnGetHistoryAsync(HttpContext context, IChatHistoryService chatHistoryService)
    {
        var profileService = context.RequestServices.GetRequiredService<ProfileService>();
        var profileInfo = await profileService.GetProfileDataAsync();

        var userInfo = await context.GetUserInfoAsync(profileInfo);
        var response = await chatHistoryService.GetMostRecentChatItemsAsync(userInfo);
        return response.AsFeedbackResponse(profileInfo);
    }

    private static async Task<IEnumerable<ChatSessionModel>> OnGetHistoryV2Async(HttpContext context, IChatHistoryService chatHistoryService)
    {
        var profileService = context.RequestServices.GetRequiredService<ProfileService>();
        var profileInfo = await profileService.GetProfileDataAsync();
        var userInfo = await context.GetUserInfoAsync(profileInfo);
        var response = await chatHistoryService.GetMostRecentChatItemsAsync(userInfo);
        var apiResponseModel = response.AsFeedbackResponse(profileInfo);

        var first = apiResponseModel.First();
        List<ChatSessionModel> sessions = apiResponseModel
            .GroupBy(msg => msg.ChatId)
            .Select(g => new ChatSessionModel(g.Key, g.ToList()))
            .ToList();

        return sessions;
    }

    private static async Task<IEnumerable<ChatHistoryResponse>> OnGetChatHistorySessionAsync(string chatId, HttpContext context, IChatHistoryService chatHistoryService)
    {
        var profileService = context.RequestServices.GetRequiredService<ProfileService>();
        var profileInfo = await profileService.GetProfileDataAsync();
        var userInfo = await context.GetUserInfoAsync(profileInfo);
        var response = await chatHistoryService.GetChatHistoryMessagesAsync(userInfo, chatId);
        var apiResponseModel = response.AsFeedbackResponse(profileInfo);
        return apiResponseModel;
    }
    
    private static async Task<IResult> OnPostChatRatingAsync(HttpContext context, ChatRatingRequest request, IChatHistoryService chatHistoryService, CancellationToken cancellationToken)
    {
        var userInfo = await context.GetUserInfoAsync();
        await chatHistoryService.RecordRatingAsync(userInfo, request);
        return Results.Ok();
    }
}
