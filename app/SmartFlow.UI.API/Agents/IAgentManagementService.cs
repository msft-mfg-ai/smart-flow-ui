// Copyright (c) Microsoft. All rights reserved.

using Shared.Models;

namespace MinimalApi.Agents;

/// <summary>
/// Interface for agent management operations across different agent providers
/// </summary>
public interface IAgentManagementService
{
    /// <summary>
    /// Lists all agents from the provider
    /// </summary>
    Task<IEnumerable<AgentViewModel>> ListAgentsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a specific agent by ID (includes system prompt if available)
    /// </summary>
    Task<AgentViewModel> GetAgentAsync(string agentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing agent's system prompt
    /// </summary>
    Task<AgentViewModel> UpdateAgentAsync(string agentId, string name, string instructions, string? description = null, string model = "gpt-4o", CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes custom system prompts for agents by name (reverts to default)
    /// </summary>
    Task<int> DeleteAgentsByNameAsync(string agentName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new agent (may not be supported by all providers)
    /// </summary>
    Task<AgentViewModel> CreateAgentAsync(string name, string instructions, string? description = null, string model = "gpt-4o", CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the provider type (e.g., "AzureAI", "CustomEndpoint")
    /// </summary>
    string ProviderType { get; }
}
