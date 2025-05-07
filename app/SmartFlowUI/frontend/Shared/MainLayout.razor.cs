﻿// Copyright (c) Microsoft. All rights reserved.

using static System.Net.WebRequestMethods;

namespace ClientApp.Shared;

public sealed partial class MainLayout
{
    private readonly MudTheme _theme = new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = AppConfiguration.ColorPaletteLightPrimary,
            AppbarBackground = AppConfiguration.ColorPaletteLightAppbarBackground,
            Secondary = AppConfiguration.ColorPaletteLightSecondary
        },
        PaletteDark = new PaletteDark
        {
            Primary = "#1277bd",
        }
    };
    private bool _drawerOpen = false;
    private bool _settingsOpen = false;
    private SettingsPanel? _settingsPanel;

    private bool _isDarkTheme
    {
        // FAILS get => LocalStorage.GetItem<bool>(StorageKeys.PrefersDarkTheme); // FAILS!
        // FAILS get => GetLocalBool(StorageKeys.PrefersDarkTheme, false);        // FAILS!
        get => false; // WORKS
        set => LocalStorage.SetItem<bool>(StorageKeys.PrefersDarkTheme, value);
    }

    private bool _isReversed
    {
        // FAILS get => LocalStorage.GetItem<bool?>(StorageKeys.PrefersReversedConversationSorting) ?? true; // FAILS!
        // FAILS get => GetLocalBool(StorageKeys.PrefersReversedConversationSorting, false);                 // FAILS!
        get => false; // WORKS
        set => LocalStorage.SetItem<bool>(StorageKeys.PrefersReversedConversationSorting, value);
    }

    // // this also fails... why...???
    // private bool GetLocalBool(string settingName, bool defaultValue = false)
    // {
    //     try
    //     {
    //         if (LocalStorage != null)
    //         {
    //             return LocalStorage.GetItem<bool?>(settingName) ?? defaultValue;
    //         }
    //         return defaultValue;
    //     }
    //     catch (Exception ex)
    //     {
    //         Debug.WriteLine($"Error getting setting {settingName}: {ex.Message}");
    //         return defaultValue;
    //     }
    // }

    private bool _isRightToLeft =>
        Thread.CurrentThread.CurrentUICulture is { TextInfo.IsRightToLeft: true };

    [Inject] public required NavigationManager Nav { get; set; }
    [Inject] public required ILocalStorageService LocalStorage { get; set; }
    [Inject] public required IDialogService Dialog { get; set; }

    private bool SettingsDisabled => new Uri(Nav.Uri).Segments.LastOrDefault() switch
    {
        "ask" or "chat" => false,
        _ => true
    };

    private string LogoImagePath
    {
        get
        {
            return AppConfiguration.LogoImagePath;
        }
    }

    private int LogoImageWidth
    {
        get
        {
            return AppConfiguration.LogoImageWidth;
        }
    }

    private bool SortDisabled
    {
        get
        {
            return true;
            //return new Uri(Nav.Uri).Segments.LastOrDefault() switch
            //{
            //    "documents" => true,
            //    _ => false
            //};
        }
    }

    private void OnMenuClicked() => _drawerOpen = !_drawerOpen;

    private void OnThemeChanged() => _isDarkTheme = !_isDarkTheme;

    private void OnIsReversedChanged() => _isReversed = !_isReversed;
}
