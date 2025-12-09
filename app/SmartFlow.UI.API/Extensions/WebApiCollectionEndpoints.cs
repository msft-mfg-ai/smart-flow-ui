// Copyright (c) Microsoft. All rights reserved.

using MinimalApi.Services.Search;
using Microsoft.AspNetCore.Http.Features;

namespace MinimalApi.Extensions;

internal static class WebApiCollectionEndpoints
{
    internal static WebApplication MapCollectionApi(this WebApplication app)
    {
        var api = app.MapGroup("api/collections");

        // Get all managed collections
        api.MapGet("", OnGetCollectionsAsync);

        // Add managed collection tag to a container
        api.MapPost("{containerName}/tag", OnAddManagedCollectionTagAsync);

        // Create a new collection with metadata
        api.MapPost("", OnCreateCollectionAsync);

        // Delete a collection (remove metadata tag)
        api.MapDelete("{containerName}", OnDeleteCollectionAsync);

        // Update collection metadata
        api.MapPut("{containerName}/metadata", OnUpdateCollectionMetadataAsync);

        // Get collection metadata
        api.MapGet("{containerName}/metadata", OnGetCollectionMetadataAsync);

        // Get all files in a container
        api.MapGet("{containerName}/files", OnGetFilesInContainerAsync);

        // Upload files to a specific container - disable request size limit for large file uploads
        api.MapPost("{containerName}/upload", OnUploadFilesToContainerAsync)
            .DisableAntiforgery()
            .WithMetadata(new DisableRequestSizeLimitAttribute());

        // Download a file from a container
        api.MapGet("{containerName}/download/{*fileName}", OnDownloadFileAsync);

        // Delete a file from a container
        api.MapDelete("{containerName}/files/{*fileName}", OnDeleteFileAsync);

        // Get all Azure AI Search indexes
        api.MapGet("indexes", OnGetSearchIndexesAsync);

        // Get detailed information about a specific index
        api.MapGet("indexes/{indexName}", OnGetSearchIndexDetailsAsync);

        // Folder management endpoints
        api.MapGet("{containerName}/folders", OnGetFolderStructureAsync);
        api.MapPost("{containerName}/folders", OnCreateFolderAsync);
        api.MapPut("{containerName}/folders/rename", OnRenameFolderAsync);
        api.MapDelete("{containerName}/folders", OnDeleteFolderAsync);

        // File metadata management
        api.MapPut("{containerName}/files/metadata/{*fileName}", OnUpdateFileMetadataAsync);
        api.MapGet("{containerName}/files/metadata/{*fileName}", OnGetFileMetadataAsync);

        // Metadata configuration
        api.MapGet("metadata-configuration", OnGetMetadataConfigurationAsync);

        // Document processing/indexing endpoint
        api.MapPost("{containerName}/process/{*fileName}", OnProcessDocumentAsync);

        return app;
    }

    private static async Task<IResult> OnGetSearchIndexesAsync(
        HttpContext context,
        [FromServices] AzureSearchService searchService,
        [FromServices] ILogger<WebApplication> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation("Getting search indexes");

            var userInfo = await context.GetUserInfoAsync();
            var indexes = await searchService.GetIndexesAsync(cancellationToken);

            return TypedResults.Ok(indexes);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting search indexes");
            return Results.Problem("Error retrieving search indexes");
        }
    }

    private static async Task<IResult> OnGetSearchIndexDetailsAsync(
        HttpContext context,
        string indexName,
        [FromServices] AzureSearchService searchService,
        [FromServices] ILogger<WebApplication> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation("Getting index details for: {IndexName}", indexName);

            var userInfo = await context.GetUserInfoAsync();
            var indexDetails = await searchService.GetIndexDetailsAsync(indexName, cancellationToken);

            if (indexDetails != null)
            {
                return TypedResults.Ok(indexDetails);
            }
            else
            {
                return Results.NotFound(new { message = $"Index '{indexName}' not found" });
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting index details for: {IndexName}", indexName);
            return Results.Problem("Error retrieving index details");
        }
    }

    private static async Task<IResult> OnGetCollectionsAsync(
        HttpContext context,
        [FromServices] DocumentService documentService,
        [FromServices] ILogger<WebApplication> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation("Getting managed collections");

            var userInfo = await context.GetUserInfoAsync();
            var collections = await documentService.GetCollectionsAsync(cancellationToken);

            context.Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate, max-age=0";
            context.Response.Headers["Pragma"] = "no-cache";

            return TypedResults.Ok(collections);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting managed collections");
            return Results.Problem("Error retrieving managed collections");
        }
    }

    private static async Task<IResult> OnCreateCollectionAsync(
        HttpContext context,
        [FromBody] CreateCollectionRequest request,
        [FromServices] DocumentService documentService,
        [FromServices] ILogger<WebApplication> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation("Creating managed collection: {ContainerName} with description: {Description}, type: {Type}, index: {IndexName}",
                request.Name, request.Description, request.Type, request.IndexName);

            var userInfo = await context.GetUserInfoAsync();
            var success = await documentService.AddManagedCollectionTagAsync(
                request.Name,
                request.Description,
                request.Type,
                request.IndexName,
                cancellationToken);

            if (success)
            {
                logger.LogInformation("Successfully created collection '{ContainerName}'", request.Name);
                return TypedResults.Ok(new { success = true, message = $"Collection '{request.Name}' created successfully" });
            }
            else
            {
                logger.LogWarning("Failed to create collection '{ContainerName}'", request.Name);
                return Results.BadRequest(new { success = false, message = $"Failed to create collection '{request.Name}'" });
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating managed collection: {ContainerName}", request.Name);
            return Results.Problem("Error creating collection");
        }
    }

    private static async Task<IResult> OnDeleteCollectionAsync(
        HttpContext context,
        string containerName,
        [FromServices] DocumentService documentService,
        [FromServices] ILogger<WebApplication> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation("Deleting blob container: {ContainerName}", containerName);

            var userInfo = await context.GetUserInfoAsync();
            var success = await documentService.DeleteContainerAsync(containerName, cancellationToken);

            if (success)
            {
                logger.LogInformation("Successfully deleted container '{ContainerName}' and all its contents", containerName);
                return TypedResults.Ok(new { success = true, message = $"Collection '{containerName}' and all its files have been deleted successfully." });
            }
            else
            {
                logger.LogWarning("Failed to delete container '{ContainerName}' - container may not exist", containerName);
                return Results.NotFound(new { success = false, message = $"Collection '{containerName}' not found" });
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting container: {ContainerName}", containerName);
            return Results.Problem("Error deleting collection");
        }
    }

    private static async Task<IResult> OnAddManagedCollectionTagAsync(
        HttpContext context,
        string containerName,
        [FromServices] DocumentService documentService,
        [FromServices] ILogger<WebApplication> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation("Creating/tagging managed collection: {ContainerName}", containerName);

            var userInfo = await context.GetUserInfoAsync();
            var success = await documentService.AddManagedCollectionTagAsync(containerName, cancellationToken: cancellationToken);

            if (success)
            {
                logger.LogInformation("Successfully created/tagged container '{ContainerName}' as managed collection", containerName);
                return TypedResults.Ok(new { success = true, message = $"Collection '{containerName}' created successfully" });
            }
            else
            {
                logger.LogWarning("Failed to create/tag container '{ContainerName}'", containerName);
                return Results.BadRequest(new { success = false, message = $"Failed to create collection '{containerName}'" });
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating/tagging managed collection: {ContainerName}", containerName);
            return Results.Problem("Error creating collection");
        }
    }

    private static async Task<IResult> OnUpdateCollectionMetadataAsync(
        HttpContext context,
        string containerName,
        [FromBody] CreateCollectionRequest request,
        [FromServices] DocumentService documentService,
        [FromServices] ILogger<WebApplication> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation("Updating metadata for collection: {ContainerName}", containerName);

            var userInfo = await context.GetUserInfoAsync();
            var success = await documentService.UpdateCollectionMetadataAsync(
                containerName,
                request.Description,
                request.Type,
                request.IndexName,
                cancellationToken);

            if (success)
            {
                logger.LogInformation("Successfully updated metadata for collection '{ContainerName}'", containerName);
                return TypedResults.Ok(new { success = true, message = $"Collection '{containerName}' metadata updated successfully" });
            }
            else
            {
                logger.LogWarning("Failed to update metadata for collection '{ContainerName}'", containerName);
                return Results.NotFound(new { success = false, message = $"Collection '{containerName}' not found" });
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating metadata for collection: {ContainerName}", containerName);
            return Results.Problem("Error updating collection metadata");
        }
    }

    private static async Task<IResult> OnGetCollectionMetadataAsync(
        HttpContext context,
        string containerName,
        [FromServices] DocumentService documentService,
        [FromServices] ILogger<WebApplication> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation("Getting metadata for collection: {ContainerName}", containerName);

            var userInfo = await context.GetUserInfoAsync();
            var metadata = await documentService.GetCollectionMetadataAsync(containerName, cancellationToken);

            if (metadata != null)
            {
                return TypedResults.Ok(metadata);
            }
            else
            {
                return Results.NotFound(new { message = $"Collection '{containerName}' not found" });
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting metadata for collection: {ContainerName}", containerName);
            return Results.Problem("Error retrieving collection metadata");
        }
    }

    private static async Task<IResult> OnGetFilesInContainerAsync(
        HttpContext context,
        string containerName,
        [FromServices] DocumentService documentService,
        [FromServices] ILogger<WebApplication> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation("Getting files from container: {ContainerName}", containerName);

            var userInfo = await context.GetUserInfoAsync();
            var files = await documentService.GetFilesInContainerAsync(containerName, cancellationToken);

            context.Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate, max-age=0";
            context.Response.Headers["Pragma"] = "no-cache";

            return TypedResults.Ok(files);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting files from container: {ContainerName}", containerName);
            return Results.Problem("Error retrieving files from container");
        }
    }

    private static async Task<IResult> OnUploadFilesToContainerAsync(
        HttpContext context,
        string containerName,
        [FromForm] IFormFileCollection files,
        [FromServices] DocumentService documentService,
        [FromServices] ILogger<WebApplication> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation("Uploading {FileCount} files to container: {ContainerName}", files.Count, containerName);

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

            var response = await documentService.UploadFilesToContainerAsync(
                userInfo,
                files,
                containerName,
                fileMetadata,
                filePathMap,
                cancellationToken);

            logger.LogInformation("Upload to container '{ContainerName}' completed: {Response}", containerName, response);

            return TypedResults.Ok(response);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error uploading files to container: {ContainerName}", containerName);
            return Results.Problem("Error uploading files to container");
        }
    }

    private static async Task<IResult> OnDeleteFileAsync(
        HttpContext context,
        string containerName,
        string fileName,
        [FromServices] DocumentService documentService,
        [FromServices] ILogger<WebApplication> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            // URL decode the filename since it comes URL-encoded from the client
            fileName = Uri.UnescapeDataString(fileName);

            logger.LogInformation("Deleting file {FileName} from container: {ContainerName}", fileName, containerName);

            var userInfo = await context.GetUserInfoAsync();
            var success = await documentService.DeleteFileFromContainerAsync(containerName, fileName, cancellationToken);

            if (success)
            {
                logger.LogInformation("Successfully deleted file '{FileName}' from container '{ContainerName}'", fileName, containerName);
                return TypedResults.Ok(new { success = true, message = $"File '{fileName}' deleted successfully" });
            }
            else
            {
                logger.LogWarning("Failed to delete file '{FileName}' from container '{ContainerName}'", fileName, containerName);
                return Results.NotFound(new { success = false, message = $"File '{fileName}' not found" });
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting file {FileName} from container: {ContainerName}", fileName, containerName);
            return Results.Problem("Error deleting file from container");
        }
    }

    private static async Task<IResult> OnDownloadFileAsync(
        HttpContext context,
        string containerName,
        string fileName,
        [FromServices] DocumentService documentService,
        [FromServices] ILogger<WebApplication> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            // URL decode the filename since it comes URL-encoded from the client
            fileName = Uri.UnescapeDataString(fileName);

            logger.LogInformation("Downloading file {FileName} from container: {ContainerName}", fileName, containerName);

            var userInfo = await context.GetUserInfoAsync();
            var (stream, contentType) = await documentService.DownloadFileAsync(containerName, fileName, cancellationToken);

            if (stream == null)
            {
                return Results.NotFound(new { message = $"File '{fileName}' not found in container '{containerName}'" });
            }

            var fileNameOnly = Path.GetFileName(fileName);
            var extension = Path.GetExtension(fileName).ToLowerInvariant();

            // Ensure content type is set correctly
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

            // For PDFs, explicitly set headers for inline viewing
            if (extension == ".pdf")
            {
                context.Response.Headers["Content-Type"] = "application/pdf";
                context.Response.Headers["Content-Disposition"] = $"inline; filename=\"{fileNameOnly}\"";
                context.Response.Headers["Accept-Ranges"] = "bytes";
                context.Response.Headers["Cache-Control"] = "public, max-age=3600";

                return Results.Stream(stream, contentType: "application/pdf", enableRangeProcessing: true);
            }

            // For Markdown and JSON files, use inline disposition so they can be viewed in browser
            // For other files, use attachment disposition to trigger download
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
            logger.LogError(ex, "Error downloading file {FileName} from container: {ContainerName}", fileName, containerName);
            return Results.Problem("Error downloading file from container");
        }
    }

    private static async Task<IResult> OnGetFolderStructureAsync(
        HttpContext context,
        string containerName,
        [FromServices] DocumentService documentService,
        [FromServices] ILogger<WebApplication> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation("Getting folder structure from container: {ContainerName}", containerName);

            var userInfo = await context.GetUserInfoAsync();
            var folderStructure = await documentService.GetFolderStructureAsync(containerName, cancellationToken);

            return TypedResults.Ok(folderStructure);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting folder structure from container: {ContainerName}", containerName);
            return Results.Problem("Error retrieving folder structure");
        }
    }

    private static async Task<IResult> OnCreateFolderAsync(
        HttpContext context,
        string containerName,
        [FromBody] CreateFolderRequest request,
        [FromServices] DocumentService documentService,
        [FromServices] ILogger<WebApplication> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation("Creating folder {FolderPath} in container: {ContainerName}", request.FolderPath, containerName);

            var userInfo = await context.GetUserInfoAsync();
            var success = await documentService.CreateFolderAsync(containerName, request.FolderPath, cancellationToken);

            if (success)
            {
                logger.LogInformation("Successfully created folder '{FolderPath}' in container '{ContainerName}'", request.FolderPath, containerName);
                return TypedResults.Ok(new { success = true, message = $"Folder '{request.FolderPath}' created successfully" });
            }
            else
            {
                logger.LogWarning("Failed to create folder '{FolderPath}' in container '{ContainerName}'", request.FolderPath, containerName);
                return Results.BadRequest(new { success = false, message = $"Failed to create folder '{request.FolderPath}'" });
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating folder {FolderPath} in container: {ContainerName}", request.FolderPath, containerName);
            return Results.Problem("Error creating folder");
        }
    }

    private static async Task<IResult> OnRenameFolderAsync(
        HttpContext context,
        string containerName,
        [FromBody] RenameFolderRequest request,
        [FromServices] DocumentService documentService,
        [FromServices] ILogger<WebApplication> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation("Renaming folder from {OldPath} to {NewPath} in container: {ContainerName}",
                request.OldFolderPath, request.NewFolderPath, containerName);

            var userInfo = await context.GetUserInfoAsync();
            var success = await documentService.RenameFolderAsync(containerName, request.OldFolderPath, request.NewFolderPath, cancellationToken);

            if (success)
            {
                logger.LogInformation("Successfully renamed folder from '{OldPath}' to '{NewPath}' in container '{ContainerName}'",
                    request.OldFolderPath, request.NewFolderPath, containerName);
                return TypedResults.Ok(new { success = true, message = $"Folder renamed successfully" });
            }
            else
            {
                logger.LogWarning("Failed to rename folder from '{OldPath}' to '{NewPath}' in container '{ContainerName}'",
                    request.OldFolderPath, request.NewFolderPath, containerName);
                return Results.BadRequest(new { success = false, message = $"Failed to rename folder" });
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error renaming folder from {OldPath} to {NewPath} in container: {ContainerName}",
                request.OldFolderPath, request.NewFolderPath, containerName);
            return Results.Problem("Error renaming folder");
        }
    }

    private static async Task<IResult> OnDeleteFolderAsync(
        HttpContext context,
        string containerName,
        [FromQuery] string folderPath,
        [FromServices] DocumentService documentService,
        [FromServices] ILogger<WebApplication> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation("Deleting folder {FolderPath} from container: {ContainerName}", folderPath, containerName);

            var userInfo = await context.GetUserInfoAsync();
            var success = await documentService.DeleteFolderAsync(containerName, folderPath, cancellationToken);

            if (success)
            {
                logger.LogInformation("Successfully deleted folder '{FolderPath}' from container '{ContainerName}'", folderPath, containerName);
                return TypedResults.Ok(new { success = true, message = $"Folder '{folderPath}' deleted successfully" });
            }
            else
            {
                logger.LogWarning("Failed to delete folder '{FolderPath}' from container '{ContainerName}'", folderPath, containerName);
                return Results.NotFound(new { success = false, message = $"Folder '{folderPath}' not found" });
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting folder {FolderPath} from container: {ContainerName}", folderPath, containerName);
            return Results.Problem("Error deleting folder");
        }
    }

    private static async Task<IResult> OnUpdateFileMetadataAsync(
        HttpContext context,
        string containerName,
        string fileName,
        [FromBody] FileMetadata metadata,
        [FromServices] DocumentService documentService,
        [FromServices] ILogger<WebApplication> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            fileName = Uri.UnescapeDataString(fileName);
            logger.LogInformation("Updating metadata for file {FileName} in container: {ContainerName}", fileName, containerName);

            var userInfo = await context.GetUserInfoAsync();
            var success = await documentService.UpdateFileMetadataAsync(containerName, fileName, metadata, cancellationToken);

            if (success)
            {
                logger.LogInformation("Successfully updated metadata for file '{FileName}' in container '{ContainerName}'", fileName, containerName);
                return TypedResults.Ok(new { success = true, message = $"Metadata updated successfully" });
            }
            else
            {
                logger.LogWarning("Failed to update metadata for file '{FileName}' in container '{ContainerName}'", fileName, containerName);
                return Results.NotFound(new { success = false, message = $"File '{fileName}' not found" });
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating metadata for file {FileName} in container: {ContainerName}", fileName, containerName);
            return Results.Problem("Error updating file metadata");
        }
    }

    private static async Task<IResult> OnGetFileMetadataAsync(
        HttpContext context,
        string containerName,
        string fileName,
        [FromServices] DocumentService documentService,
        [FromServices] ILogger<WebApplication> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            fileName = Uri.UnescapeDataString(fileName);
            logger.LogInformation("Getting metadata for file {FileName} from container: {ContainerName}", fileName, containerName);

            var userInfo = await context.GetUserInfoAsync();
            var metadata = await documentService.GetFileMetadataAsync(containerName, fileName, cancellationToken);

            if (metadata != null)
            {
                return TypedResults.Ok(metadata);
            }
            else
            {
                return Results.NotFound(new { message = $"Metadata for file '{fileName}' not found" });
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting metadata for file {FileName} from container: {ContainerName}", fileName, containerName);
            return Results.Problem("Error retrieving file metadata");
        }
    }

    private static Task<IResult> OnGetMetadataConfigurationAsync(
        HttpContext context,
        [FromServices] IConfiguration configuration,
        [FromServices] ILogger<WebApplication> logger)
    {
        try
        {
            logger.LogInformation("Getting metadata configuration");

            var metadataConfig = configuration.GetSection("MetadataConfiguration").Get<MetadataConfiguration>();

            if (metadataConfig == null)
            {
                logger.LogWarning("No metadata configuration found, returning default");
                metadataConfig = new MetadataConfiguration
                {
                    Name = "Default",
                    Description = "Default configuration",
                    Fields = new List<MetadataFieldConfiguration>()
                };
            }

            return Task.FromResult<IResult>(TypedResults.Ok(metadataConfig));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting metadata configuration");
            return Task.FromResult<IResult>(Results.Problem("Error retrieving metadata configuration"));
        }
    }

    private static async Task<IResult> OnProcessDocumentAsync(
        HttpContext context,
        string containerName,
        string fileName,
        [FromServices] DocumentService documentService,
        [FromServices] IHttpClientFactory httpClientFactory,
        [FromServices] IConfiguration configuration,
        [FromServices] ILogger<WebApplication> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            // URL decode the filename
            fileName = Uri.UnescapeDataString(fileName);
            
            logger.LogInformation("Processing document {FileName} from container {ContainerName} for indexing", 
                fileName, containerName);

            var userInfo = await context.GetUserInfoAsync();
            
            // Get file metadata
            var metadata = await documentService.GetFileMetadataAsync(containerName, fileName, cancellationToken);
            
            // Construct blob path - BlobPath contains the full path within the storage container
            // Agent-Hub API expects blob_path to be the full path (directory + filename)
            var blobPath = !string.IsNullOrEmpty(metadata?.BlobPath) ? metadata.BlobPath : fileName;
            
            logger.LogInformation("File metadata retrieved: BlobPath={BlobPath}, HasMetadata={HasMetadata}", 
                blobPath, metadata != null);
            
            // Get the Agent-Hub-JCI API endpoint from configuration
            var agentHubEndpoint = configuration["DocumentToolsAPIEndpoint"];
            if (string.IsNullOrEmpty(agentHubEndpoint))
            {
                logger.LogError("DocumentToolsAPIEndpoint not configured in appsettings");
                return Results.Problem("Document processing service endpoint not configured");
            }
            
            // Extract just the filename without path for the file_name field
            var fileNameOnly = Path.GetFileName(blobPath);
            
            // Prepare request for Agent-Hub-JCI
            var vectorizeRequest = new
            {
                file_name = fileNameOnly,
                blob_path = blobPath,
                equipment_category = metadata?.EquipmentCategory,
                equipment_subcategory = metadata?.EquipmentSubcategory,
                equipment_part = metadata?.EquipmentPart,
                equipment_part_subcategory = metadata?.EquipmentPartSubcategory,
                product = metadata?.Product,
                manufacturer = metadata?.Manufacturer,
                document_type = metadata?.DocumentType,
                is_required_for_cde = metadata?.IsRequiredForCde,
                added_to_index = "No"
            };
            
            var targetUrl = $"{agentHubEndpoint}/knowledge-base/vectorize-document";
            logger.LogInformation("Calling Agent-Hub API at {Url} for file {FileName} with blob_path {BlobPath}", 
                targetUrl, fileNameOnly, blobPath);
            
            // Call Agent-Hub-JCI API
            var httpClient = httpClientFactory.CreateClient();
            httpClient.Timeout = TimeSpan.FromMinutes(5); // Set a longer timeout for document processing
            
            var response = await httpClient.PostAsJsonAsync(
                targetUrl,
                vectorizeRequest,
                cancellationToken);
            
            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogInformation("Successfully initiated vectorization for {FileName}: {Response}", 
                    fileName, responseContent);
                return TypedResults.Ok(new 
                { 
                    success = true, 
                    message = $"Document '{fileName}' indexing started successfully" 
                });
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogError("Failed to vectorize document {FileName}: Status={Status}, Error={Error}", 
                    fileName, response.StatusCode, errorContent);
                return Results.Problem($"Failed to start document indexing: {response.StatusCode} - {errorContent}");
            }
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "HTTP error calling Agent-Hub API for {FileName} from container {ContainerName}", 
                fileName, containerName);
            return Results.Problem("Error connecting to document processing service");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing document {FileName} from container {ContainerName}", 
                fileName, containerName);
            return Results.Problem("Error initiating document indexing");
        }
    }
}
