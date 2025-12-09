// Copyright (c) Microsoft. All rights reserved.

using System.Timers;

namespace SmartFlow.UI.Client.Pages;

public sealed partial class VoiceChat : IDisposable
{
    private string? _errorMessage;
    private string _connectionStatus = "Disconnected";
    private bool _isConnected = false;
    private bool _isConnecting = false;
    private bool _isListening = false;
    private bool _hasAudioData = false;
    private string _sessionDuration = "00:00";
    private List<TranscriptMessage> _transcript = new();
    private System.Timers.Timer? _sessionTimer;
    private DateTime _sessionStartTime;
    private IJSObjectReference? _voiceLiveModule;

    [Inject] public required ApiClient ApiClient { get; set; }
    [Inject] public required IJSRuntime JSRuntime { get; set; }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            try
            {
                _voiceLiveModule = await JSRuntime.InvokeAsync<IJSObjectReference>(
                    "import", "./js/voiceLive.js");
            }
            catch (Exception ex)
            {
                _errorMessage = $"Failed to load Voice Live module: {ex.Message}";
                StateHasChanged();
            }
        }
    }

    private async Task StartVoiceChatAsync()
    {
        try
        {
            _isConnecting = true;
            _errorMessage = null;
            _connectionStatus = "Connecting...";
            StateHasChanged();

            Console.WriteLine("Starting Voice Chat...");

            // Get authentication token from server
            var tokenResponse = await ApiClient.GetVoiceLiveTokenAsync();
            
            if (tokenResponse == null)
            {
                throw new Exception("Failed to get Voice Live authentication token");
            }

            Console.WriteLine($"Token received - Project: {tokenResponse.ProjectName}, Agent: {tokenResponse.AgentId}");

            // Initialize Voice Live WebSocket connection via JavaScript
            if (_voiceLiveModule != null)
            {
                var dotNetRef = DotNetObjectReference.Create(this);
                
                Console.WriteLine("Calling JavaScript initialize...");
                
                await _voiceLiveModule.InvokeVoidAsync("initialize", 
                    tokenResponse.WebSocketUrl,
                    tokenResponse.ApiVersion,
                    tokenResponse.ProjectName,
                    tokenResponse.AgentId,
                    tokenResponse.AgentAccessToken,
                    tokenResponse.AuthorizationToken,
                    tokenResponse.SpeechKey,
                    dotNetRef);

                Console.WriteLine("JavaScript initialize called successfully");
                
                // Note: Don't set _isConnected = true here
                // Wait for the OnConnectionOpened callback from JavaScript
                // For now, we'll wait a bit to see if connection succeeds
                await Task.Delay(1000);
                
                // Check if we got an error
                if (_connectionStatus == "Error")
                {
                    throw new Exception(_errorMessage ?? "Connection failed");
                }
            }
            else
            {
                throw new Exception("Voice Live module not loaded");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in StartVoiceChatAsync: {ex}");
            _errorMessage = $"Failed to start voice chat: {ex.Message}";
            _connectionStatus = "Error";
            _isConnected = false;
        }
        finally
        {
            _isConnecting = false;
            StateHasChanged();
        }
    }

    private async Task StopVoiceChatAsync()
    {
        try
        {
            if (_voiceLiveModule != null)
            {
                await _voiceLiveModule.InvokeVoidAsync("disconnect");
            }

            _isConnected = false;
            _isListening = false;
            _hasAudioData = false;
            _connectionStatus = "Disconnected";
            StopSessionTimer();
            StateHasChanged();
        }
        catch (Exception ex)
        {
            _errorMessage = $"Error stopping voice chat: {ex.Message}";
            StateHasChanged();
        }
    }

    private async Task ToggleListeningAsync()
    {
        try
        {
            if (_voiceLiveModule == null) return;

            if (_isListening)
            {
                // Stop listening and automatically send the audio
                await _voiceLiveModule.InvokeVoidAsync("stopListening");
                _isListening = false;
                
                // Automatically send the audio
                if (_hasAudioData)
                {
                    await _voiceLiveModule.InvokeVoidAsync("sendAudio");
                    _hasAudioData = false;
                }
            }
            else
            {
                await _voiceLiveModule.InvokeVoidAsync("startListening");
                _isListening = true;
                _hasAudioData = false;
            }
            StateHasChanged();
        }
        catch (Exception ex)
        {
            _errorMessage = $"Error toggling microphone: {ex.Message}";
            StateHasChanged();
        }
    }

    private async Task SendAudioAsync()
    {
        try
        {
            if (_voiceLiveModule == null) return;

            await _voiceLiveModule.InvokeVoidAsync("sendAudio");
            _isListening = false;
            _hasAudioData = false;
            StateHasChanged();
        }
        catch (Exception ex)
        {
            _errorMessage = $"Error sending audio: {ex.Message}";
            StateHasChanged();
        }
    }

    [JSInvokable]
    public void OnUserTranscript(string text)
    {
        _transcript.Add(new TranscriptMessage
        {
            Text = text,
            IsUser = true,
            Timestamp = DateTime.Now
        });
        _hasAudioData = true;
        StateHasChanged();
    }

    [JSInvokable]
    public void OnConnectionOpened()
    {
        Console.WriteLine("WebSocket connection opened!");
        _isConnected = true;
        _connectionStatus = "Connected";
        _sessionStartTime = DateTime.Now;
        StartSessionTimer();
        StateHasChanged();
    }

    [JSInvokable]
    public void OnAgentResponse(string text)
    {
        _transcript.Add(new TranscriptMessage
        {
            Text = text,
            IsUser = false,
            Timestamp = DateTime.Now
        });
        StateHasChanged();
    }

    [JSInvokable]
    public void OnError(string error)
    {
        _errorMessage = error;
        _connectionStatus = "Error";
        StateHasChanged();
    }

    [JSInvokable]
    public void OnConnectionClosed()
    {
        _isConnected = false;
        _isListening = false;
        _connectionStatus = "Disconnected";
        StopSessionTimer();
        StateHasChanged();
    }

    private void StartSessionTimer()
    {
        _sessionTimer = new System.Timers.Timer(1000);
        _sessionTimer.Elapsed += UpdateSessionDuration;
        _sessionTimer.Start();
    }

    private void StopSessionTimer()
    {
        if (_sessionTimer != null)
        {
            _sessionTimer.Stop();
            _sessionTimer.Dispose();
            _sessionTimer = null;
        }
        _sessionDuration = "00:00";
    }

    private void UpdateSessionDuration(object? sender, ElapsedEventArgs e)
    {
        var duration = DateTime.Now - _sessionStartTime;
        _sessionDuration = $"{duration.Minutes:D2}:{duration.Seconds:D2}";
        InvokeAsync(StateHasChanged);
    }

    private Color GetStatusColor()
    {
        return _connectionStatus switch
        {
            "Connected" => Color.Success,
            "Connecting..." => Color.Info,
            "Error" => Color.Error,
            _ => Color.Default
        };
    }

    public void Dispose()
    {
        StopSessionTimer();
        _voiceLiveModule?.DisposeAsync();
    }

    private class TranscriptMessage
    {
        public required string Text { get; set; }
        public required bool IsUser { get; set; }
        public required DateTime Timestamp { get; set; }
    }
}
