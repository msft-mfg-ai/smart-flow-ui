// Copyright (c) Microsoft. All rights reserved.

using Shared.Models;

namespace SmartFlow.UI.Client.Pages;

public sealed partial class CollectionIndexManagement : IDisposable
{
    private readonly CancellationTokenSource _cancellationTokenSource = new();

    [Inject] public required ApiClient Client { get; set; }
    [Inject] public required ISnackbar Snackbar { get; set; }
    [Inject] public required ILogger<CollectionIndexManagement> Logger { get; set; }
    [Inject] public required IDialogService DialogService { get; set; }

    private List<SearchIndexInfo> _indexes = new();
    private List<CollectionInfo> _collections = new();
    private Dictionary<string, SearchIndexInfo?> _indexDetails = new();
    private HashSet<string> _expandedIndexes = new();
    private HashSet<string> _loadingIndexDetails = new();

    private bool _isLoadingIndexes = false;
    private bool _isLoadingCollections = false;
    private string _indexFilter = "";

    private IEnumerable<SearchIndexInfo> _filteredIndexes =>
        string.IsNullOrWhiteSpace(_indexFilter)
            ? _indexes
            : _indexes.Where(i => i.Name.Contains(_indexFilter, StringComparison.OrdinalIgnoreCase));

    protected override async Task OnInitializedAsync()
    {
        await LoadDataAsync();
    }

    private async Task LoadDataAsync()
    {
        await Task.WhenAll(
            LoadIndexesAsync(),
            LoadCollectionsAsync()
        );
    }

    private async Task LoadIndexesAsync()
    {
        _isLoadingIndexes = true;
        try
        {
            _indexes = await Client.GetSearchIndexesAsync();
            Logger.LogInformation("Loaded {Count} indexes", _indexes.Count);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error loading indexes");
            SnackBarError("Failed to load indexes. Please check your Azure Search configuration.");
        }
        finally
        {
            _isLoadingIndexes = false;
            StateHasChanged();
        }
    }

    private async Task LoadCollectionsAsync()
    {
        _isLoadingCollections = true;
        try
        {
            _collections = await Client.GetCollectionsAsync();
            Logger.LogInformation("Loaded {Count} collections", _collections.Count);
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

    private async Task ToggleIndexExpansionAsync(string indexName)
    {
        if (_expandedIndexes.Contains(indexName))
        {
            _expandedIndexes.Remove(indexName);
        }
        else
        {
            _expandedIndexes.Add(indexName);

            // Load index details if not already loaded
            if (!_indexDetails.ContainsKey(indexName))
            {
                await LoadIndexDetailsAsync(indexName);
            }
        }

        StateHasChanged();
    }

    private async Task LoadIndexDetailsAsync(string indexName)
    {
        _loadingIndexDetails.Add(indexName);
        StateHasChanged();

        try
        {
            var details = await Client.GetSearchIndexDetailsAsync(indexName);
            _indexDetails[indexName] = details;
            Logger.LogInformation("Loaded details for index {IndexName} with {FieldCount} fields",
                indexName, details?.Fields.Count ?? 0);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error loading details for index {IndexName}", indexName);
            SnackBarError($"Failed to load details for index '{indexName}'");
            _indexDetails[indexName] = null;
        }
        finally
        {
            _loadingIndexDetails.Remove(indexName);
            StateHasChanged();
        }
    }

    private IEnumerable<CollectionInfo> GetAssociatedCollections(string indexName)
    {
        return _collections.Where(c =>
            !string.IsNullOrEmpty(c.IndexName) &&
            c.IndexName.Equals(indexName, StringComparison.OrdinalIgnoreCase));
    }

    private async Task ShowAssociateCollectionDialogAsync(string indexName)
    {
        // Get collections that don't have an index or have a different index
        var availableCollections = _collections
            .Where(c => string.IsNullOrEmpty(c.IndexName) ||
                       !c.IndexName.Equals(indexName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (!availableCollections.Any())
        {
            SnackBarError("No available collections to associate. All collections are already associated with this or another index.");
            return;
        }

        var parameters = new DialogParameters<AssociateCollectionDialog>
        {
            { x => x.IndexName, indexName },
            { x => x.AvailableCollections, availableCollections }
        };

        var options = new DialogOptions
        {
            MaxWidth = MaxWidth.Small,
            FullWidth = true,
            CloseButton = true
        };

        var dialog = await DialogService.ShowAsync<AssociateCollectionDialog>(
            "Associate Collection with Index",
            parameters,
            options);

        var result = await dialog.Result;

        if (result is { Canceled: false, Data: string selectedCollectionName })
        {
            await AssociateCollectionWithIndexAsync(selectedCollectionName, indexName);
        }
    }

    private async Task AssociateCollectionWithIndexAsync(string collectionName, string indexName)
    {
        try
        {
            var collection = _collections.FirstOrDefault(c => c.Name == collectionName)!;
            if (collection == null)
            {
                SnackBarError($"Collection '{collectionName}' not found");
                return;
            }

            var success = await Client.UpdateCollectionMetadataAsync(
                collectionName,
                collection.Description,
                collection.Type,
                indexName);

            if (success)
            {
                SnackBarMessage($"Successfully associated collection '{collectionName}' with index '{indexName}'");
                await LoadCollectionsAsync();
            }
            else
            {
                SnackBarError($"Failed to associate collection '{collectionName}' with index '{indexName}'");
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error associating collection {CollectionName} with index {IndexName}",
                collectionName, indexName);
            SnackBarError($"Error associating collection: {ex.Message}");
        }
    }

    private async Task RemoveIndexFromCollectionAsync(string collectionName)
    {
        var collection = _collections.FirstOrDefault(c => c.Name == collectionName);
        if (collection == null)
        {
            SnackBarError($"Collection '{collectionName}' not found");
            return;
        }

        // Show confirmation dialog
        var parameters = new DialogParameters
        {
            { "ContentText", $"Are you sure you want to remove the index association from collection '{collectionName}'? The collection will remain but will no longer be associated with index '{collection.IndexName!}'." },
            { "ButtonText", "Remove Association" },
            { "Color", Color.Warning }
        };

        var dialog = await DialogService.ShowAsync<ConfirmationDialog>("Confirm Index Removal", parameters);
        var result = await dialog.Result;

        if (result!.Canceled)
            return;

        try
        {
            var success = await Client.UpdateCollectionMetadataAsync(
                collectionName,
                collection.Description,
                collection.Type,
                null); // Set indexName to null to remove association

            if (success)
            {
                SnackBarMessage($"Successfully removed index association from collection '{collectionName}'");
                await LoadCollectionsAsync();
            }
            else
            {
                SnackBarError($"Failed to remove index association from collection '{collectionName}'");
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error removing index from collection {CollectionName}", collectionName);
            SnackBarError($"Error removing index association: {ex.Message}");
        }
    }

    private string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }

    private void SnackBarMessage(string? message) => SnackBarAdd(false, message);
    private void SnackBarError(string? message) => SnackBarAdd(true, message);

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

    public void Dispose() => _cancellationTokenSource.Cancel();
}
