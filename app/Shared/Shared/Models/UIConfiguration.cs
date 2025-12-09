// Copyright (c) Microsoft. All rights reserved.

using System.Text.Json.Serialization;

namespace Shared.Models;

/// <summary>
/// UI Configuration model for client-side theme and behavior settings
/// </summary>
public class UIConfiguration
{
    /// <summary>
    /// Primary color for the light theme
    /// </summary>
    [JsonPropertyName("colorPaletteLightPrimary")]
    public string ColorPaletteLightPrimary { get; set; } = "#84B1CB";

    /// <summary>
    /// Secondary color for the light theme
    /// </summary>
    [JsonPropertyName("colorPaletteLightSecondary")]
    public string ColorPaletteLightSecondary { get; set; } = "#287FA4";

    /// <summary>
    /// App bar background color
    /// </summary>
    [JsonPropertyName("colorPaletteLightAppbarBackground")]
    public string ColorPaletteLightAppbarBackground { get; set; } = "#84B1CB";

    /// <summary>
    /// Logo image path
    /// </summary>
    [JsonPropertyName("logoImagePath")]
    public string LogoImagePath { get; set; } = "icon-512.png";

    /// <summary>
    /// Logo image width
    /// </summary>
    [JsonPropertyName("logoImageWidth")]
    public int LogoImageWidth { get; set; } = 150;

    /// <summary>
    /// Welcome/Hello text displayed to users
    /// </summary>
    [JsonPropertyName("helloText")]
    public string HelloText { get; set; } = "How can I help you today?";

    /// <summary>
    /// Whether to show sample questions
    /// </summary>
    [JsonPropertyName("showSampleQuestions")]
    public bool ShowSampleQuestions { get; set; } = true;

    /// <summary>
    /// Whether to show premium AOAI toggle selection
    /// </summary>
    [JsonPropertyName("showPremiumAOAIToggleSelection")]
    public bool ShowPremiumAOAIToggleSelection { get; set; } = true;

    /// <summary>
    /// Disclaimer message to display
    /// </summary>
    [JsonPropertyName("disclaimerMessage")]
    public string DisclaimerMessage { get; set; } = string.Empty;

    /// <summary>
    /// Comma-delimited list of enabled navigation paths (e.g., "chat,documents,history")
    /// If empty or null, defaults to "chat" only
    /// </summary>
    [JsonPropertyName("enabledNavigationPaths")]
    public string? EnabledNavigationPaths { get; set; }
}
