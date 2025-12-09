// Copyright (c) Microsoft. All rights reserved.

using Shared.Json;
using Shared.Models;

namespace MinimalApi.Services.ChatHistory;

public class DocumentService
{
    private readonly AzureBlobStorageService _blobStorageService;
    private readonly AppConfiguration _configuration;
    private readonly BlobServiceClient _blobServiceClient;

    public DocumentService(AzureBlobStorageService blobStorageService, AppConfiguration configuration, BlobServiceClient blobServiceClient)
    {
        _blobStorageService = blobStorageService;
        _configuration = configuration;
        _blobServiceClient = blobServiceClient;
    }

    public async Task<UploadDocumentsResponse> CreateDocumentUploadAsync(UserInformation userInfo, IFormFileCollection files, string selectedProfile, Dictionary<string, string>? fileMetadata, CancellationToken cancellationToken)
    {
        var response = await _blobStorageService.UploadFilesAsync(userInfo, files, selectedProfile, new Dictionary<string, string>(), null, cancellationToken);
        return response;
    }

    /// <summary>
    /// Gets all blob storage containers that have the metadata tag "managed-collection" set to "true"
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of collection information with metadata</returns>
    public async Task<List<CollectionInfo>> GetCollectionsAsync(CancellationToken cancellationToken = default)
    {
        var collections = new List<CollectionInfo>();

        await foreach (var containerItem in _blobServiceClient.GetBlobContainersAsync(BlobContainerTraits.Metadata, cancellationToken: cancellationToken))
        {
            if (containerItem.Properties?.Metadata != null &&
                containerItem.Properties.Metadata.TryGetValue("managedcollection", out var value) &&
                value?.Equals("true", StringComparison.OrdinalIgnoreCase) == true)
            {
                var description = containerItem.Properties.Metadata.TryGetValue("description", out var desc) ? desc : null;
                var type = containerItem.Properties.Metadata.TryGetValue("type", out var typeValue) ? typeValue : null;
                var indexName = containerItem.Properties.Metadata.TryGetValue("indexname", out var indexValue) ? indexValue : null;

                collections.Add(new CollectionInfo(containerItem.Name, description, type, indexName));
            }
        }

        return collections;
    }

    /// <summary>
    /// Adds the "managed-collection": "true" metadata tag to the specified container along with optional description and type.
    /// Creates the container if it doesn't exist.
    /// </summary>
    /// <param name="containerName">The name of the container to tag</param>
    /// <param name="description">Optional description of the collection</param>
    /// <param name="type">Optional type/category of the collection</param>
    /// <param name="indexName">Optional Azure AI Search index name to associate</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if successful, false otherwise</returns>
    public async Task<bool> AddManagedCollectionTagAsync(string containerName, string? description = null, string? type = null, string? indexName = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);

            // Create the container if it doesn't exist
            if (!await containerClient.ExistsAsync(cancellationToken))
            {
                await containerClient.CreateAsync(cancellationToken: cancellationToken);
            }

            // Get current metadata (if container already existed, preserve existing metadata)
            var properties = await containerClient.GetPropertiesAsync(cancellationToken: cancellationToken);
            var metadata = properties.Value.Metadata ?? new Dictionary<string, string>();

            // Add or update the managed-collection tag
            metadata["managedcollection"] = "true";

            // Add or update description if provided
            if (!string.IsNullOrWhiteSpace(description))
            {
                metadata["description"] = description;
            }

            // Add or update type if provided
            if (!string.IsNullOrWhiteSpace(type))
            {
                metadata["type"] = type;
            }

            // Add or update index name if provided
            if (!string.IsNullOrWhiteSpace(indexName))
            {
                metadata["indexname"] = indexName;
            }
            else
            {
                metadata.Remove("indexname");
            }

            await containerClient.SetMetadataAsync(metadata, cancellationToken: cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Updates the metadata for an existing collection
    /// </summary>
    /// <param name="containerName">The name of the container</param>
    /// <param name="description">Optional description of the collection</param>
    /// <param name="type">Optional type/category of the collection</param>
    /// <param name="indexName">Optional Azure AI Search index name to associate</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if successful, false otherwise</returns>
    public async Task<bool> UpdateCollectionMetadataAsync(string containerName, string? description = null, string? type = null, string? indexName = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);

            if (!await containerClient.ExistsAsync(cancellationToken))
            {
                return false;
            }

            // Get current metadata
            var properties = await containerClient.GetPropertiesAsync(cancellationToken: cancellationToken);
            var metadata = properties.Value.Metadata ?? new Dictionary<string, string>();

            // Update description
            if (!string.IsNullOrWhiteSpace(description))
            {
                metadata["description"] = description;
            }
            else
            {
                metadata.Remove("description");
            }

            // Update type
            if (!string.IsNullOrWhiteSpace(type))
            {
                metadata["type"] = type;
            }
            else
            {
                metadata.Remove("type");
            }

            // Update index name
            if (!string.IsNullOrWhiteSpace(indexName))
            {
                metadata["indexname"] = indexName;
            }
            else
            {
                metadata.Remove("indexname");
            }

            await containerClient.SetMetadataAsync(metadata, cancellationToken: cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Removes the "managed-collection" metadata tag from the specified container.
    /// The container and its contents remain intact, it just removes it from the managed collections list.
    /// </summary>
    /// <param name="containerName">The name of the container to untag</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if successful, false otherwise</returns>
    public async Task<bool> RemoveManagedCollectionTagAsync(string containerName, CancellationToken cancellationToken = default)
    {
        try
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);

            if (!await containerClient.ExistsAsync(cancellationToken))
            {
                return false;
            }

            // Get current metadata
            var properties = await containerClient.GetPropertiesAsync(cancellationToken: cancellationToken);
            var metadata = properties.Value.Metadata ?? new Dictionary<string, string>();

            // Remove the managed-collection tag and associated metadata
            metadata.Remove("managedcollection");
            metadata.Remove("description");
            metadata.Remove("type");

            await containerClient.SetMetadataAsync(metadata, cancellationToken: cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Deletes a blob container completely, removing all files and the container itself
    /// </summary>
    /// <param name="containerName">The name of the container to delete</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if successful, false otherwise</returns>
    public async Task<bool> DeleteContainerAsync(string containerName, CancellationToken cancellationToken = default)
    {
        return await _blobStorageService.DeleteContainerAsync(containerName, cancellationToken);
    }

    /// <summary>
    /// Gets metadata for a specific collection
    /// </summary>
    /// <param name="containerName">The name of the container</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Collection information with metadata, or null if not found</returns>
    public async Task<CollectionInfo?> GetCollectionMetadataAsync(string containerName, CancellationToken cancellationToken = default)
    {
        try
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);

            if (!await containerClient.ExistsAsync(cancellationToken))
            {
                return null;
            }

            var properties = await containerClient.GetPropertiesAsync(cancellationToken: cancellationToken);
            var metadata = properties.Value.Metadata;

            if (metadata == null)
            {
                return new CollectionInfo(containerName);
            }

            var description = metadata.TryGetValue("description", out var desc) ? desc : null;
            var type = metadata.TryGetValue("type", out var typeValue) ? typeValue : null;
            var indexName = metadata.TryGetValue("indexname", out var indexValue) ? indexValue : null;

            return new CollectionInfo(containerName, description, type, indexName);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Gets all files (blobs) in the specified container along with their associated processing files
    /// </summary>
    /// <param name="containerName">The name of the container</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of container file information including main files and their processing files</returns>
    public async Task<List<ContainerFileInfo>> GetFilesInContainerAsync(string containerName, CancellationToken cancellationToken = default)
    {
        var files = new List<ContainerFileInfo>();
        var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);

        if (!await containerClient.ExistsAsync(cancellationToken))
        {
            return files;
        }

        // Get the extract container client
        var extractContainerName = $"{containerName}-extract";
        var extractContainerClient = _blobServiceClient.GetBlobContainerClient(extractContainerName);
        var extractContainerExists = await extractContainerClient.ExistsAsync(cancellationToken);

        await foreach (var blobItem in containerClient.GetBlobsAsync(traits: Azure.Storage.Blobs.Models.BlobTraits.Metadata, cancellationToken: cancellationToken))
        {
            // Skip placeholder files
            if (blobItem.Name.EndsWith("/.folderplaceholder"))
            {
                continue;
            }

            // Extract folder path from the blob name
            var pathParts = blobItem.Name.Split('/');
            var folderPath = pathParts.Length > 1
                ? string.Join("/", pathParts.Take(pathParts.Length - 1))
                : "";

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

            var fileInfo = new ContainerFileInfo(blobItem.Name)
            {
                FolderPath = folderPath,
                Metadata = metadata
            };

            // Check for processing files in the extract container
            if (extractContainerExists)
            {
                var baseNameWithoutExtension = Path.GetFileNameWithoutExtension(blobItem.Name);
                // Use blob-style path parsing (forward slashes) instead of OS-specific Path.GetDirectoryName
                var lastSlashIndex = blobItem.Name.LastIndexOf('/');
                var directoryPath = lastSlashIndex >= 0 ? blobItem.Name.Substring(0, lastSlashIndex) : "";

                // Look for files with the same base name in the extract container
                var searchPrefix = string.IsNullOrEmpty(directoryPath)
                    ? baseNameWithoutExtension
                    : $"{directoryPath}/{baseNameWithoutExtension}";

                await foreach (var extractBlobItem in extractContainerClient.GetBlobsAsync(prefix: searchPrefix, cancellationToken: cancellationToken))
                {
                    var extractBaseName = Path.GetFileNameWithoutExtension(extractBlobItem.Name);
                    var originalBaseName = Path.GetFileNameWithoutExtension(blobItem.Name);
                    fileInfo.ProcessingFiles.Add(extractBlobItem.Name);
                }
            }

            files.Add(fileInfo);
        }

        return files;
    }

    /// <summary>
    /// Uploads files to a specific container using the blob storage service
    /// </summary>
    /// <param name="userInfo">User information</param>
    /// <param name="files">Files to upload</param>
    /// <param name="containerName">Target container name</param>
    /// <param name="metadata">Optional metadata for the files</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Upload response with details of uploaded files</returns>
    public async Task<UploadDocumentsResponse> UploadFilesToContainerAsync(
        UserInformation userInfo,
        IFormFileCollection files,
        string containerName,
        Dictionary<string, string>? metadata = null,
        Dictionary<string, string>? filePathMap = null,
        CancellationToken cancellationToken = default)
    {
        var fileMetadata = metadata ?? new Dictionary<string, string>();
        var response = await _blobStorageService.UploadFilesAsync(userInfo, files, containerName, fileMetadata, filePathMap, cancellationToken);
        return response;
    }

    /// <summary>
    /// Deletes a file from a container
    /// <param name="containerName">The name of the container</param>
    /// <param name="fileName">The name of the file to delete</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if file was deleted successfully, false otherwise</returns>
    public async Task<bool> DeleteFileFromContainerAsync(
        string containerName,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);

            if (!await containerClient.ExistsAsync(cancellationToken))
            {
                return false;
            }

            var blobClient = containerClient.GetBlobClient(fileName);

            if (!await blobClient.ExistsAsync(cancellationToken))
            {
                return false;
            }

            // Delete the main file
            await blobClient.DeleteAsync(cancellationToken: cancellationToken);

            // Delete associated processing files from the extract container
            var extractContainerName = $"{containerName}-extract";
            var extractContainerClient = _blobServiceClient.GetBlobContainerClient(extractContainerName);
            var extractContainerExists = await extractContainerClient.ExistsAsync(cancellationToken);

            if (extractContainerExists)
            {
                var baseNameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
                var directoryPath = Path.GetDirectoryName(fileName);

                // Look for files with the same base name in the extract container
                var searchPrefix = string.IsNullOrEmpty(directoryPath)
                    ? baseNameWithoutExtension
                    : $"{directoryPath}/{baseNameWithoutExtension}";

                await foreach (var extractBlobItem in extractContainerClient.GetBlobsAsync(prefix: searchPrefix, cancellationToken: cancellationToken))
                {
                    var extractBlobClient = extractContainerClient.GetBlobClient(extractBlobItem.Name);
                    await extractBlobClient.DeleteIfExistsAsync(cancellationToken: cancellationToken);
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
    /// Downloads a file from a specific container
    /// </summary>
    /// <param name="containerName">The name of the container</param>
    /// <param name="fileName">The name of the file to download</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Tuple containing the file stream and content type</returns>
    public async Task<(Stream? stream, string contentType)> DownloadFileAsync(
        string containerName,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);

            if (!await containerClient.ExistsAsync(cancellationToken))
            {
                return (null, string.Empty);
            }

            var blobClient = containerClient.GetBlobClient(fileName);

            if (!await blobClient.ExistsAsync(cancellationToken))
            {
                return (null, string.Empty);
            }

            var download = await blobClient.DownloadAsync(cancellationToken);
            var properties = await blobClient.GetPropertiesAsync(cancellationToken: cancellationToken);

            var contentType = properties.Value.ContentType;

            // Set appropriate content type based on file extension if not already set
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

    public async Task<List<DocumentUpload>> GetDocumentUploadsAsync(UserInformation user, string? profileId = null)
    {
        return null;
    }

    /// <summary>
    /// Gets the folder structure for a container
    /// </summary>
    public async Task<FolderNode> GetFolderStructureAsync(string containerName, CancellationToken cancellationToken = default)
    {
        var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
        var rootNode = new FolderNode("root", "");
        var folderMap = new Dictionary<string, FolderNode>();
        folderMap[""] = rootNode;

        await foreach (var blobItem in containerClient.GetBlobsAsync(cancellationToken: cancellationToken))
        {
            var path = blobItem.Name;
            var pathParts = path.Split('/');
            var isPlaceholder = path.EndsWith("/.folderplaceholder");

            // Build folder hierarchy
            var currentPath = "";
            for (int i = 0; i < pathParts.Length - 1; i++)
            {
                var parentPath = currentPath;
                currentPath = string.IsNullOrEmpty(currentPath) ? pathParts[i] : $"{currentPath}/{pathParts[i]}";

                if (!folderMap.ContainsKey(currentPath))
                {
                    var folderNode = new FolderNode(pathParts[i], currentPath);
                    folderMap[currentPath] = folderNode;

                    if (folderMap.ContainsKey(parentPath))
                    {
                        folderMap[parentPath].Children.Add(folderNode);
                    }
                }
            }

            // Increment file count for the containing folder (but not for placeholder files)
            if (!isPlaceholder)
            {
                var folderPath = string.Join("/", pathParts.Take(pathParts.Length - 1));
                if (folderMap.ContainsKey(folderPath))
                {
                    folderMap[folderPath].FileCount++;
                }
            }
        }

        return rootNode;
    }

    /// <summary>
    /// Creates a folder in a container by uploading a placeholder file
    /// </summary>
    public async Task<bool> CreateFolderAsync(string containerName, string folderPath, CancellationToken cancellationToken = default)
    {
        try
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);

            if (!await containerClient.ExistsAsync(cancellationToken))
            {
                return false;
            }

            // Create a placeholder file to represent the folder
            var placeholderPath = $"{folderPath.TrimEnd('/')}/.folderplaceholder";
            var blobClient = containerClient.GetBlobClient(placeholderPath);

            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(""));
            await blobClient.UploadAsync(stream, overwrite: true, cancellationToken: cancellationToken);

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Renames a folder by moving all files within it
    /// </summary>
    public async Task<bool> RenameFolderAsync(string containerName, string oldFolderPath, string newFolderPath, CancellationToken cancellationToken = default)
    {
        try
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);

            if (!await containerClient.ExistsAsync(cancellationToken))
            {
                return false;
            }

            oldFolderPath = oldFolderPath.TrimEnd('/') + "/";
            newFolderPath = newFolderPath.TrimEnd('/') + "/";

            // Find all blobs in the old folder
            var blobsToMove = new List<string>();
            await foreach (var blobItem in containerClient.GetBlobsAsync(prefix: oldFolderPath, cancellationToken: cancellationToken))
            {
                blobsToMove.Add(blobItem.Name);
            }

            // Move each blob
            foreach (var oldBlobName in blobsToMove)
            {
                var newBlobName = newFolderPath + oldBlobName.Substring(oldFolderPath.Length);
                var sourceBlobClient = containerClient.GetBlobClient(oldBlobName);
                var destBlobClient = containerClient.GetBlobClient(newBlobName);

                // Copy to new location
                await destBlobClient.StartCopyFromUriAsync(sourceBlobClient.Uri, cancellationToken: cancellationToken);

                // Wait for copy to complete
                await Task.Delay(100, cancellationToken);

                // Delete old blob
                await sourceBlobClient.DeleteIfExistsAsync(cancellationToken: cancellationToken);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Deletes a folder and all files within it
    /// </summary>
    public async Task<bool> DeleteFolderAsync(string containerName, string folderPath, CancellationToken cancellationToken = default)
    {
        try
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);

            if (!await containerClient.ExistsAsync(cancellationToken))
            {
                return false;
            }

            folderPath = folderPath.TrimEnd('/') + "/";

            // Find and delete all blobs in the folder
            await foreach (var blobItem in containerClient.GetBlobsAsync(prefix: folderPath, cancellationToken: cancellationToken))
            {
                var blobClient = containerClient.GetBlobClient(blobItem.Name);
                await blobClient.DeleteIfExistsAsync(cancellationToken: cancellationToken);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Updates metadata for a file
    /// </summary>
    public async Task<bool> UpdateFileMetadataAsync(string containerName, string fileName, FileMetadata metadata, CancellationToken cancellationToken = default)
    {
        try
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);

            if (!await containerClient.ExistsAsync(cancellationToken))
            {
                return false;
            }

            var blobClient = containerClient.GetBlobClient(fileName);

            if (!await blobClient.ExistsAsync(cancellationToken))
            {
                return false;
            }

            var blobMetadata = new Dictionary<string, string>
            {
                ["filename"] = metadata.FileName ?? "",
                ["blobpath"] = metadata.BlobPath ?? "",
                ["equipmentcategory"] = metadata.EquipmentCategory ?? "",
                ["equipmentsubcategory"] = metadata.EquipmentSubcategory ?? "",
                ["equipmentpart"] = metadata.EquipmentPart ?? "",
                ["equipmentpartsubcategory"] = metadata.EquipmentPartSubcategory ?? "",
                ["product"] = metadata.Product ?? "",
                ["manufacturer"] = metadata.Manufacturer ?? "",
                ["documenttype"] = metadata.DocumentType ?? "",
                ["isrequiredforcde"] = metadata.IsRequiredForCde ?? "No",
                ["addedtoindex"] = metadata.AddedToIndex ?? "No"
            };

            await blobClient.SetMetadataAsync(blobMetadata, cancellationToken: cancellationToken);

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Gets metadata for a file
    /// </summary>
    public async Task<FileMetadata?> GetFileMetadataAsync(string containerName, string fileName, CancellationToken cancellationToken = default)
    {
        try
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);

            if (!await containerClient.ExistsAsync(cancellationToken))
            {
                return null;
            }

            var blobClient = containerClient.GetBlobClient(fileName);

            if (!await blobClient.ExistsAsync(cancellationToken))
            {
                return null;
            }

            var properties = await blobClient.GetPropertiesAsync(cancellationToken: cancellationToken);
            var metadata = properties.Value.Metadata;

            return new FileMetadata
            {
                FileName = metadata.TryGetValue("filename", out var fn) ? fn : Path.GetFileName(fileName),
                BlobPath = metadata.TryGetValue("blobpath", out var bp) ? bp : fileName,
                EquipmentCategory = metadata.TryGetValue("equipmentcategory", out var ec) ? ec : "",
                EquipmentSubcategory = metadata.TryGetValue("equipmentsubcategory", out var es) ? es : "",
                EquipmentPart = metadata.TryGetValue("equipmentpart", out var ep) ? ep : "",
                EquipmentPartSubcategory = metadata.TryGetValue("equipmentpartsubcategory", out var eps) ? eps : "",
                Product = metadata.TryGetValue("product", out var p) ? p : "",
                Manufacturer = metadata.TryGetValue("manufacturer", out var m) ? m : "",
                DocumentType = metadata.TryGetValue("documenttype", out var dt) ? dt : "",
                IsRequiredForCde = metadata.TryGetValue("isrequiredforcde", out var irc) ? irc : "No",
                AddedToIndex = metadata.TryGetValue("addedtoindex", out var ati) ? ati : "No"
            };
        }
        catch
        {
            return null;
        }
    }
}
