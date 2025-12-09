// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Extensions.AI;
using MinimalApi.Services.Profile.Prompts;
using System.Text.RegularExpressions;
using Shared.Models;

namespace MinimalApi.Agents;

/// <summary>
/// Image Generation Chat Agent using Microsoft Agent Framework
/// </summary>
internal sealed class ImageGenerationChatAgent : IChatService
{
    private readonly ILogger<ImageGenerationChatAgent> _logger;
    private readonly IConfiguration _configuration;
    private readonly OpenAIClientFacade _openAIClientFacade;
    private readonly TextToImageService _textToImageService;

    public ImageGenerationChatAgent(OpenAIClientFacade openAIClientFacade,
                                    TextToImageService textToImageService,
                                    ILogger<ImageGenerationChatAgent> logger,
                                    IConfiguration configuration)
    {
        _openAIClientFacade = openAIClientFacade;
        _textToImageService = textToImageService;
        _logger = logger;
        _configuration = configuration;
    }

    public async IAsyncEnumerable<ChatChunkResponse> ReplyAsync(UserInformation user, ProfileDefinition profile, ChatRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder();
        var userMessage = request.LastUserQuestion;
        string? finalImageUrl = null;

        string? previousImageUrl = TryExtractPreviousImageUrl(request);

        if (previousImageUrl != null)
        {
            finalImageUrl = await AttemptEditImageAsync(previousImageUrl, userMessage, cancellationToken);
        }

        if (string.IsNullOrEmpty(finalImageUrl))
        {
            finalImageUrl = await GenerateNewImageAsync(userMessage, cancellationToken);
        }

        if (string.IsNullOrEmpty(finalImageUrl))
        {
            _logger.LogWarning("No images could be generated or edited for the user message: {UserMessage}", userMessage);
            yield return new ChatChunkResponse("No images could be generated or edited.", null);
            yield break;
        }

        // Generate enhanced image HTML with border and download button
        var imageId = Guid.NewGuid().ToString("N")[..8];
        var content = ImageHtmlGenerator.GenerateEnhancedImageHtml(finalImageUrl, imageId);

        sb.Append(content);
        yield return new ChatChunkResponse(content);

        var contextData = new ResponseContext(profile.Name, Array.Empty<SupportingContentRecord>(), Array.Empty<ThoughtRecord>(), request.ChatTurnId, request.ChatId, string.Empty, null);
        var result = new ApproachResponse(Answer: sb.ToString(), CitationBaseUrl: string.Empty, contextData);

        yield return new ChatChunkResponse(string.Empty, result);
    }

    private string? TryExtractPreviousImageUrl(ChatRequest request)
    {
        var history = request.History;
        
        // Check for uploaded images on the first chat turn (when history is empty or only contains the current turn)
        bool isFirstChatTurn = history == null || history.Length == 0 || 
                              (history.Length == 1 && string.IsNullOrEmpty(history[0].Assistant));
        
        if (isFirstChatTurn && request.FileUploads?.Any() == true)
        {
            // Look for the first image file upload
            var imageUpload = request.FileUploads.FirstOrDefault(file => 
                file.ContentType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true);
            
            if (imageUpload != null)
            {
                _logger.LogInformation("Found uploaded image for first chat turn: {FileName} with content type: {ContentType}", 
                    imageUpload.FileName, imageUpload.ContentType);
                return imageUpload.DataUrl;
            }
        }

        if (history == null || !history.Any())
        {
            return null;
        }

        var lastTurnWithAssistantMessage = history
            .Where(turn => !string.IsNullOrEmpty(turn.Assistant))
            .LastOrDefault();

        if (lastTurnWithAssistantMessage != null)
        {
            // Look for enhanced HTML image first
            var htmlMatch = Regex.Match(lastTurnWithAssistantMessage.Assistant!, @"<img src=""([^""]+)""");
            if (htmlMatch.Success && htmlMatch.Groups.Count > 1)
            {
                var imageUrl = htmlMatch.Groups[1].Value;
                _logger.LogInformation("Found previous image URL from HTML: {PreviousImageUrl}", imageUrl);
                return imageUrl;
            }

            // Fall back to markdown format
            var markdownMatch = Regex.Match(lastTurnWithAssistantMessage.Assistant!, @"!\[.*?\]\((.*?)\)");
            if (markdownMatch.Success && markdownMatch.Groups.Count > 1)
            {
                var imageUrl = markdownMatch.Groups[1].Value;
                _logger.LogInformation("Found previous image URL from Markdown: {PreviousImageUrl}", imageUrl);
                return imageUrl;
            }
        }
        return null;
    }

    private async Task<string?> AttemptEditImageAsync(string imageUrl, string prompt, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Attempting to edit previous image using URL: {ImageUrl} with prompt: {Prompt}", imageUrl, prompt);
        try
        {
            string? editedImageUrl = await _textToImageService.EditImageFromDataUrlAsync(imageUrl, prompt);
            if (!string.IsNullOrEmpty(editedImageUrl))
            {
                _logger.LogInformation("Successfully edited image. New URL: {EditedImageUrl}", editedImageUrl);
                return editedImageUrl;
            }
            else
            {
                _logger.LogWarning("Editing image with URL {ImageUrl} did not return a new URL. Falling back to new image generation.", imageUrl);
                return null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error editing image with URL {ImageUrl}. Falling back to new image generation.", imageUrl);
            return null;
        }
    }

    private async Task<string?> GenerateNewImageAsync(string prompt, CancellationToken cancellationToken)
    {
        try
        {
            string? imageUrl = await _textToImageService.NewImageAsync(prompt);
            if (!string.IsNullOrEmpty(imageUrl))
            {
                return imageUrl;
            }
            else
            {
                return null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating new image");
            return null;
        }
    }
}
