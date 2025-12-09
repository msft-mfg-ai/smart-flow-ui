// Copyright (c) Microsoft. All rights reserved.

using Shared.Models;

namespace MinimalApi.Extensions;

/// <summary>
/// Endpoints for search indexing operations that proxy to the Agent-Hub-JCI backend.
/// </summary>
internal static class WebApiSearchEndpoints
{
    private const string DocumentToolsEndpointKey = "DocumentToolsAPIEndpoint";
    private const string DocumentToolsApiKeyKey = "DocumentToolsAPIKey";

    internal static WebApplication MapSearchApi(this WebApplication app)
    {
        var api = app.MapGroup("api/search");

        // Trigger push-based document indexing
        api.MapPost("process", OnTriggerPushIndexingAsync);

        // Get indexing job status
        api.MapGet("status/{correlationId}", OnGetIndexingStatusAsync);

        return app;
    }

    /// <summary>
    /// Triggers push-based document indexing by proxying to Agent-Hub-JCI backend.
    /// </summary>
    private static async Task<IResult> OnTriggerPushIndexingAsync(
        HttpContext context,
        [FromBody] PushIndexingRequest request,
        [FromServices] IHttpClientFactory httpClientFactory,
        [FromServices] IConfiguration configuration,
        [FromServices] ILogger<WebApplication> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var agentHubEndpoint = configuration[DocumentToolsEndpointKey];
            if (string.IsNullOrEmpty(agentHubEndpoint))
            {
                logger.LogError("DocumentToolsAPIEndpoint not configured in appsettings");
                return Results.Problem("Search indexing service endpoint not configured");
            }

            // Prepare request for Agent-Hub-JCI
            var backendRequest = new
            {
                container_name = request.ContainerName,
                recreate_index = request.RecreateIndex
            };

            var targetUrl = $"{agentHubEndpoint}/api/search/process";
            logger.LogInformation("Calling Agent-Hub API for push indexing at {Url} for container {ContainerName}", 
                targetUrl, request.ContainerName);

            var httpClient = httpClientFactory.CreateClient();
            httpClient.Timeout = TimeSpan.FromMinutes(5);

            // Add API key if configured
            var apiKey = configuration[DocumentToolsApiKeyKey];
            if (!string.IsNullOrEmpty(apiKey))
            {
                httpClient.DefaultRequestHeaders.Add("X-API-Key", apiKey);
            }

            var response = await httpClient.PostAsJsonAsync(targetUrl, backendRequest, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogInformation("Successfully triggered push indexing for {ContainerName}: {Response}", 
                    request.ContainerName, responseContent);
                
                // Parse and return the response
                var result = System.Text.Json.JsonSerializer.Deserialize<PushIndexingResponse>(
                    responseContent, 
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                
                return TypedResults.Ok(result);
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogError("Failed to trigger push indexing for {ContainerName}: Status={Status}, Error={Error}", 
                    request.ContainerName, response.StatusCode, errorContent);
                return Results.Problem($"Failed to trigger indexing: {response.StatusCode} - {errorContent}");
            }
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "HTTP error calling Agent-Hub API for push indexing");
            return Results.Problem("Error connecting to search indexing service");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error triggering push indexing for container {ContainerName}", request.ContainerName);
            return Results.Problem("Error triggering document indexing");
        }
    }

    /// <summary>
    /// Gets the status of an indexing job by proxying to Agent-Hub-JCI backend.
    /// </summary>
    private static async Task<IResult> OnGetIndexingStatusAsync(
        HttpContext context,
        string correlationId,
        [FromServices] IHttpClientFactory httpClientFactory,
        [FromServices] IConfiguration configuration,
        [FromServices] ILogger<WebApplication> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var agentHubEndpoint = configuration[DocumentToolsEndpointKey];
            if (string.IsNullOrEmpty(agentHubEndpoint))
            {
                logger.LogError("DocumentToolsAPIEndpoint not configured in appsettings");
                return Results.Problem("Search indexing service endpoint not configured");
            }

            var targetUrl = $"{agentHubEndpoint}/api/search/status/{correlationId}";
            logger.LogInformation("Calling Agent-Hub API for indexing status at {Url}", targetUrl);

            var httpClient = httpClientFactory.CreateClient();
            httpClient.Timeout = TimeSpan.FromSeconds(30);

            // Add API key if configured
            var apiKey = configuration[DocumentToolsApiKeyKey];
            if (!string.IsNullOrEmpty(apiKey))
            {
                httpClient.DefaultRequestHeaders.Add("X-API-Key", apiKey);
            }

            var response = await httpClient.GetAsync(targetUrl, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogDebug("Indexing status for {CorrelationId}: {Response}", correlationId, responseContent);
                
                // Parse and return the response
                var result = System.Text.Json.JsonSerializer.Deserialize<PushIndexingStatusResponse>(
                    responseContent, 
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                
                return TypedResults.Ok(result);
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                logger.LogWarning("Indexing job not found for correlation ID: {CorrelationId}", correlationId);
                return Results.NotFound($"Indexing job not found: {correlationId}");
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogError("Failed to get indexing status for {CorrelationId}: Status={Status}, Error={Error}", 
                    correlationId, response.StatusCode, errorContent);
                return Results.Problem($"Failed to get indexing status: {response.StatusCode}");
            }
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "HTTP error calling Agent-Hub API for indexing status");
            return Results.Problem("Error connecting to search indexing service");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting indexing status for {CorrelationId}", correlationId);
            return Results.Problem("Error getting indexing status");
        }
    }
}
