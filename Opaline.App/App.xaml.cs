using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Opaline.Core.Api;
using Opaline.Core.Auth;
using Opaline.Core.Playback;
using Opaline.Core.Services;
using Opaline.Core.Services.Ryd;
using Opaline.Core.Services.SponsorBlock;
using Opaline.Core.Storage;
using Opaline.App.Services;
using Opaline.App.ViewModels;

namespace Opaline.App;

public partial class App : Application
{
    private Window? _window;
    public static IServiceProvider Services { get; private set; } = null!;

    public App()
    {
        InitializeComponent();
        Services = ConfigureServices();
    }

    private static IServiceProvider ConfigureServices()
    {
        var sc = new ServiceCollection();
        sc.AddSingleton(_ => new HttpClient { Timeout = TimeSpan.FromSeconds(45) });
        sc.AddSingleton<ITokenStore, FileTokenStore>();
        sc.AddSingleton(sp => new OAuthClient(sp.GetRequiredService<HttpClient>(), sp.GetRequiredService<ITokenStore>()));
        sc.AddSingleton(sp =>
        {
            var client = new InnertubeClient(sp.GetRequiredService<HttpClient>(), ClientIdentity.Android);
            client.AttachAuth(sp.GetRequiredService<OAuthClient>());
            return client;
        });
        sc.AddSingleton<IYouTubeService, YouTubeService>();
        sc.AddSingleton<SignatureTimestampService>();
        sc.AddSingleton<SignatureSolverService>();
        sc.AddSingleton<NSolverService>();
        sc.AddSingleton<PoTokenService>();
        sc.AddSingleton<StreamUrlResolver>();
        sc.AddSingleton<PlaybackService>();
        sc.AddSingleton<SponsorBlockService>();
        sc.AddSingleton<ReturnYouTubeDislikeService>();
        sc.AddSingleton<WatchHistoryStore>();
        sc.AddSingleton<INavigationService, NavigationService>();
        sc.AddSingleton<IThemeService, ThemeService>();
        sc.AddTransient<ShellViewModel>();
        sc.AddTransient<HomeViewModel>();
        sc.AddTransient<SearchViewModel>();
        sc.AddTransient<WatchViewModel>();
        sc.AddTransient<SubscriptionsViewModel>();
        sc.AddTransient<LibraryViewModel>();
        sc.AddTransient<ShortsViewModel>();
        sc.AddTransient<SettingsViewModel>();
        sc.AddTransient<AuthViewModel>();
        return sc.BuildServiceProvider();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }
}
