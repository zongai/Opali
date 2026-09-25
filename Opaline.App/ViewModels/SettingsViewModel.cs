using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Opaline.App.Services;
using Opaline.Core.Config;
using Opaline.Core.Services.Ryd;
using Opaline.Core.Services.SponsorBlock;

namespace Opaline.App.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly IThemeService _theme;
    private readonly SponsorBlockService _sb;
    private readonly ReturnYouTubeDislikeService _ryd;

    [ObservableProperty] private int selectedThemeIndex;
    [ObservableProperty] private bool sponsorBlockEnabled;
    [ObservableProperty] private bool rydEnabled;
    [ObservableProperty] private string solverBaseUrl;

    public SettingsViewModel(
        IThemeService theme,
        SponsorBlockService sb,
        ReturnYouTubeDislikeService ryd)
    {
        _theme = theme;
        _sb = sb;
        _ryd = ryd;
        SelectedThemeIndex = theme.CurrentTheme switch
        {
            ElementTheme.Light => 1,
            ElementTheme.Dark => 2,
            _ => 0
        };
        SponsorBlockEnabled = sb.Enabled;
        RydEnabled = ryd.Enabled;
        SolverBaseUrl = AppUrls.SolverServer.BaseUrl;
    }

    partial void OnSelectedThemeIndexChanged(int value)
    {
        var theme = value switch
        {
            1 => ElementTheme.Light,
            2 => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
        _theme.SetTheme(theme);
    }

    partial void OnSponsorBlockEnabledChanged(bool value) => _sb.Enabled = value;
    partial void OnRydEnabledChanged(bool value) => _ryd.Enabled = value;
    partial void OnSolverBaseUrlChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            AppUrls.SolverServer.BaseUrl = value.Trim();
    }
}
