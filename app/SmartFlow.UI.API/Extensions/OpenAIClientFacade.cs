using Azure;
using Azure.Core;
using Assistants.Hub.API.Assistants.RAG;
using Azure.Core.Pipeline;
using System.ClientModel.Primitives;
using Microsoft.Extensions.AI;
using OpenAI.Chat;

namespace MinimalApi.Extensions;

/// <summary>
/// Facade for OpenAI client operations, migrated to Microsoft Agent Framework
/// </summary>
public class OpenAIClientFacade
{
    private readonly AppConfiguration _config;
    private readonly string _standardChatGptDeployment;
    private readonly string? _embeddingsDeploymentKey;
    private readonly string _standardServiceEndpoint;
    private readonly TokenCredential _tokenCredential;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly SearchClientFactory _searchClientFactory;
    private readonly AzureKeyCredential _azureKeyCredential;
    private readonly string _apimKey;

    private readonly AzureOpenAIClient _standardChatGptClient;
    private readonly AzureOpenAIClient _standardEmbeddingsClient;
    private readonly IChatClient _chatClient;

    public OpenAIClientFacade(AppConfiguration configuration, AzureKeyCredential azureKeyCredential, TokenCredential tokenCredential, IHttpClientFactory httpClientFactory, SearchClientFactory searchClientFactory, string apimKey = null)
    {
        ArgumentNullException.ThrowIfNull(configuration, "AppConfiguration");
        ArgumentNullException.ThrowIfNull(configuration.AOAIStandardChatGptDeployment, "AOAIStandardChatGptDeployment");
        ArgumentNullException.ThrowIfNull(configuration.AOAIStandardServiceEndpoint, "AOAIStandardServiceEndpoint");

        _config = configuration;
        _standardChatGptDeployment = _config.AOAIStandardChatGptDeployment;
        _embeddingsDeploymentKey = _config.AOAIEmbeddingsDeploymentKey;
        _standardServiceEndpoint = _config.AOAIStandardServiceEndpoint;

        _azureKeyCredential = azureKeyCredential;
        _tokenCredential = tokenCredential;
        _httpClientFactory = httpClientFactory;
        _searchClientFactory = searchClientFactory;
        _apimKey = apimKey;

        // Create chat client with APIM key header if provided
        if (!string.IsNullOrEmpty(_apimKey))
        {
            var httpClient = _httpClientFactory.CreateClient();
            httpClient.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", _apimKey);
            
            var clientOptions = new AzureOpenAIClientOptions
            {
                Transport = new HttpClientPipelineTransport(httpClient)
            };

            if (azureKeyCredential != null)
                _standardChatGptClient = new AzureOpenAIClient(new Uri(_standardServiceEndpoint), _azureKeyCredential, clientOptions);
            else
                _standardChatGptClient = new AzureOpenAIClient(new Uri(_standardServiceEndpoint), _tokenCredential, clientOptions);
        }
        else
        {
            if (azureKeyCredential != null)
                _standardChatGptClient = new AzureOpenAIClient(new Uri(_standardServiceEndpoint), _azureKeyCredential);
            else
                _standardChatGptClient = new AzureOpenAIClient(new Uri(_standardServiceEndpoint), _tokenCredential);
        }

        // Create the IChatClient for Agent Framework
        ChatClient nativeChatClient = _standardChatGptClient.GetChatClient(_standardChatGptDeployment);
        _chatClient = nativeChatClient.AsIChatClient();

        // Create embeddings client if key is configured
        if (!string.IsNullOrEmpty(_embeddingsDeploymentKey))
        {
            var embeddingsKeyCredential = new AzureKeyCredential(_embeddingsDeploymentKey);
            
            if (!string.IsNullOrEmpty(_embeddingsDeploymentKey))
            {
                var httpClient = _httpClientFactory.CreateClient();
                httpClient.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", _embeddingsDeploymentKey);
                
                var clientOptions = new AzureOpenAIClientOptions
                {
                    Transport = new HttpClientPipelineTransport(httpClient)
                };

                _standardEmbeddingsClient = new AzureOpenAIClient(new Uri(_standardServiceEndpoint), embeddingsKeyCredential, clientOptions);
            }
            else
            {
                _standardEmbeddingsClient = new AzureOpenAIClient(new Uri(_standardServiceEndpoint), embeddingsKeyCredential);
            }
        }
    }

    /// <summary>
    /// Gets the deployment name for the chat model
    /// </summary>
    public string GetKernelDeploymentName()
    {
        return _standardChatGptDeployment;
    }

    /// <summary>
    /// Gets the Azure OpenAI client for chat operations
    /// </summary>
    public AzureOpenAIClient GetChatClient()
    {
        return _standardChatGptClient;
    }

    /// <summary>
    /// Gets the IChatClient for Microsoft Agent Framework operations
    /// </summary>
    public IChatClient GetAgentFrameworkChatClient()
    {
        return _chatClient;
    }

    /// <summary>
    /// Gets the Azure OpenAI client for embeddings operations
    /// </summary>
    public AzureOpenAIClient GetEmbeddingsClient()
    {
        return _standardEmbeddingsClient ?? _standardChatGptClient;
    }

    /// <summary>
    /// Gets the search client factory for RAG operations
    /// </summary>
    public SearchClientFactory GetSearchClientFactory()
    {
        return _searchClientFactory;
    }

    /// <summary>
    /// Creates a new IChatClient instance for the specified deployment
    /// </summary>
    public IChatClient CreateChatClient(string? deploymentName = null)
    {
        var deployment = deploymentName ?? _standardChatGptDeployment;
        ChatClient nativeChatClient = _standardChatGptClient.GetChatClient(deployment);
        return nativeChatClient.AsIChatClient();
    }
}
