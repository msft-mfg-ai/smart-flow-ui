// Copyright (c) Microsoft. All rights reserved.

namespace Shared.Models;

public class ProfileDefinition
{
    public ProfileDefinition()
    {
        Name = "Undefined";
        Id = string.Empty;
        Approach = string.Empty;
        SecurityModel = string.Empty;
        SecurityModelGroupMembership = new List<string>();
        SampleQuestions = new List<string>();
        UserPromptTemplates = new List<UserPromptTemplate>();
        ChatSystemMessageFile = string.Empty;
        ChatSystemMessage = string.Empty;
    }
    
    public ProfileDefinition(string name)
    {
        Name = name;
        Id = string.Empty;
        Approach = string.Empty;
        SecurityModel = string.Empty;
        SecurityModelGroupMembership = new List<string>();
        SampleQuestions = new List<string>();
        UserPromptTemplates = new List<UserPromptTemplate>();
        ChatSystemMessageFile = string.Empty;
        ChatSystemMessage = string.Empty;
    }
    
    public ProfileDefinition(
        string name,
        string id,
        string approach,
        string securityModel,
        bool allowFileUpload,
        string? azureAIAgentID,
        List<string> securityModelGroupMembership,
        List<string> sampleQuestions,
        RAGSettingsSummary? ragSettingsSummary,
        AssistantEndpointSettingsSummary? assistantEndpointSettingsSummary)
    {
        Name = name;
        Id = id;
        Approach = approach;
        SecurityModel = securityModel;
        AllowFileUpload = allowFileUpload;
        AzureAIAgentID = azureAIAgentID;
        SampleQuestions = sampleQuestions;
        RAGSettings = ragSettingsSummary;
        AssistantEndpointSettings = assistantEndpointSettingsSummary;
        SecurityModelGroupMembership = securityModelGroupMembership ?? ([]);
        UserPromptTemplates = new List<UserPromptTemplate>();
        ChatSystemMessageFile = string.Empty;
        ChatSystemMessage = string.Empty;
    }

    public string Name { get; set; } = "Undefined";
    public string Id { get; set; } = string.Empty;
    public string Approach { get; set; } = string.Empty;
    public string SecurityModel { get; set; } = string.Empty;
    public bool AllowFileUpload { get; set; }
    public List<string> SecurityModelGroupMembership { get; set; } = new();
    public RAGSettingsSummary? RAGSettings { get; set; }
    public AssistantEndpointSettingsSummary? AssistantEndpointSettings { get; set; }
    public string ChatSystemMessageFile { get; set; } = string.Empty;
    public string ChatSystemMessage { get; set; } = string.Empty;
    public List<string> SampleQuestions { get; set; } = new();
    public List<UserPromptTemplate> UserPromptTemplates { get; set; } = new();
    public string? AzureAIAgentID { get; set; }
}

public class RAGSettingsSummary
{
    public string DocumentRetrievalSchema { get; set; } = string.Empty;
    public string DocumentRetrievalEmbeddingsDeployment { get; set; } = string.Empty;
    public string DocumentRetrievalIndexName { get; set; } = string.Empty;
    public string? DocumentIndexerName { get; set; }
    public int DocumentRetrievalDocumentCount { get; set; }
    public int DocumentRetrievalMaxSourceTokens { get; set; } = 12000;
    public string ChatSystemMessage { get; set; } = string.Empty;
    public string ChatUserMessage { get; set; } = string.Empty;
    public string ChatSystemMessageFile { get; set; } = string.Empty;
    public string StorageContianer { get; set; } = string.Empty;
    public bool CitationUseSourcePage { get; set; }
    public bool UseSemanticRanker { get; set; }
    public string? SemanticConfigurationName { get; set; }
    public int KNearestNeighborsCount { get; set; } = 3;
    public bool Exhaustive { get; set; } = false;
    
    public IEnumerable<ProfileUserSelectionOption> ProfileUserSelectionOptions { get; set; } = new List<ProfileUserSelectionOption>();
}

public class ProfileUserSelectionOption
{
    public string DisplayName { get; set; } = string.Empty;
    public string IndexFieldName { get; set; } = string.Empty;
}

public class DocumentCollectionRAGSettings
{
    public string GenerateSearchQueryPluginName { get; set; } = string.Empty;
    public string GenerateSearchQueryPluginQueryFunctionName { get; set; } = string.Empty;
    public string DocumentRetrievalPluginName { get; set; } = string.Empty;
    public string DocumentRetrievalPluginQueryFunctionName { get; set; } = string.Empty;
    public string DocumentRetrievalIndexName { get; set; } = string.Empty;
    public int DocumentRetrievalDocumentCount { get; set; }
    public string ChatSystemMessageFile { get; set; } = string.Empty;
    public string StorageContianer { get; set; } = string.Empty;
}

public class AssistantEndpointSettingsSummary
{
    public string APIEndpointSetting { get; set; } = string.Empty;
    public string APIEndpointKeySetting { get; set; } = string.Empty;
    public bool AllowFileUpload { get; set; }
}
