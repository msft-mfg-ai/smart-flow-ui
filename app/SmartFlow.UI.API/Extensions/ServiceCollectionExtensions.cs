// Copyright (c) Microsoft. All rights reserved.

using Azure;
using Azure.Core;
using Azure.Identity;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Azure.Cosmos.Fluent;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MinimalApi.Services.HealthChecks;
using System.ClientModel.Primitives;
using Microsoft.Extensions.Azure;
using MinimalApi.Agents;

namespace MinimalApi.Extensions;

internal static class ServiceCollectionExtensions
{
    internal static IServiceCollection AddAzureServices(this IServiceCollection services, AppConfiguration configuration)
    {
        // Create DefaultAzureCredential with appropriate options
        var azureCredential = CreateDefaultAzureCredential(configuration);

        // Register TokenCredential for DI
        services.AddSingleton<TokenCredential>(azureCredential);

        // Register BlobServiceClient and BlobContainerClient
        RegisterBlobServices(services, configuration, azureCredential);

        // Register CosmosClient - prefer connection string if provided, otherwise use credential
        RegisterCosmosClient(services, configuration, azureCredential);

        // Register OpenAI and Search clients - use keys if provided, otherwise use credential
        RegisterOpenAIServices(services, configuration, azureCredential);
        RegisterSearchServices(services, configuration, azureCredential);

        RegisterDomainServices(services, configuration);

        return services;
    }

    private static DefaultAzureCredential CreateDefaultAzureCredential(AppConfiguration configuration)
    {
        var options = new DefaultAzureCredentialOptions();

        if (!string.IsNullOrEmpty(configuration.VisualStudioTenantId))
        {
            options.VisualStudioTenantId = configuration.VisualStudioTenantId;
        }
        else if (!string.IsNullOrEmpty(configuration.UserAssignedManagedIdentityClientId))
        {
            options.ManagedIdentityClientId = configuration.UserAssignedManagedIdentityClientId;
        }

        return new DefaultAzureCredential(options);
    }

    private static void RegisterBlobServices(IServiceCollection services, AppConfiguration configuration, TokenCredential azureCredential)
    {
        // Prefer connection string if provided, otherwise use endpoint with credential
        if (!string.IsNullOrEmpty(configuration.AzureStorageAccountConnectionString))
        {
            services.AddSingleton<BlobServiceClient>(sp =>
            {
                return new BlobServiceClient(configuration.AzureStorageAccountConnectionString);
            });
        }
        else if (!string.IsNullOrEmpty(configuration.AzureStorageAccountEndpoint))
        {
            services.AddSingleton<BlobServiceClient>(sp =>
            {
                return new BlobServiceClient(new Uri(configuration.AzureStorageAccountEndpoint), azureCredential);
            });
        }

        services.AddSingleton<BlobContainerClient>(sp =>
        {
            var container = configuration.AzureStorageContainer;
            return sp.GetRequiredService<BlobServiceClient>().GetBlobContainerClient(container);
        });
    }

    private static void RegisterCosmosClient(IServiceCollection services, AppConfiguration configuration, TokenCredential azureCredential)
    {
        // Prefer connection string if provided, otherwise use endpoint with credential
        if (!string.IsNullOrEmpty(configuration.CosmosDBConnectionString))
        {
            services.AddSingleton<CosmosClient>(sp =>
            {
                var builder = new CosmosClientBuilder(configuration.CosmosDBConnectionString);
                return builder.Build();
            });
        }
        else if (!string.IsNullOrEmpty(configuration.CosmosDbEndpoint))
        {
            services.AddSingleton<CosmosClient>(sp =>
            {
                return new CosmosClient(configuration.CosmosDbEndpoint, azureCredential);
            });
        }
    }

    private static void RegisterOpenAIServices(IServiceCollection services, AppConfiguration configuration, TokenCredential azureCredential)
    {
        services.AddSingleton<OpenAIClientFacade>(sp =>
        {
            // Use key if provided, otherwise use credential
            var keyCredential = !string.IsNullOrEmpty(configuration.AOAIStandardServiceKey)
                ? new AzureKeyCredential(configuration.AOAIStandardServiceKey)
                : null;

            var tokenCredential = keyCredential == null ? azureCredential : null;

            return new OpenAIClientFacade(
                configuration,
                keyCredential,
                tokenCredential,
                sp.GetRequiredService<IHttpClientFactory>(),
                sp.GetRequiredService<SearchClientFactory>(),
                configuration.AzureAIGatewayAPIMKey);
        });
    }

    private static void RegisterSearchServices(IServiceCollection services, AppConfiguration configuration, TokenCredential azureCredential)
    {
        services.AddSingleton<SearchClientFactory>(sp =>
        {
            // Use key if provided, otherwise use credential
            var keyCredential = !string.IsNullOrEmpty(configuration.AzureSearchServiceKey)
                ? new AzureKeyCredential(configuration.AzureSearchServiceKey)
                : null;

            var tokenCredential = keyCredential == null ? azureCredential : null;

            return new SearchClientFactory(configuration, tokenCredential, keyCredential);
        });
    }

    private static void RegisterDomainServices(IServiceCollection services, AppConfiguration configuration)
    {
        // Add ChatHistory and document upload services if the connection string is provided
        if (string.IsNullOrEmpty(configuration.CosmosDBConnectionString) && string.IsNullOrEmpty(configuration.CosmosDbEndpoint))
        {
            services.AddScoped<IChatHistoryService, ChatHistoryServiceStub>();
        }
        else
        {
            services.AddSingleton<IChatHistoryService, ChatHistoryService>();
        }

        services.AddSingleton<DocumentService>();

        // Register both agent management service implementations
        services.AddSingleton<AzureAIAgentManagementService>();
        services.AddSingleton<CustomEndpointAgentManagementService>();
        
        // Register the factory
        services.AddSingleton<AgentManagementServiceFactory>();
        
        // Register IAgentManagementService using the factory
        services.AddSingleton<IAgentManagementService>(sp =>
        {
            var factory = sp.GetRequiredService<AgentManagementServiceFactory>();
            return factory.CreateAgentManagementService();
        });

        // Only register Azure AI Foundry-dependent services if endpoint is configured
        if (!string.IsNullOrEmpty(configuration.AzureAIFoundryProjectEndpoint))
        {
            services.AddSingleton<AzureAIAgentChatService>();
        }
        
        services.AddSingleton<ProjectService>();
        services.AddSingleton<AzureSearchService>();
        services.AddSingleton<ImageGenerationChatAgent>();
        services.AddSingleton<TextToImageService>();
        services.AddSingleton<ChatService>();
        // Register ChatService as the default IChatService implementation for M365 integration
        services.AddSingleton<IChatService>(sp => sp.GetRequiredService<ChatService>());
        services.AddSingleton<RAGChatService>();
        services.AddSingleton<EndpointChatService>();
        services.AddSingleton<EndpointChatServiceV2>();
        services.AddSingleton<EndpointTaskService>();
        services.AddSingleton<AzureBlobStorageService>();
        services.AddHttpClient<EndpointTaskService>(client =>
        {
            client.Timeout = TimeSpan.FromMinutes(5);
        });
    }

    private static void SetupOpenAIClientsUsingOnBehalfOfOthersFlowAndSubscriptionKey(IServiceProvider sp, IHttpContextAccessor httpContextAccessor, AppConfiguration config, string? standardServiceEndpoint, out AzureOpenAIClient? openAIClient3, out AzureOpenAIClient? openAIClient4)
    {
        var credential = new OnBehalfOfCredential(
                            tenantId: config.AzureTenantID,
                            clientId: config.AzureServicePrincipalClientID,
                            clientSecret: config.AzureServicePrincipalClientSecret,
                            userAssertion: httpContextAccessor.HttpContext?.Request?.Headers[config.XMsTokenAadAccessToken],
                            new OnBehalfOfCredentialOptions
                            {
                                AuthorityHost = new Uri(config.AzureAuthorityHost)
                            });

        using var httpClient = sp.GetService<IHttpClientFactory>().CreateClient();

        //if the configuration specifies a subscription key, add it to the request headers
        if (config.OcpApimSubscriptionKey != null)
        {
            httpClient.DefaultRequestHeaders.Add(config.OcpApimSubscriptionHeaderName, config.OcpApimSubscriptionKey);
        }

        openAIClient3 = new AzureOpenAIClient(new Uri(standardServiceEndpoint), credential, new AzureOpenAIClientOptions
        {
            Audience = config.AzureServicePrincipalOpenAIAudience,
            Transport = new HttpClientPipelineTransport(httpClient)
        });

        openAIClient4 = new AzureOpenAIClient(new Uri(standardServiceEndpoint), credential, new AzureOpenAIClientOptions
        {
            Audience = config.AzureServicePrincipalOpenAIAudience,
            Transport = new HttpClientPipelineTransport(httpClient)
        });
    }

    internal static IServiceCollection AddCrossOriginResourceSharing(this IServiceCollection services)
    {
        services.AddCors(
            options =>
                options.AddDefaultPolicy(
                    policy =>
                        policy.AllowAnyOrigin()
                            .AllowAnyHeader()
                            .AllowAnyMethod()));

        return services;
    }

    internal static IServiceCollection AddCustomHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddCheck<CosmosDbReadinessHealthCheck>("CosmosDB Readiness Health Check", failureStatus: HealthStatus.Degraded, tags: ["readiness"])
            .AddCheck<AzureStorageReadinessHealthCheck>("Azure Storage Readiness Health Check", failureStatus: HealthStatus.Degraded, tags: ["readiness"]);
            //TODO: this is commented out until a refactor of the profiles is done. The Search Index must exist in order to check its readiness.
            //.AddCheck<AzureSearchReadinessHealthCheck>("Azure Search Readiness Health Check", failureStatus: HealthStatus.Degraded, tags: ["readiness"]);

        return services;
    }

    internal static WebApplication MapCustomHealthChecks(this WebApplication app)
    {
        // Readiness endpoint - runs health checks tagged with "readiness"
        app.MapHealthChecks("/healthz/ready", new HealthCheckOptions
        {
            Predicate = healthCheck => healthCheck.Tags.Contains("readiness")
        });

        // Liveness and startup endpoints - simple endpoints that always return healthy
        app.MapHealthChecks("/healthz/live");
        app.MapHealthChecks("/healthz/startup");

        return app;
    }
}
