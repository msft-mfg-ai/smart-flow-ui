// Copyright (c) Microsoft. All rights reserved.

using System.Text.Json.Serialization;

namespace MinimalApi;

public class AppConfiguration
{
    public string DataProtectionKeyContainer { get; private set; } = "dataprotectionkeys";

    public bool EnableDataProtectionBlobKeyStorage { get; private set; }

    public string UserDocumentUploadBlobStorageContentContainer { get; private set; } = "content";
    public string UserDocumentUploadBlobStorageExtractContainer { get; private set; } = "content-extract";


    public bool UseManagedIdentityResourceAccess { get; init; }
    public string UserAssignedManagedIdentityClientId { get; init; }
    public string VisualStudioTenantId { get; init; }


    // CosmosDB
    public string? CosmosDbEndpoint { get; init; }

    public string? CosmosDBConnectionString { get; init; }


    // Azure Search
    public string? AzureSearchServiceEndpoint { get; init; }

    public string? AzureSearchServiceKey { get; init; }

    public string? AzureSearchServiceIndexName { get; init; }


    // Azure Storage
    public string AzureStorageAccountEndpoint { get; init; }
    public string AzureStorageAccountConnectionString { get; init; }

    public string AzureStorageUserUploadContainer { get; init; }

    public string AzureStorageContainer { get; init; }


    public string DocumentUploadStrategy { get; init; } = "AzureNative";


    // Ingestion Pipeline
    public string? IngestionPipelineAPI { get; init; }

    public string IngestionPipelineAPIKey { get; init; }


    [JsonPropertyName("APPLICATIONINSIGHTS_CONNECTION_STRING")]
    public string? ApplicationInsightsConnectionString { get; set; }

    // On-Behalf-Of (OBO) Flow
    [JsonPropertyName("AZURE_SP_CLIENT_ID")]
    public string? AzureServicePrincipalClientID { get; set; }
    [JsonPropertyName("AZURE_SP_CLIENT_SECRET")]
    public string? AzureServicePrincipalClientSecret { get; set; }
    [JsonPropertyName("AZURE_TENANT_ID")]
    public string? AzureTenantID { get; set; }
    [JsonPropertyName("AZURE_AUTHORITY_HOST")]
    public string? AzureAuthorityHost { get; set; }
    [JsonPropertyName("AZURE_SP_OPENAI_AUDIENCE")]
    public string? AzureServicePrincipalOpenAIAudience { get; set; }

    public string OcpApimSubscriptionHeaderName { get; init; } = "Ocp-Apim-Subscription-Key";
    public string OcpApimSubscriptionKey { get; init; } = "Ocp-Apim-Subscription-Key";
    public string XMsTokenAadAccessToken { get; init; } = "X-MS-TOKEN-AAD-ACCESS-TOKEN";

    public string? AOAIStandardChatGptDeployment { get; init; }
    public string? AOAIEmbeddingsDeploymentKey { get; init; }
    public string? AOAIStandardServiceEndpoint { get; init; }
    public string? AOAIStandardServiceKey { get; init; }

    // Azure AI Gateway / APIM
    public string? AzureAIGatewayAPIMKey { get; init; }

    // Azure AI Projects
    public string? AzureAIFoundryProjectEndpoint { get; init; }

    // Profile configuration
    public string? ProfileConfigurationBlobStorageContainer { get; init; }
    public string? ProfileConfiguration { get; init; }
    public string ProfileFileName { get; init; } = "profiles";
}
