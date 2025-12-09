// Copyright (c) Microsoft. All rights reserved.
using MinimalApi.Agents;

namespace MinimalApi.Extensions;

internal static class WebApiExtensions
{
    internal static WebApplication MapApi(this WebApplication app)
    {
        var api = app.MapGroup("api");

        // UI Configuration endpoint
        api.MapGet("config/ui", OnGetUIConfigAsync);

        // Get recent feedback
        api.MapGet("feedback", OnGetFeedbackAsync);

        // Get source file
        api.MapGet("documents/{fileName}", OnGetSourceFileAsync);

        // Get enable logout
        api.MapGet("user", (Delegate)OnGetUserAsync);

        // User document
        api.MapPost("documents", OnPostDocumentAsync);
        api.MapGet("user/documents", OnGetUserDocumentsAsync);
        api.MapGet("collection/documents/{profileId}", OnGetCollectionDocumentsAsync);

        // Azure Search Native Index documents
        //api.MapPost("native/index/documents", OnPostNativeIndexDocumentsAsync);

        // Profile Selections
        api.MapGet("profile/selections", OnGetProfileUserSelectionOptionsAsync);

        api.MapGet("profiles/info", OnGetProfilesInfoAsync);
        api.MapGet("profiles/reload", OnGetProfilesReloadAsync);

        api.MapGet("token/csrf", OnGetAntiforgeryToken);

        api.MapGet("status", OnGetStatus);

        api.MapGet("tag", OnTagSyncAsync);

        api.MapGet("headers", OnGetHeadersAsync);
        return app;
    }

    private static IResult OnGetHeadersAsync(HttpContext context)
    {
        var headers = new Dictionary<string, string>();
        foreach (var header in context.Request.Headers)
        {
            if (headers.Keys.Contains(header.Key))
            {
                headers[header.Key] = header.Value.FirstOrDefault() ?? "";
            }
            else
            {
                headers.Add(header.Key, header.Value.FirstOrDefault() ?? "");
            }
        }
        return Results.Ok(headers);
    }

    private static IResult OnGetStatus()
    {
        return Results.Ok("OK");
    }

    private static IResult OnGetUIConfigAsync(IConfiguration configuration)
    {
        var uiConfig = new UIConfiguration
        {
            ColorPaletteLightPrimary = configuration.GetValue<string>("ColorPaletteLightPrimary", "#84B1CB"),
            ColorPaletteLightSecondary = configuration.GetValue<string>("ColorPaletteLightSecondary", "#287FA4"),
            ColorPaletteLightAppbarBackground = configuration.GetValue<string>("ColorPaletteLightAppbarBackground", "#84B1CB"),
            LogoImagePath = configuration.GetValue<string>("LogoImagePath", "icon-512.png"),
            LogoImageWidth = configuration.GetValue<int>("LogoImageWidth", 150),
            HelloText = configuration.GetValue<string>("HelloText", "How can I help you today?"),
            ShowSampleQuestions = configuration.GetValue<bool>("ShowSampleQuestions", true),
            ShowPremiumAOAIToggleSelection = configuration.GetValue<bool>("ShowPremiumAOAIToggleSelection", true),
            DisclaimerMessage = configuration.GetValue<string>("DisclaimerMessage", string.Empty),
            EnabledNavigationPaths = configuration.GetValue<string?>("EnabledNavigationPaths", null)
        };

        return Results.Ok(uiConfig);
    }

    private static IResult OnGetAntiforgeryToken(HttpContext context, IAntiforgery antiforgery)
    {
        var tokens = antiforgery.GetAndStoreTokens(context);
        return TypedResults.Ok(tokens?.RequestToken ?? string.Empty);
    }

    private static async Task<IResult> OnGetProfileUserSelectionOptionsAsync(HttpContext context, string profileId, SearchClientFactory searchClientFactory, ILogger<WebApplication> logger)
    {
        var profileService = context.RequestServices.GetRequiredService<ProfileService>();
        var profileInfo = await profileService.GetProfileDataAsync();
        var profileDefinition = profileInfo.Profiles.FirstOrDefault(x => x.Id == profileId);
        if (profileDefinition == null)
            return Results.BadRequest("Profile does not found.");

        if (profileDefinition.RAGSettings == null)
            return Results.BadRequest("Profile does not support user selection");

        var searchClient = searchClientFactory.GetOrCreateClient(profileDefinition.RAGSettings.DocumentRetrievalIndexName);
        var selectionOptions = new List<UserSelectionOption>();
        foreach (var selectionOption in profileDefinition.RAGSettings.ProfileUserSelectionOptions)
        {
            try
            {

                var searchOptions = new SearchOptions { Size = 0, Facets = { $"{selectionOption.IndexFieldName},count:{100}" }, };
                SearchResults<SearchDocument> results = await searchClient.SearchAsync<SearchDocument>("*", searchOptions);
                if (results.Facets != null && results.Facets.ContainsKey(selectionOption.IndexFieldName))
                {
                    var selectionValues = new List<string>();
                    foreach (FacetResult facet in results.Facets[selectionOption.IndexFieldName])
                        selectionValues.Add(facet.Value.ToString());

                    selectionOptions.Add(new UserSelectionOption(selectionOption.DisplayName, selectionValues.OrderBy(x => x)));
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Error getting user selection options for profile {profileId}");
                selectionOptions.Add(new UserSelectionOption(selectionOption.DisplayName, new string[] { }));
            }
        }

        // Set headers to prevent caching
        context.Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate, max-age=0";
        context.Response.Headers["Pragma"] = "no-cache";
        var result = new UserSelectionModel(selectionOptions);
        return Results.Ok(result);
    }

    private static async Task<IResult> OnGetProfilesInfoAsync(HttpContext context, ILogger<WebApplication> logger)
    {
        var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, IncludeFields = true };
        var profileService = context.RequestServices.GetRequiredService<ProfileService>();
        var profileInfo = await profileService.GetProfileDataAsync();
        return Results.Json(profileInfo, contentType: "application/json; charset=utf-8", statusCode: 200, options: jsonOptions);
    }

    private static async Task<IResult> OnGetProfilesReloadAsync(HttpContext context, ILogger<WebApplication> logger, IConfiguration configuration)
    {
        var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, IncludeFields = true };
        var profileService = context.RequestServices.GetRequiredService<ProfileService>();
        var profileInfo = await profileService.ReloadAsync();
        return Results.Json(profileInfo, contentType: "application/json; charset=utf-8", statusCode: 200, options: jsonOptions);
    }

    private static async Task<IResult> OnGetSourceFileAsync(HttpContext context, string fileName, BlobServiceClient blobServiceClient, IConfiguration configuration)
    {
        try
        {
            int underscoreIndex = fileName.IndexOf('|');  // Find the first underscore

            if (underscoreIndex != -1)
            {
                string profileName = fileName.Substring(0, underscoreIndex); // Get the substring before the underscore
                string blobName = fileName.Substring(underscoreIndex + 1); // Get the substring after the underscore

                Console.WriteLine($"Filename:{fileName} Container: {profileName} BlobName: {blobName}");


                // Get user information
                var profileService = context.RequestServices.GetRequiredService<ProfileService>();
                var profileInfo = await profileService.GetProfileDataAsync();
                var userInfo = await context.GetUserInfoAsync(profileInfo);
                var profile = profileInfo.Profiles.FirstOrDefault(x => x.Id == profileName);
                if (profile == null || !userInfo.HasAccess(profile))
                {
                    throw new UnauthorizedAccessException("User does not have access to this profile");
                }

                ArgumentNullException.ThrowIfNull(profile.RAGSettings, "Profile RAGSettings is null");
                var blobContainerClient = blobServiceClient.GetBlobContainerClient(profile.RAGSettings.StorageContianer);
                var blobClient = blobContainerClient.GetBlobClient(blobName);
                Console.WriteLine($"Blob Uri:{blobClient.Uri}");
                if (await blobClient.ExistsAsync())
                {
                    var stream = new MemoryStream();
                    await blobClient.DownloadToAsync(stream);
                    stream.Position = 0; // Reset stream position to the beginning

                    return Results.File(stream, "application/pdf");
                }
                else
                {
                    return Results.NotFound("File not found");
                }
            }
            else
            {
                throw new ArgumentOutOfRangeException("Invalid file name format");
            }
        }
        catch (Exception)
        {
            // Log the exception details
            return Results.Problem("Internal server error");
        }
    }

    private static async Task<IResult> OnPostDocumentAsync(HttpContext context, [FromForm] IFormFileCollection files,
        [FromServices] AzureBlobStorageService service,
        [FromServices] DocumentService documentService,
        [FromServices] ILogger<AzureBlobStorageService> logger,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Upload documents");
        var userInfo = await context.GetUserInfoAsync();
        var fileMetadataContent = context.Request.Headers["X-FILE-METADATA"];
        var selectedProfile = context.Request.Headers["X-PROFILE-METADATA"];
        Dictionary<string, string>? fileMetadata = null;
        if (!string.IsNullOrEmpty(fileMetadataContent))
            fileMetadata = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(fileMetadataContent);


        var response = await documentService.CreateDocumentUploadAsync(userInfo, files, selectedProfile, fileMetadata, cancellationToken);
        logger.LogInformation("Upload documents: {x}", response);

        return TypedResults.Ok(response);
    }


    private static async Task<IResult> OnGetUserAsync(HttpContext context)
    {
        var userInfo = await context.GetUserInfoAsync();
        context.Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate, max-age=0";
        context.Response.Headers["Pragma"] = "no-cache";
        return Results.Ok(userInfo);
    }

    private static async Task<IResult> OnGetUserDocumentsAsync(HttpContext context, DocumentService documentService)
    {
        var userInfo = await context.GetUserInfoAsync();
        var documents = await documentService.GetDocumentUploadsAsync(userInfo, null);
        return TypedResults.Ok(documents.Select(d => new DocumentSummary(d.Id, d.SourceName, d.ContentType, d.Size, d.Status, d.StatusMessage, d.ProcessingProgress, d.Timestamp, d.Metadata)));
    }

    private static async Task<IResult> OnGetCollectionDocumentsAsync(HttpContext context, DocumentService documentService, string profileId)
    {
        var userInfo = await context.GetUserInfoAsync();
        var documents = await documentService.GetDocumentUploadsAsync(userInfo, profileId);
        return TypedResults.Ok(documents.Select(d => new DocumentSummary(d.Id, d.SourceName, d.ContentType, d.Size, d.Status, d.StatusMessage, d.ProcessingProgress, d.Timestamp, d.Metadata)));
    }

    private static async Task<IEnumerable<ChatHistoryResponse>> OnGetFeedbackAsync(HttpContext context, IChatHistoryService chatHistoryService)
    {
        var profileService = context.RequestServices.GetRequiredService<ProfileService>();
        var profileInfo = await profileService.GetProfileDataAsync();
        var userInfo = await context.GetUserInfoAsync(profileInfo);
        var response = await chatHistoryService.GetMostRecentRatingsItemsAsync(userInfo);
        return response.AsFeedbackResponse(profileInfo);
    }

    private static async Task<IResult> OnTagSyncAsync([FromServices] BlobServiceClient blobServiceClient)
    {
        BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient("content-kill-memo");

        // Loop through each blob in the container
        await foreach (BlobItem blobItem in containerClient.GetBlobsAsync())
        {
            // Get the BlobClient
            BlobClient blobClient = containerClient.GetBlobClient(blobItem.Name);

            // Fetch the metadata
            var properties = await blobClient.GetPropertiesAsync();
            var tags = await blobClient.GetTagsAsync();

            Console.WriteLine($"Blob Name: {blobItem.Name}");
            Console.WriteLine("Metadata Tags:");

            // Loop through and print metadata
            foreach (var m in properties.Value.Metadata)
            {
                Console.WriteLine($"  {m.Key}: {m.Value}");
            }


            var existingMetadata = properties.Value.Metadata;
            // Loop through and print blob index tags
            foreach (var tag in tags.Value.Tags)
            {
                if (tag.Key == "Type")
                {
                    Console.WriteLine($"  {tag.Key}: {tag.Value}");
                    existingMetadata.Add("Type", tag.Value);
                    await blobClient.SetMetadataAsync(existingMetadata);
                }

            }

            Console.WriteLine(); // Blank line for readability
        }
        return Results.Ok("OK");
    }
}
