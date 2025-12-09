// Copyright (c) Microsoft. All rights reserved.

using Azure;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Shared.Models;

namespace MinimalApi.Services.Search;

/// <summary>
/// Service for interacting with Azure AI Search
/// </summary>
public class AzureSearchService
{
    private readonly SearchIndexClient? _searchIndexClient;
    private readonly ILogger<AzureSearchService> _logger;

    public AzureSearchService(IConfiguration configuration, ILogger<AzureSearchService> logger)
    {
        _logger = logger;
        
        var searchEndpoint = configuration["AzureSearchServiceEndpoint"];
        var searchKey = configuration["AzureSearchServiceKey"];

        if (!string.IsNullOrEmpty(searchEndpoint) && !string.IsNullOrEmpty(searchKey))
        {
            try
            {
                var credential = new AzureKeyCredential(searchKey);
                _searchIndexClient = new SearchIndexClient(new Uri(searchEndpoint), credential);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize Azure Search client");
            }
        }
        else
        {
            _logger.LogWarning("Azure Search configuration not found");
        }
    }

    /// <summary>
    /// Gets all indexes from Azure AI Search
    /// </summary>
    public async Task<List<SearchIndexInfo>> GetIndexesAsync(CancellationToken cancellationToken = default)
    {
        var indexes = new List<SearchIndexInfo>();

        if (_searchIndexClient == null)
        {
            _logger.LogWarning("Search client not initialized");
            return indexes;
        }

        try
        {
            await foreach (var index in _searchIndexClient.GetIndexNamesAsync(cancellationToken))
            {
                indexes.Add(new SearchIndexInfo(index));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving search indexes");
        }

        return indexes;
    }

    /// <summary>
    /// Gets detailed information about a specific index
    /// </summary>
    public async Task<SearchIndexInfo?> GetIndexDetailsAsync(string indexName, CancellationToken cancellationToken = default)
    {
        if (_searchIndexClient == null)
        {
            _logger.LogWarning("Search client not initialized");
            return null;
        }

        try
        {
            var response = await _searchIndexClient.GetIndexAsync(indexName, cancellationToken);
            var index = response.Value;

            var indexInfo = new SearchIndexInfo(index.Name);

            foreach (var field in index.Fields)
            {
                indexInfo.Fields.Add(new SearchIndexField
                {
                    Name = field.Name,
                    Type = field.Type.ToString(),
                    IsSearchable = field.IsSearchable ?? false,
                    IsFilterable = field.IsFilterable ?? false,
                    IsSortable = field.IsSortable ?? false,
                    IsFacetable = field.IsFacetable ?? false,
                    IsKey = field.IsKey ?? false,
                    IsRetrievable = field.IsHidden != true
                });
            }

            // Try to get index statistics
            try
            {
                var stats = await _searchIndexClient.GetIndexStatisticsAsync(indexName, cancellationToken);
                indexInfo.DocumentCount = stats.Value.DocumentCount;
                indexInfo.StorageSize = stats.Value.StorageSize;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not retrieve statistics for index {IndexName}", indexName);
            }

            return indexInfo;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving index details for {IndexName}", indexName);
            return null;
        }
    }
}
