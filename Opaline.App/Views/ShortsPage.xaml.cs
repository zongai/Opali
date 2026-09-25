using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Opaline.App.Services;
using Opaline.App.ViewModels;

namespace Opaline.App.Views;

public sealed partial class ShortsPage : Page
{
    public ShortsViewModel ViewModel { get; }

    public ShortsPage()
    {
        ViewModel = App.Services.GetRequiredService<ShortsViewModel>();
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (ViewModel.Shorts.Count == 0)
            await ViewModel.LoadAsync();
    }

    private async void ShortsFlip_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ShortsFlip.SelectedIndex >= ViewModel.Shorts.Count - 2)
            await ViewModel.LoadMoreAsync();
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string id })
        {
            var nav = App.Services.GetRequiredService<INavigationService>();
            nav.Navigate(typeof(WatchPage), id);
        }
    }
}
