// Copyright (c) Microsoft. All rights reserved.

using Shared.Models;

namespace MinimalApi.Services.ChatHistory;

public class ProjectService
{
    private const string ProjectContainerName = "project-files";
    private const string ProjectExtractContainerName = "project-files-extract";
    private const string ProjectMetadataContainerName = "project-metadata";

    private readonly AzureBlobStorageService _blobStorageService;
    private readonly BlobServiceClient _blobServiceClient;

    public ProjectService(AzureBlobStorageService blobStorageService, BlobServiceClient blobServiceClient)
    {
        _blobStorageService = blobStorageService;
        _blobServiceClient = blobServiceClient;
    }

    /// <summary>
    /// Gets all projects from the project metadata container
    /// </summary>
    public async Task<List<CollectionInfo>> GetProjectsAsync(CancellationToken cancellationToken = default)
    {
        var projects = new List<CollectionInfo>();
        var containerClient = _blobServiceClient.GetBlobContainerClient(ProjectMetadataContainerName);

        if (!await containerClient.ExistsAsync(cancellationToken))
        {
            await containerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
            return projects;
        }

        await foreach (var blobItem in containerClient.GetBlobsAsync(traits: BlobTraits.Metadata, cancellationToken: cancellationToken))
        {
            var projectName = Path.GetFileNameWithoutExtension(blobItem.Name);
            var description = blobItem.Metadata?.TryGetValue("description", out var desc) == true ? desc : null;
            var type = blobItem.Metadata?.TryGetValue("type", out var typeValue) == true ? typeValue : null;

            projects.Add(new CollectionInfo(projectName, description, type));
        }

        return projects;
    }

    /// <summary>
    /// Creates a new project by adding a metadata blob and creating the project folder
    /// </summary>
    public async Task<bool> CreateProjectAsync(string projectName, string? description = null, string? type = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(ProjectMetadataContainerName);
            await containerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

            var blobName = $"{projectName}.json";
            var blobClient = containerClient.GetBlobClient(blobName);

            // Check if project already exists
            if (await blobClient.ExistsAsync(cancellationToken))
            {
                return false;
            }

            // Create metadata blob
            var metadata = new Dictionary<string, string>();
            if (!string.IsNullOrWhiteSpace(description))
                metadata["description"] = description;
            if (!string.IsNullOrWhiteSpace(type))
                metadata["type"] = type;

            var projectInfo = new { name = projectName, description, type, createdDate = DateTime.UtcNow };
            var json = System.Text.Json.JsonSerializer.Serialize(projectInfo);

            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
            await blobClient.UploadAsync(stream, new BlobHttpHeaders { ContentType = "application/json" }, metadata, cancellationToken: cancellationToken);

            // Ensure main container exists
            var projectContainerClient = _blobServiceClient.GetBlobContainerClient(ProjectContainerName);
            await projectContainerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

            // Create the project folder by creating a placeholder blob
            var projectFolderBlob = projectContainerClient.GetBlobClient($"{projectName}/.folderplaceholder");
            using var placeholderStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(""));
            await projectFolderBlob.UploadAsync(placeholderStream, overwrite: true, cancellationToken: cancellationToken);

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Deletes a project folder and all its files
    /// </summary>
    public async Task<bool> DeleteProjectAsync(string projectName, CancellationToken cancellationToken = default)
    {
        try
        {
            // Delete the metadata blob
            var metadataContainerClient = _blobServiceClient.GetBlobContainerClient(ProjectMetadataContainerName);
            if (await metadataContainerClient.ExistsAsync(cancellationToken))
            {
                var blobName = $"{projectName}.json";
                var blobClient = metadataContainerClient.GetBlobClient(blobName);
                await blobClient.DeleteIfExistsAsync(cancellationToken: cancellationToken);
            }

            // Delete all files in the project folder (project-files/{projectName}/*)
            var containerClient = _blobServiceClient.GetBlobContainerClient(ProjectContainerName);
            if (await containerClient.ExistsAsync(cancellationToken))
            {
                var projectFolderPrefix = $"{projectName}/";
                await foreach (var blobItem in containerClient.GetBlobsAsync(prefix: projectFolderPrefix, cancellationToken: cancellationToken))
                {
                    var blobClient = containerClient.GetBlobClient(blobItem.Name);
                    await blobClient.DeleteIfExistsAsync(cancellationToken: cancellationToken);
                }
            }

            // Delete all processing files in the project folder (project-files-extract/{projectName}/*)
            var extractContainerClient = _blobServiceClient.GetBlobContainerClient(ProjectExtractContainerName);
            if (await extractContainerClient.ExistsAsync(cancellationToken))
            {
                var projectFolderPrefix = $"{projectName}/";
                await foreach (var blobItem in extractContainerClient.GetBlobsAsync(prefix: projectFolderPrefix, cancellationToken: cancellationToken))
                {
                    var blobClient = extractContainerClient.GetBlobClient(blobItem.Name);
                    await blobClient.DeleteIfExistsAsync(cancellationToken: cancellationToken);
                }
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Updates project metadata
    /// </summary>
    public async Task<bool> UpdateProjectMetadataAsync(string projectName, string? description = null, string? type = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(ProjectMetadataContainerName);
            if (!await containerClient.ExistsAsync(cancellationToken))
            {
                return false;
            }

            var blobName = $"{projectName}.json";
            var blobClient = containerClient.GetBlobClient(blobName);

            if (!await blobClient.ExistsAsync(cancellationToken))
            {
                return false;
            }

            // Update metadata
            var metadata = new Dictionary<string, string>();
            if (!string.IsNullOrWhiteSpace(description))
                metadata["description"] = description;
            if (!string.IsNullOrWhiteSpace(type))
                metadata["type"] = type;

            await blobClient.SetMetadataAsync(metadata, cancellationToken: cancellationToken);

            // Update blob content
            var projectInfo = new { name = projectName, description, type, updatedDate = DateTime.UtcNow };
            var json = System.Text.Json.JsonSerializer.Serialize(projectInfo);

            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
            await blobClient.UploadAsync(stream, overwrite: true, cancellationToken: cancellationToken);

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Gets project metadata
    /// </summary>
    public async Task<CollectionInfo?> GetProjectMetadataAsync(string projectName, CancellationToken cancellationToken = default)
    {
        try
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(ProjectMetadataContainerName);
            if (!await containerClient.ExistsAsync(cancellationToken))
            {
                return null;
            }

            var blobName = $"{projectName}.json";
            var blobClient = containerClient.GetBlobClient(blobName);

            if (!await blobClient.ExistsAsync(cancellationToken))
            {
                return null;
            }

            var properties = await blobClient.GetPropertiesAsync(cancellationToken: cancellationToken);
            var metadata = properties.Value.Metadata;

            var description = metadata?.TryGetValue("description", out var desc) == true ? desc : null;
            var type = metadata?.TryGetValue("type", out var typeValue) == true ? typeValue : null;

            return new CollectionInfo(projectName, description, type);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Deletes all workflow processing files for a project (entire project folder in extract container)
    /// </summary>
    public async Task<bool> DeleteProjectWorkflowAsync(string projectName, CancellationToken cancellationToken = default)
    {
        try
        {
            var extractContainerClient = _blobServiceClient.GetBlobContainerClient(ProjectExtractContainerName);
            if (!await extractContainerClient.ExistsAsync(cancellationToken))
            {
                return false;
            }

            // Delete all blobs in the project folder
            var projectFolderPrefix = $"{projectName}/";
            var deletedCount = 0;

            await foreach (var blobItem in extractContainerClient.GetBlobsAsync(prefix: projectFolderPrefix, cancellationToken: cancellationToken))
            {
                var blobClient = extractContainerClient.GetBlobClient(blobItem.Name);
                await blobClient.DeleteIfExistsAsync(cancellationToken: cancellationToken);
                deletedCount++;
            }

            return deletedCount > 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Gets all files for a specific project from its folder
    /// </summary>
    public async Task<List<ContainerFileInfo>> GetProjectFilesAsync(string projectName, CancellationToken cancellationToken = default)
    {
        var files = new List<ContainerFileInfo>();
        var containerClient = _blobServiceClient.GetBlobContainerClient(ProjectContainerName);

        if (!await containerClient.ExistsAsync(cancellationToken))
        {
            return files;
        }

        var extractContainerClient = _blobServiceClient.GetBlobContainerClient(ProjectExtractContainerName);
        var extractContainerExists = await extractContainerClient.ExistsAsync(cancellationToken);
        
        var projectFolderPrefix = $"{projectName}/";

        // Get all project-level processing files first
        var projectProcessingFiles = new List<string>();
        if (extractContainerExists)
        {
            await foreach (var extractBlobItem in extractContainerClient.GetBlobsAsync(prefix: projectFolderPrefix, cancellationToken: cancellationToken))
            {
                projectProcessingFiles.Add(extractBlobItem.Name);
            }
        }

        // Get all input files in the project folder
        await foreach (var blobItem in containerClient.GetBlobsAsync(traits: BlobTraits.Metadata, prefix: projectFolderPrefix, cancellationToken: cancellationToken))
        {
            // Skip placeholder files
            if (blobItem.Name.EndsWith("/.folderplaceholder"))
            {
                continue;
            }

            // Get description from blob metadata
            var description = blobItem.Metadata?.TryGetValue("description", out var desc) == true ? desc : null;
            
            // Extract metadata from blob properties
            FileMetadata? metadata = null;
            if (blobItem.Metadata != null && blobItem.Metadata.Count > 0)
            {
                metadata = new FileMetadata
                {
                    FileName = blobItem.Metadata.TryGetValue("filename", out var fn) ? fn : Path.GetFileName(blobItem.Name),
                    BlobPath = blobItem.Metadata.TryGetValue("blobpath", out var bp) ? bp : blobItem.Name,
                    EquipmentCategory = blobItem.Metadata.TryGetValue("equipmentcategory", out var ec) ? ec : "",
                    EquipmentSubcategory = blobItem.Metadata.TryGetValue("equipmentsubcategory", out var es) ? es : "",
                    EquipmentPart = blobItem.Metadata.TryGetValue("equipmentpart", out var ep) ? ep : "",
                    EquipmentPartSubcategory = blobItem.Metadata.TryGetValue("equipmentpartsubcategory", out var eps) ? eps : "",
                    Product = blobItem.Metadata.TryGetValue("product", out var p) ? p : "",
                    Manufacturer = blobItem.Metadata.TryGetValue("manufacturer", out var m) ? m : "",
                    DocumentType = blobItem.Metadata.TryGetValue("documenttype", out var dt) ? dt : "",
                    IsRequiredForCde = blobItem.Metadata.TryGetValue("isrequiredforcde", out var irc) ? irc : "No",
                    AddedToIndex = blobItem.Metadata.TryGetValue("addedtoindex", out var ati) ? ati : "No"
                };
            }

            var fileInfo = new ContainerFileInfo(blobItem.Name, description)
            {
                Metadata = metadata
            };

            // Check for processing files - no need to check metadata, just match by base name
            if (extractContainerExists)
            {
                var baseNameWithoutExtension = Path.GetFileNameWithoutExtension(blobItem.Name);

                await foreach (var extractBlobItem in extractContainerClient.GetBlobsAsync(prefix: projectFolderPrefix, cancellationToken: cancellationToken))
                {
                    if (Path.GetFileNameWithoutExtension(extractBlobItem.Name).StartsWith(baseNameWithoutExtension, StringComparison.OrdinalIgnoreCase))
                    {
                        fileInfo.ProcessingFiles.Add(extractBlobItem.Name);
                    }
                }
            }

            files.Add(fileInfo);
        }

        // Add all project-level processing files to the first file entry (if any files exist)
        // This is a workaround since processing files are project-level, not per-file
        if (files.Any() && projectProcessingFiles.Any())
        {
            files[0].ProcessingFiles.AddRange(projectProcessingFiles);
        }

        return files;
    }

    /// <summary>
    /// Uploads files to a project folder
    /// </summary>
    public async Task<UploadDocumentsResponse> UploadFilesToProjectAsync(
        UserInformation userInfo,
        IFormFileCollection files,
        string projectName,
        Dictionary<string, string>? metadata = null,
        Dictionary<string, string>? filePathMap = null,
        CancellationToken cancellationToken = default)
    {
        // Create a new filePathMap that prepends the project folder to each file
        var projectFilePathMap = new Dictionary<string, string>();
        
        foreach (var file in files)
        {
            var originalFileName = file.FileName;
            string targetPath;
            
            // If filePathMap exists and has this file, use it; otherwise use the original filename
            if (filePathMap != null && filePathMap.TryGetValue(originalFileName, out var mappedPath))
            {
                targetPath = $"{projectName}/{mappedPath}";
            }
            else
            {
                targetPath = $"{projectName}/{originalFileName}";
            }
            
            projectFilePathMap[originalFileName] = targetPath;
        }

        var fileMetadata = metadata ?? new Dictionary<string, string>();
        // Remove the project metadata tag since we're using folder-based organization
        fileMetadata.Remove("project");

        var response = await _blobStorageService.UploadFilesAsync(userInfo, files, ProjectContainerName, fileMetadata, projectFilePathMap, cancellationToken);
        return response;
    }

    /// <summary>
    /// Deletes a file from a project folder
    /// </summary>
    public async Task<bool> DeleteFileFromProjectAsync(string projectName, string fileName, CancellationToken cancellationToken = default)
    {
        try
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(ProjectContainerName);

            if (!await containerClient.ExistsAsync(cancellationToken))
            {
                return false;
            }

            // The fileName should already include the project folder prefix if it came from GetProjectFilesAsync
            // But ensure it starts with the project name
            var blobName = fileName.StartsWith($"{projectName}/") ? fileName : $"{projectName}/{fileName}";
            var blobClient = containerClient.GetBlobClient(blobName);

            if (!await blobClient.ExistsAsync(cancellationToken))
            {
                return false;
            }

            // Delete the main file
            await blobClient.DeleteAsync(cancellationToken: cancellationToken);

            // Delete associated processing files in the project folder
            var extractContainerClient = _blobServiceClient.GetBlobContainerClient(ProjectExtractContainerName);
            var extractContainerExists = await extractContainerClient.ExistsAsync(cancellationToken);

            if (extractContainerExists)
            {
                var baseNameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
                // Search entire project folder for processing files matching the base name
                var projectFolderPrefix = $"{projectName}/";

                await foreach (var extractBlobItem in extractContainerClient.GetBlobsAsync(prefix: projectFolderPrefix, cancellationToken: cancellationToken))
                {
                    if (Path.GetFileNameWithoutExtension(extractBlobItem.Name).StartsWith(baseNameWithoutExtension, StringComparison.OrdinalIgnoreCase))
                    {
                        var extractBlobClient = extractContainerClient.GetBlobClient(extractBlobItem.Name);
                        await extractBlobClient.DeleteIfExistsAsync(cancellationToken: cancellationToken);
                    }
                }
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Downloads a file from a project folder
    /// </summary>
    public async Task<(Stream? stream, string contentType)> DownloadFileAsync(
        string projectName,
        string fileName,
        bool isProcessingFile = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var containerName = isProcessingFile ? ProjectExtractContainerName : ProjectContainerName;
            var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);

            if (!await containerClient.ExistsAsync(cancellationToken))
            {
                return (null, string.Empty);
            }

            // For both input and processing files, the fileName should already include the project folder path
            // Verify that the file is in the correct project folder
            if (!fileName.StartsWith($"{projectName}/", StringComparison.OrdinalIgnoreCase))
            {
                return (null, string.Empty);
            }

            var blobClient = containerClient.GetBlobClient(fileName);

            if (!await blobClient.ExistsAsync(cancellationToken))
            {
                return (null, string.Empty);
            }

            var download = await blobClient.DownloadAsync(cancellationToken);
            var blobProperties = await blobClient.GetPropertiesAsync(cancellationToken: cancellationToken);
            var contentType = blobProperties.Value.ContentType;

            if (string.IsNullOrEmpty(contentType))
            {
                contentType = Path.GetExtension(fileName).ToLowerInvariant() switch
                {
                    ".pdf" => "application/pdf",
                    ".md" => "text/markdown",
                    ".txt" => "text/plain",
                    ".json" => "application/json",
                    _ => "application/octet-stream"
                };
            }

            return (download.Value.Content, contentType);
        }
        catch
        {
            return (null, string.Empty);
        }
    }

    /// <summary>
    /// Updates the description metadata for a specific file in a project
    /// </summary>
    public async Task<bool> UpdateFileDescriptionAsync(string projectName, string fileName, string? description, CancellationToken cancellationToken = default)
    {
        try
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(ProjectContainerName);
            
            if (!await containerClient.ExistsAsync(cancellationToken))
            {
                return false;
            }

            // The fileName should already include the project folder prefix if it came from GetProjectFilesAsync
            // But ensure it starts with the project name
            var blobName = fileName.StartsWith($"{projectName}/") ? fileName : $"{projectName}/{fileName}";
            var blobClient = containerClient.GetBlobClient(blobName);
            
            if (!await blobClient.ExistsAsync(cancellationToken))
            {
                return false;
            }

            // Get current metadata and update description
            var properties = await blobClient.GetPropertiesAsync(cancellationToken: cancellationToken);
            var metadata = new Dictionary<string, string>(properties.Value.Metadata);
            
            if (!string.IsNullOrWhiteSpace(description))
            {
                metadata["description"] = description;
            }
            else
            {
                metadata.Remove("description");
            }

            await blobClient.SetMetadataAsync(metadata, cancellationToken: cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
