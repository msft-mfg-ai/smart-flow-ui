// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Extensions.Options;

namespace MinimalApi.Services;

public sealed class AzureBlobStorageService(BlobServiceClient blobServiceClient, IOptions<AppConfiguration> configuration, ProfileService profileService)
{
    internal async Task<UploadDocumentsResponse> UploadFilesAsync(UserInformation userInfo, IEnumerable<IFormFile> files, string storageContainerName, IDictionary<string, string> metadata, Dictionary<string, string>? filePathMap, CancellationToken cancellationToken)
    {
        try
        {
            var container = blobServiceClient.GetBlobContainerClient(storageContainerName);
            if (!await container.ExistsAsync())
            {
                // Create the container
                await container.CreateAsync();
                Console.WriteLine("Container created.");
            }

            List<UploadDocumentFileSummary> uploadedFiles = [];
            foreach (var file in files)
            {
                // Try to get the full path from the filePathMap, otherwise use the file.FileName
                var fileName = file.FileName;
                if (filePathMap != null && filePathMap.TryGetValue(fileName, out var fullPath))
                {
                    fileName = fullPath;
                }

                Console.WriteLine($"[UPLOAD DEBUG] Starting upload:");
                Console.WriteLine($"[UPLOAD DEBUG]   file.FileName = '{file.FileName}'");
                Console.WriteLine($"[UPLOAD DEBUG]   resolved fileName (from map or original) = '{fileName}'");
                Console.WriteLine($"[UPLOAD DEBUG]   Incoming metadata keys: {string.Join(", ", metadata.Keys)}");

                await using var stream = file.OpenReadStream();
                // Use the full file path (preserving folder structure if present)
                var blobName = fileName.Replace("\\", "/");
                Console.WriteLine($"[UPLOAD DEBUG]   blobName (after normalize) = '{blobName}'");

                var blobClient = container.GetBlobClient(blobName);
                Console.WriteLine($"[UPLOAD DEBUG]   blobClient.Name = '{blobClient.Name}'");
                //if (await blobClient.ExistsAsync(cancellationToken))
                //{
                //    continue;
                //}

                var url = blobClient.Uri.AbsoluteUri;
                await using var fileStream = file.OpenReadStream();
                await blobClient.UploadAsync(fileStream, overwrite: true);

                // Set metadata after upload
                // Note: Azure blob metadata keys are automatically converted to lowercase
                var uploadMetadata = new Dictionary<string, string>(metadata, StringComparer.OrdinalIgnoreCase);
                uploadMetadata["filename"] = Path.GetFileName(blobName);
                // Include container name in blobpath to show full path: containername/blobname
                uploadMetadata["blobpath"] = $"{storageContainerName}/{blobClient.Name}";
                uploadMetadata.Remove("folderPath"); // Remove client-side routing metadata

                Console.WriteLine($"[SAVE DEBUG] Saving metadata to Azure:");
                Console.WriteLine($"[SAVE DEBUG]   Container = '{storageContainerName}'");
                Console.WriteLine($"[SAVE DEBUG]   blobClient.Name = '{blobClient.Name}'");
                Console.WriteLine($"[SAVE DEBUG]   filename = '{uploadMetadata["filename"]}'");
                Console.WriteLine($"[SAVE DEBUG]   blobpath = '{uploadMetadata["blobpath"]}'");
                Console.WriteLine($"[SAVE DEBUG]   All metadata keys being saved: {string.Join(", ", uploadMetadata.Keys)}");

                await blobClient.SetMetadataAsync(uploadMetadata, cancellationToken: cancellationToken);

                var companyName = metadata.TryGetValue("CompanyName", out string? companyNameValue) ? companyNameValue : string.Empty;
                var industry = metadata.TryGetValue("Industry", out string? industryValue) ? industryValue : string.Empty;

                uploadedFiles.Add(new UploadDocumentFileSummary(blobName, file.Length, companyName, industry));
            }

            if (uploadedFiles.Count is 0)
            {
                return UploadDocumentsResponse.FromError("No files were uploaded. Either the files already exist or the files are not PDFs or images.");
            }

            return new UploadDocumentsResponse([.. uploadedFiles]);
        }
        catch (Exception ex)
        {
            return UploadDocumentsResponse.FromError(ex.ToString());
        }
    }

    internal async Task<bool> DeleteContainerAsync(string containerName, CancellationToken cancellationToken = default)
    {
        try
        {
            var container = blobServiceClient.GetBlobContainerClient(containerName);
            return await container.DeleteIfExistsAsync(cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error deleting container '{containerName}': {ex.Message}");
            return false;
        }
    }

    private static string BlobNameFromFilePage(string filename, long page = 0)
    {
        return Path.GetExtension(filename).ToLower() is ".pdf"
            ? $"{Path.GetFileNameWithoutExtension(filename)}_{page}.pdf"
            : Path.GetFileName(filename);
    }
}
