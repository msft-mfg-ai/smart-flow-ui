// Copyright (c) Microsoft. All rights reserved.

using Microsoft.AspNetCore.Http.Features;

namespace MinimalApi.Extensions;

internal static class WebApiProjectEndpoints
{
    private const string ProjectContainerName = "project-files";
    private const string DocumentToolsServiceName = "Document Tools API";
    private const string HealthCheckLivenessPath = "/healthz/live";
    private const string SpecExtractorPath = "/agent/spec-extractor";
    private const string SpecExtractorV2Path = "/agent/spec-analyzer-workflow";
    private const string PlanExtractorPath = "/agent/plan-extractor";
    private const string WorkflowStatusPath = "/workflow/status";
    private const string DefaultAnalysisMessage = "Please extract the specification summary and explain the key sections.";
    private const string DefaultSpecV2AnalysisMessage = "Analyze this specification document completely";
    private const string DefaultPlanAnalysisMessage = "Please extract the plan summary I uploaded and explain the key sections.";
    private const int HttpClientTimeoutSeconds = 10;

    // Configuration keys
    private const string DocumentToolsEndpointKey = "DocumentToolsAPIEndpoint";
    private const string DocumentToolsApiKeyKey = "DocumentToolsAPIKey";
    private const string StorageEndpointKey = "AzureStorageAccountEndpoint";
    private const string StorageConnectionStringKey = "AzureStorageAccountConnectionString";

    internal static WebApplication MapProjectApi(this WebApplication app)
    {
        var api = app.MapGroup("api/projects");

        // Get all projects
        api.MapGet("", OnGetProjectsAsync);

        // Create a new project
        api.MapPost("", OnCreateProjectAsync);

        // Delete a project
        api.MapDelete("{projectName}", OnDeleteProjectAsync);

        // Update project metadata
        api.MapPut("{projectName}/metadata", OnUpdateProjectMetadataAsync);

        // Get project metadata
        api.MapGet("{projectName}/metadata", OnGetProjectMetadataAsync);

        // Get all files in a project
        api.MapGet("{projectName}/files", OnGetFilesInProjectAsync);

        // Upload files to a specific project - disable request size limit for large file uploads
        api.MapPost("{projectName}/upload", OnUploadFilesToProjectAsync)
            .DisableAntiforgery()
            .WithMetadata(new DisableRequestSizeLimitAttribute());

        // Download a file from a project
        api.MapGet("{projectName}/download/{*fileName}", OnDownloadProjectFileAsync);

        // Delete a file from a project
        api.MapDelete("{projectName}/files/{*fileName}", OnDeleteProjectFileAsync);

        // Update file description (using query parameter for description)
        api.MapPut("{projectName}/files/description", OnUpdateFileDescriptionAsync);

        // Analyze a file in a project
        api.MapPost("{projectName}/analyze", OnAnalyzeProjectFileAsync);

        // Analyze a file in a project using spec-extractor-v2
        api.MapPost("{projectName}/analyze-spec-v2", OnAnalyzeProjectSpecV2Async);

        // Analyze a file in a project using plan-extractor
        api.MapPost("{projectName}/analyze-plan", OnAnalyzePlanProjectFileAsync);

        // Get workflow status for a project
        api.MapGet("{projectName}/workflow/status", OnGetWorkflowStatusAsync);

        // Delete workflow files for a project
        api.MapDelete("{projectName}/workflow", OnDeleteProjectWorkflowAsync);

        // Check connectivity to Document Tools API
        api.MapGet("status/document-tools", OnCheckDocumentToolsStatusAsync);

        return app;
    }

    private static async Task<IResult> OnCheckDocumentToolsStatusAsync(
        HttpContext context,
        [FromServices] IConfiguration configuration,
        [FromServices] ILogger<WebApplication> logger,
        [FromServices] IHttpClientFactory httpClientFactory,
        CancellationToken cancellationToken)
    {
        var diagnostics = new Dictionary<string, object>();
        string? errorMessage = null;

        try
        {
            var documentToolsEndpoint = configuration[DocumentToolsEndpointKey];
            var documentToolsApiKey = configuration[DocumentToolsApiKeyKey];

            diagnostics["configured_endpoint"] = documentToolsEndpoint ?? "Not configured";
            diagnostics["api_key_configured"] = !string.IsNullOrEmpty(documentToolsApiKey);
            diagnostics["timestamp"] = DateTime.UtcNow;

            if (string.IsNullOrEmpty(documentToolsEndpoint))
            {
                errorMessage = $"{DocumentToolsEndpointKey} is not configured";
                logger.LogWarning("{Service} endpoint is not configured", DocumentToolsServiceName);
                
                return CreateUnhealthyResponse(errorMessage, diagnostics);
            }

            if (string.IsNullOrEmpty(documentToolsApiKey))
            {
                logger.LogWarning("{Service} API key is not configured", DocumentToolsServiceName);
                diagnostics["warning"] = "API key is not configured - authentication may fail";
            }

            // Test connectivity to the liveness endpoint
            using var httpClient = httpClientFactory.CreateClient();
            httpClient.Timeout = TimeSpan.FromSeconds(HttpClientTimeoutSeconds);
            
            if (!string.IsNullOrEmpty(documentToolsApiKey))
            {
                httpClient.DefaultRequestHeaders.Add("X-API-Key", documentToolsApiKey);
            }

            var livenessUrl = $"{documentToolsEndpoint.TrimEnd('/')}{HealthCheckLivenessPath}";
            diagnostics["test_url"] = livenessUrl;

            var startTime = DateTime.UtcNow;
            var response = await httpClient.GetAsync(livenessUrl, cancellationToken);
            var duration = DateTime.UtcNow - startTime;

            diagnostics["response_time_ms"] = duration.TotalMilliseconds;
            diagnostics["status_code"] = (int)response.StatusCode;
            diagnostics["status_code_name"] = response.StatusCode.ToString();

            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
                diagnostics["response_body"] = responseContent;

                logger.LogInformation(
                    "{Service} is healthy. Endpoint: {Endpoint}, Response time: {Duration}ms",
                    DocumentToolsServiceName,
                    documentToolsEndpoint,
                    duration.TotalMilliseconds);

                return TypedResults.Ok(new
                {
                    status = "healthy",
                    service = DocumentToolsServiceName,
                    message = $"Successfully connected to {DocumentToolsServiceName}",
                    diagnostics = diagnostics
                });
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                diagnostics["response_body"] = errorContent;
                errorMessage = $"HTTP {response.StatusCode}: {response.ReasonPhrase}";

                logger.LogWarning(
                    "{Service} returned non-success status. Endpoint: {Endpoint}, Status: {Status}, Body: {Body}",
                    DocumentToolsServiceName,
                    documentToolsEndpoint,
                    response.StatusCode,
                    errorContent);

                return CreateUnhealthyResponse(errorMessage, diagnostics);
            }
        }
        catch (HttpRequestException ex)
        {
            errorMessage = $"HTTP request failed: {ex.Message}";
            diagnostics["exception_type"] = ex.GetType().Name;
            diagnostics["exception_message"] = ex.Message;
            
            if (ex.InnerException != null)
            {
                diagnostics["inner_exception"] = ex.InnerException.Message;
            }

            logger.LogError(ex, "Failed to connect to {Service}", DocumentToolsServiceName);
            return CreateUnhealthyResponse(errorMessage, diagnostics);
        }
        catch (TaskCanceledException ex)
        {
            errorMessage = $"Request timed out after {HttpClientTimeoutSeconds} seconds";
            diagnostics["exception_type"] = "Timeout";
            diagnostics["exception_message"] = ex.Message;

            logger.LogError(ex, "{Service} health check timed out", DocumentToolsServiceName);
            return CreateUnhealthyResponse(errorMessage, diagnostics);
        }
        catch (Exception ex)
        {
            errorMessage = $"Unexpected error: {ex.Message}";
            diagnostics["exception_type"] = ex.GetType().Name;
            diagnostics["exception_message"] = ex.Message;
            diagnostics["stack_trace"] = ex.StackTrace;

            logger.LogError(ex, "Unexpected error during {Service} health check", DocumentToolsServiceName);
            return CreateUnhealthyResponse(errorMessage, diagnostics);
        }
    }

    private static IResult CreateUnhealthyResponse(string errorMessage, Dictionary<string, object> diagnostics)
    {
        return TypedResults.Ok(new
        {
            status = "unhealthy",
            service = DocumentToolsServiceName,
            error = errorMessage,
            diagnostics = diagnostics
        });
    }

    private static async Task<IResult> OnDeleteProjectWorkflowAsync(
        HttpContext context,
        string projectName,
        [FromServices] ProjectService projectService,
        [FromServices] ILogger<WebApplication> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation("Deleting workflow files for project: {ProjectName}", projectName);

            var userInfo = await context.GetUserInfoAsync();
            var success = await projectService.DeleteProjectWorkflowAsync(projectName, cancellationToken);

            if (success)
            {
                logger.LogInformation("Successfully deleted workflow files for project '{ProjectName}'", projectName);
                return TypedResults.Ok(new { success = true, message = $"Workflow files for project '{projectName}' deleted successfully" });
            }
            else
            {
                logger.LogWarning("Failed to delete workflow files for project '{ProjectName}'", projectName);
                return Results.NotFound(new { success = false, message = $"Workflow files for project '{projectName}' not found" });
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting workflow files for project: {ProjectName}", projectName);
            return Results.Problem("Error deleting workflow files");
        }
    }

    private static async Task<IResult> OnAnalyzeProjectFileAsync(
        HttpContext context,
        string projectName,
        [FromServices] ProjectService projectService,
        [FromServices] IConfiguration configuration,
        [FromServices] ILogger<WebApplication> logger,
        [FromServices] IHttpClientFactory httpClientFactory,
        [FromServices] BlobServiceClient blobServiceClient,
        CancellationToken cancellationToken)
    {
        return await AnalyzeProjectAsync(
            context, 
            projectName, 
            projectService, 
            configuration, 
            logger, 
            httpClientFactory, 
            blobServiceClient, 
            cancellationToken,
            SpecExtractorPath,
            DefaultAnalysisMessage,
            "spec");
    }

    private static async Task<IResult> OnAnalyzeProjectSpecV2Async(
        HttpContext context,
        string projectName,
        [FromServices] ProjectService projectService,
        [FromServices] IConfiguration configuration,
        [FromServices] ILogger<WebApplication> logger,
        [FromServices] IHttpClientFactory httpClientFactory,
        [FromServices] BlobServiceClient blobServiceClient,
        CancellationToken cancellationToken)
    {
        return await AnalyzeProjectAsync(
            context, 
            projectName, 
            projectService, 
            configuration, 
            logger, 
            httpClientFactory, 
            blobServiceClient, 
            cancellationToken,
            SpecExtractorV2Path,
            DefaultSpecV2AnalysisMessage,
            "spec-v2");
    }

    private static async Task<IResult> OnAnalyzePlanProjectFileAsync(
        HttpContext context,
        string projectName,
        [FromServices] ProjectService projectService,
        [FromServices] IConfiguration configuration,
        [FromServices] ILogger<WebApplication> logger,
        [FromServices] IHttpClientFactory httpClientFactory,
        [FromServices] BlobServiceClient blobServiceClient,
        CancellationToken cancellationToken)
    {
        return await AnalyzeProjectAsync(
            context, 
            projectName, 
            projectService, 
            configuration, 
            logger, 
            httpClientFactory, 
            blobServiceClient, 
            cancellationToken,
            PlanExtractorPath,
            DefaultPlanAnalysisMessage,
            "plan");
    }

    private static async Task<IResult> AnalyzeProjectAsync(
        HttpContext context,
        string projectName,
        ProjectService projectService,
        IConfiguration configuration,
        ILogger<WebApplication> logger,
        IHttpClientFactory httpClientFactory,
        BlobServiceClient blobServiceClient,
        CancellationToken cancellationToken,
        string extractorPath,
        string defaultMessage,
        string analysisType)
    {
        try
        {
            logger.LogInformation("Analyzing project ({AnalysisType}): {ProjectName}", analysisType, projectName);

            var userInfo = await context.GetUserInfoAsync();
            
            // Get the document tools API endpoint and key from configuration
            var documentToolsEndpoint = configuration[DocumentToolsEndpointKey];
            var documentToolsApiKey = configuration[DocumentToolsApiKeyKey];
            
            if (string.IsNullOrEmpty(documentToolsEndpoint))
            {
                logger.LogError("{Service} endpoint not configured", DocumentToolsServiceName);
                return Results.Problem($"{DocumentToolsServiceName} not configured");
            }

            // Get all input files for the project
            var projectFiles = await projectService.GetProjectFilesAsync(projectName, cancellationToken);
            
            if (!projectFiles.Any())
            {
                logger.LogWarning("No files found in project '{ProjectName}'", projectName);
                return Results.BadRequest(new { success = false, message = $"No files found in project '{projectName}'" });
            }

            // Build file URLs for all input files in the project
            var baseUrl = GetStorageBaseUrl(configuration, logger);
            if (baseUrl == null)
            {
                return Results.Problem("Storage account not configured");
            }

            // Get blob container client to read metadata
            var containerClient = blobServiceClient.GetBlobContainerClient(ProjectContainerName);
            
            // Create file objects with url, filename, and description from metadata
            var filesList = new List<object>();
            foreach (var file in projectFiles)
            {
                var blobClient = containerClient.GetBlobClient(file.FileName);
                var properties = await blobClient.GetPropertiesAsync(cancellationToken: cancellationToken);
                
                // Try to get description from metadata, otherwise leave it empty
                var description = string.Empty;
                if (properties.Value.Metadata?.TryGetValue("description", out var desc) == true && !string.IsNullOrWhiteSpace(desc))
                {
                    description = desc;
                }
                
                filesList.Add(new
                {
                    url = $"{baseUrl}/{ProjectContainerName}/{file.FileName}",
                    filename = Path.GetFileName(file.FileName),
                    description = description
                });
            }
            
            var files = filesList.ToArray();

            logger.LogInformation("Analyzing {FileCount} files in project '{ProjectName}' using {AnalysisType}", files.Length, projectName, analysisType);

            logger.LogInformation("Triggering {AnalysisType} analysis for {FileCount} files in project '{ProjectName}'", analysisType, files.Length, projectName);

            // Fire and forget - start the analysis without waiting for completion
            _ = Task.Run(async () =>
            {
                try
                {
                    using var httpClient = httpClientFactory.CreateClient();
                    if (!string.IsNullOrEmpty(documentToolsApiKey))
                    {
                        httpClient.DefaultRequestHeaders.Add("X-API-Key", documentToolsApiKey);
                    }
                    
                    var requestBody = new
                    {
                        message = defaultMessage,
                        project_name = projectName,
                        files = files
                    };
                    
                    var jsonContent = System.Text.Json.JsonSerializer.Serialize(requestBody);
                    using var content = new StringContent(jsonContent, System.Text.Encoding.UTF8, "application/json");
                    
                    var response = await httpClient.PostAsync($"{documentToolsEndpoint}{extractorPath}", content);

                    if (response.IsSuccessStatusCode)
                    {
                        logger.LogInformation("Successfully triggered {AnalysisType} analysis for {FileCount} files in project '{ProjectName}'", analysisType, files.Length, projectName);
                    }
                    else
                    {
                        var errorContent = await response.Content.ReadAsStringAsync();
                        logger.LogError("Failed to trigger {AnalysisType} analysis for project '{ProjectName}': {StatusCode} - {Error}", 
                            analysisType, projectName, response.StatusCode, errorContent);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Background error analyzing project ({AnalysisType}): {ProjectName}", analysisType, projectName);
                }
            });

            // Return immediately
            return TypedResults.Ok(new 
            { 
                success = true, 
                message = $"{char.ToUpper(analysisType[0]) + analysisType.Substring(1)} analysis started for {files.Length} file(s) in project '{projectName}'",
                project_name = projectName,
                file_count = files.Length,
                analysis_type = analysisType
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error analyzing project ({AnalysisType}): {ProjectName}", analysisType, projectName);
            return Results.Problem($"Error analyzing project: {ex.Message}");
        }
    }

    private static async Task<IResult> OnGetWorkflowStatusAsync(
        HttpContext context,
        string projectName,
        [FromServices] IConfiguration configuration,
        [FromServices] ILogger<WebApplication> logger,
        [FromServices] IHttpClientFactory httpClientFactory,
        CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation("Getting workflow status for project: {ProjectName}", projectName);

            var userInfo = await context.GetUserInfoAsync();
            
            // Get the document tools API endpoint and key from configuration
            var documentToolsEndpoint = configuration[DocumentToolsEndpointKey];
            var documentToolsApiKey = configuration[DocumentToolsApiKeyKey];
            
            if (string.IsNullOrEmpty(documentToolsEndpoint))
            {
                logger.LogError("{Service} endpoint not configured", DocumentToolsServiceName);
                return Results.Problem($"{DocumentToolsServiceName} not configured");
            }

            // Call the external document-tools API to get workflow status
            using var httpClient = httpClientFactory.CreateClient();
            httpClient.Timeout = TimeSpan.FromSeconds(HttpClientTimeoutSeconds);
            
            if (!string.IsNullOrEmpty(documentToolsApiKey))
            {
                httpClient.DefaultRequestHeaders.Add("X-API-Key", documentToolsApiKey);
            }
            
            var statusUrl = $"{documentToolsEndpoint.TrimEnd('/')}{WorkflowStatusPath}/{projectName}";
            var response = await httpClient.GetAsync(statusUrl, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogInformation("Successfully retrieved workflow status for project '{ProjectName}'", projectName);
                
                // Parse and return the response
                var statusData = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(responseContent);
                return TypedResults.Ok(statusData);
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                logger.LogWarning("Workflow status not found for project '{ProjectName}'", projectName);
                return Results.NotFound(new { success = false, message = $"Workflow status not found for project '{projectName}'" });
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogError("Failed to get workflow status for project '{ProjectName}': {StatusCode} - {Error}", 
                    projectName, response.StatusCode, errorContent);
                return Results.Problem($"Failed to get workflow status: {response.StatusCode} - {errorContent}");
            }
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "HTTP error getting workflow status for project: {ProjectName}", projectName);
            return Results.Problem($"HTTP error getting workflow status: {ex.Message}");
        }
        catch (TaskCanceledException ex)
        {
            logger.LogError(ex, "Timeout getting workflow status for project: {ProjectName}", projectName);
            return Results.Problem($"Request timed out getting workflow status");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting workflow status for project: {ProjectName}", projectName);
            return Results.Problem($"Error getting workflow status: {ex.Message}");
        }
    }

    private static string? GetStorageBaseUrl(IConfiguration configuration, ILogger logger)
    {
        var storageAccountEndpoint = configuration[StorageEndpointKey];
        var connectionString = configuration[StorageConnectionStringKey];

        if (!string.IsNullOrEmpty(storageAccountEndpoint))
        {
            return storageAccountEndpoint.TrimEnd('/');
        }
        else if (!string.IsNullOrEmpty(connectionString))
        {
            // Parse account name from connection string
            var accountNameMatch = System.Text.RegularExpressions.Regex.Match(connectionString, @"AccountName=([^;]+)");
            if (accountNameMatch.Success)
            {
                var accountName = accountNameMatch.Groups[1].Value;
                return $"https://{accountName}.blob.core.windows.net";
            }
            else
            {
                logger.LogError("Could not determine storage account URL");
                return null;
            }
        }
        else
        {
            logger.LogError("Storage account configuration not found");
            return null;
        }
    }

    private static async Task<IResult> OnGetProjectsAsync(
        HttpContext context,
        [FromServices] ProjectService projectService,
        [FromServices] ILogger<WebApplication> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation("Getting projects");

            var userInfo = await context.GetUserInfoAsync();
            var projects = await projectService.GetProjectsAsync(cancellationToken);

            context.Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate, max-age=0";
            context.Response.Headers["Pragma"] = "no-cache";

            return TypedResults.Ok(projects);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting projects");
            return Results.Problem("Error retrieving projects");
        }
    }

    private static async Task<IResult> OnCreateProjectAsync(
        HttpContext context,
        [FromBody] CreateCollectionRequest request,
        [FromServices] ProjectService projectService,
        [FromServices] ILogger<WebApplication> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation("Creating project: {ProjectName} with description: {Description}, type: {Type}",
                request.Name, request.Description, request.Type);

            var userInfo = await context.GetUserInfoAsync();
            var success = await projectService.CreateProjectAsync(
                request.Name,
                request.Description,
                request.Type,
                cancellationToken);

            if (success)
            {
                logger.LogInformation("Successfully created project '{ProjectName}'", request.Name);
                return TypedResults.Ok(new { success = true, message = $"Project '{request.Name}' created successfully" });
            }
            else
            {
                logger.LogWarning("Failed to create project '{ProjectName}'", request.Name);
                return Results.BadRequest(new { success = false, message = $"Failed to create project '{request.Name}'" });
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating project: {ProjectName}", request.Name);
            return Results.Problem("Error creating project");
        }
    }

    private static async Task<IResult> OnDeleteProjectAsync(
        HttpContext context,
        string projectName,
        [FromServices] ProjectService projectService,
        [FromServices] ILogger<WebApplication> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation("Deleting project: {ProjectName}", projectName);

            var userInfo = await context.GetUserInfoAsync();
            var success = await projectService.DeleteProjectAsync(projectName, cancellationToken);

            if (success)
            {
                logger.LogInformation("Successfully deleted project '{ProjectName}'", projectName);
                return TypedResults.Ok(new { success = true, message = $"Project '{projectName}' deleted successfully" });
            }
            else
            {
                logger.LogWarning("Failed to delete project '{ProjectName}'", projectName);
                return Results.NotFound(new { success = false, message = $"Project '{projectName}' not found" });
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting project: {ProjectName}", projectName);
            return Results.Problem("Error deleting project");
        }
    }

    private static async Task<IResult> OnUpdateProjectMetadataAsync(
        HttpContext context,
        string projectName,
        [FromBody] CreateCollectionRequest request,
        [FromServices] ProjectService projectService,
        [FromServices] ILogger<WebApplication> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation("Updating metadata for project: {ProjectName}", projectName);

            var userInfo = await context.GetUserInfoAsync();
            var success = await projectService.UpdateProjectMetadataAsync(
                projectName,
                request.Description,
                request.Type,
                cancellationToken);

            if (success)
            {
                logger.LogInformation("Successfully updated metadata for project '{ProjectName}'", projectName);
                return TypedResults.Ok(new { success = true, message = $"Project '{projectName}' metadata updated successfully" });
            }
            else
            {
                logger.LogWarning("Failed to update metadata for project '{ProjectName}'", projectName);
                return Results.NotFound(new { success = false, message = $"Project '{projectName}' not found" });
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating metadata for project: {ProjectName}", projectName);
            return Results.Problem("Error updating project metadata");
        }
    }

    private static async Task<IResult> OnGetProjectMetadataAsync(
        HttpContext context,
        string projectName,
        [FromServices] ProjectService projectService,
        [FromServices] ILogger<WebApplication> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation("Getting metadata for project: {ProjectName}", projectName);

            var userInfo = await context.GetUserInfoAsync();
            var metadata = await projectService.GetProjectMetadataAsync(projectName, cancellationToken);

            if (metadata != null)
            {
                return TypedResults.Ok(metadata);
            }
            else
            {
                return Results.NotFound(new { message = $"Project '{projectName}' not found" });
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting metadata for project: {ProjectName}", projectName);
            return Results.Problem("Error retrieving project metadata");
        }
    }

    private static async Task<IResult> OnGetFilesInProjectAsync(
        HttpContext context,
        string projectName,
        [FromServices] ProjectService projectService,
        [FromServices] ILogger<WebApplication> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation("Getting files from project: {ProjectName}", projectName);

            var userInfo = await context.GetUserInfoAsync();
            var files = await projectService.GetProjectFilesAsync(projectName, cancellationToken);

            context.Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate, max-age=0";
            context.Response.Headers["Pragma"] = "no-cache";

            return TypedResults.Ok(files);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting files from project: {ProjectName}", projectName);
            return Results.Problem("Error retrieving files from project");
        }
    }

    private static async Task<IResult> OnUploadFilesToProjectAsync(
        HttpContext context,
        string projectName,
        [FromForm] IFormFileCollection files,
        [FromServices] ProjectService projectService,
        [FromServices] ILogger<WebApplication> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation("Uploading {FileCount} files to project: {ProjectName}", files.Count, projectName);

            var userInfo = await context.GetUserInfoAsync();

            // Read optional metadata from headers
            var fileMetadataContent = context.Request.Headers["X-FILE-METADATA"];
            Dictionary<string, string>? fileMetadata = null;
            if (!string.IsNullOrEmpty(fileMetadataContent))
            {
                fileMetadata = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(fileMetadataContent);
            }

            // Read file path mapping from headers
            var filePathMapContent = context.Request.Headers["X-FILE-PATH-MAP"];
            Dictionary<string, string>? filePathMap = null;
            if (!string.IsNullOrEmpty(filePathMapContent))
            {
                filePathMap = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(filePathMapContent);
            }

            // Ensure project tag is set
            fileMetadata ??= new Dictionary<string, string>();
            fileMetadata["project"] = projectName;

            var response = await projectService.UploadFilesToProjectAsync(
                userInfo,
                files,
                projectName,
                fileMetadata,
                filePathMap,
                cancellationToken);

            logger.LogInformation("Upload to project '{ProjectName}' completed: {Response}", projectName, response);

            return TypedResults.Ok(response);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error uploading files to project: {ProjectName}", projectName);
            return Results.Problem("Error uploading files to project");
        }
    }

    private static async Task<IResult> OnDeleteProjectFileAsync(
        HttpContext context,
        string projectName,
        string fileName,
        [FromServices] ProjectService projectService,
        [FromServices] ILogger<WebApplication> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            fileName = Uri.UnescapeDataString(fileName);

            logger.LogInformation("Deleting file {FileName} from project: {ProjectName}", fileName, projectName);

            var userInfo = await context.GetUserInfoAsync();
            var success = await projectService.DeleteFileFromProjectAsync(projectName, fileName, cancellationToken);

            if (success)
            {
                logger.LogInformation("Successfully deleted file '{FileName}' from project '{ProjectName}'", fileName, projectName);
                return TypedResults.Ok(new { success = true, message = $"File '{fileName}' deleted successfully" });
            }
            else
            {
                logger.LogWarning("Failed to delete file '{FileName}' from project '{ProjectName}'", fileName, projectName);
                return Results.NotFound(new { success = false, message = $"File '{fileName}' not found" });
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting file {FileName} from project: {ProjectName}", fileName, projectName);
            return Results.Problem("Error deleting file from project");
        }
    }

    private static async Task<IResult> OnDownloadProjectFileAsync(
        HttpContext context,
        string projectName,
        string fileName,
        [FromServices] ProjectService projectService,
        [FromServices] ILogger<WebApplication> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            fileName = Uri.UnescapeDataString(fileName);
            var isProcessingFile = context.Request.Query.ContainsKey("processing");

            logger.LogInformation("Downloading file {FileName} from project: {ProjectName} (processing: {IsProcessing})",
                fileName, projectName, isProcessingFile);

            var userInfo = await context.GetUserInfoAsync();
            var (stream, contentType) = await projectService.DownloadFileAsync(projectName, fileName, isProcessingFile, cancellationToken);

            if (stream == null)
            {
                return Results.NotFound(new { message = $"File '{fileName}' not found in project '{projectName}'" });
            }

            var fileNameOnly = Path.GetFileName(fileName);
            var extension = Path.GetExtension(fileName).ToLowerInvariant();

            if (string.IsNullOrEmpty(contentType) || contentType == "application/octet-stream")
            {
                contentType = extension switch
                {
                    ".pdf" => "application/pdf",
                    ".md" => "text/markdown",
                    ".txt" => "text/plain",
                    ".json" => "application/json",
                    _ => "application/octet-stream"
                };
            }

            if (extension == ".pdf")
            {
                context.Response.Headers["Content-Type"] = "application/pdf";
                context.Response.Headers["Content-Disposition"] = $"inline; filename=\"{fileNameOnly}\"";
                context.Response.Headers["Accept-Ranges"] = "bytes";
                context.Response.Headers["Cache-Control"] = "public, max-age=3600";

                return Results.Stream(stream, contentType: "application/pdf", enableRangeProcessing: true);
            }

            var enableRangeProcessing = extension == ".pdf";
            var fileDownloadName = (extension == ".pdf" || extension == ".md" || extension == ".json") ? null : fileNameOnly;

            if (fileDownloadName == null)
            {
                context.Response.Headers["Content-Disposition"] = $"inline; filename=\"{fileNameOnly}\"";
            }

            return Results.Stream(
                stream,
                contentType: contentType,
                fileDownloadName: fileDownloadName,
                enableRangeProcessing: enableRangeProcessing);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error downloading file {FileName} from project: {ProjectName}", fileName, projectName);
            return Results.Problem("Error downloading file from project");
        }
    }

    private static async Task<IResult> OnUpdateFileDescriptionAsync(
        HttpContext context,
        string projectName,
        [FromBody] UpdateFileDescriptionRequest request,
        [FromServices] ProjectService projectService,
        [FromServices] ILogger<WebApplication> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            // Read fileName from query parameter
            if (!context.Request.Query.TryGetValue("fileName", out var fileNameValue) || string.IsNullOrEmpty(fileNameValue))
            {
                logger.LogWarning("UpdateFileDescription called without fileName parameter");
                return Results.BadRequest(new { success = false, message = "fileName query parameter is required" });
            }
            
            var fileName = Uri.UnescapeDataString(fileNameValue.ToString());
            
            logger.LogInformation("Updating description for file '{FileName}' in project '{ProjectName}' to '{Description}'", 
                fileName, projectName, request?.Description ?? "(null)");

            var userInfo = await context.GetUserInfoAsync();
            var success = await projectService.UpdateFileDescriptionAsync(projectName, fileName, request?.Description, cancellationToken);

            if (success)
            {
                logger.LogInformation("Successfully updated description for file '{FileName}' in project '{ProjectName}'", fileName, projectName);
                return TypedResults.Ok(new { success = true, message = $"File description updated successfully" });
            }
            else
            {
                logger.LogWarning("Failed to update description for file '{FileName}' in project '{ProjectName}'", fileName, projectName);
                return Results.NotFound(new { success = false, message = $"File '{fileName}' not found" });
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating description for file in project: {ProjectName}", projectName);
            return Results.Problem("Error updating file description");
        }
    }
}

public record UpdateFileDescriptionRequest(string? Description);
