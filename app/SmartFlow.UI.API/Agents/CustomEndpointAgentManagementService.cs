// Copyright (c) Microsoft. All rights reserved.

using Shared.Models;
using System.Text;
using System.Text.Json;

namespace MinimalApi.Agents;

/// <summary>
/// Agent management service that communicates with a custom REST endpoint
/// </summary>
public class CustomEndpointAgentManagementService : IAgentManagementService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<CustomEndpointAgentManagementService> _logger;
    private readonly string _endpointUrl;
    private readonly string? _apiKey;

    public string ProviderType => "CustomEndpoint";

    public CustomEndpointAgentManagementService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<CustomEndpointAgentManagementService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;

        // Load endpoint configuration
        _endpointUrl = _configuration["CustomAgentEndpoint"] ?? string.Empty;
        _apiKey = _configuration["CustomAgentApiKey"];

        if (string.IsNullOrWhiteSpace(_endpointUrl))
        {
            _logger.LogWarning("CustomAgentEndpoint is not configured. Custom endpoint agent management will not be available.");
        }
    }

    public async Task<IEnumerable<AgentViewModel>> ListAgentsAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_endpointUrl))
        {
            throw new InvalidOperationException("CustomAgentEndpoint is not configured.");
        }

        try
        {
            using var httpClient = CreateHttpClient();
            var response = await httpClient.GetAsync($"{_endpointUrl}/agent-management/agents", cancellationToken);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var agents = JsonSerializer.Deserialize<List<AgentViewModel>>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return agents ?? new List<AgentViewModel>();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to list agents from custom endpoint: {Endpoint}", _endpointUrl);
            throw new InvalidOperationException($"Failed to communicate with custom agent endpoint: {ex.Message}", ex);
        }
    }

    public async Task<AgentViewModel> GetAgentAsync(string agentId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_endpointUrl))
        {
            throw new InvalidOperationException("CustomAgentEndpoint is not configured.");
        }

        if (string.IsNullOrWhiteSpace(agentId))
        {
            throw new ArgumentException("Agent ID cannot be null or empty.", nameof(agentId));
        }

        try
        {
            using var httpClient = CreateHttpClient();
            
            // Get agent metadata
            var agentResponse = await httpClient.GetAsync(
                $"{_endpointUrl}/agent-management/agents/{Uri.EscapeDataString(agentId)}", 
                cancellationToken);
            agentResponse.EnsureSuccessStatusCode();

            var agentContent = await agentResponse.Content.ReadAsStringAsync(cancellationToken);
            var agent = JsonSerializer.Deserialize<AgentViewModel>(agentContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (agent == null)
            {
                throw new InvalidOperationException("Failed to deserialize agent response from custom endpoint.");
            }

            // Get system prompt
            try
            {
                var promptResponse = await httpClient.GetAsync(
                    $"{_endpointUrl}/agent-management/agents/{Uri.EscapeDataString(agentId)}/system-prompt", 
                    cancellationToken);
                
                if (promptResponse.IsSuccessStatusCode)
                {
                    var promptContent = await promptResponse.Content.ReadAsStringAsync(cancellationToken);
                    var promptData = JsonSerializer.Deserialize<SystemPromptResponse>(promptContent, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    
                    if (promptData != null && !string.IsNullOrWhiteSpace(promptData.SystemPrompt))
                    {
                        agent.Instructions = promptData.SystemPrompt;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get system prompt for agent {AgentId}, using default", agentId);
            }

            return agent;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to get agent from custom endpoint: {Endpoint}", _endpointUrl);
            throw new InvalidOperationException($"Failed to communicate with custom agent endpoint: {ex.Message}", ex);
        }
    }

    public async Task<AgentViewModel> UpdateAgentAsync(
        string agentId,
        string name,
        string instructions,
        string? description = null,
        string model = "gpt-4o",
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_endpointUrl))
        {
            throw new InvalidOperationException("CustomAgentEndpoint is not configured.");
        }

        if (string.IsNullOrWhiteSpace(agentId))
        {
            throw new ArgumentException("Agent ID cannot be null or empty.", nameof(agentId));
        }

        if (string.IsNullOrWhiteSpace(instructions))
        {
            throw new ArgumentException("Agent instructions cannot be null or empty.", nameof(instructions));
        }

        try
        {
            using var httpClient = CreateHttpClient();
            
            // Update system prompt using the dedicated endpoint
            var promptData = new
            {
                system_prompt = instructions
            };

            var jsonContent = JsonSerializer.Serialize(promptData);
            using var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            var response = await httpClient.PutAsync(
                $"{_endpointUrl}/agent-management/agents/{Uri.EscapeDataString(agentId)}/system-prompt", 
                content, 
                cancellationToken);
            response.EnsureSuccessStatusCode();

            // Fetch the updated agent to return
            return await GetAgentAsync(agentId, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to update agent at custom endpoint: {Endpoint}", _endpointUrl);
            throw new InvalidOperationException($"Failed to communicate with custom agent endpoint: {ex.Message}", ex);
        }
    }

    public async Task<int> DeleteAgentsByNameAsync(string agentName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_endpointUrl))
        {
            throw new InvalidOperationException("CustomAgentEndpoint is not configured.");
        }

        if (string.IsNullOrWhiteSpace(agentName))
        {
            throw new ArgumentException("Agent name cannot be null or empty.", nameof(agentName));
        }

        try
        {
            // For the custom endpoint, we'll delete the custom prompt for agents matching the name
            // First, get all agents
            var agents = await ListAgentsAsync(cancellationToken);
            var matchingAgents = agents.Where(a => 
                string.Equals(a.Name, agentName, StringComparison.OrdinalIgnoreCase)).ToList();

            if (!matchingAgents.Any())
            {
                return 0;
            }

            using var httpClient = CreateHttpClient();
            int deletedCount = 0;

            // Delete custom prompt for each matching agent
            foreach (var agent in matchingAgents)
            {
                try
                {
                    var response = await httpClient.DeleteAsync(
                        $"{_endpointUrl}/agent-management/agents/{Uri.EscapeDataString(agent.Id)}/system-prompt", 
                        cancellationToken);
                    
                    if (response.IsSuccessStatusCode)
                    {
                        deletedCount++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete custom prompt for agent {AgentId}", agent.Id);
                }
            }

            return deletedCount;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to delete agents from custom endpoint: {Endpoint}", _endpointUrl);
            throw new InvalidOperationException($"Failed to communicate with custom agent endpoint: {ex.Message}", ex);
        }
    }

    // Not implemented for custom endpoint
    public Task<AgentViewModel> CreateAgentAsync(
        string name,
        string instructions,
        string? description = null,
        string model = "gpt-4o",
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("Creating agents is not supported for custom endpoints.");
    }

    private HttpClient CreateHttpClient()
    {
        var httpClient = _httpClientFactory.CreateClient();
        
        if (!string.IsNullOrWhiteSpace(_apiKey))
        {
            httpClient.DefaultRequestHeaders.Add("api-key", _apiKey);
        }

        return httpClient;
    }

    private class SystemPromptResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("system_prompt")]
        public string? SystemPrompt { get; set; }
    }
}
