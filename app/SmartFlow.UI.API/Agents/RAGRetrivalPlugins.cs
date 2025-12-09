using System.ComponentModel;
using MinimalApi.Services.Search.IndexDefinitions;

namespace Assistants.Hub.API.Assistants.RAG;

/// <summary>
/// RAG Retrieval Plugins for Microsoft Agent Framework
/// Note: This class no longer uses Semantic Kernel's KernelFunction attribute
/// Instead, it provides direct async methods for RAG retrieval
/// </summary>
public class RAGRetrivalPlugins
{
    private readonly SearchClientFactory _searchClientFactory;
    private readonly AzureOpenAIClient _azureOpenAIClient;

    public RAGRetrivalPlugins(SearchClientFactory searchClientFactory, AzureOpenAIClient azureOpenAIClient)
    {
        _searchClientFactory = searchClientFactory;
        _azureOpenAIClient = azureOpenAIClient;
    }

    /// <summary>
    /// Gets relevant information based on the provided search term.
    /// </summary>
    /// <param name="settings">Vector search settings from profile</param>
    /// <param name="searchQuery">The search query</param>
    /// <returns>A list of relevant source information based on the provided search term.</returns>
    public async Task<IEnumerable<KnowledgeSource>> GetKnowledgeSourcesAsync(VectorSearchSettings settings, string searchQuery)
    {
        ArgumentNullException.ThrowIfNull(settings, nameof(settings));

        // Get the appropriate search function based on the index schema definition
        var searchLogic = GetSearchLogic(settings);
        var results = await searchLogic(searchQuery);

        return results;
    }

    private Func<string, Task<IEnumerable<KnowledgeSource>>> GetSearchLogic(VectorSearchSettings settings)
    {
        switch (settings.IndexSchemaDefinition)
        {
            case "KwiecienV2":
                var kwiecienLogic = new SearchLogic<KwiecienCustomIndexDefinitionV2>(
                    _azureOpenAIClient, 
                    _searchClientFactory, 
                    KwiecienCustomIndexDefinitionV2.SelectFieldNames, 
                    KwiecienCustomIndexDefinitionV2.EmbeddingsFieldName, 
                    settings);
                return async (query) => await kwiecienLogic.SearchAsync(query);

            case "AISearchV1":
                var aiSearchLogic = new SearchLogic<AISearchIndexerIndexDefinintion>(
                    _azureOpenAIClient, 
                    _searchClientFactory, 
                    AISearchIndexerIndexDefinintion.SelectFieldNames, 
                    AISearchIndexerIndexDefinintion.EmbeddingsFieldName, 
                    settings);
                return async (query) => await aiSearchLogic.SearchAsync(query);

            case "CustomRutzscoV1":
                var customRutzscoLogic = new SearchLogic<CustomRutzscoV1IndexDefinition>(
                    _azureOpenAIClient, 
                    _searchClientFactory, 
                    CustomRutzscoV1IndexDefinition.SelectFieldNames, 
                    CustomRutzscoV1IndexDefinition.EmbeddingsFieldName, 
                    settings);
                return async (query) => await customRutzscoLogic.SearchAsync(query);

            default:
                throw new ArgumentException($"Unsupported IndexSchemaDefinition: {settings.IndexSchemaDefinition}", 
                    nameof(settings.IndexSchemaDefinition));
        }
    }
}
