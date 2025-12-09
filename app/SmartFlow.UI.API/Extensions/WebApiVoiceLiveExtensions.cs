// Copyright (c) Microsoft. All rights reserved.

using Azure.Core;
using Azure.Identity;
using Microsoft.AspNetCore.Mvc;

namespace MinimalApi.Extensions;

/// <summary>
/// Extension methods for Voice Live API endpoints
/// </summary>
internal static class WebApiVoiceLiveExtensions
{
    internal static WebApplication MapVoiceLiveApi(this WebApplication app)
    {
        var api = app.MapGroup("api/voicelive");

        // Get Voice Live authentication token and configuration
        api.MapGet("token", OnGetVoiceLiveTokenAsync);

        return app;
    }

    private static async Task<IResult> OnGetVoiceLiveTokenAsync(
        HttpContext context,
        [FromServices] IConfiguration configuration,
        [FromServices] ILogger<WebApplication> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation("Getting Voice Live authentication token");

            // Read configuration
            var azureAIFoundryProjectEndpoint = configuration["AzureAIFoundryProjectEndpoint"];
            var agentId = configuration["VoiceLive:AgentId"];
            var speechRegion = configuration["AZURE_SPEECH_REGION"];
            var speechKey = configuration["AZURE_SPEECH_KEY"];

            if (string.IsNullOrEmpty(azureAIFoundryProjectEndpoint))
            {
                logger.LogError("AzureAIFoundryProjectEndpoint is not configured");
                return Results.Problem("Voice Live is not configured. Missing AzureAIFoundryProjectEndpoint.");
            }

            if (string.IsNullOrEmpty(agentId))
            {
                logger.LogError("VoiceLive:AgentId is not configured");
                return Results.Problem("Voice Live is not configured. Missing VoiceLive:AgentId.");
            }

            if (string.IsNullOrEmpty(speechRegion))
            {
                logger.LogError("AZURE_SPEECH_REGION is not configured");
                return Results.Problem("Voice Live is not configured. Missing AZURE_SPEECH_REGION.");
            }

            if (string.IsNullOrEmpty(speechKey))
            {
                logger.LogError("AZURE_SPEECH_KEY is not configured");
                return Results.Problem("Voice Live is not configured. Missing AZURE_SPEECH_KEY.");
            }

            // Extract project name from endpoint (last segment of path)
            var projectUri = new Uri(azureAIFoundryProjectEndpoint);
            var projectName = projectUri.Segments[^1].TrimEnd('/');

            logger.LogInformation("Project Name: {ProjectName}", projectName);
            logger.LogInformation("Agent ID: {AgentId}", agentId);
            logger.LogInformation("Speech Region: {SpeechRegion}", speechRegion);

            // Get Azure AI Foundry agent access token using DefaultAzureCredential
            // We need TWO different tokens with different audiences:
            // 1. agent_access_token → https://ai.azure.com (for AI Foundry Agent)
            // 2. Authorization → https://cognitiveservices.azure.com (for Cognitive Services Gateway)
            
            var credential = new DefaultAzureCredential();
            
            // Get agent access token (for AI Foundry agent)
            var agentTokenContext = new TokenRequestContext(
                scopes: new[] { "https://ai.azure.com/.default" }
            );
            var agentTokenResult = await credential.GetTokenAsync(agentTokenContext, cancellationToken);
            var agentAccessToken = agentTokenResult.Token;
            
            logger.LogInformation("Successfully obtained agent access token with audience: https://ai.azure.com");
            
            // Get authorization token (for Cognitive Services gateway)
            var authTokenContext = new TokenRequestContext(
                scopes: new[] { "https://cognitiveservices.azure.com/.default" }
            );
            var authTokenResult = await credential.GetTokenAsync(authTokenContext, cancellationToken);
            var authorizationToken = authTokenResult.Token;
            
            logger.LogInformation("Successfully obtained authorization token with audience: https://cognitiveservices.azure.com");

            // Build WebSocket URL
            var websocketUrl = $"wss://{projectName}.cognitiveservices.azure.com/voice-agent/realtime";
            var apiVersion = "2025-05-01-preview";

            var response = new VoiceLiveTokenResponse(
                WebSocketUrl: websocketUrl,
                ApiVersion: apiVersion,
                AgentId: agentId,
                ProjectName: projectName,
                AgentAccessToken: agentAccessToken,
                AuthorizationToken: authorizationToken,
                SpeechKey: speechKey,
                SpeechRegion: speechRegion
            );

            // Set headers to prevent caching of tokens
            context.Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate, max-age=0";
            context.Response.Headers["Pragma"] = "no-cache";

            return TypedResults.Ok(response);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting Voice Live authentication token");
            return Results.Problem("Error getting Voice Live authentication token: " + ex.Message);
        }
    }
}
