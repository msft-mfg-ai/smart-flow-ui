// Copyright (c) Microsoft. All rights reserved.

namespace MinimalApi.Agents;

/// <summary>
/// Factory for creating the appropriate agent management service based on configuration
/// </summary>
public class AgentManagementServiceFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AgentManagementServiceFactory> _logger;

    public AgentManagementServiceFactory(
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        ILogger<AgentManagementServiceFactory> logger)
    {
        _serviceProvider = serviceProvider;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Creates the appropriate agent management service based on configuration.
    /// Priority: CustomAgentEndpoint > AzureAIFoundryProjectEndpoint
    /// </summary>
    public IAgentManagementService CreateAgentManagementService()
    {
        // Check for custom endpoint configuration first
        var customEndpoint = _configuration["CustomAgentEndpoint"];
        if (!string.IsNullOrWhiteSpace(customEndpoint))
        {
            _logger.LogInformation("Using Custom Endpoint Agent Management Service: {Endpoint}", customEndpoint);
            return _serviceProvider.GetRequiredService<CustomEndpointAgentManagementService>();
        }

        // Fall back to Azure AI
        var azureEndpoint = _configuration["AzureAIFoundryProjectEndpoint"];
        if (!string.IsNullOrWhiteSpace(azureEndpoint))
        {
            _logger.LogInformation("Using Azure AI Agent Management Service: {Endpoint}", azureEndpoint);
            return _serviceProvider.GetRequiredService<AzureAIAgentManagementService>();
        }

        throw new InvalidOperationException(
            "No agent management endpoint configured. Please configure either 'CustomAgentEndpoint' or 'AzureAIFoundryProjectEndpoint'.");
    }
}
