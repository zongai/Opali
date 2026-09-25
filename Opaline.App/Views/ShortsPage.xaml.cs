using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Opaline.App.Services;
using Opaline.App.ViewModels;
using Opaline.Core.Models;

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

    private void Shorts_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Video video)
            App.Services.GetRequiredService<INavigationService>().Navigate(typeof(WatchPage), video.Id);
    }
}
