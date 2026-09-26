using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Opaline.App.Services;
using Opaline.Core.Config;
using Opaline.Core.Playback;
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
    [ObservableProperty] private string solverBaseUrl = "";
    [ObservableProperty] private string botGuardStatus = "未初始化";
    [ObservableProperty] private bool preferAv1 = true;
    [ObservableProperty] private bool autoDubEnabled = true;
    [ObservableProperty] private bool ignoreAiDubs = true;
    [ObservableProperty] private string autoDubLanguage = "";

    public SettingsViewModel(
        IThemeService theme,
        SponsorBlockService sb,
        ReturnYouTubeDislikeService ryd,
        PoTokenService poToken)
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
        PreferAv1 = Av1Support.IsPreferred;
        AutoDubEnabled = AutoDubPreference.IsEnabled;
        IgnoreAiDubs = AutoDubPreference.IgnoreAiDubs;
        AutoDubLanguage = AutoDubPreference.LanguageOverride ?? "";
        BotGuardStatus = poToken.HasLocalBotGuard
            ? "WebView2 BotGuard 已挂载（失败时回退远程 /get_pot）"
            : "仅远程 /get_pot（启动后将尝试挂载 WebView2）";
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

    partial void OnPreferAv1Changed(bool value) => Av1Support.IsPreferred = value;
    partial void OnAutoDubEnabledChanged(bool value) => AutoDubPreference.IsEnabled = value;
    partial void OnIgnoreAiDubsChanged(bool value) => AutoDubPreference.IgnoreAiDubs = value;
    partial void OnAutoDubLanguageChanged(string value)
        => AutoDubPreference.LanguageOverride = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
