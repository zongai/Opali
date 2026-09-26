using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Opaline.App.Services;
using Opaline.App.ViewModels;
using Opaline.App.Views;
using Opaline.Core.Services;
using Windows.Graphics;

namespace Opaline.App;

public sealed partial class MainWindow : Window
{
    public ShellViewModel ViewModel { get; }

    private readonly INavigationService _nav;
    private readonly PlaybackQueue _queue;
    private bool _navReady;

    public MainWindow()
    {
        try
        {
            ViewModel = App.Services.GetRequiredService<ShellViewModel>();
            _nav = App.Services.GetRequiredService<INavigationService>();
            _queue = App.Services.GetRequiredService<PlaybackQueue>();
            _queue.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(PlaybackQueue.IsMiniPlayerVisible) or nameof(PlaybackQueue.NowPlaying))
                    DispatcherQueue.TryEnqueue(UpdateMiniPlayer);
            };

            InitializeComponent();

            try { ExtendsContentIntoTitleBar = true; }
            catch (Exception ex) { CrashLog.Write("MainWindow.ExtendsContentIntoTitleBar", ex); }

            try { App.Services.GetRequiredService<IThemeService>().Attach(this); }
            catch (Exception ex) { CrashLog.Write("MainWindow.ThemeAttach", ex); }

            try
            {
                var hwnd = Win32Interop.GetWindowFromWindowId(AppWindow.Id);
                if (hwnd != IntPtr.Zero)
                {
                    var dpi = GetDpiForWindow(hwnd);
                    var scale = dpi > 0 ? dpi / 96.0 : 1.0;
                    AppWindow.Resize(new SizeInt32((int)(1020 * scale), (int)(680 * scale)));
                }
            }
            catch (Exception ex) { CrashLog.Write("MainWindow.Resize", ex); }

            _nav.Initialize(ContentFrame);
            ContentFrame.Navigated += ContentFrame_Navigated;
        }
        catch (Exception ex)
        {
            CrashLog.Write("MainWindow.ctor", ex);
            throw;
        }
    }

    private void NavView_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _navReady = true;
            // Selecting first item triggers SelectionChanged → single navigate
            if (NavView.MenuItems.Count > 0)
                NavView.SelectedItem = NavView.MenuItems[0];
            else
                _nav.Navigate(typeof(HomePage));
        }
        catch (Exception ex)
        {
            CrashLog.Write("NavView_Loaded", ex);
        }
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (!_navReady) return;
        try
        {
            if (args.IsSettingsSelected)
            {
                _nav.Navigate(typeof(SettingsPage));
                return;
            }

            if (args.SelectedItem is NavigationViewItem item && item.Tag is string tag)
            {
                switch (tag)
                {
                    case "home":
                        _nav.Navigate(typeof(HomePage));
                        break;
                    case "shorts":
                        _nav.Navigate(typeof(ShortsPage));
                        break;
                    case "subscriptions":
                        _nav.Navigate(typeof(SubscriptionsPage));
                        break;
                    case "library":
                        _nav.Navigate(typeof(LibraryPage));
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            CrashLog.Write("NavView_SelectionChanged", ex);
        }
    }

    private void NavView_BackRequested(NavigationView sender, NavigationViewBackRequestedEventArgs args)
    {
        if (ContentFrame.CanGoBack)
            ContentFrame.GoBack();
    }

    private void ContentFrame_Navigated(object sender, NavigationEventArgs e)
    {
        ViewModel.CanGoBack = ContentFrame.CanGoBack;
    }

    private async void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
        var q = sender.Text?.Trim();
        if (string.IsNullOrEmpty(q) || q.Length < 2)
        {
            sender.ItemsSource = null;
            return;
        }

        try
        {
            var yt = App.Services.GetRequiredService<IYouTubeService>();
            var suggestions = await yt.GetSuggestionsAsync(q);
            sender.ItemsSource = suggestions;
        }
        catch
        {
            sender.ItemsSource = null;
        }
    }

    private void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        var query = args.QueryText?.Trim();
        if (string.IsNullOrEmpty(query)) return;
        _nav.Navigate(typeof(SearchPage), query);
    }

    private void SearchBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args.SelectedItem is string s)
            sender.Text = s;
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);

    private void UpdateMiniPlayer()
    {
        try
        {
            MiniPlayerBar.Visibility = _queue.IsMiniPlayerVisible
                ? Microsoft.UI.Xaml.Visibility.Visible
                : Microsoft.UI.Xaml.Visibility.Collapsed;
            MiniTitle.Text = _queue.NowPlaying?.Title ?? "";
        }
        catch (Exception ex) { CrashLog.Write("UpdateMiniPlayer", ex); }
    }

    private void MiniOpen_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (_queue.NowPlaying is { } v)
            _nav.Navigate(typeof(WatchPage), v.Id);
    }

    private void MiniNext_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var next = _queue.PlayNext();
        if (next is not null)
            _nav.Navigate(typeof(WatchPage), next.Id);
    }

    private void MiniClose_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        => _queue.Clear();
}
