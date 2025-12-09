// Copyright (c) Microsoft. All rights reserved.

using Shared.Models;

namespace SmartFlow.UI.Client.Pages;

public sealed partial class Projects : IDisposable
{
    private const long MaxIndividualFileSize = 1_024 * 1_024 * 250; // 250MB

    private MudForm _form = null!;
    private MudForm _createProjectForm = null!;
    private bool _createProjectFormValid = false;

    private bool _isLoadingDocuments = false;
    private bool _isUploadingDocuments = false;
    private bool _isIndexingDocuments = false;
    private bool _isLoadingProjects = false;
    private bool _showUploadSection = false; // Hidden by default - user clicks "Upload Document" to show
    private string _filter = "";
    private string _projectFilter = "";
    private int _filteredProjectCount = 0;
    private HashSet<string> _deletingFiles = new(); // Track files being deleted
    private HashSet<string> _analyzingFiles = new(); // Track files being analyzed (project-level)
    private HashSet<string> _editingFileDescriptions = new(); // Track files being edited
    private Dictionary<string, string> _editingDescriptions = new(); // Track temporary description values during editing
    private bool _isWorkflowProcessing = false; // Unified flag for any workflow processing in progress
    private System.Text.Json.JsonElement? _workflowStatus = null; // Store workflow status
    private System.Threading.Timer? _statusPollTimer = null; // Timer for polling status
    private bool _isPolling = false; // Track if polling is active to prevent concurrent polls
    private const int StatusPollIntervalMs = 10000; // Poll every 10 seconds

    // Store a cancelation token that will be used to cancel if the user disposes of this component.
    private readonly CancellationTokenSource _cancellationTokenSource = new();

    [Inject] public required ApiClient Client { get; set; }
    [Inject] public required ISnackbar Snackbar { get; set; }
    [Inject] public required ILogger<Projects> Logger { get; set; }
    [Inject] public required IJSRuntime JSRuntime { get; set; }
    [Inject] public required HttpClient HttpClient { get; set; }
    [Inject] public required IDialogService DialogService { get; set; }

    // Project management
    private List<CollectionInfo> _projects = new();
    private string _selectedProject = "";
    private CollectionInfo? _selectedProjectInfo = null;
    private List<ContainerFileInfo> _projectFiles = new();
    private bool _showCreateProjectForm = false;
    private string _newProjectName = "";
    private string _newProjectDescription = "";
    private string _newProjectType = "";

    // Edit project metadata
    private bool _showEditMetadataForm = false;
    private string _editProjectDescription = "";
    private string _editProjectType = "";

    protected override async Task OnInitializedAsync()
    {
        // Load projects
        await LoadProjectsAsync();
    }

    protected override void OnAfterRender(bool firstRender)
    {
        if (!firstRender)
        {
            // Reset counter before filter runs
            _filteredProjectCount = 0;
        }
    }

    private async Task LoadProjectsAsync()
    {
        _isLoadingProjects = true;
        try
        {
            _projects = await Client.GetProjectsAsync();
            // Auto-select first project if available
            if (_projects.Any())
            {
                await SelectProjectAsync(_projects.First().Name);
            }
            else
            {
                _selectedProject = "";
                _selectedProjectInfo = null;
                _projectFiles.Clear();
                _fileUploads.Clear();
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error loading projects");
            SnackBarError("Failed to load projects");
        }
        finally
        {
            _isLoadingProjects = false;
            StateHasChanged();
        }
    }

    private async Task SelectProjectAsync(string projectName)
    {
        if (_selectedProject != projectName)
        {
            // Stop any existing status polling when switching projects
            StopStatusPolling();
            _isWorkflowProcessing = false;
            _workflowStatus = null;
            
            _selectedProject = projectName;
            _selectedProjectInfo = _projects.FirstOrDefault(c => c.Name == projectName);
            _fileUploads.Clear(); // Clear any selected files when switching projects
            _filter = ""; // Clear filter when switching projects
            _showCreateProjectForm = false; // Hide create form when selecting a project
            _showUploadSection = false; // Hide upload section when switching projects
            _showEditMetadataForm = false; // Hide edit metadata form when selecting projects
            await LoadProjectFilesAsync();
            
            // Check for existing workflow status
            await CheckWorkflowStatusAsync();
        }
    }

    private async Task CheckWorkflowStatusAsync()
    {
        if (string.IsNullOrEmpty(_selectedProject))
            return;

        try
        {
            var status = await Client.GetProjectWorkflowStatusAsync(_selectedProject);
            
            if (status.HasValue)
            {
                _workflowStatus = status.Value;
                
                // Check if any stages are still processing
                var hasIncomplete = HasIncompleteStages(status.Value);
                
                _isWorkflowProcessing = hasIncomplete;
                
                if (hasIncomplete)
                {
                    StartStatusPolling();
                }
                
                StateHasChanged();
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error checking workflow status for project {ProjectName}", _selectedProject);
        }
    }

    /// <summary>
    /// Checks if any stage in the workflow status is not complete or failed (i.e., still processing)
    /// </summary>
    private bool HasIncompleteStages(System.Text.Json.JsonElement status)
    {
        if (!status.TryGetProperty("stages", out var stages))
            return false;

        foreach (var stage in stages.EnumerateObject())
        {
            var stageData = stage.Value;
            if (stageData.TryGetProperty("status", out var statusProp))
            {
                var statusStr = statusProp.GetString();
                if (statusStr != "Complete" && statusStr != "Failed")
                {
                    return true;
                }
            }
        }
        
        return false;
    }

    private async Task LoadProjectFilesAsync()
    {
        if (string.IsNullOrEmpty(_selectedProject))
            return;

        _isLoadingDocuments = true;
        try
        {
            _projectFiles = await Client.GetProjectFilesAsync(_selectedProject);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error loading project files for {Project}", _selectedProject);
            SnackBarError($"Failed to load files from project '{_selectedProject}'");
        }
        finally
        {
            _isLoadingDocuments = false;
            StateHasChanged();
        }
    }

    private void ShowCreateProjectForm()
    {
        _newProjectName = "";
        _newProjectDescription = "";
        _newProjectType = "";
        _createProjectFormValid = false;
        _showCreateProjectForm = true;
        _showEditMetadataForm = false;
    }

    private void CancelCreateProject()
    {
        _showCreateProjectForm = false;
        _newProjectName = "";
        _newProjectDescription = "";
        _newProjectType = "";
        _createProjectFormValid = false;
    }

    private string ValidateProjectName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Project name is required";

        // Azure Storage container naming rules
        if (name.Length < 3 || name.Length > 63)
            return "Project name must be between 3 and 63 characters";

        if (!System.Text.RegularExpressions.Regex.IsMatch(name, "^[a-z0-9]([a-z0-9-]*[a-z0-9])?$"))
            return "Project name must contain only lowercase letters, numbers, and hyphens, and must start and end with a letter or number";

        if (name.Contains("--"))
            return "Project name cannot contain consecutive hyphens";

        if (_projects.Any(c => c.Name == name))
            return "A project with this name already exists";

        return null!;
    }

    private async Task CreateProjectAsync()
    {
        if (string.IsNullOrWhiteSpace(_newProjectName) || !_createProjectFormValid)
        {
            SnackBarError("Please enter a valid project name");
            return;
        }

        try
        {
            var success = await Client.CreateProjectAsync(
                _newProjectName, 
                string.IsNullOrWhiteSpace(_newProjectDescription) ? null : _newProjectDescription,
                string.IsNullOrWhiteSpace(_newProjectType) ? null : _newProjectType);
            
            if (success)
            {
                SnackBarMessage($"Project '{_newProjectName}' created successfully");
                _showCreateProjectForm = false;
                var createdProjectName = _newProjectName;
                _newProjectName = "";
                _newProjectDescription = "";
                _newProjectType = "";
                await LoadProjectsAsync();
                // Auto-select the newly created project
                await SelectProjectAsync(createdProjectName);
            }
            else
            {
                SnackBarError($"Failed to create project '{_newProjectName}'");
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error creating project {ProjectName}", _newProjectName);
            SnackBarError($"Error creating project: {ex.Message}");
        }
    }

    private void ShowEditMetadataForm()
    {
        if (_selectedProjectInfo != null)
        {
            _editProjectDescription = _selectedProjectInfo.Description ?? "";
            _editProjectType = _selectedProjectInfo.Type ?? "";
            _showEditMetadataForm = true;
            _showCreateProjectForm = false;
            _showUploadSection = false;
        }
    }

    private void CancelEditMetadata()
    {
        _showEditMetadataForm = false;
        _editProjectDescription = "";
        _editProjectType = "";
    }

    private async Task ShowDeleteProjectDialogAsync()
    {
        if (string.IsNullOrEmpty(_selectedProject))
        {
            SnackBarError("No project selected");
            return;
        }

        // Show confirmation dialog
        var parameters = new DialogParameters
        {
            { "ContentText", $"Are you sure you want to delete project '{_selectedProject}'? This action cannot be undone. All files and metadata in this project will be deleted." },
            { "ButtonText", "Delete Project" },
            { "Color", Color.Error }
        };

        var dialog = await DialogService.ShowAsync<ConfirmationDialog>("Confirm Project Deletion", parameters);
        var result = await dialog.Result;

        if (result.Canceled)
            return;

        await DeleteProjectAsync();
    }

    private async Task DeleteProjectAsync()
    {
        if (string.IsNullOrEmpty(_selectedProject))
        {
            SnackBarError("No project selected");
            return;
        }

        try
        {
            var projectToDelete = _selectedProject;
            var success = await Client.DeleteProjectAsync(projectToDelete);

            if (success)
            {
                SnackBarMessage($"Project '{projectToDelete}' deleted successfully");
                _selectedProject = "";
                _selectedProjectInfo = null;
                _projectFiles.Clear();
                _fileUploads.Clear();
                await LoadProjectsAsync();
            }
            else
            {
                SnackBarError($"Failed to delete project '{projectToDelete}'");
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error deleting project {ProjectName}", _selectedProject);
            SnackBarError($"Error deleting project: {ex.Message}");
        }
    }

    private async Task UpdateProjectMetadataAsync()
    {
        if (string.IsNullOrEmpty(_selectedProject))
        {
            SnackBarError("No project selected");
            return;
        }

        try
        {
            var success = await Client.UpdateProjectMetadataAsync(
                _selectedProject,
                string.IsNullOrWhiteSpace(_editProjectDescription) ? null : _editProjectDescription,
                string.IsNullOrWhiteSpace(_editProjectType) ? null : _editProjectType);

            if (success)
            {
                SnackBarMessage($"Project '{_selectedProject}' metadata updated successfully");
                _showEditMetadataForm = false;
                
                // Refresh the project info
                _selectedProjectInfo = await Client.GetProjectMetadataAsync(_selectedProject);
                
                // Update the project in the list
                var projectInList = _projects.FirstOrDefault(c => c.Name == _selectedProject);
                if (projectInList != null && _selectedProjectInfo != null)
                {
                    projectInList.Description = _selectedProjectInfo.Description;
                    projectInList.Type = _selectedProjectInfo.Type;
                }
                
                StateHasChanged();
            }
            else
            {
                SnackBarError($"Failed to update metadata for project '{_selectedProject}'");
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error updating project metadata for {ProjectName}", _selectedProject);
            SnackBarError($"Error updating project metadata: {ex.Message}");
        }
    }

    private bool OnFileFilter(ContainerFileInfo fileInfo) => 
        string.IsNullOrWhiteSpace(_filter) || fileInfo.FileName.Contains(_filter, StringComparison.OrdinalIgnoreCase);

    private bool OnProjectFilter(CollectionInfo projectInfo)
    {
        if (string.IsNullOrWhiteSpace(_projectFilter))
        {
            _filteredProjectCount = _projects.Count;
            return true;
        }

        var filter = _projectFilter.ToLower();
        
        var matches = projectInfo.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                     (!string.IsNullOrWhiteSpace(projectInfo.Type) && 
                      projectInfo.Type.Contains(filter, StringComparison.OrdinalIgnoreCase)) ||
                     (!string.IsNullOrWhiteSpace(projectInfo.Description) && 
                      projectInfo.Description.Contains(filter, StringComparison.OrdinalIgnoreCase));

        if (matches)
        {
            _filteredProjectCount++;
        }

        return matches;
    }

    private async Task OnProjectSelectionChangedAsync(CollectionInfo? project)
    {
        if (project != null)
        {
            await SelectProjectAsync(project.Name);
        }
    }

    private async Task RefreshAsync()
    {
        await LoadProjectFilesAsync();
    }

    private async Task SubmitFilesForUploadAsync()
    {
        if (!_fileUploads.Any())
        {
            SnackBarError("Please select files to upload");
            return;
        }

        if (string.IsNullOrEmpty(_selectedProject))
        {
            SnackBarError("Please select a project to upload to");
            return;
        }

        _isUploadingDocuments = true;

        try
        {
            var metadata = new Dictionary<string, string>();
            
            // Add description to metadata if provided
            if (!string.IsNullOrWhiteSpace(_fileDescription))
            {
                metadata["description"] = _fileDescription;
            }
            
            var result = await Client.UploadFilesToProjectAsync(
                _fileUploads.ToArray(), 
                MaxIndividualFileSize, 
                _selectedProject, 
                metadata);

            Logger.LogInformation("Upload result: {Result}", result);

            if (result.IsSuccessful)
            {
                SnackBarMessage($"Uploaded {result.UploadedFiles.Length} document(s) to '{_selectedProject}'");
                _fileUploads.Clear();
                _fileDescription = string.Empty; // Clear description after successful upload
                _showUploadSection = false; // Hide upload section after successful upload
            }
            else
            {
                SnackBarError($"Failed to upload documents. {result.Error}");
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error uploading files to project {Project}", _selectedProject);
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
    private string _fileDescription = string.Empty;

    private async Task AnalyzeProjectFileAsync(string fileName)
    {
        if (string.IsNullOrEmpty(_selectedProject) || string.IsNullOrEmpty(fileName))
            return;

        // Add to analyzing set
        _analyzingFiles.Add(fileName);
        StateHasChanged();

        try
        {
            Logger.LogInformation("Analyzing file {FileName} in {Project}", fileName, _selectedProject);
            
            var success = await Client.AnalyzeProjectAsync(_selectedProject);
            
            if (success)
            {
                SnackBarMessage($"Analysis for '{fileName}' started successfully");
                // Refresh the file list after a short delay to see any processing files
                await Task.Delay(2000);
                await RefreshAsync();
            }
            else
            {
                SnackBarError($"Failed to start analysis for '{fileName}'");
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error analyzing file {FileName}", fileName);
            SnackBarError($"Error analyzing file: {ex.Message}");
        }
        finally
        {
            // Remove from analyzing set
            _analyzingFiles.Remove(fileName);
            StateHasChanged();
        }
    }

    private async Task AnalyzeSelectedProjectAsync()
    {
        if (string.IsNullOrEmpty(_selectedProject))
        {
            SnackBarError("No project selected");
            return;
        }

        _isWorkflowProcessing = true;
        _workflowStatus = null;
        StateHasChanged();

        try
        {
            Logger.LogInformation("Analyzing project {Project}", _selectedProject);
            
            var success = await Client.AnalyzeProjectAsync(_selectedProject);
            
            if (success)
            {
                SnackBarMessage($"Spec analysis started for project '{_selectedProject}'");
                
                // Start polling for status
                StartStatusPolling();
            }
            else
            {
                _isWorkflowProcessing = false;
                SnackBarError($"Failed to start spec analysis for project '{_selectedProject}'");
            }
        }
        catch (Exception ex)
        {
            _isWorkflowProcessing = false;
            Logger.LogError(ex, "Error analyzing project {ProjectName}", _selectedProject);
            SnackBarError($"Error analyzing project: {ex.Message}");
        }
        finally
        {
            StateHasChanged();
        }
    }

    private async Task AnalyzeSpecV2SelectedProjectAsync()
    {
        if (string.IsNullOrEmpty(_selectedProject))
        {
            SnackBarError("No project selected");
            return;
        }

        _isWorkflowProcessing = true;
        _workflowStatus = null;
        StateHasChanged();

        try
        {
            Logger.LogInformation("Analyzing project with Spec V2 {Project}", _selectedProject);
            
            var success = await Client.AnalyzeProjectSpecV2Async(_selectedProject);
            
            if (success)
            {
                SnackBarMessage($"Spec V2 analysis started for project '{_selectedProject}'");
                
                // Start polling for status
                StartStatusPolling();
            }
            else
            {
                _isWorkflowProcessing = false;
                SnackBarError($"Failed to start spec V2 analysis for project '{_selectedProject}'");
            }
        }
        catch (Exception ex)
        {
            _isWorkflowProcessing = false;
            Logger.LogError(ex, "Error analyzing project with Spec V2 {ProjectName}", _selectedProject);
            SnackBarError($"Error analyzing project with Spec V2: {ex.Message}");
        }
        finally
        {
            StateHasChanged();
        }
    }

    private async Task AnalyzePlanSelectedProjectAsync()
    {
        if (string.IsNullOrEmpty(_selectedProject))
        {
            SnackBarError("No project selected");
            return;
        }

        _isWorkflowProcessing = true;
        _workflowStatus = null;
        StateHasChanged();

        try
        {
            Logger.LogInformation("Analyzing plan for project {Project}", _selectedProject);
            
            var success = await Client.AnalyzePlanProjectAsync(_selectedProject);
            
            if (success)
            {
                SnackBarMessage($"Plan analysis started for project '{_selectedProject}'");
                
                // Start polling for status
                StartStatusPolling();
            }
            else
            {
                _isWorkflowProcessing = false;
                SnackBarError($"Failed to start plan analysis for project '{_selectedProject}'");
            }
        }
        catch (Exception ex)
        {
            _isWorkflowProcessing = false;
            Logger.LogError(ex, "Error analyzing plan for project {ProjectName}", _selectedProject);
            SnackBarError($"Error analyzing plan: {ex.Message}");
        }
        finally
        {
            StateHasChanged();
        }
    }

    private void StartStatusPolling()
    {
        StopStatusPolling(); // Ensure any existing timer is stopped

        // Start a new timer to poll the status periodically
        _statusPollTimer = new System.Threading.Timer(async _ =>
        {
            await PollWorkflowStatusAsync();
        }, null, StatusPollIntervalMs, StatusPollIntervalMs);
    }

    private async Task PollWorkflowStatusAsync()
    {
        // Prevent concurrent polling
        if (_isPolling || string.IsNullOrEmpty(_selectedProject))
            return;

        _isPolling = true;

        try
        {
            var status = await Client.GetProjectWorkflowStatusAsync(_selectedProject);
            
            if (status.HasValue)
            {
                _workflowStatus = status.Value;
                
                // Check if any stages are still processing
                var hasIncomplete = HasIncompleteStages(status.Value);
                
                // Update UI on the Blazor render thread
                await InvokeAsync(async () =>
                {
                    _isWorkflowProcessing = hasIncomplete;
                    
                    await LoadProjectFilesAsync();
                    
                    if (!hasIncomplete)
                    {
                        Logger.LogInformation("Workflow completed for project {ProjectName}", _selectedProject);
                        StopStatusPolling();
                        SnackBarMessage($"Analysis completed for project '{_selectedProject}'");
                    }
                    
                    StateHasChanged();
                });
            }
            else
            {
                // No status found - might be too early or workflow doesn't exist
                Logger.LogDebug("No workflow status found for project {ProjectName}", _selectedProject);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error polling workflow status for project {ProjectName}", _selectedProject);
        }
        finally
        {
            _isPolling = false;
        }
    }

    private void StopStatusPolling()
    {
        _statusPollTimer?.Dispose();
        _statusPollTimer = null;
    }

    public void Dispose()
    {
        // Cancel any ongoing operations and timers
        _cancellationTokenSource.Cancel();

        // Ensure timers are stopped
        StopStatusPolling();
    }

    private async Task ViewFileAsync(string fileName, bool isProcessingFile = false)
    {
        if (string.IsNullOrEmpty(_selectedProject) || string.IsNullOrEmpty(fileName))
            return;

        try
        {
            var fileUrl = await Client.GetProjectFileUrlAsync(_selectedProject, fileName, isProcessingFile);
            
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
        if (string.IsNullOrEmpty(_selectedProject) || string.IsNullOrEmpty(fileName))
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

        if (result.Canceled)
            return;

        // Add to deleting set
        _deletingFiles.Add(fileName);
        StateHasChanged();

        try
        {
            Logger.LogInformation("Deleting file {FileName} from {Project}", fileName, _selectedProject);
            
            var success = await Client.DeleteFileFromProjectAsync(_selectedProject, fileName);
            
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

    private async Task ShowDeleteWorkflowDialogAsync()
    {
        if (string.IsNullOrEmpty(_selectedProject))
        {
            SnackBarError("No project selected");
            return;
        }

        // Show confirmation dialog
        var parameters = new DialogParameters
        {
            { "ContentText", $"Are you sure you want to delete ALL workflow files for project '{_selectedProject}'? This action cannot be undone. All processing files will be permanently deleted." },
            { "ButtonText", "Delete All Workflow Files" },
            { "Color", Color.Error }
        };

        var dialog = await DialogService.ShowAsync<ConfirmationDialog>("Confirm Workflow Deletion", parameters);
        var result = await dialog.Result;

        if (result.Canceled)
            return;

        await DeleteProjectWorkflowAsync();
    }

    private async Task DeleteProjectWorkflowAsync()
    {
        if (string.IsNullOrEmpty(_selectedProject))
        {
            SnackBarError("No project selected");
            return;
        }

        try
        {
            Logger.LogInformation("Deleting workflow files for project {Project}", _selectedProject);
            
            var success = await Client.DeleteProjectWorkflowAsync(_selectedProject);
            
            if (success)
            {
                SnackBarMessage($"Workflow files for project '{_selectedProject}' deleted successfully");
                await RefreshAsync();
            }
            else
            {
                SnackBarError($"Failed to delete workflow files for project '{_selectedProject}'");
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error deleting workflow files for project {ProjectName}", _selectedProject);
            SnackBarError($"Error deleting workflow files: {ex.Message}");
        }
    }

    private string GetProjectItemClass(CollectionInfo project)
    {
        var baseClass = "project-item";
        return project.Name == _selectedProject 
            ? $"{baseClass} project-item-selected" 
            : baseClass;
    }

    private string GetProjectTextStyle(CollectionInfo project)
    {
        return project.Name == _selectedProject 
            ? "font-weight: 500;" 
            : "font-weight: 400;";
    }

    private string GetFileIcon(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".md" => Icons.Material.Filled.Description,
            ".json" => Icons.Material.Filled.DataObject,
            ".pdf" => Icons.Custom.FileFormats.FilePdf,
            ".txt" => Icons.Material.Filled.TextSnippet,
            _ => Icons.Material.Filled.InsertDriveFile
        };
    }

    /// <summary>
    /// Gets the display name for a file by removing the project name prefix if present.
    /// Files are typically stored as "projectname/filename.ext" so this strips the prefix.
    /// </summary>
    private string GetFileDisplayName(string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
            return fileName;

        // Check if the file has a path prefix (e.g., "projectname/filename.ext")
        var lastSlashIndex = fileName.LastIndexOf('/');
        if (lastSlashIndex >= 0 && lastSlashIndex < fileName.Length - 1)
        {
            return fileName.Substring(lastSlashIndex + 1);
        }

        return fileName;
    }

    private void StartEditingDescription(ContainerFileInfo file)
    {
        _editingFileDescriptions.Add(file.FileName);
        _editingDescriptions[file.FileName] = file.Description ?? string.Empty;
        StateHasChanged();
    }

    private void CancelEditingDescription(string fileName)
    {
        _editingFileDescriptions.Remove(fileName);
        _editingDescriptions.Remove(fileName);
        StateHasChanged();
    }

    private async Task SaveFileDescriptionAsync(ContainerFileInfo file)
    {
        if (!_editingDescriptions.TryGetValue(file.FileName, out var newDescription))
            return;

        try
        {
            Logger.LogInformation("Saving description for file {FileName}: '{Description}'", file.FileName, newDescription);
            
            var success = await Client.UpdateFileDescriptionAsync(_selectedProject, file.FileName, string.IsNullOrWhiteSpace(newDescription) ? null : newDescription);
            
            if (success)
            {
                // Update the local file object immediately for UI responsiveness
                file.Description = string.IsNullOrWhiteSpace(newDescription) ? null : newDescription;
                _editingFileDescriptions.Remove(file.FileName);
                _editingDescriptions.Remove(file.FileName);
                
                SnackBarMessage("File description updated successfully");
                
                // Reload files from server to ensure we have the latest persisted data
                await LoadProjectFilesAsync();
            }
            else
            {
                SnackBarError("Failed to update file description");
                Logger.LogWarning("UpdateFileDescriptionAsync returned false for {FileName}", file.FileName);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error updating file description for {FileName}", file.FileName);
            SnackBarError($"Error updating description: {ex.Message}");
        }
    }
}

