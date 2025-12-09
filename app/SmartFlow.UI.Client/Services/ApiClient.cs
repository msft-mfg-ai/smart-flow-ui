// Copyright (c) Microsoft. All rights reserved.

using SmartFlow.UI.Client.Utilities;

namespace SmartFlow.UI.Client.Services;

public sealed class ApiClient(HttpClient httpClient)
{
    private static UserInformation? _UserInformation = null;
    public async Task IngestionTriggerAsync(string sourceContainer, string indexName)
    {
        var request = new IngestionRequest(sourceContainer, $"{sourceContainer}-extract", indexName);
        var json = System.Text.Json.JsonSerializer.Serialize(request, SerializerOptions.Default);
        using var body = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await httpClient.PostAsync("api/ingestion/trigger", body);
    }

    // Collection Management APIs
    public async Task<List<CollectionInfo>> GetCollectionsAsync()
    {
        try
        {
            var response = await httpClient.GetAsync("api/collections");
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<List<CollectionInfo>>() ?? new List<CollectionInfo>();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error fetching collections: {ex.Message}");
            return new List<CollectionInfo>();
        }
    }

    public async Task<bool> CreateCollectionAsync(string containerName, string? description = null, string? type = null, string? indexName = null)
    {
        try
        {
            var request = new CreateCollectionRequest
            {
                Name = containerName,
                Description = description,
                Type = type,
                IndexName = indexName
            };

            var json = System.Text.Json.JsonSerializer.Serialize(request, SerializerOptions.Default);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync("api/collections", content);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error creating collection: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> DeleteCollectionAsync(string containerName)
    {
        try
        {
            var response = await httpClient.DeleteAsync($"api/collections/{containerName}");
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error deleting collection: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> UpdateCollectionMetadataAsync(string containerName, string? description = null, string? type = null, string? indexName = null)
    {
        try
        {
            var request = new CreateCollectionRequest
            {
                Name = containerName,
                Description = description,
                Type = type,
                IndexName = indexName
            };

            var json = System.Text.Json.JsonSerializer.Serialize(request, SerializerOptions.Default);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await httpClient.PutAsync($"api/collections/{containerName}/metadata", content);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error updating collection metadata: {ex.Message}");
            return false;
        }
    }

    public async Task<CollectionInfo?> GetCollectionMetadataAsync(string containerName)
    {
        try
        {
            var response = await httpClient.GetAsync($"api/collections/{containerName}/metadata");
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<CollectionInfo>();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error fetching collection metadata: {ex.Message}");
            return null;
        }
    }

    public async Task<List<ContainerFileInfo>> GetCollectionFilesAsync(string containerName)
    {
        try
        {
            var response = await httpClient.GetAsync($"api/collections/{containerName}/files");
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<List<ContainerFileInfo>>() ?? new List<ContainerFileInfo>();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error fetching collection files: {ex.Message}");
            return new List<ContainerFileInfo>();
        }
    }

    public async Task<bool> DeleteFileFromCollectionAsync(string containerName, string fileName)
    {
        try
        {
            var encodedFileName = Uri.EscapeDataString(fileName);
            var response = await httpClient.DeleteAsync($"api/collections/{containerName}/files/{encodedFileName}");
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error deleting file from collection: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> ProcessDocumentLayoutAsync(string containerName, string fileName)
    {
        try
        {
            var response = await httpClient.PostAsync($"api/collections/{containerName}/process/{fileName}", null);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error processing document layout: {ex.Message}");
            return false;
        }
    }

    public async Task<string?> GetFileUrlAsync(string containerName, string fileName)
    {
        try
        {
            var encodedFileName = Uri.EscapeDataString(fileName);
            return $"api/collections/{containerName}/download/{encodedFileName}";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error generating file URL: {ex.Message}");
            return null;
        }
    }

    public async Task<UploadDocumentsResponse> UploadFilesToCollectionAsync(IReadOnlyList<IBrowserFile> files, long maxAllowedSize, string containerName, IDictionary<string, string>? metadata = null)
    {
        try
        {
            using var content = new MultipartFormDataContent();

            // Extract folder path from metadata if present
            string folderPrefix = "";
            if (metadata != null)
            {
                Console.WriteLine($"[CLIENT DEBUG] Metadata contains {metadata.Count} keys:");
                foreach (var kvp in metadata)
                {
                    Console.WriteLine($"[CLIENT DEBUG]   {kvp.Key} = '{kvp.Value}'");
                }

                if (metadata.TryGetValue("folderPath", out var folderPath))
                {
                    folderPrefix = folderPath.ToString().TrimEnd('/') + "/";
                    Console.WriteLine($"[CLIENT DEBUG] Extracted folderPrefix = '{folderPrefix}'");
                }
                else
                {
                    Console.WriteLine($"[CLIENT DEBUG] No 'folderPath' key found in metadata");
                }
            }
            else
            {
                Console.WriteLine($"[CLIENT DEBUG] Metadata is null");
            }

            // Create a mapping of original filename to full path for metadata
            var filePathMap = new Dictionary<string, string>();

            foreach (var file in files)
            {
#pragma warning disable CA2000 // Dispose objects before losing scope
                var fileContent = new StreamContent(file.OpenReadStream(maxAllowedSize));
#pragma warning restore CA2000 // Dispose objects before losing scope
                fileContent.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType);

                // Sanitize the file name to replace spaces and problematic characters
                var sanitizedFileName = FileNameSanitizer.Sanitize(file.Name);
                
                // Prepend folder path to filename if specified
                var fullPath = folderPrefix + sanitizedFileName;
                filePathMap[file.Name] = fullPath;

                Console.WriteLine($"[CLIENT DEBUG] Creating path map: '{file.Name}' -> '{fullPath}' (sanitized from '{file.Name}')");

                // Note: ASP.NET Core will strip path from filename for security, but we'll send full path in metadata
                content.Add(fileContent, "files", sanitizedFileName);
            }

            var tokenResponse = await httpClient.GetAsync("api/token/csrf");
            tokenResponse.EnsureSuccessStatusCode();
            var token = await tokenResponse.Content.ReadAsStringAsync();
            token = token.Trim('"');

            content.Headers.Add("X-CSRF-TOKEN-FORM", token);
            content.Headers.Add("X-CSRF-TOKEN-HEADER", token);

            if (metadata != null)
            {
                string serializedHeaders = System.Text.Json.JsonSerializer.Serialize(metadata);
                content.Headers.Add("X-FILE-METADATA", serializedHeaders);
            }

            // Send file path mapping so server knows the intended full paths
            if (filePathMap.Count > 0)
            {
                string serializedPathMap = System.Text.Json.JsonSerializer.Serialize(filePathMap);
                content.Headers.Add("X-FILE-PATH-MAP", serializedPathMap);
            }

            var response = await httpClient.PostAsync($"api/collections/{containerName}/upload", content);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<UploadDocumentsResponse>();
            return result ?? UploadDocumentsResponse.FromError("Unable to upload files, unknown error.");
        }
        catch (Exception ex)
        {
            return UploadDocumentsResponse.FromError(ex.ToString());
        }
    }

    // Project Management APIs
    public async Task<List<CollectionInfo>> GetProjectsAsync()
    {
        try
        {
            var response = await httpClient.GetAsync("api/projects");
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<List<CollectionInfo>>() ?? new List<CollectionInfo>();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error fetching projects: {ex.Message}");
            return new List<CollectionInfo>();
        }
    }

    public async Task<bool> CreateProjectAsync(string projectName, string? description = null, string? type = null)
    {
        try
        {
            var request = new CreateCollectionRequest
            {
                Name = projectName,
                Description = description,
                Type = type
            };

            var json = System.Text.Json.JsonSerializer.Serialize(request, SerializerOptions.Default);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync("api/projects", content);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error creating project: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> DeleteProjectAsync(string projectName)
    {
        try
        {
            var response = await httpClient.DeleteAsync($"api/projects/{projectName}");
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error deleting project: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> UpdateProjectMetadataAsync(string projectName, string? description = null, string? type = null)
    {
        try
        {
            var request = new CreateCollectionRequest
            {
                Name = projectName,
                Description = description,
                Type = type
            };

            var json = System.Text.Json.JsonSerializer.Serialize(request, SerializerOptions.Default);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await httpClient.PutAsync($"api/projects/{projectName}/metadata", content);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error updating project metadata: {ex.Message}");
            return false;
        }
    }

    public async Task<CollectionInfo?> GetProjectMetadataAsync(string projectName)
    {
        try
        {
            var response = await httpClient.GetAsync($"api/projects/{projectName}/metadata");
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<CollectionInfo>();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error fetching project metadata: {ex.Message}");
            return null;
        }
    }

    public async Task<List<ContainerFileInfo>> GetProjectFilesAsync(string projectName)
    {
        try
        {
            var response = await httpClient.GetAsync($"api/projects/{projectName}/files");
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<List<ContainerFileInfo>>() ?? new List<ContainerFileInfo>();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error fetching project files: {ex.Message}");
            return new List<ContainerFileInfo>();
        }
    }

    public async Task<bool> DeleteFileFromProjectAsync(string projectName, string fileName)
    {
        try
        {
            var encodedFileName = Uri.EscapeDataString(fileName);
            var response = await httpClient.DeleteAsync($"api/projects/{projectName}/files/{encodedFileName}");
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error deleting file from project: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> ProcessProjectDocumentLayoutAsync(string projectName, string fileName)
    {
        try
        {
            var response = await httpClient.PostAsync($"api/projects/{projectName}/process/{fileName}", null);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error processing project document layout: {ex.Message}");
            return false;
        }
    }

    public async Task<string?> GetProjectFileUrlAsync(string projectName, string fileName, bool isProcessingFile = false)
    {
        try
        {
            var encodedFileName = Uri.EscapeDataString(fileName);
            var processingParam = isProcessingFile ? "?processing=true" : "";
            return $"api/projects/{projectName}/download/{encodedFileName}{processingParam}";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error generating project file URL: {ex.Message}");
            return null;
        }
    }

    public async Task<UploadDocumentsResponse> UploadFilesToProjectAsync(IReadOnlyList<IBrowserFile> files, long maxAllowedSize, string projectName, IDictionary<string, string>? metadata = null)
    {
        try
        {
            using var content = new MultipartFormDataContent();

            // Extract folder path from metadata if present
            string folderPrefix = "";
            if (metadata != null)
            {
                Console.WriteLine($"[CLIENT DEBUG PROJECT] Metadata contains {metadata.Count} keys:");
                foreach (var kvp in metadata)
                {
                    Console.WriteLine($"[CLIENT DEBUG PROJECT]   {kvp.Key} = '{kvp.Value}'");
                }

                if (metadata.TryGetValue("folderPath", out var folderPath))
                {
                    folderPrefix = folderPath.ToString().TrimEnd('/') + "/";
                    Console.WriteLine($"[CLIENT DEBUG PROJECT] Extracted folderPrefix = '{folderPrefix}'");
                }
                else
                {
                    Console.WriteLine($"[CLIENT DEBUG PROJECT] No 'folderPath' key found in metadata");
                }
            }
            else
            {
                Console.WriteLine($"[CLIENT DEBUG PROJECT] Metadata is null");
            }

            // Create a mapping of original filename to full path for metadata
            var filePathMap = new Dictionary<string, string>();

            foreach (var file in files)
            {
#pragma warning disable CA2000 // Dispose objects before losing scope
                var fileContent = new StreamContent(file.OpenReadStream(maxAllowedSize));
#pragma warning restore CA2000 // Dispose objects before losing scope
                fileContent.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType);

                // Sanitize the file name to replace spaces and problematic characters
                var sanitizedFileName = FileNameSanitizer.Sanitize(file.Name);
                
                // Prepend folder path to filename if specified
                var fullPath = folderPrefix + sanitizedFileName;
                filePathMap[file.Name] = fullPath;

                Console.WriteLine($"[CLIENT DEBUG] Creating path map for project: '{file.Name}' -> '{fullPath}' (sanitized)");

                content.Add(fileContent, sanitizedFileName, sanitizedFileName);
            }

            var tokenResponse = await httpClient.GetAsync("api/token/csrf");
            tokenResponse.EnsureSuccessStatusCode();
            var token = await tokenResponse.Content.ReadAsStringAsync();
            token = token.Trim('"');

            content.Headers.Add("X-CSRF-TOKEN-FORM", token);
            content.Headers.Add("X-CSRF-TOKEN-HEADER", token);

            // Add project name to metadata
            var fileMetadata = metadata ?? new Dictionary<string, string>();
            fileMetadata["project"] = projectName;

            string serializedHeaders = System.Text.Json.JsonSerializer.Serialize(fileMetadata);
            content.Headers.Add("X-FILE-METADATA", serializedHeaders);

            // Send file path mapping so server knows the intended full paths
            if (filePathMap.Count > 0)
            {
                string serializedPathMap = System.Text.Json.JsonSerializer.Serialize(filePathMap);
                content.Headers.Add("X-FILE-PATH-MAP", serializedPathMap);
                Console.WriteLine($"[CLIENT DEBUG] Sending file path map header for project with {filePathMap.Count} entries");
            }

            var response = await httpClient.PostAsync($"api/projects/{projectName}/upload", content);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<UploadDocumentsResponse>();
            return result ?? UploadDocumentsResponse.FromError("Unable to upload files, unknown error.");
        }
        catch (Exception ex)
        {
            return UploadDocumentsResponse.FromError(ex.ToString());
        }
    }

    public async Task<bool> AnalyzeProjectAsync(string projectName)
    {
        try
        {
            var response = await httpClient.PostAsync($"api/projects/{projectName}/analyze", null);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error analyzing project: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> AnalyzeProjectSpecV2Async(string projectName)
    {
        try
        {
            var response = await httpClient.PostAsync($"api/projects/{projectName}/analyze-spec-v2", null);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error analyzing project spec v2: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> AnalyzePlanProjectAsync(string projectName)
    {
        try
        {
            var response = await httpClient.PostAsync($"api/projects/{projectName}/analyze-plan", null);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error analyzing project plan: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> DeleteProjectWorkflowAsync(string projectName)
    {
        try
        {
            var response = await httpClient.DeleteAsync($"api/projects/{projectName}/workflow");
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error deleting project workflow: {ex.Message}");
            return false;
        }
    }

    public async Task<System.Text.Json.JsonElement?> GetProjectWorkflowStatusAsync(string projectName)
    {
        try
        {
            var response = await httpClient.GetAsync($"api/projects/{projectName}/workflow/status");
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                return System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(content);
            }
            return null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error fetching project workflow status: {ex.Message}");
            return null;
        }
    }

    public async Task<bool> UpdateFileDescriptionAsync(string projectName, string fileName, string? description)
    {
        try
        {
            var encodedFileName = Uri.EscapeDataString(fileName);
            var request = new { Description = description };
            var json = System.Text.Json.JsonSerializer.Serialize(request, SerializerOptions.Default);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await httpClient.PutAsync($"api/projects/{projectName}/files/description?fileName={encodedFileName}", content);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error updating file description: {ex.Message}");
            return false;
        }
    }

    // Search Index APIs
    public async Task<List<SearchIndexInfo>> GetSearchIndexesAsync()
    {
        try
        {
            var response = await httpClient.GetAsync("api/collections/indexes");
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<List<SearchIndexInfo>>() ?? new List<SearchIndexInfo>();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error fetching search indexes: {ex.Message}");
            return new List<SearchIndexInfo>();
        }
    }

    public async Task<SearchIndexInfo?> GetSearchIndexDetailsAsync(string indexName)
    {
        try
        {
            var response = await httpClient.GetAsync($"api/collections/indexes/{Uri.EscapeDataString(indexName)}");
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<SearchIndexInfo>();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error fetching search index details: {ex.Message}");
            return null;
        }
    }

    public async Task<UserInformation> GetUserAsync()
    {
        if (Cache.UserInformation != null)
            return Cache.UserInformation;

        try
        {
            var response = await httpClient.GetAsync("api/user");
            response.EnsureSuccessStatusCode();

            // sometimes the api/user call is returning an HTML error page... this crashes hard and shows you nothing...
            //var user = await response.Content.ReadFromJsonAsync<UserInformation>();

            var userInfo = await response.Content.ReadAsStringAsync();
            // if the input is invalid (i.e. error occurred...)
            if (userInfo[0..30].Contains("<html", StringComparison.CurrentCultureIgnoreCase))
            {
                return new UserInformation(false, "errorUser", "errorUser", "Call to api/user failed!", [], []);
            }

            // otherwise, if it's good - convert this into a user profile
            var user = System.Text.Json.JsonSerializer.Deserialize<UserInformation>(userInfo, SerializerOptions.Default);
            Cache.SetUserInformation(user);
            return user;
        }
        catch (Exception ex)
        {
            var errorMessage = $"Error fetching user information: {ex.Message}";
            Debug.WriteLine(errorMessage);
            return new UserInformation(false, "ERROR!", "ERROR!", errorMessage, [], []);
        }
    }
    public async Task<List<DocumentSummary>> GetUserDocumentsAsync()
    {
        var response = await httpClient.GetAsync("api/user/documents");
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<List<DocumentSummary>>();
    }
    public async Task<List<DocumentSummary>> GetCollectionDocumentsAsync(string profileId)
    {
        var response = await httpClient.GetAsync($"api/collection/documents/{profileId}");
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<List<DocumentSummary>>();
    }
    public async Task<UserSelectionModel> GetProfileUserSelectionModelAsync(string profileId)
    {
        var response = await httpClient.GetAsync($"api/profile/selections?profileId={profileId}");
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<UserSelectionModel>();
    }
    public async Task<UploadDocumentsResponse> UploadDocumentsAsync(IReadOnlyList<IBrowserFile> files, long maxAllowedSize, string selectedProfile, IDictionary<string, string>? metadata = null)
    {
        try
        {
            using var content = new MultipartFormDataContent();

            foreach (var file in files)
            {
#pragma warning disable CA2000 // Dispose objects before losing scope
                var fileContent = new StreamContent(file.OpenReadStream(maxAllowedSize));
#pragma warning restore CA2000 // Dispose objects before losing scope
                fileContent.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType);

                // Sanitize the file name to replace spaces and problematic characters
                var sanitizedFileName = FileNameSanitizer.Sanitize(file.Name);
                
                content.Add(fileContent, sanitizedFileName, sanitizedFileName);
            }

            var tokenResponse = await httpClient.GetAsync("api/token/csrf");
            tokenResponse.EnsureSuccessStatusCode();
            var token = await tokenResponse.Content.ReadAsStringAsync();
            token = token.Trim('"');

            // set token
            content.Headers.Add("X-CSRF-TOKEN-FORM", token);
            content.Headers.Add("X-CSRF-TOKEN-HEADER", token);

            if (metadata != null)
            {
                // Serialize the dictionary to a JSON string
                string serializedHeaders = System.Text.Json.JsonSerializer.Serialize(metadata);

                // Add the serialized dictionary as a single header value
                content.Headers.Add("X-FILE-METADATA", serializedHeaders);
            }

            if (selectedProfile != null)
            {
                content.Headers.Add("X-PROFILE-METADATA", selectedProfile);
            }

            var response = await httpClient.PostAsync("api/documents", content);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<UploadDocumentsResponse>();
            return result ?? UploadDocumentsResponse.FromError("Unable to upload files, unknown error.");
        }
        catch (Exception ex)
        {
            return UploadDocumentsResponse.FromError(ex.ToString());
        }
    }
    public async Task<DocumentIndexResponse> NativeIndexDocumentsAsync(UploadDocumentsResponse documentList) // DocumentIndexRequest indexRequest)
    {
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(documentList, SerializerOptions.Default);
            using var body = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync("api/native/index/documents", body);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<DocumentIndexResponse>();
            return result ?? DocumentIndexResponse.FromError("Unable to index files, unknown error.");
        }
        catch (Exception ex)
        {
            return DocumentIndexResponse.FromError(ex.ToString());
        }
    }
    public async IAsyncEnumerable<DocumentSummary> GetDocumentsAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var response = await httpClient.GetAsync("api/user/documents", cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            var options = SerializerOptions.Default;

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);

            await foreach (var document in System.Text.Json.JsonSerializer.DeserializeAsyncEnumerable<DocumentSummary>(stream, options, cancellationToken))
            {
                if (document is null)
                {
                    continue;
                }

                yield return document;
            }
        }
    }
    public async IAsyncEnumerable<ChatHistoryResponse> GetFeedbackAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var response = await httpClient.GetAsync("api/feedback", cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            var options = SerializerOptions.Default;

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);

            await foreach (var document in System.Text.Json.JsonSerializer.DeserializeAsyncEnumerable<ChatHistoryResponse>(stream, options, cancellationToken))
            {
                if (document is null)
                {
                    continue;
                }

                yield return document;
            }
        }
    }
    public async IAsyncEnumerable<ChatHistoryResponse> GetHistoryAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var response = await httpClient.GetAsync("api/chat/history", cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            var options = SerializerOptions.Default;
            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await foreach (var document in System.Text.Json.JsonSerializer.DeserializeAsyncEnumerable<ChatHistoryResponse>(stream, options, cancellationToken))
            {
                if (document is null)
                {
                    continue;
                }

                yield return document;
            }
        }
    }
    public async IAsyncEnumerable<ChatSessionModel> GetHistoryV2Async([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var response = await httpClient.GetAsync("api/chat/history-v2", cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            var options = SerializerOptions.Default;
            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await foreach (var session in System.Text.Json.JsonSerializer.DeserializeAsyncEnumerable<ChatSessionModel>(stream, options, cancellationToken))
            {
                if (session is null)
                {
                    continue;
                }

                yield return session;
            }
        }
    }
    public async IAsyncEnumerable<ChatHistoryResponse> GetChatHistorySessionAsync([EnumeratorCancellation] CancellationToken cancellationToken, string chatId)
    {
        var response = await httpClient.GetAsync($"api/chat/history/{chatId}", cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            var options = SerializerOptions.Default;
            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await foreach (var document in System.Text.Json.JsonSerializer.DeserializeAsyncEnumerable<ChatHistoryResponse>(stream, options, cancellationToken))
            {
                if (document is null)
                {
                    continue;
                }

                yield return document;
            }
        }
    }
    public async Task ChatRatingAsync(ChatRatingRequest request)
    {
        await PostBasicAsync(request, "api/chat/rating");
    }

    public async Task<VoiceLiveTokenResponse?> GetVoiceLiveTokenAsync()
    {
        try
        {
            var response = await httpClient.GetAsync("api/voicelive/token");
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<VoiceLiveTokenResponse>();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error fetching Voice Live token: {ex.Message}");
            return null;
        }
    }

    private async Task PostBasicAsync<TRequest>(TRequest request, string apiRoute)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(request, SerializerOptions.Default);
        using var body = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await httpClient.PostAsync(apiRoute, body);
    }
    public async Task<(ProfileInfo, string)> GetProfilesInfoAsync()
    {
        return await LoadProfilesAsync("api/profiles/info");
    }
    public async Task<(ProfileInfo, string)> GetProfilesReloadAsync()
    {
        return await LoadProfilesAsync("api/profiles/reload");
    }
    private async Task<(ProfileInfo, string)> LoadProfilesAsync(string apiToCall)
    {
       var profileInfo = new ProfileInfo();
       var rawJson = string.Empty;
       var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, IncludeFields = true, WriteIndented = true };
        try
        {
            profileInfo = await httpClient.GetFromJsonAsync<ProfileInfo>(apiToCall, options: jsonOptions);
            rawJson = System.Text.Json.JsonSerializer.Serialize(profileInfo, jsonOptions);
        }
        catch (Exception ex)
        {
            var errorMsg = $"Error reading profile info! {ex.Message}";
            Debug.WriteLine(errorMsg);
            profileInfo = new ProfileInfo();
            rawJson = errorMsg;
        }
        return (profileInfo!, rawJson);
    }
    public async IAsyncEnumerable<ChatChunkResponse> StreamChatAsync(ChatRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "api/chat/streaming")
        {
            Headers = { { "Accept", "application/json" } },
            Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(request), Encoding.UTF8, "application/json")
        };
        httpRequest.SetBrowserResponseStreamingEnabled(true);
        using var response = await httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await foreach (var chunk in System.Text.Json.JsonSerializer.DeserializeAsyncEnumerable<ChatChunkResponse>(responseStream, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true, DefaultBufferSize = 32 }, cancellationToken))
        {
            if (chunk == null)
                continue;
            yield return chunk;
        }
    }

    // Folder Management APIs
    public async Task<FolderNode> GetFolderStructureAsync(string containerName)
    {
        try
        {
            var response = await httpClient.GetAsync($"api/collections/{containerName}/folders");
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<FolderNode>() ?? new FolderNode();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error fetching folder structure: {ex.Message}");
            return new FolderNode();
        }
    }

    public async Task<bool> CreateFolderAsync(string containerName, string folderPath)
    {
        try
        {
            var request = new CreateFolderRequest
            {
                CollectionName = containerName,
                FolderPath = folderPath
            };

            var json = System.Text.Json.JsonSerializer.Serialize(request, SerializerOptions.Default);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync($"api/collections/{containerName}/folders", content);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error creating folder: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> RenameFolderAsync(string containerName, string oldFolderPath, string newFolderPath)
    {
        try
        {
            var request = new RenameFolderRequest
            {
                CollectionName = containerName,
                OldFolderPath = oldFolderPath,
                NewFolderPath = newFolderPath
            };

            var json = System.Text.Json.JsonSerializer.Serialize(request, SerializerOptions.Default);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await httpClient.PutAsync($"api/collections/{containerName}/folders/rename", content);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error renaming folder: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> DeleteFolderAsync(string containerName, string folderPath)
    {
        try
        {
            var encodedPath = Uri.EscapeDataString(folderPath);
            var response = await httpClient.DeleteAsync($"api/collections/{containerName}/folders?folderPath={encodedPath}");
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error deleting folder: {ex.Message}");
            return false;
        }
    }

    // File Metadata Management APIs
    public async Task<bool> UpdateFileMetadataAsync(string containerName, string fileName, FileMetadata metadata)
    {
        try
        {
            var encodedFileName = Uri.EscapeDataString(fileName);
            var json = System.Text.Json.JsonSerializer.Serialize(metadata, SerializerOptions.Default);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await httpClient.PutAsync($"api/collections/{containerName}/files/metadata/{encodedFileName}", content);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error updating file metadata: {ex.Message}");
            return false;
        }
    }

    public async Task<FileMetadata?> GetFileMetadataAsync(string containerName, string fileName)
    {
        try
        {
            var encodedFileName = Uri.EscapeDataString(fileName);
            var response = await httpClient.GetAsync($"api/collections/{containerName}/files/metadata/{encodedFileName}");
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<FileMetadata>();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error fetching file metadata: {ex.Message}");
            return null;
        }
    }

    public async Task<MetadataConfiguration?> GetMetadataConfigurationAsync()
    {
        try
        {
            var response = await httpClient.GetAsync("api/collections/metadata-configuration");
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<MetadataConfiguration>();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error fetching metadata configuration: {ex.Message}");
            return null;
        }
    }

    // Collection Indexing Workflow APIs
    public async Task<System.Text.Json.JsonElement?> GetCollectionIndexingWorkflowStatusAsync(string containerName)
    {
        try
        {
            var response = await httpClient.GetAsync($"api/collections/{containerName}/indexing/status");
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                return System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(content);
            }
            return null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error fetching collection indexing workflow status: {ex.Message}");
            return null;
        }
    }

    public async Task<bool> IndexCollectionAsync(string containerName)
    {
        try
        {
            var response = await httpClient.PostAsync($"api/collections/{containerName}/index", null);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error indexing collection: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> DeleteCollectionIndexingWorkflowAsync(string containerName)
    {
        try
        {
            var response = await httpClient.DeleteAsync($"api/collections/{containerName}/indexing");
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error deleting collection indexing workflow: {ex.Message}");
            return false;
        }
    }

    // Agent-Hub Push Indexing APIs
    /// <summary>
    /// Triggers push-based RAG indexing for a collection via Agent-Hub-JCI.
    /// This approach uses APIM and works with private endpoints.
    /// </summary>
    /// <param name="containerName">Name of the container to index</param>
    /// <param name="recreateIndex">Whether to recreate the index from scratch</param>
    /// <returns>Response with correlation ID for tracking, or null on error</returns>
    public async Task<PushIndexingResponse?> TriggerPushIndexingAsync(string containerName, bool recreateIndex = false)
    {
        try
        {
            var request = new PushIndexingRequest
            {
                ContainerName = containerName,
                RecreateIndex = recreateIndex
            };

            var json = System.Text.Json.JsonSerializer.Serialize(request, SerializerOptions.Default);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync("api/search/process", content);

            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<PushIndexingResponse>();
            }

            Debug.WriteLine($"Push indexing trigger failed: {response.StatusCode}");
            return null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error triggering push indexing: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Gets the status of a push indexing job.
    /// </summary>
    /// <param name="correlationId">Correlation ID from the initial trigger response</param>
    /// <returns>Current status of the indexing job, or null on error</returns>
    public async Task<PushIndexingStatusResponse?> GetPushIndexingStatusAsync(string correlationId)
    {
        try
        {
            var response = await httpClient.GetAsync($"api/search/status/{correlationId}");

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                Debug.WriteLine($"Push indexing status response: {content}");
                
                var result = System.Text.Json.JsonSerializer.Deserialize<PushIndexingStatusResponse>(
                    content,
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                
                Debug.WriteLine($"Deserialized status: {result?.Status}, Progress: {result?.ProgressCount}/{result?.TotalCount}");
                return result;
            }

            Debug.WriteLine($"Get push indexing status failed: {response.StatusCode}");
            return null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error getting push indexing status: {ex.Message}");
            return null;
        }
    }
}
