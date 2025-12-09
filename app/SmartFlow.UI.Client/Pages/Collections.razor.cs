// Copyright (c) Microsoft. All rights reserved.

using Shared.Models;

namespace SmartFlow.UI.Client.Pages;

public sealed partial class Collections : IDisposable
{
    private const long MaxIndividualFileSize = 1_024 * 1_024 * 250; // 250MB

    private MudForm _form = null!;
    private MudForm _createCollectionForm = null!;
    private bool _createCollectionFormValid = false;

    private bool _isLoadingDocuments = false;
    private bool _isUploadingDocuments = false;
    private bool _isIndexingDocuments = false;
    private bool _isLoadingCollections = false;
    private bool _showUploadSection = false; // Hidden by default - user clicks "Upload Document" to show
    private string _filter = "";
    private HashSet<string> _processingFiles = new(); // Track files being processed
    private HashSet<string> _deletingFiles = new(); // Track files being deleted
    private bool _isIndexing = false; // Track if vector indexing is in progress
    private System.Text.Json.JsonElement? _indexingWorkflowStatus = null; // Store indexing workflow status
    private System.Threading.Timer? _indexingStatusPollTimer = null; // Timer for polling indexing status
    private bool _isPollingIndexing = false; // Track if polling is active to prevent concurrent polls
    private const int IndexingStatusPollIntervalMs = 10000; // Poll every 10 seconds

    // Push indexing state (for Agent-Hub-JCI integration)
    private bool _isPushIndexing = false;
    private string? _pushIndexingCorrelationId = null;
    private PushIndexingStatusResponse? _pushIndexingStatus = null;
    private System.Threading.Timer? _pushIndexingStatusPollTimer = null;
    private bool _isPollingPushIndexing = false;

    // Store a cancelation token that will be used to cancel if the user disposes of this component.
    private readonly CancellationTokenSource _cancellationTokenSource = new();

    [Inject] public required ApiClient Client { get; set; }
    [Inject] public required ISnackbar Snackbar { get; set; }
    [Inject] public required ILogger<Collections> Logger { get; set; }
    [Inject] public required IJSRuntime JSRuntime { get; set; }
    [Inject] public required HttpClient HttpClient { get; set; }
    [Inject] public required IDialogService DialogService { get; set; }

    // Collection management
    private List<CollectionInfo> _collections = new();
    private List<SearchIndexInfo> _availableIndexes = new();
    private string _selectedCollection = "";
    private CollectionInfo? _selectedCollectionInfo = null;
    private List<ContainerFileInfo> _collectionFiles = new();
    private bool _showCreateCollectionForm = false;
    private string _newCollectionName = "";
    private string _newCollectionDescription = "";
    private string _newCollectionType = "";
    private string _newCollectionIndexName = "";

    // Edit collection metadata
    private bool _showEditMetadataForm = false;
    private string _editCollectionDescription = "";
    private string _editCollectionType = "";
    private string _editCollectionIndexName = "";

    protected override async Task OnInitializedAsync()
    {
        // Load collections and available indexes
        await Task.WhenAll(
            LoadCollectionsAsync(),
            LoadAvailableIndexesAsync()
        );
    }

    private async Task LoadAvailableIndexesAsync()
    {
        try
        {
            _availableIndexes = await Client.GetSearchIndexesAsync();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error loading available indexes");
            // Don't show error to user - index field will just be empty
        }
    }

    private async Task LoadCollectionsAsync()
    {
        _isLoadingCollections = true;
        try
        {
            _collections = await Client.GetCollectionsAsync();
            // Auto-select first collection if available
            if (_collections.Any())
            {
                await SelectCollectionAsync(_collections.First().Name);
            }
            else
            {
                _selectedCollection = "";
                _selectedCollectionInfo = null;
                _collectionFiles.Clear();
                _fileUploads.Clear();
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error loading collections");
            SnackBarError("Failed to load collections");
        }
        finally
        {
            _isLoadingCollections = false;
            StateHasChanged();
        }
    }

    private async Task SelectCollectionAsync(string collectionName)
    {
        if (_selectedCollection != collectionName)
        {
            // Stop any existing indexing status polling when switching collections
            StopIndexingStatusPolling();
            _isIndexing = false;
            _indexingWorkflowStatus = null;

            _selectedCollection = collectionName;
            _selectedCollectionInfo = _collections.FirstOrDefault(c => c.Name == collectionName);
            _fileUploads.Clear(); // Clear any selected files when switching collections
            _filter = ""; // Clear filter when switching collections
            _showCreateCollectionForm = false; // Hide create form when selecting a collection
            _showUploadSection = false; // Hide upload section when switching collections
            _showEditMetadataForm = false; // Hide edit metadata form when switching collections
            _currentFolderPath = ""; // Reset to show root level files
            _selectedFolder = null; // Clear selected folder
            await Task.WhenAll(
                LoadCollectionFilesAsync(),
                LoadFolderStructureAsync()
            );

            // Check for existing indexing workflow status
            await CheckIndexingWorkflowStatusAsync();
        }
    }

    private async Task LoadCollectionFilesAsync()
    {
        if (string.IsNullOrEmpty(_selectedCollection))
            return;

        _isLoadingDocuments = true;
        try
        {
            _collectionFiles = await Client.GetCollectionFilesAsync(_selectedCollection);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error loading collection files for {Collection}", _selectedCollection);
            SnackBarError($"Failed to load files from collection '{_selectedCollection}'");
        }
        finally
        {
            _isLoadingDocuments = false;
            StateHasChanged();
        }
    }

    private IEnumerable<ContainerFileInfo> GetFilteredFiles()
    {
        if (_collectionFiles == null || !_collectionFiles.Any())
        {
            Logger.LogInformation($"GetFilteredFiles: No files loaded. _collectionFiles is null or empty.");
            return Enumerable.Empty<ContainerFileInfo>();
        }

        Logger.LogInformation($"GetFilteredFiles: Total files: {_collectionFiles.Count}, _currentFolderPath: '{_currentFolderPath}', _selectedCollection: '{_selectedCollection}'");
        
        // Log first few files to see their folder paths
        var sampleFiles = _collectionFiles.Take(5).ToList();
        foreach (var file in sampleFiles)
        {
            Logger.LogInformation($"  Sample File: {file.FileName}, FolderPath: '{file.FolderPath ?? "NULL"}'");
        }

        IEnumerable<ContainerFileInfo> filteredFiles;

        // If no folder is selected, show ALL files in the collection
        if (string.IsNullOrEmpty(_currentFolderPath))
        {
            Logger.LogInformation($"No folder selected - showing all files in collection");
            filteredFiles = _collectionFiles;
        }
        else
        {
            Logger.LogInformation($"Showing files in folder: '{_currentFolderPath}'");
            // Show only files in the selected folder (not in subfolders)
            filteredFiles = _collectionFiles.Where(f => f.FolderPath == _currentFolderPath);
        }

        Logger.LogInformation($"After folder filter: {filteredFiles.Count()} files");

        // Apply text filter if present
        if (!string.IsNullOrWhiteSpace(_filter))
        {
            filteredFiles = filteredFiles.Where(f => f.FileName.Contains(_filter, StringComparison.OrdinalIgnoreCase));
            Logger.LogInformation($"After text filter '{_filter}': {filteredFiles.Count()} files");
        }

        return filteredFiles;
    }

    private void ShowCreateCollectionForm()
    {
        _newCollectionName = "";
        _newCollectionDescription = "";
        _newCollectionType = "";
        _newCollectionIndexName = "";
        _createCollectionFormValid = false;
        _showCreateCollectionForm = true;
        _showEditMetadataForm = false;
    }

    private void CancelCreateCollection()
    {
        _showCreateCollectionForm = false;
        _newCollectionName = "";
        _newCollectionDescription = "";
        _newCollectionType = "";
        _newCollectionIndexName = "";
        _createCollectionFormValid = false;
    }

    private string ValidateCollectionName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Collection name is required";

        // Azure Storage container naming rules
        if (name.Length < 3 || name.Length > 63)
            return "Collection name must be between 3 and 63 characters";

        if (!System.Text.RegularExpressions.Regex.IsMatch(name, "^[a-z0-9]([a-z0-9-]*[a-z0-9])?$"))
            return "Collection name must contain only lowercase letters, numbers, and hyphens, and must start and end with a letter or number";

        if (name.Contains("--"))
            return "Collection name cannot contain consecutive hyphens";

        if (_collections.Any(c => c.Name == name))
            return "A collection with this name already exists";

        return null!;
    }

    private async Task CreateCollectionAsync()
    {
        if (string.IsNullOrWhiteSpace(_newCollectionName) || !_createCollectionFormValid)
        {
            SnackBarError("Please enter a valid collection name");
            return;
        }

        try
        {
            var success = await Client.CreateCollectionAsync(
                _newCollectionName,
                string.IsNullOrWhiteSpace(_newCollectionDescription) ? null : _newCollectionDescription,
                string.IsNullOrWhiteSpace(_newCollectionType) ? null : _newCollectionType,
                string.IsNullOrWhiteSpace(_newCollectionIndexName) ? null : _newCollectionIndexName);

            if (success)
            {
                SnackBarMessage($"Collection '{_newCollectionName}' created successfully");
                _showCreateCollectionForm = false;
                var createdCollectionName = _newCollectionName;
                _newCollectionName = "";
                _newCollectionDescription = "";
                _newCollectionType = "";
                _newCollectionIndexName = "";
                await LoadCollectionsAsync();
                // Auto-select the newly created collection
                await SelectCollectionAsync(createdCollectionName);
            }
            else
            {
                SnackBarError($"Failed to create collection '{_newCollectionName}'");
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error creating collection {CollectionName}", _newCollectionName);
            SnackBarError($"Error creating collection: {ex.Message}");
        }
    }

    private void ShowEditMetadataForm()
    {
        if (_selectedCollectionInfo != null)
        {
            _editCollectionDescription = _selectedCollectionInfo.Description ?? "";
            _editCollectionType = _selectedCollectionInfo.Type ?? "";
            _editCollectionIndexName = _selectedCollectionInfo.IndexName ?? "";
            _showEditMetadataForm = true;
            _showCreateCollectionForm = false;
            _showUploadSection = false;
        }
    }

    private void CancelEditMetadata()
    {
        _showEditMetadataForm = false;
        _editCollectionDescription = "";
        _editCollectionType = "";
        _editCollectionIndexName = "";
    }

    private async Task ShowDeleteCollectionDialogAsync()
    {
        if (string.IsNullOrEmpty(_selectedCollection))
        {
            SnackBarError("No collection selected");
            return;
        }

        // Show confirmation dialog
        var parameters = new DialogParameters
        {
            { "ContentText", $"Are you sure you want to remove collection '{_selectedCollection}'? This will only remove the collection metadata tag. The container and all its files will remain intact and can be re-added as a collection later." },
            { "ButtonText", "Remove Collection" },
            { "Color", Color.Error }
        };

        var dialog = await DialogService.ShowAsync<ConfirmationDialog>("Confirm Collection Removal", parameters);
        var result = await dialog.Result;

        if (result.Canceled)
            return;

        await DeleteCollectionAsync();
    }

    private async Task DeleteCollectionAsync()
    {
        if (string.IsNullOrEmpty(_selectedCollection))
        {
            SnackBarError("No collection selected");
            return;
        }

        try
        {
            var collectionToDelete = _selectedCollection;
            var success = await Client.DeleteCollectionAsync(collectionToDelete);

            if (success)
            {
                SnackBarMessage($"Collection '{collectionToDelete}' removed successfully. The container and its files remain intact.");
                _selectedCollection = "";
                _selectedCollectionInfo = null;
                _collectionFiles.Clear();
                _fileUploads.Clear();
                await LoadCollectionsAsync();
            }
            else
            {
                SnackBarError($"Failed to remove collection '{collectionToDelete}'");
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error removing collection {CollectionName}", _selectedCollection);
            SnackBarError($"Error removing collection: {ex.Message}");
        }
    }

    private async Task UpdateCollectionMetadataAsync()
    {
        if (string.IsNullOrEmpty(_selectedCollection))
        {
            SnackBarError("No collection selected");
            return;
        }

        try
        {
            var success = await Client.UpdateCollectionMetadataAsync(
                _selectedCollection,
                string.IsNullOrWhiteSpace(_editCollectionDescription) ? null : _editCollectionDescription,
                string.IsNullOrWhiteSpace(_editCollectionType) ? null : _editCollectionType,
                string.IsNullOrWhiteSpace(_editCollectionIndexName) ? null : _editCollectionIndexName);

            if (success)
            {
                SnackBarMessage($"Collection '{_selectedCollection}' metadata updated successfully");
                _showEditMetadataForm = false;

                // Refresh the collection info
                _selectedCollectionInfo = await Client.GetCollectionMetadataAsync(_selectedCollection);

                // Update the collection in the list
                var collectionInList = _collections.FirstOrDefault(c => c.Name == _selectedCollection);
                if (collectionInList != null && _selectedCollectionInfo != null)
                {
                    collectionInList.Description = _selectedCollectionInfo.Description;
                    collectionInList.Type = _selectedCollectionInfo.Type;
                    collectionInList.IndexName = _selectedCollectionInfo.IndexName;
                }

                StateHasChanged();
            }
            else
            {
                SnackBarError($"Failed to update metadata for collection '{_selectedCollection}'");
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error updating collection metadata for {CollectionName}", _selectedCollection);
            SnackBarError($"Error updating collection metadata: {ex.Message}");
        }
    }

    private bool OnFileFilter(ContainerFileInfo fileInfo) =>
        string.IsNullOrWhiteSpace(_filter) || fileInfo.FileName.Contains(_filter, StringComparison.OrdinalIgnoreCase);

    private async Task RefreshAsync()
    {
        await LoadCollectionFilesAsync();
    }

    private async Task SubmitFilesForUploadAsync()
    {
        if (!_fileUploads.Any())
        {
            SnackBarError("Please select files to upload");
            return;
        }

        if (string.IsNullOrEmpty(_selectedCollection))
        {
            SnackBarError("Please select a collection to upload to");
            return;
        }

        _isUploadingDocuments = true;

        try
        {
            var metadata = new Dictionary<string, string>();

            // Try multiple ways to detect the current folder
            string? targetFolder = null;

            Logger.LogInformation("[CLIENT] _uploadFolderPath = '{UploadFolderPath}'", _uploadFolderPath ?? "NULL");
            Logger.LogInformation("[CLIENT] _currentFolderPath = '{CurrentFolderPath}'", _currentFolderPath ?? "NULL");
            Logger.LogInformation("[CLIENT] _selectedCollection = '{SelectedCollection}'", _selectedCollection ?? "NULL");

            // 1. Check if user manually entered a folder path in the upload form
            if (!string.IsNullOrEmpty(_uploadFolderPath))
            {
                targetFolder = _uploadFolderPath;
                Logger.LogInformation("[CLIENT] Using manually entered folder path: {FolderPath}", targetFolder);
            }
            // 2. Check if there's a folder selected in the tree
            else if (!string.IsNullOrEmpty(_currentFolderPath))
            {
                // If the current folder path equals the selected collection name,
                // we're at the root of the collection (container), so don't add a folder prefix
                if (_currentFolderPath == _selectedCollection)
                {
                    Logger.LogInformation("[CLIENT] Current folder is collection root, uploading to container root (no subfolder)");
                    targetFolder = null; // Upload to container root
                }
                else
                {
                    targetFolder = _currentFolderPath;
                    Logger.LogInformation("[CLIENT] Using selected folder from tree: {FolderPath}", targetFolder);
                }
            }
            // Note: Do not auto-detect folders from existing files since collections are blob containers

            if (!string.IsNullOrEmpty(targetFolder))
            {
                var normalizedFolder = targetFolder.Replace("\\", "/").Trim('/');
                if (!string.IsNullOrEmpty(normalizedFolder))
                {
                    metadata["folderPath"] = normalizedFolder;
                    Logger.LogInformation("[CLIENT] Uploading to folder: {FolderPath}", normalizedFolder);
                }
                else
                {
                    Logger.LogInformation("[CLIENT] Folder resolved to root after normalization");
                }
            }
            else
            {
                Logger.LogInformation("[CLIENT] No folder detected, uploading to root");
            }
            var result = await Client.UploadFilesToCollectionAsync(
                _fileUploads.ToArray(),
                MaxIndividualFileSize,
                _selectedCollection,
                metadata);

            Logger.LogInformation("Upload result: {Result}", result);

            if (result.IsSuccessful)
            {
                var uploadLocation = !string.IsNullOrEmpty(targetFolder) ? $"'{_selectedCollection}/{targetFolder}'" : $"'{_selectedCollection}'";
                SnackBarMessage($"Uploaded {result.UploadedFiles.Length} document(s) to {uploadLocation}");
                _fileUploads.Clear();
                _uploadFolderPath = ""; // Clear the folder path input

                // Delay closing to prevent JSInterop errors with file reading
                await Task.Delay(100);
                _showUploadSection = false; // Hide upload section after successful upload
            }
            else
            {
                SnackBarError($"Failed to upload documents. {result.Error}");
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error uploading files to collection {Collection}", _selectedCollection);
            SnackBarError($"Error uploading files: {ex.Message}");
        }
        finally
        {
            _isUploadingDocuments = false;
            await RefreshAsync();
        }
    }

    private void SnackBarMessage(string? message) { SnackBarAdd(false, message); }
    private void SnackBarError(string? message) { SnackBarAdd(true, message); }
    private void SnackBarAdd(bool isError, string? message)
    {
        Snackbar.Add(
            message ?? "Error occurred!",
            isError ? Severity.Error : Severity.Success,
            static options =>
            {
                options.ShowCloseIcon = true;
                options.VisibleStateDuration = 10_000;
            });
    }

    private IList<IBrowserFile> _fileUploads = new List<IBrowserFile>();

    private void UploadFiles(IReadOnlyList<IBrowserFile> files)
    {
        foreach (var file in files)
        {
            _fileUploads.Add(file);
        }
    }

    private void RemoveFile(IBrowserFile file)
    {
        _fileUploads.Remove(file);
        StateHasChanged();
    }

    private async Task ProcessDocumentLayoutAsync(string fileName)
    {
        if (string.IsNullOrEmpty(_selectedCollection) || string.IsNullOrEmpty(fileName))
            return;

        // Add to processing set
        _processingFiles.Add(fileName);
        StateHasChanged();

        try
        {
            Logger.LogInformation("Processing document layout for {FileName} in {Collection}", fileName, _selectedCollection);

            var success = await Client.ProcessDocumentLayoutAsync(_selectedCollection, fileName);

            if (success)
            {
                SnackBarMessage($"Document '{fileName}' processing started successfully");
            }
            else
            {
                SnackBarError($"Failed to start processing for '{fileName}'");
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error processing document layout for {FileName}", fileName);
            SnackBarError($"Error processing document: {ex.Message}");
        }
        finally
        {
            // Remove from processing set
            _processingFiles.Remove(fileName);
            StateHasChanged();
        }
    }

    private async Task ViewFileAsync(string fileName, bool isProcessingFile = false)
    {
        if (string.IsNullOrEmpty(_selectedCollection) || string.IsNullOrEmpty(fileName))
            return;

        try
        {
            var containerName = isProcessingFile ? $"{_selectedCollection}-extract" : _selectedCollection;
            var fileUrl = await Client.GetFileUrlAsync(containerName, fileName);

            if (!string.IsNullOrEmpty(fileUrl))
            {
                var extension = Path.GetExtension(fileName).ToLowerInvariant();

                if (extension == ".pdf")
                {
                    var parameters = new DialogParameters<CollectionPdfViewerDialog>
                    {
                        { x => x.FileName, Path.GetFileName(fileName) },
                        { x => x.FileUrl, fileUrl }
                    };
                    var options = new DialogOptions { MaxWidth = MaxWidth.Large, FullWidth = true, CloseButton = true, CloseOnEscapeKey = true };
                    await DialogService.ShowAsync<CollectionPdfViewerDialog>(Path.GetFileName(fileName), parameters, options);
                }
                else if (extension == ".md")
                {
                    var parameters = new DialogParameters<MarkdownViewerDialog>
                    {
                        { x => x.FileName, Path.GetFileName(fileName) },
                        { x => x.FileUrl, fileUrl }
                    };
                    var options = new DialogOptions { MaxWidth = MaxWidth.Large, FullWidth = true, CloseButton = true, CloseOnEscapeKey = true };
                    await DialogService.ShowAsync<MarkdownViewerDialog>(Path.GetFileName(fileName), parameters, options);
                }
                else if (extension == ".json")
                {
                    var parameters = new DialogParameters<JsonViewerDialog>
                    {
                        { x => x.FileName, Path.GetFileName(fileName) },
                        { x => x.FileUrl, fileUrl }
                    };
                    var options = new DialogOptions { MaxWidth = MaxWidth.Large, FullWidth = true, CloseButton = true, CloseOnEscapeKey = true };
                    await DialogService.ShowAsync<JsonViewerDialog>(Path.GetFileName(fileName), parameters, options);
                }
                else
                {
                    SnackBarError($"File type '{extension}' is not supported for viewing");
                }
            }
            else
            {
                SnackBarError("Failed to generate file URL");
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error viewing file {FileName}", fileName);
            SnackBarError($"Error viewing file: {ex.Message}");
        }
    }

    private bool CanViewFile(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension is ".pdf" or ".md" or ".json";
    }

    private async Task DeleteFileAsync(string fileName)
    {
        if (string.IsNullOrEmpty(_selectedCollection) || string.IsNullOrEmpty(fileName))
            return;

        // Show confirmation dialog
        var parameters = new DialogParameters
        {
            { "ContentText", $"Are you sure you want to delete '{fileName}'? This action cannot be undone. Associated processing files will also be deleted." },
            { "ButtonText", "Delete" },
            { "Color", Color.Error }
        };

        var dialog = await DialogService.ShowAsync<ConfirmationDialog>("Confirm Deletion", parameters);
        var result = await dialog.Result;

        if (result?.Canceled ?? true)
            return;

        // Add to deleting set
        _deletingFiles.Add(fileName);
        StateHasChanged();

        try
        {
            Logger.LogInformation("Deleting file {FileName} from {Collection}", fileName, _selectedCollection);

            var success = await Client.DeleteFileFromCollectionAsync(_selectedCollection, fileName);

            if (success)
            {
                SnackBarMessage($"File '{fileName}' deleted successfully");
                await RefreshAsync();
            }
            else
            {
                SnackBarError($"Failed to delete file '{fileName}'");
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error deleting file {FileName}", fileName);
            SnackBarError($"Error deleting file: {ex.Message}");
        }
        finally
        {
            // Remove from deleting set
            _deletingFiles.Remove(fileName);
            StateHasChanged();
        }
    }

    // Folder management
    private FolderNode _folderStructure = new();
    private FolderNode? _selectedFolder = null;
    private string _currentFolderPath = "";
    private string _uploadFolderPath = "";

    private string GetUploadTargetName()
    {
        if (!string.IsNullOrEmpty(_currentFolderPath) && _currentFolderPath != _selectedCollection)
        {
            return $"{_selectedCollection}/{_currentFolderPath}";
        }
        return _selectedCollection;
    }
    private bool _showFolderView = true;

    private async Task LoadFolderStructureAsync()
    {
        try
        {
            if (string.IsNullOrEmpty(_selectedCollection))
            {
                _folderStructure = new FolderNode("root", "");
                return;
            }

            // Get the folder structure for the SELECTED collection only
            var collectionNode = await Client.GetFolderStructureAsync(_selectedCollection);

            // Use the children of the collection as the root - don't show the collection itself
            _folderStructure = new FolderNode("root", "");
            _folderStructure.Children = collectionNode.Children;

            // Recursively expand all folders in the tree
            foreach (var child in _folderStructure.Children)
            {
                ExpandAllFolders(child);
            }

            Logger.LogInformation($"Loaded folder structure for '{_selectedCollection}': {_folderStructure.Children.Count} top-level folders");

            StateHasChanged();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error loading folder structure");
            SnackBarError("Failed to load folder structure");
            _folderStructure = new FolderNode("root", "");
        }
    }

    private void ExpandAllFolders(FolderNode node)
    {
        node.IsExpanded = true;
        foreach (var child in node.Children)
        {
            ExpandAllFolders(child);
        }
    }

    private void LogTreeStructure(FolderNode node, int level)
    {
        var indent = new string(' ', level * 2);
        Logger.LogInformation($"{indent}- {node.Name} (Children: {node.Children.Count}, IsExpanded: {node.IsExpanded}, Files: {node.FileCount})");
        foreach (var child in node.Children)
        {
            LogTreeStructure(child, level + 1);
        }
    }

    private async Task OnFolderSelectedAsync(FolderNode folder)
    {
        _selectedFolder = folder;

        // Since the tree now only shows subfolders (not the collection itself),
        // the folder.Path is already the correct folder path within the collection
        _currentFolderPath = folder.Path;

        Logger.LogInformation($"Selected folder: {folder.Path} in collection: {_selectedCollection}");

        await LoadCollectionFilesAsync();
    }

    private async Task ShowCreateFolderDialogAsync(FolderNode? parentFolder = null)
    {
        var parameters = new DialogParameters<TextInputDialog>
        {
            { x => x.ContentText, parentFolder != null
                ? $"Enter name for new subfolder in '{parentFolder.Name}'"
                : "Enter name for new folder" },
            { x => x.Label, "Folder Name" },
            { x => x.ButtonText, "CREATE" },
            { x => x.CancelText, "CANCEL" },
            { x => x.ButtonColor, Color.Primary },
            { x => x.HelperText, "Folder names cannot end with . / \\ or contain consecutive dots. Max 1,024 characters." },
            { x => x.MaxLength, 1024 },
            { x => x.ValidationFunc, new Func<string, string?>(ValidateFolderName) }
        };

        var options = new DialogOptions { CloseButton = true, MaxWidth = MaxWidth.Small, FullWidth = true };
        var dialog = await DialogService.ShowAsync<TextInputDialog>("Create Folder", parameters, options);
        var result = await dialog.Result;

        if (!result.Canceled && result.Data is string folderName && !string.IsNullOrWhiteSpace(folderName))
        {
            await CreateFolderAsync(folderName, parentFolder);
        }
    }

    private string? ValidateFolderName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Folder name is required";

        if (name.Length < 1 || name.Length > 1024)
            return "Folder name must be between 1 and 1,024 characters";

        // Check for invalid endings
        if (name.EndsWith('.') || name.EndsWith('/') || name.EndsWith('\\'))
            return "Folder name cannot end with . / or \\";

        // Check for consecutive dots
        if (name.Contains(".."))
            return "Folder name cannot contain consecutive dots";

        // Check for path segments ending with dot
        var segments = name.Split(new[] { '/', '\\' }, StringSplitOptions.None);
        if (segments.Any(s => s.EndsWith('.') && s != "."))
            return "Path segments cannot end with a dot";

        return null;
    }

    private async Task CreateFolderAsync(string folderName, FolderNode? parentFolder = null)
    {
        if (string.IsNullOrEmpty(_selectedCollection) || string.IsNullOrWhiteSpace(folderName))
            return;

        string folderPath;

        if (parentFolder != null && parentFolder.Path != _selectedCollection)
        {
            // Remove collection name from parent path to get the actual folder path
            var parentPath = parentFolder.Path.StartsWith(_selectedCollection + "/")
                ? parentFolder.Path.Substring(_selectedCollection.Length + 1)
                : parentFolder.Path;
            folderPath = $"{parentPath}/{folderName}";
        }
        else
        {
            // Creating at root level of collection
            folderPath = folderName;
        }

        Logger.LogInformation($"Creating folder: Collection='{_selectedCollection}', FolderPath='{folderPath}'");

        try
        {
            var success = await Client.CreateFolderAsync(_selectedCollection, folderPath);

            if (success)
            {
                SnackBarMessage($"Folder '{folderPath}' created in '{_selectedCollection}'");
                await LoadFolderStructureAsync();
            }
            else
            {
                SnackBarError($"Failed to create folder '{folderName}'");
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error creating folder {FolderName}", folderName);
            SnackBarError($"Error creating folder: {ex.Message}");
        }
    }

    private async Task RenameFolderAsync(FolderNode folder)
    {
        if (string.IsNullOrEmpty(_selectedCollection) || folder == null)
            return;

        // Show input dialog for new name
        var result = await DialogService.ShowMessageBox(
            "Rename Folder",
            $"Enter new name for folder '{folder.Name}'",
            yesText: "Rename",
            cancelText: "Cancel");

        if (result == true)
        {
            // This is simplified - you'd want to get the actual new name from a proper input dialog
            var newFolderName = "renamed-folder";
            var pathParts = folder.Path.Split('/');
            pathParts[pathParts.Length - 1] = newFolderName;
            var newFolderPath = string.Join("/", pathParts);

            try
            {
                var success = await Client.RenameFolderAsync(_selectedCollection, folder.Path, newFolderPath);

                if (success)
                {
                    SnackBarMessage($"Folder renamed successfully");
                    await LoadFolderStructureAsync();
                }
                else
                {
                    SnackBarError("Failed to rename folder");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error renaming folder {FolderPath}", folder.Path);
                SnackBarError($"Error renaming folder: {ex.Message}");
            }
        }
    }

    private async Task DeleteFolderAsync(FolderNode folder)
    {
        if (string.IsNullOrEmpty(_selectedCollection) || folder == null)
            return;

        var parameters = new DialogParameters
        {
            { "ContentText", $"Are you sure you want to delete folder '{folder.Name}' and all its contents? This action cannot be undone." },
            { "ButtonText", "Delete" },
            { "Color", Color.Error }
        };

        var dialog = await DialogService.ShowAsync<ConfirmationDialog>("Confirm Folder Deletion", parameters);
        var result = await dialog.Result;

        if (result.Canceled)
            return;

        try
        {
            // Extract folder path without collection name prefix
            var folderPath = folder.Path.StartsWith(_selectedCollection + "/")
                ? folder.Path.Substring(_selectedCollection.Length + 1)
                : folder.Path;

            Logger.LogInformation($"Deleting folder: Collection='{_selectedCollection}', FolderPath='{folderPath}'");

            var success = await Client.DeleteFolderAsync(_selectedCollection, folderPath);

            if (success)
            {
                SnackBarMessage($"Folder '{folder.Name}' deleted successfully");
                _selectedFolder = null;
                _currentFolderPath = "";
                await LoadFolderStructureAsync();
            }
            else
            {
                SnackBarError($"Failed to delete folder '{folder.Name}'");
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error deleting folder {FolderPath}", folder.Path);
            SnackBarError($"Error deleting folder: {ex.Message}");
        }
    }

    private async Task ShowEditFileMetadataDialogAsync(ContainerFileInfo fileInfo)
    {
        if (string.IsNullOrEmpty(_selectedCollection) || fileInfo == null)
            return;

        try
        {
            // Load existing metadata
            var metadata = await Client.GetFileMetadataAsync(_selectedCollection, fileInfo.FileName);

            if (metadata == null)
            {
                // Create new metadata with default values
                metadata = new FileMetadata
                {
                    FileName = fileInfo.FileName,
                    BlobPath = string.IsNullOrEmpty(fileInfo.FolderPath)
                        ? fileInfo.FileName
                        : $"{fileInfo.FolderPath}/{fileInfo.FileName}"
                };
            }

            var parameters = new DialogParameters<FileMetadataDialog>
            {
                { x => x.FileName, fileInfo.FileName },
                { x => x.Metadata, metadata }
            };

            var options = new DialogOptions { MaxWidth = MaxWidth.Medium, FullWidth = true, CloseButton = true };
            var dialog = await DialogService.ShowAsync<FileMetadataDialog>("Edit File Metadata", parameters, options);
            var result = await dialog.Result;

            if (!result.Canceled && result.Data is FileMetadata updatedMetadata)
            {
                var success = await Client.UpdateFileMetadataAsync(_selectedCollection, fileInfo.FileName, updatedMetadata);

                if (success)
                {
                    SnackBarMessage("File metadata updated successfully");
                    fileInfo.Metadata = updatedMetadata;
                    StateHasChanged();
                }
                else
                {
                    SnackBarError("Failed to update file metadata");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error updating file metadata for {FileName}", fileInfo.FileName);
            SnackBarError($"Error updating file metadata: {ex.Message}");
        }
    }

    private async Task CheckIndexingWorkflowStatusAsync()
    {
        if (string.IsNullOrEmpty(_selectedCollection))
            return;

        try
        {
            var status = await Client.GetCollectionIndexingWorkflowStatusAsync(_selectedCollection);

            if (status.HasValue)
            {
                _indexingWorkflowStatus = status.Value;

                // Check if workflow is still in progress
                if (status.Value.TryGetProperty("stages", out var stages))
                {
                    var hasIncomplete = false;
                    foreach (var stage in stages.EnumerateObject())
                    {
                        if (stage.Value.TryGetProperty("status", out var stageStatus))
                        {
                            var statusStr = stageStatus.GetString();
                            if (statusStr != "Complete" && statusStr != "Failed")
                            {
                                hasIncomplete = true;
                                break;
                            }
                        }
                    }

                    if (hasIncomplete)
                    {
                        _isIndexing = true;
                        StartIndexingStatusPolling();
                    }
                }

                StateHasChanged();
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error checking indexing workflow status for collection {CollectionName}", _selectedCollection);
        }
    }

    private async Task IndexSelectedCollectionAsync()
    {
        if (string.IsNullOrEmpty(_selectedCollection))
        {
            SnackBarError("No collection selected");
            return;
        }

        _isIndexing = true;
        _indexingWorkflowStatus = null;
        StateHasChanged();

        try
        {
            Logger.LogInformation("Starting vector indexing for collection {Collection}", _selectedCollection);

            var success = await Client.IndexCollectionAsync(_selectedCollection);

            if (success)
            {
                SnackBarMessage($"Vector indexing started for collection '{_selectedCollection}'");

                // Start polling for status
                StartIndexingStatusPolling();
            }
            else
            {
                _isIndexing = false;
                SnackBarError($"Failed to start vector indexing for collection '{_selectedCollection}'");
            }
        }
        catch (Exception ex)
        {
            _isIndexing = false;
            Logger.LogError(ex, "Error starting vector indexing for collection {CollectionName}", _selectedCollection);
            SnackBarError($"Error starting vector indexing: {ex.Message}");
        }
        finally
        {
            StateHasChanged();
        }
    }

    private void StartIndexingStatusPolling()
    {
        StopIndexingStatusPolling(); // Ensure any existing timer is stopped

        // Start a new timer to poll the status periodically
        _indexingStatusPollTimer = new System.Threading.Timer(async _ =>
        {
            await PollIndexingWorkflowStatusAsync();
        }, null, IndexingStatusPollIntervalMs, IndexingStatusPollIntervalMs);
    }

    private async Task PollIndexingWorkflowStatusAsync()
    {
        // Prevent concurrent polling
        if (_isPollingIndexing || string.IsNullOrEmpty(_selectedCollection))
            return;

        _isPollingIndexing = true;

        try
        {
            var status = await Client.GetCollectionIndexingWorkflowStatusAsync(_selectedCollection);

            if (status.HasValue)
            {
                _indexingWorkflowStatus = status.Value;

                // Check if all stages are complete or failed
                var allComplete = true;
                if (status.Value.TryGetProperty("stages", out var stages))
                {
                    foreach (var stage in stages.EnumerateObject())
                    {
                        if (stage.Value.TryGetProperty("status", out var stageStatus))
                        {
                            var statusStr = stageStatus.GetString();
                            if (statusStr != "Complete" && statusStr != "Failed")
                            {
                                allComplete = false;
                                break;
                            }
                        }
                    }
                }

                // Update UI on the Blazor render thread
                await InvokeAsync(async () =>
                {
                    await LoadCollectionFilesAsync();

                    if (allComplete)
                    {
                        Logger.LogInformation("Vector indexing workflow completed for collection {CollectionName}", _selectedCollection);
                        _isIndexing = false;
                        StopIndexingStatusPolling();
                        SnackBarMessage($"Vector indexing completed for collection '{_selectedCollection}'");
                    }

                    StateHasChanged();
                });
            }
            else
            {
                // No status found - might be too early or workflow doesn't exist
                Logger.LogDebug("No indexing workflow status found for collection {CollectionName}", _selectedCollection);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error polling indexing workflow status for collection {CollectionName}", _selectedCollection);
        }
        finally
        {
            _isPollingIndexing = false;
        }
    }

    private void StopIndexingStatusPolling()
    {
        _indexingStatusPollTimer?.Dispose();
        _indexingStatusPollTimer = null;
    }

    private async Task ShowDeleteIndexingWorkflowDialogAsync()
    {
        if (string.IsNullOrEmpty(_selectedCollection))
        {
            SnackBarError("No collection selected");
            return;
        }

        // Show confirmation dialog
        var parameters = new DialogParameters
        {
            { "ContentText", $"Are you sure you want to delete the vector indexing workflow for collection '{_selectedCollection}'? This action cannot be undone. All workflow files will be permanently deleted." },
            { "ButtonText", "Delete Indexing Workflow" },
            { "Color", Color.Error }
        };

        var dialog = await DialogService.ShowAsync<ConfirmationDialog>("Confirm Workflow Deletion", parameters);
        var result = await dialog.Result;

        if (result!.Canceled)
            return;

        await DeleteCollectionIndexingWorkflowAsync();
    }

    private async Task DeleteCollectionIndexingWorkflowAsync()
    {
        if (string.IsNullOrEmpty(_selectedCollection))
        {
            SnackBarError("No collection selected");
            return;
        }

        try
        {
            Logger.LogInformation("Deleting indexing workflow files for collection {Collection}", _selectedCollection!);

            var success = await Client.DeleteCollectionIndexingWorkflowAsync(_selectedCollection);

            if (success)
            {
                SnackBarMessage($"Indexing workflow for collection '{_selectedCollection}' deleted successfully");
                _indexingWorkflowStatus = null;
                _isIndexing = false;
                StopIndexingStatusPolling();
                await LoadCollectionFilesAsync();
            }
            else
            {
                SnackBarError($"Failed to delete indexing workflow for collection '{_selectedCollection}'");
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error deleting indexing workflow for collection {CollectionName}", _selectedCollection);
            SnackBarError($"Error deleting indexing workflow: {ex.Message}");
        }
    }

    public void Dispose()
    {
        // Cancel any ongoing operations and timers
        _cancellationTokenSource.Cancel();
        _cancellationTokenSource.Dispose();

        // Ensure indexing timers are stopped
        StopIndexingStatusPolling();
        StopPushIndexingStatusPolling();
    }

    private string _collectionFilter = "";

    private bool OnCollectionFilter(CollectionInfo collection)
    {
        if (string.IsNullOrWhiteSpace(_collectionFilter))
            return true;

        var filter = _collectionFilter.ToLower();
        
        return collection.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
               (!string.IsNullOrWhiteSpace(collection.Type) && 
                collection.Type.Contains(filter, StringComparison.OrdinalIgnoreCase)) ||
               (!string.IsNullOrWhiteSpace(collection.Description) && 
                collection.Description.Contains(filter, StringComparison.OrdinalIgnoreCase));
    }

    private string GetCollectionItemClass(CollectionInfo collection)
    {
        var baseClass = "collection-item";
        return collection.Name == _selectedCollection 
            ? $"{baseClass} collection-item-selected" 
            : baseClass;
    }

    private string GetCollectionTextStyle(CollectionInfo collection)
    {
        return collection.Name == _selectedCollection 
            ? "font-weight: 500;" 
            : "font-weight: 400;";
    }

    // Combined item class for unified folder/file view
    private class CombinedItem
    {
        public string Name { get; set; } = "";
        public bool IsFolder { get; set; }
        public FolderNode? FolderNode { get; set; }
        public ContainerFileInfo? FileInfo { get; set; }
    }

    private IEnumerable<CombinedItem> GetCombinedItems()
    {
        var items = new List<CombinedItem>();

        // Add folders first
        if (_folderStructure?.Children?.Any() == true)
        {
            var folders = string.IsNullOrEmpty(_currentFolderPath)
                ? _folderStructure.Children
                : GetCurrentFolderNode()?.Children ?? new List<FolderNode>();

            foreach (var folder in folders)
            {
                if (string.IsNullOrWhiteSpace(_filter) || folder.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase))
                {
                    items.Add(new CombinedItem
                    {
                        Name = folder.Name,
                        IsFolder = true,
                        FolderNode = folder
                    });
                }
            }
        }

        // Add files
        var files = GetFilteredFiles();
        foreach (var file in files)
        {
            items.Add(new CombinedItem
            {
                Name = Path.GetFileName(file.FileName),
                IsFolder = false,
                FileInfo = file
            });
        }

        return items;
    }

    private FolderNode? GetCurrentFolderNode()
    {
        if (string.IsNullOrEmpty(_currentFolderPath) || _folderStructure == null)
            return _folderStructure;

        var pathParts = _currentFolderPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        FolderNode? current = _folderStructure;

        foreach (var part in pathParts)
        {
            current = current?.Children.FirstOrDefault(c => c.Name.Equals(part, StringComparison.OrdinalIgnoreCase));
            if (current == null)
                break;
        }

        return current;
    }

    private async Task NavigateToFolder(FolderNode folder)
    {
        _selectedFolder = folder;
        _currentFolderPath = folder.Path;
        
        Logger.LogInformation($"Navigating to folder: {folder.Path}");
        
        await LoadCollectionFilesAsync();
    }

    private void NavigateToParentFolder()
    {
        if (string.IsNullOrEmpty(_currentFolderPath))
            return;

        var pathParts = _currentFolderPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        
        if (pathParts.Length == 1)
        {
            // Go to root
            _currentFolderPath = "";
            _selectedFolder = null;
        }
        else
        {
            // Go to parent folder
            _currentFolderPath = string.Join("/", pathParts.Take(pathParts.Length - 1));
            _selectedFolder = GetCurrentFolderNode();
        }

        StateHasChanged();
    }

    #region Push Indexing (Agent-Hub-JCI Integration)

    /// <summary>
    /// Shows confirmation dialog and triggers push-based indexing for the selected collection.
    /// This uses the Agent-Hub-JCI push indexing strategy which works with APIM and private endpoints.
    /// </summary>
    private async Task ShowPushIndexingDialogAsync()
    {
        if (string.IsNullOrEmpty(_selectedCollection))
        {
            SnackBarError("No collection selected");
            return;
        }

        // Show confirmation dialog with option to recreate index
        var parameters = new DialogParameters
        {
            { "ContentText", $"Start push-based vector indexing for collection '{_selectedCollection}'? This will index all documents using the Agent-Hub indexing service.\n\nNote: This operation processes documents in the background and may take several minutes depending on the number of files." },
            { "ButtonText", "Start Indexing" },
            { "Color", Color.Primary }
        };

        var dialog = await DialogService.ShowAsync<ConfirmationDialog>("Start Push Indexing", parameters);
        var result = await dialog.Result;

        if (result!.Canceled)
            return;

        await TriggerPushIndexingAsync(recreateIndex: false);
    }

    /// <summary>
    /// Triggers push-based indexing for the selected collection.
    /// </summary>
    private async Task TriggerPushIndexingAsync(bool recreateIndex = false)
    {
        if (string.IsNullOrEmpty(_selectedCollection))
        {
            SnackBarError("No collection selected");
            return;
        }

        _isPushIndexing = true;
        _pushIndexingStatus = null;
        StateHasChanged();

        try
        {
            Logger.LogInformation("Starting push-based indexing for collection {Collection}, recreateIndex={RecreateIndex}", 
                _selectedCollection, recreateIndex);

            var response = await Client.TriggerPushIndexingAsync(_selectedCollection, recreateIndex);

            if (response != null)
            {
                _pushIndexingCorrelationId = response.CorrelationId;
                SnackBarMessage($"Push indexing started for collection '{_selectedCollection}'. Tracking ID: {response.CorrelationId}");

                // Start polling for status
                StartPushIndexingStatusPolling();
            }
            else
            {
                _isPushIndexing = false;
                SnackBarError($"Failed to start push indexing for collection '{_selectedCollection}'");
            }
        }
        catch (Exception ex)
        {
            _isPushIndexing = false;
            Logger.LogError(ex, "Error starting push indexing for collection {CollectionName}", _selectedCollection);
            SnackBarError($"Error starting push indexing: {ex.Message}");
        }
        finally
        {
            StateHasChanged();
        }
    }

    /// <summary>
    /// Starts polling for push indexing status.
    /// </summary>
    private void StartPushIndexingStatusPolling()
    {
        StopPushIndexingStatusPolling(); // Ensure any existing timer is stopped

        // Start a new timer to poll the status periodically
        _pushIndexingStatusPollTimer = new System.Threading.Timer(async _ =>
        {
            await PollPushIndexingStatusAsync();
        }, null, 2000, IndexingStatusPollIntervalMs); // Start after 2 seconds, then poll every 10 seconds
    }

    /// <summary>
    /// Polls the push indexing status endpoint.
    /// </summary>
    private async Task PollPushIndexingStatusAsync()
    {
        // Prevent concurrent polling
        if (_isPollingPushIndexing || string.IsNullOrEmpty(_pushIndexingCorrelationId))
            return;

        _isPollingPushIndexing = true;

        try
        {
            var status = await Client.GetPushIndexingStatusAsync(_pushIndexingCorrelationId);

            if (status != null)
            {
                _pushIndexingStatus = status;
                
                Logger.LogInformation("Push indexing status received: Status='{Status}', Progress={Progress}/{Total}", 
                    status.Status ?? "null", status.ProgressCount, status.TotalCount);

                // Check if indexing is complete or failed (with null safety)
                var statusValue = status.Status ?? "";
                var isComplete = statusValue.Equals("completed", StringComparison.OrdinalIgnoreCase);
                var isFailed = statusValue.Equals("failed", StringComparison.OrdinalIgnoreCase);

                // Update UI on the Blazor render thread
                await InvokeAsync(async () =>
                {
                    if (isComplete)
                    {
                        Logger.LogInformation("Push indexing completed for collection {CollectionName}. Processed: {Processed}/{Total}", 
                            _selectedCollection, status.ProgressCount, status.TotalCount);
                        _isPushIndexing = false;
                        StopPushIndexingStatusPolling();
                        SnackBarMessage($"Push indexing completed for collection '{_selectedCollection}'. Indexed {status.ProgressCount} documents.");
                        
                        // Refresh the file list
                        await LoadCollectionFilesAsync();
                    }
                    else if (isFailed)
                    {
                        Logger.LogWarning("Push indexing failed for collection {CollectionName}: {Error}", 
                            _selectedCollection, status.ErrorMessage);
                        _isPushIndexing = false;
                        StopPushIndexingStatusPolling();
                        SnackBarError($"Push indexing failed: {status.ErrorMessage ?? "Unknown error"}");
                    }

                    StateHasChanged();
                });
            }
            else
            {
                Logger.LogWarning("No push indexing status returned for correlation ID {CorrelationId}", _pushIndexingCorrelationId);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error polling push indexing status for correlation ID {CorrelationId}", _pushIndexingCorrelationId);
        }
        finally
        {
            _isPollingPushIndexing = false;
        }
    }

    /// <summary>
    /// Stops polling for push indexing status.
    /// </summary>
    private void StopPushIndexingStatusPolling()
    {
        _pushIndexingStatusPollTimer?.Dispose();
        _pushIndexingStatusPollTimer = null;
    }

    #endregion
}

