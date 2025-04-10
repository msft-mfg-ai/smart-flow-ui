﻿// Copyright (c) Microsoft. All rights reserved.

using System.ComponentModel;
using MinimalApi.Services.Search.IndexDefinitions;


namespace MinimalApi.Services.Skills;

public sealed class RetrieveRelatedDocumentSkillKM
{
    private readonly AppConfiguration _configuration;
    private readonly SearchClientFactory _searchClientFactory;
    private readonly AzureOpenAIClient _openAIClient;

    public RetrieveRelatedDocumentSkillKM(AppConfiguration config, SearchClientFactory searchClientFactory, AzureOpenAIClient openAIClient)
    {
        _configuration= config;
        _searchClientFactory = searchClientFactory;
        _openAIClient = openAIClient;
    }

    [KernelFunction("Retrieval"), Description("Search more information")]
    public async Task<string> RetrievalV2Async([Description("search query")] string searchQuery, KernelArguments arguments)
    {
        var profile = arguments[ContextVariableOptions.Profile] as ProfileDefinition;

        ArgumentNullException.ThrowIfNull(profile, "Profile is not set.");
        ArgumentNullException.ThrowIfNull(profile.RAGSettings, "Profile RAGSettings not set.");


        searchQuery = searchQuery.Replace("\"", string.Empty);
        arguments[ContextVariableOptions.Intent] = searchQuery;
        if (profile.RAGSettings.ProfileUserSelectionOptions != null && profile.RAGSettings.ProfileUserSelectionOptions.Any())
        {
            var sb = new StringBuilder();
            int count = 0;
            foreach (var o in profile.RAGSettings.ProfileUserSelectionOptions)
            {
                if (arguments.ContainsName(o.DisplayName) == false)
                    continue;

                var value = arguments[o.DisplayName] as string;
                if (!string.IsNullOrWhiteSpace(value))
                {
                    if (profile.RAGSettings.ProfileUserSelectionOptions.Count() > 1 && count > 0)
                        sb.Append(" and ");

                    var filter = $"{o.IndexFieldName} eq '{arguments[o.DisplayName]}'";
                    sb.Append(filter);
                    count++;
                }
            }

            if (sb.Length > 0)
                arguments[ContextVariableOptions.SelectedFilters] = sb.ToString();
        }

        var searchLogic = ResolveRetrievalLogic(_openAIClient, _searchClientFactory, profile.RAGSettings, _configuration.AOAIEmbeddingsDeployment, profile.RAGSettings.DocumentRetrievalPluginQueryFunctionName);
        var result = await searchLogic(searchQuery, arguments);

        if (!result.Sources.Any())
        {
            arguments[ContextVariableOptions.Knowledge] = "NO_SOURCES";
            return "NO_SOURCES";
        }

        //arguments[ContextVariableOptions.Knowledge] = result.FormattedSourceText;
        //arguments[ContextVariableOptions.KnowledgeSummary] = result;


        arguments[ContextVariableOptions.SelectedFilters] = "type eq 'Competitor'";
        var searchLogicV2 = ResolveRetrievalLogic(_openAIClient, _searchClientFactory, profile.RAGSettings, _configuration.AOAIEmbeddingsDeployment, profile.RAGSettings.DocumentRetrievalPluginQueryFunctionName);
        var resultV2 = await searchLogic(searchQuery, arguments);

        var rsb  = new StringBuilder();
        rsb.AppendLine("COMPETITION SOURCES");
        rsb.AppendLine(resultV2.FormattedSourceText);
        rsb.AppendLine("INDUSTRY SOURCES");
        rsb.AppendLine(result.FormattedSourceText);

        arguments[ContextVariableOptions.Knowledge] = rsb.ToString();
        arguments[ContextVariableOptions.KnowledgeSummary] = result;

        return result.FormattedSourceText;
    }

    private Func<string, KernelArguments,Task<KnowledgeSourceSummary>> ResolveRetrievalLogic(AzureOpenAIClient client, SearchClientFactory factory, RAGSettingsSummary ragSettings, string embeddingModelName, string version)
    {
        async Task<KnowledgeSourceSummary> func1(string searchQuery, KernelArguments arguments)
        {
            var logic = new SearchLogic<AIStudioIndexDefinition>(client, factory, ragSettings.DocumentRetrievalIndexName, embeddingModelName, AIStudioIndexDefinition.EmbeddingsFieldName, AIStudioIndexDefinition.SelectFieldNames, ResolveDocumentCount(ragSettings.DocumentRetrievalDocumentCount), ragSettings.DocumentRetrievalMaxSourceTokens);
            return await logic.SearchAsync(searchQuery, arguments);
        }

        async Task<KnowledgeSourceSummary> func2(string searchQuery, KernelArguments arguments)
        {
            var logic = new SearchLogic<AISearchIndexerIndexDefinintion>(client, factory, ragSettings.DocumentRetrievalIndexName, embeddingModelName, AISearchIndexerIndexDefinintion.EmbeddingsFieldName, AISearchIndexerIndexDefinintion.SelectFieldNames, ResolveDocumentCount(ragSettings.DocumentRetrievalDocumentCount), ragSettings.DocumentRetrievalMaxSourceTokens);
            return await logic.SearchAsync(searchQuery, arguments);
        }

        async Task<KnowledgeSourceSummary> func3(string searchQuery, KernelArguments arguments)
        {
            var logic = new SearchLogic<KwiecienCustomIndexDefinition>(client, factory, ragSettings.DocumentRetrievalIndexName, embeddingModelName, KwiecienCustomIndexDefinition.EmbeddingsFieldName, KwiecienCustomIndexDefinition.SelectFieldNames, ResolveDocumentCount(ragSettings.DocumentRetrievalDocumentCount), ragSettings.DocumentRetrievalMaxSourceTokens);
            return await logic.SearchAsync(searchQuery, arguments);
        }

        async Task<KnowledgeSourceSummary> func4(string searchQuery, KernelArguments arguments)
        {
            var logic = new SearchLogic<KwiecienCustomIndexDefinitionV2>(client, factory, ragSettings.DocumentRetrievalIndexName, embeddingModelName, KwiecienCustomIndexDefinitionV2.EmbeddingsFieldName, KwiecienCustomIndexDefinitionV2.SelectFieldNames, ResolveDocumentCount(ragSettings.DocumentRetrievalDocumentCount), ragSettings.DocumentRetrievalMaxSourceTokens);
            return await logic.SearchAsync(searchQuery, arguments);
        }

        if (version == AIStudioIndexDefinition.Name)
            return func1;
        if (version == AISearchIndexerIndexDefinintion.Name)
            return func2;
        if (version == KwiecienCustomIndexDefinition.Name)
            return func3;
        if (version == KwiecienCustomIndexDefinitionV2.Name)
            return func4;

        throw new InvalidOperationException("Invalid search implementation.");
    }

    private int ResolveDocumentCount(int documentRetrievalDocumentCount) => documentRetrievalDocumentCount > 0 ? documentRetrievalDocumentCount : _configuration.SearchIndexDocumentCount;
}
