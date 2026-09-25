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
using Windows.Graphics;

namespace Opaline.App;

public sealed partial class MainWindow : Window
{
    public ShellViewModel ViewModel { get; }

    private readonly INavigationService _nav;
    private readonly IYouTubeService? _yt; // resolved lazily for suggestions

    public MainWindow()
    {
        ViewModel = App.Services.GetRequiredService<ShellViewModel>();
        _nav = App.Services.GetRequiredService<INavigationService>();

        InitializeComponent();
        ExtendsContentIntoTitleBar = true;

        // Attach theme service to this window
        App.Services.GetRequiredService<IThemeService>().Attach(this);

        // Size window ~1280×800 DIP
        var hwnd = Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        var scale = GetDpiForWindow(hwnd) / 96.0;
        AppWindow.Resize(new SizeInt32((int)(1280 * scale), (int)(800 * scale)));

        _nav.Initialize(ContentFrame);
        ContentFrame.Navigated += ContentFrame_Navigated;
    }

    private void NavView_Loaded(object sender, RoutedEventArgs e)
    {
        // Default to Home
        NavView.SelectedItem = NavView.MenuItems[0];
        _nav.Navigate(typeof(HomePage));
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
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
            var yt = App.Services.GetRequiredService<Opaline.Core.Services.IYouTubeService>();
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
}
