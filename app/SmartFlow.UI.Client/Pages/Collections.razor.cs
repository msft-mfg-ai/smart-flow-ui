﻿// Copyright (c) Microsoft. All rights reserved.

using Shared.Models;

namespace SmartFlow.UI.Client.Pages;

public sealed partial class Collections : IDisposable
{
    private const long MaxIndividualFileSize = 1_024 * 1_024 * 10;

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

    // Store a cancelation token that will be used to cancel if the user disposes of this component.
    private readonly CancellationTokenSource _cancellationTokenSource = new();

    [Inject] public required ApiClient Client { get; set; }
    [Inject] public required ISnackbar Snackbar { get; set; }
    [Inject] public required ILogger<Collections> Logger { get; set; }
    [Inject] public required IJSRuntime JSRuntime { get; set; }
    [Inject] public required HttpClient HttpClient { get; set; }
    [Inject] public required IDialogService DialogService { get; set; }

    // Collection management
    private List<string> _collections = new();
    private string _selectedCollection = "";
    private List<ContainerFileInfo> _collectionFiles = new();
    private bool _showCreateCollectionForm = false;
    private string _newCollectionName = "";

    protected override async Task OnInitializedAsync()
    {
        // Load collections
        await LoadCollectionsAsync();
    }

    private async Task LoadCollectionsAsync()
    {
        _isLoadingCollections = true;
        try
        {
            _collections = await Client.GetCollectionsAsync();
            // Don't auto-select - require explicit user selection
            _selectedCollection = "";
            _collectionFiles.Clear();
            _fileUploads.Clear();
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
            _selectedCollection = collectionName;
            _fileUploads.Clear(); // Clear any selected files when switching collections
            _filter = ""; // Clear filter when switching collections
            _showCreateCollectionForm = false; // Hide create form when selecting a collection
            _showUploadSection = false; // Hide upload section when switching collections
            await LoadCollectionFilesAsync();
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

    private void ShowCreateCollectionForm()
    {
        _newCollectionName = "";
        _createCollectionFormValid = false;
        _showCreateCollectionForm = true;
    }

    private void CancelCreateCollection()
    {
        _showCreateCollectionForm = false;
        _newCollectionName = "";
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

        if (_collections.Contains(name))
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
            var success = await Client.CreateCollectionAsync(_newCollectionName);
            if (success)
            {
                SnackBarMessage($"Collection '{_newCollectionName}' created successfully");
                _showCreateCollectionForm = false;
                var createdCollectionName = _newCollectionName;
                _newCollectionName = "";
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
            var result = await Client.UploadFilesToCollectionAsync(
                _fileUploads.ToArray(), 
                MaxIndividualFileSize, 
                _selectedCollection, 
                metadata);

            Logger.LogInformation("Upload result: {Result}", result);

            if (result.IsSuccessful)
            {
                SnackBarMessage($"Uploaded {result.UploadedFiles.Length} document(s) to '{_selectedCollection}'");
                _fileUploads.Clear();
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

    public void Dispose() => _cancellationTokenSource.Cancel();
}
