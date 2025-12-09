using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Extensions.AI;
using Microsoft.Agents.AI;
using Shared.Models;
using System.Reflection;
using OpenAI.Chat;

namespace MinimalApi.Agents
{        
    /// <summary>
    /// Azure AI Agent Management Service using Microsoft Agent Framework
    /// Note: Microsoft Agent Framework doesn't have built-in persistent agent management like the old API
    /// This implementation creates agents on-the-fly
    /// </summary>
    public class AzureAIAgentManagementService : IAgentManagementService
    { 
        private readonly OpenAIClientFacade _openAIClientFacade;
        private readonly IConfiguration _configuration;
        private readonly IChatClient _chatClient;

        public string ProviderType => "AzureAI";

        public AzureAIAgentManagementService(OpenAIClientFacade openAIClientFacade, IConfiguration configuration)
        {    
            _configuration = configuration;
            _openAIClientFacade = openAIClientFacade;
            
            var azureAIFoundryProjectEndpoint = _configuration["AzureAIFoundryProjectEndpoint"];
            ArgumentNullException.ThrowIfNullOrEmpty(azureAIFoundryProjectEndpoint, "AzureAIFoundryProjectEndpoint");
            
            var deploymentName = _configuration["AzureAIFoundryDeploymentName"] ?? "gpt-4o";
            
            ChatClient nativeChatClient = new AzureOpenAIClient(
                new Uri(azureAIFoundryProjectEndpoint),
                new DefaultAzureCredential())
                .GetChatClient(deploymentName);
            
            _chatClient = nativeChatClient.AsIChatClient();
        }

        public async Task<object> CreateAgentIfNotExistsAsync()
        {
            // Microsoft Agent Framework creates agents on-the-fly
            // Return a placeholder object indicating the agent is ready
            return new
            {
                Message = "Microsoft Agent Framework uses on-the-fly agent creation",
                Instructions = LoadEmbeddedResource("MinimalApi.Services.Profile.Prompts.RAGChatSystemPrompt.txt")
            };
        }

        public Task<AgentViewModel> GetAgentAsync(string agentId, CancellationToken cancellationToken = default)
        {
            // Microsoft Agent Framework doesn't have persistent agent storage in the same way
            // Return a mock agent view model
            return Task.FromResult(new AgentViewModel
            {
                Id = agentId,
                Name = "Azure AI Agent",
                Instructions = "Agent created on-the-fly using Microsoft Agent Framework",
                Description = "Microsoft Agent Framework agent",
                Model = "gpt-4o",
                CreatedAt = DateTime.UtcNow,
                Tools = new List<string>()
            });
        }

        public Task<AgentViewModel> CreateAgentAsync(
            string name, 
            string instructions, 
            string? description = null, 
            string model = "gpt-4o", 
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Agent name cannot be null or empty.", nameof(name));
            }

            if (string.IsNullOrWhiteSpace(instructions))
            {
                throw new ArgumentException("Agent instructions cannot be null or empty.", nameof(instructions));
            }

            // Return a view model representing the agent configuration
            // Actual agent will be created on-the-fly when used
            var agentViewModel = new AgentViewModel
            {
                Id = Guid.NewGuid().ToString(),
                Name = name,
                Instructions = instructions,
                Description = description ?? string.Empty,
                Model = model,
                CreatedAt = DateTime.UtcNow,
                Tools = new List<string>()
            };

            return Task.FromResult(agentViewModel);
        }

        public Task<AgentViewModel> UpdateAgentAsync(
            string agentId, 
            string name, 
            string instructions, 
            string? description = null, 
            string model = "gpt-4o", 
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(agentId))
            {
                throw new ArgumentException("Agent ID cannot be null or empty.", nameof(agentId));
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Agent name cannot be null or empty.", nameof(name));
            }

            if (string.IsNullOrWhiteSpace(instructions))
            {
                throw new ArgumentException("Agent instructions cannot be null or empty.", nameof(instructions));
            }

            // Return updated view model
            var agentViewModel = new AgentViewModel
            {
                Id = agentId,
                Name = name,
                Instructions = instructions,
                Description = description ?? string.Empty,
                Model = model,
                CreatedAt = DateTime.UtcNow,
                Tools = new List<string>()
            };

            return Task.FromResult(agentViewModel);
        }

        public Task<IEnumerable<AgentViewModel>> ListAgentsAsync(CancellationToken cancellationToken = default)
        {
            // Microsoft Agent Framework doesn't have persistent agent storage
            // Return an empty list or a list of predefined agents
            var agents = new List<AgentViewModel>();
            
            return Task.FromResult<IEnumerable<AgentViewModel>>(agents);
        }

        public Task<int> DeleteAgentsByNameAsync(string agentName, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(agentName))
            {
                throw new ArgumentException("Agent name cannot be null or empty.", nameof(agentName));
            }

            // Microsoft Agent Framework doesn't have persistent agent storage
            // Return 0 as nothing to delete
            return Task.FromResult(0);
        }

        private string LoadEmbeddedResource(string resourceName)
        {
            var assembly = Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                throw new InvalidOperationException($"Embedded resource '{resourceName}' not found.");
            }
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();      
        }
    }
}
