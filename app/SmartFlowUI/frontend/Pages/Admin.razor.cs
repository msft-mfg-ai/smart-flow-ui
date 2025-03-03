﻿// Copyright (c) Microsoft. All rights reserved.

namespace ClientApp.Pages;

public sealed partial class Admin : IDisposable
{
    private MudForm _form = null!;

    private bool _isLoadingProfiles = false;
    private ProfileInfo _profileInfo = new ProfileInfo();
    private string _profileRawData = string.Empty;

    // Store a cancelation token that will be used to cancel if the user disposes of this component.
    private readonly CancellationTokenSource _cancellationTokenSource = new();

    [Inject] public required ApiClient Client { get; set; }
    [Inject] public required ISnackbar Snackbar { get; set; }
    [Inject] public required ILogger<Docs> Logger { get; set; }
    [Inject] public required IJSRuntime JSRuntime { get; set; }
    [Inject] public required HttpClient httpClient { get; set; }

    protected override void OnInitialized()
    {
    }
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        await base.OnAfterRenderAsync(firstRender);
        if (firstRender)
        {
            //showInfo("First render...  fetching profiles...");
            await GetProfileInfoAsync();
            StateHasChanged();
        }
    }

    private async Task GetProfileInfoAsync()
    {
        _ = await Task.FromResult(true);
        _isLoadingProfiles = true;
        //showInfo("Calling getProfileInfo...");
        var profileData = string.Empty;
        try
        {
            (_profileInfo, _profileRawData) = await Client.GetProfilesInfoAsync();
        }
        catch (Exception ex)
        {
            showWarning($"Failed to read profileInfo! {ex.Message}");
            showWarning(profileData);
        }
        _isLoadingProfiles = false;
        StateHasChanged();
    }

    private async Task ReloadProfileInfoAsync()
    {
        _ = await Task.FromResult(true);
        _isLoadingProfiles = true;
        showInfo("Calling reloadProfileInfo...");
        var profileData = string.Empty;
        try
        {
            (_profileInfo, _profileRawData) = await Client.GetProfilesReloadAsync();
        }
        catch (Exception ex)
        {
            showWarning($"Failed to reload profileInfo! {ex.Message}");
            showWarning(profileData);
        }
        _isLoadingProfiles = false;
        StateHasChanged();
    }
    private async Task RefreshAsync()
    {
        await GetProfileInfoAsync();
    }
    private void showInfo(string message)
    {
        showMessage(message, Severity.Info);
    }
    private void showWarning(string message)
    {
        showMessage(message, Severity.Warning);
    }
    private void showMessage(string message, Severity severity)
    {
        Snackbar.Add(
            message,
            severity,
            static options =>
            {
                options.ShowCloseIcon = true;
                options.VisibleStateDuration = 10_000;
            });
    }
    public void Dispose() => _cancellationTokenSource.Cancel();
}
