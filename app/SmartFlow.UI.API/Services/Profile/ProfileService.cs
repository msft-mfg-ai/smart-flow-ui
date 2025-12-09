// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Extensions.Options;

namespace MinimalApi.Services.Profile;

public class ProfileService
{
    private ProfileInfo? _profileInfo;
    private readonly BlobServiceClient _blobClient;
    private readonly AppConfiguration _appConfiguration;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ProfileService> _logger;
    private readonly SemaphoreSlim _semaphore = new(1, 1);


    public ProfileService(IOptions<AppConfiguration> appConfiguration, IConfiguration configuration, BlobServiceClient blobServiceClient, ILogger<ProfileService> logger)
    {
        _appConfiguration = appConfiguration.Value;
        _configuration = configuration;
        _blobClient = blobServiceClient;
        _logger = logger;
    }


    public async Task<ProfileInfo> GetProfileDataAsync()
    {
        if (_profileInfo?.Profiles.Count > 0)
        {
            return _profileInfo!;
        }

        await _semaphore.WaitAsync();
        try
        {
            if (_profileInfo?.Profiles.Count > 0)
            {
                return _profileInfo!;
            }
            // need to load
            return await ReloadAsync();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<ProfileInfo> ReloadAsync()
    {
        var (data, loadingMsg, source) = await LoadAsync();
        _profileInfo = new ProfileInfo(data,
        string.IsNullOrWhiteSpace(source) ? "No source location found!" : source,
        string.IsNullOrWhiteSpace(loadingMsg) ? "No loading message found!" : loadingMsg,
        _configuration);

        return _profileInfo;
    }

    private string LogLoadingMessage(string message)
    {
        _logger.LogInformation(message);
        return message;
    }

    private async Task<(List<ProfileDefinition> data, string loadingMsg, string source)> LoadAsync()
    {
        // Reset the profile data and loading message
        var loadingMessage = string.Empty;
        var profileSource = string.Empty;

        var jsonOptions = new System.Text.Json.JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        var profileConfigurationBlobStorageContainer = _appConfiguration.ProfileConfigurationBlobStorageContainer;
        if (!string.IsNullOrEmpty(profileConfigurationBlobStorageContainer))
        {
            try
            {
                profileSource = "Storage";
                loadingMessage += LogLoadingMessage($"Found storage container, looking for profiles.json in {profileConfigurationBlobStorageContainer}... ");
                var container = _blobClient.GetBlobContainerClient(profileConfigurationBlobStorageContainer);
                var blobClient = container.GetBlobClient("profiles.json");
                var downloadResult = await blobClient.DownloadContentAsync();

                var profileStorageData = System.Text.Json.JsonSerializer.Deserialize<List<ProfileDefinition>>(Encoding.UTF8.GetString(downloadResult.Value.Content), jsonOptions);
                if (profileStorageData != null)
                {
                    loadingMessage += LogLoadingMessage($"{profileStorageData.Count} profiles were loaded from storage file in {profileConfigurationBlobStorageContainer} on {DateTime.Now:MMMM d} at {DateTime.Now:HH:mm:ss}!");
                    return (profileStorageData, loadingMessage, profileSource);
                }
            }
            catch (Exception ex)
            {
                var errorMsg = $"Error loading profiles.json from storage container {profileConfigurationBlobStorageContainer}: {ex.Message}";
                loadingMessage += LogLoadingMessage(errorMsg);
                var fakeData = new List<ProfileDefinition> { new(errorMsg) };
                return (fakeData, loadingMessage, profileSource);
            }
        }

        var profileConfig = _appConfiguration.ProfileConfiguration;
        if (!string.IsNullOrEmpty(profileConfig))
        {
            try
            {
                profileSource = "Config";
                loadingMessage += LogLoadingMessage("Found Profile Configuration Key, decoding the value... ");
                var bytes = Convert.FromBase64String(profileConfig);
                var profileConfigData = System.Text.Json.JsonSerializer.Deserialize<List<ProfileDefinition>>(Encoding.UTF8.GetString(bytes), jsonOptions);
                if (profileConfigData != null)
                {
                    loadingMessage += LogLoadingMessage($"{profileConfigData.Count} profiles were loaded from configuration key value on {DateTime.Now:MMMM d} at {DateTime.Now:HH:mm:ss}!");
                    return (profileConfigData, loadingMessage, profileSource);
                }
            }
            catch (Exception ex)
            {
                var errorMsg = $"Error loading profile data from Config Key: {ex.Message}";
                loadingMessage += LogLoadingMessage(errorMsg);
                var fakeData = new List<ProfileDefinition> { new(errorMsg) };
                return (fakeData, loadingMessage, profileSource);
            }
        }

        try
        {
            profileSource = "Embedded";
            loadingMessage += LogLoadingMessage("Loading Profile from project embedded file... ");
            var fileName = _appConfiguration.ProfileFileName;
            var profileFileData = LoadEmbeddedProflies(fileName);
            loadingMessage += LogLoadingMessage($"{profileFileData.Count} profiles were loaded from embedded file on {DateTime.Now:MMMM d} at {DateTime.Now:HH:mm:ss}!");
            return (profileFileData, loadingMessage, profileSource);
        }
        catch (Exception ex)
        {
            var errorMsg = $"Error loading embedded profile data: {ex.Message}";
            loadingMessage += LogLoadingMessage(errorMsg);
            var fakeData = new List<ProfileDefinition> { new(errorMsg) };
            return (fakeData, loadingMessage, profileSource);
        }
    }

    private static List<ProfileDefinition> LoadEmbeddedProflies(string name)
    {
        var resourceName = $"SmartFlow.UI.API.Services.Profile.{name}.json";
        var assembly = Assembly.GetExecutingAssembly();
        using Stream stream = assembly.GetManifestResourceStream(resourceName) ?? throw new ArgumentException($"The resource {resourceName} was not found.");
        using StreamReader reader = new(stream);
        var jsonText = reader.ReadToEnd();
        var profiles = Newtonsoft.Json.JsonConvert.DeserializeObject<List<ProfileDefinition>>(jsonText);
        return profiles ?? [];
    }
}
