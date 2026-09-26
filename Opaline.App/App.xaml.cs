using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Opaline.Core.Api;
using Opaline.Core.Auth;
using Opaline.Core.Playback;
using Opaline.Core.Services;
using Opaline.Core.Services.Translation;
using Opaline.Core.Services.Ryd;
using Opaline.Core.Services.SponsorBlock;
using Opaline.Core.Storage;
using Opaline.App.Services;
using Opaline.App.ViewModels;

namespace Opaline.App;

public partial class App : Application
{
    private Window? _window;
    public static Window? MainWindow { get; private set; }
    public static IServiceProvider Services { get; private set; } = null!;

    public App()
    {
        // Capture anything that would otherwise silent-exit
        UnhandledException += (_, e) =>
        {
            CrashLog.Write("App.UnhandledException", e.Exception);
            e.Handled = true; // try to keep process alive long enough to show UI
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            CrashLog.Write("AppDomain.UnhandledException",
                e.ExceptionObject as Exception);
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            CrashLog.Write("TaskScheduler.UnobservedTaskException", e.Exception);
            e.SetObserved();
        };

        try
        {
            InitializeComponent();
            Services = ConfigureServices();
        }
        catch (Exception ex)
        {
            CrashLog.Write("App.ctor", ex);
            throw;
        }
    }

    private static IServiceProvider ConfigureServices()
    {
        var sc = new ServiceCollection();

        // Separate clients: shared DefaultRequestHeaders on one instance is racy
        sc.AddSingleton(_ =>
        {
            var http = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
            return http;
        });

        sc.AddSingleton<ITokenStore, FileTokenStore>();
        sc.AddSingleton(sp => new OAuthClient(
            sp.GetRequiredService<HttpClient>(),
            sp.GetRequiredService<ITokenStore>()));
        sc.AddSingleton(sp =>
        {
            // Fresh HttpClient for Innertube so User-Agent headers do not clash with OAuth
            var http = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
            var client = new InnertubeClient(http, ClientIdentity.Android);
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
        sc.AddSingleton<IDownloadService, DownloadService>();
        sc.AddSingleton<TranslationService>();
        sc.AddSingleton<WatchHistoryStore>();
        sc.AddSingleton<PlaybackQueue>();
        sc.AddSingleton<INavigationService, NavigationService>();
        sc.AddSingleton<IThemeService, ThemeService>();
        sc.AddTransient<ShellViewModel>();
        sc.AddTransient<HomeViewModel>();
        sc.AddTransient<SearchViewModel>();
        sc.AddTransient<WatchViewModel>();
        sc.AddTransient<ChannelViewModel>();
        sc.AddTransient<SubscriptionsViewModel>();
        sc.AddTransient<LibraryViewModel>();
        sc.AddTransient<ShortsViewModel>();
        sc.AddTransient<SettingsViewModel>();
        sc.AddTransient<AuthViewModel>();
        return sc.BuildServiceProvider();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            _window = new MainWindow();
            MainWindow = _window;
            _window.Activate();
            _ = Av1HardwareProbe.ProbeAsync();


            // WebView2 BotGuard local pot (async; failure → remote /get_pot)
            try
            {
                var dq = _window.DispatcherQueue;
                var minter = new WebView2BotGuardMinter(dq);
                Services.GetRequiredService<PoTokenService>().AttachBotGuard(minter);
                _ = minter.EnsureInitializedAsync();
            }
            catch (Exception bgEx)
            {
                CrashLog.Write("BotGuard.Attach", bgEx);
            }
        }
        catch (Exception ex)
        {
            CrashLog.Write("OnLaunched", ex);
            // Last-ditch: try a bare window so the user sees something
            try
            {
                var fallback = new Window();
                fallback.Content = new TextBlock
                {
                    Text = "Opaline failed to start.\n\n" + ex.Message +
                           "\n\nDetails: crash.log (next to Opaline.App.exe)\n" + CrashLog.LogPath,
                    TextWrapping = TextWrapping.WrapWholeWords,
                    Margin = new Thickness(24),
                };
                fallback.Activate();
                _window = fallback;
            }
            catch (Exception ex2)
            {
                CrashLog.Write("OnLaunched.fallback", ex2);
                throw;
            }
        }
    }
}
