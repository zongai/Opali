using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Opaline.App.Services;
using Opaline.App.ViewModels;
using Opaline.Core.Models;

namespace Opaline.App.Views;

public sealed partial class HomePage : Page
{
    public HomeViewModel ViewModel { get; }

    public HomePage()
    {
        ViewModel = App.Services.GetRequiredService<HomeViewModel>();
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (ViewModel.Videos.Count == 0)
            await ViewModel.LoadAsync();
    }

    private void VideoGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Video video)
        {
            var nav = App.Services.GetRequiredService<INavigationService>();
            nav.Navigate(typeof(WatchPage), video.Id);
        }
    }
}
