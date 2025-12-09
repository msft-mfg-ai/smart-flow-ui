// Copyright (c) Microsoft. All rights reserved.

using Shared.Models;

namespace SmartFlow.UI.Client.Pages;

public sealed partial class AgentManagement : IDisposable
{
    private AgentViewModel[]? _agents;
    private AgentViewModel? _selectedAgent;
    private string? _selectedAgentId;
    private bool _isLoading = true;
    private bool _isLoadingAgent = false;
    private bool _isEditingPrompt = false;
    private bool _isSaving = false;
    private bool _isDeleting = false;
    private string? _loadError = null;
    
    private string _editedInstructions = string.Empty;

    private readonly CancellationTokenSource _cancellationTokenSource = new();

    [Inject] public required HttpClient Http { get; set; }
    [Inject] public required ISnackbar Snackbar { get; set; }
    [Inject] public required ILogger<AgentManagement> Logger { get; set; }
    [Inject] public required IDialogService DialogService { get; set; }

    protected override async Task OnInitializedAsync()
    {
        await LoadAgentsAsync();
    }

    private async Task LoadAgentsAsync()
    {
        _isLoading = true;
        _loadError = null;
        StateHasChanged();

        try
        {
            _agents = await Http.GetFromJsonAsync<AgentViewModel[]>("api/agents", _cancellationTokenSource.Token);
            Logger.LogInformation("Successfully loaded {Count} agents", _agents?.Length ?? 0);
            
            // Auto-select first agent if available
            if (_agents != null && _agents.Any() && string.IsNullOrEmpty(_selectedAgentId))
            {
                await SelectAgentAsync(_agents.First().Id);
            }
        }
        catch (HttpRequestException ex)
        {
            _loadError = $"Failed to connect to the server: {ex.Message}";
            Logger.LogError(ex, "HTTP error loading agents");
            _agents = Array.Empty<AgentViewModel>();
            Snackbar.Add(_loadError, Severity.Error);
        }
        catch (Exception ex)
        {
            _loadError = $"An error occurred: {ex.Message}";
            Logger.LogError(ex, "Error loading agents");
            _agents = Array.Empty<AgentViewModel>();
            Snackbar.Add(_loadError, Severity.Error);
        }
        finally
        {
            _isLoading = false;
            StateHasChanged();
        }
    }

    private async Task RefreshAgentsAsync()
    {
        _isEditingPrompt = false;
        _editedInstructions = string.Empty;
        await LoadAgentsAsync();
        Snackbar.Add("Agents refreshed", Severity.Success);
    }

    private async Task SelectAgentAsync(string agentId)
    {
        _selectedAgentId = agentId;
        _selectedAgent = null;
        _isEditingPrompt = false;
        _editedInstructions = string.Empty;
        _isLoadingAgent = true;
        StateHasChanged();

        try
        {
            // Fetch agent details with system prompt
            _selectedAgent = await Http.GetFromJsonAsync<AgentViewModel>(
                $"api/agents/{Uri.EscapeDataString(agentId)}", 
                _cancellationTokenSource.Token);
            
            Logger.LogInformation("Successfully loaded agent details for {AgentId}", agentId);
            
            // Initialize edit buffer with current instructions
            _editedInstructions = _selectedAgent?.Instructions ?? string.Empty;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error loading agent details for {AgentId}", agentId);
            Snackbar.Add($"Failed to load agent details: {ex.Message}", Severity.Error);
        }
        finally
        {
            _isLoadingAgent = false;
            StateHasChanged();
        }
    }

    private void CancelEdit()
    {
        _isEditingPrompt = false;
        // Reset to original instructions
        _editedInstructions = _selectedAgent?.Instructions ?? string.Empty;
    }

    private async Task SaveChangesAsync()
    {
        if (_selectedAgent == null || string.IsNullOrWhiteSpace(_editedInstructions))
        {
            Snackbar.Add("System prompt cannot be empty", Severity.Warning);
            return;
        }

        _isSaving = true;
        StateHasChanged();

        try
        {
            var updatedAgent = new AgentViewModel
            {
                Id = _selectedAgent.Id,
                Name = _selectedAgent.Name,
                Instructions = _editedInstructions,
                Description = _selectedAgent.Description,
                Model = _selectedAgent.Model,
                CreatedAt = _selectedAgent.CreatedAt,
                Tools = _selectedAgent.Tools
            };

            var response = await Http.PutAsJsonAsync(
                $"api/agent/{Uri.EscapeDataString(_selectedAgent.Id)}", 
                updatedAgent, 
                _cancellationTokenSource.Token);

            if (response.IsSuccessStatusCode)
            {
                Snackbar.Add($"System prompt updated successfully", Severity.Success);
                Logger.LogInformation("Successfully updated system prompt for agent: {Name} (ID: {Id})", 
                    _selectedAgent.Name, _selectedAgent.Id);
                
                // Reload agent to get fresh data
                await SelectAgentAsync(_selectedAgent.Id);
                
                // Exit edit mode
                _isEditingPrompt = false;
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                Logger.LogWarning("Failed to update agent {Id}. Status: {Status}, Error: {Error}", 
                    _selectedAgent.Id, response.StatusCode, errorContent);
                Snackbar.Add($"Failed to update system prompt: {errorContent}", Severity.Error);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error updating agent: {Name} (ID: {Id})", 
                _selectedAgent.Name, _selectedAgent.Id);
            Snackbar.Add($"Error updating system prompt: {ex.Message}", Severity.Error);
        }
        finally
        {
            _isSaving = false;
            StateHasChanged();
        }
    }

    private async Task DeleteCustomPromptAsync()
    {
        if (_selectedAgent == null)
            return;

        var parameters = new DialogParameters
        {
            { "ContentText", $"Are you sure you want to delete the custom system prompt for '{_selectedAgent.Name}'? The agent will revert to its default prompt." },
            { "ButtonText", "Delete" },
            { "Color", Color.Error }
        };

        var options = new DialogOptions { CloseButton = true, MaxWidth = MaxWidth.Small };
        var dialog = DialogService.Show<ConfirmationDialog>("Delete Custom Prompt", parameters, options);
        var result = await dialog.Result;

        if (result.Canceled)
            return;

        _isDeleting = true;
        StateHasChanged();

        try
        {
            var response = await Http.DeleteAsync(
                $"api/agents/{Uri.EscapeDataString(_selectedAgent.Name)}", 
                _cancellationTokenSource.Token);

            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                var deleteResult = System.Text.Json.JsonSerializer.Deserialize<DeleteAgentResponse>(
                    responseContent, 
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (deleteResult?.DeletedCount > 0)
                {
                    Snackbar.Add($"Custom prompt deleted. Agent will use default prompt.", Severity.Success);
                    Logger.LogInformation("Deleted custom prompt for agent: {Name}", _selectedAgent.Name);
                    
                    // Reload the agent to show default prompt
                    await SelectAgentAsync(_selectedAgent.Id);
                }
                else
                {
                    Snackbar.Add("No custom prompt found to delete", Severity.Info);
                }
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                Logger.LogWarning("Failed to delete custom prompt. Status: {Status}, Error: {Error}", 
                    response.StatusCode, errorContent);
                Snackbar.Add($"Failed to delete custom prompt: {errorContent}", Severity.Error);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error deleting custom prompt for agent: {Name}", _selectedAgent.Name);
            Snackbar.Add($"Error deleting custom prompt: {ex.Message}", Severity.Error);
        }
        finally
        {
            _isDeleting = false;
            StateHasChanged();
        }
    }

    public void Dispose()
    {
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
    }

    private class DeleteAgentResponse
    {
        public int DeletedCount { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
