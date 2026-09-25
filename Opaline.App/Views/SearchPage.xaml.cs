using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Opaline.App.Services;
using Opaline.App.ViewModels;
using Opaline.Core.Models;

namespace Opaline.App.Views;

public sealed partial class SearchPage : Page
{
    public SearchViewModel ViewModel { get; }

    public SearchPage()
    {
        ViewModel = App.Services.GetRequiredService<SearchViewModel>();
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is string query)
            await ViewModel.SearchAsync(query);
    }

    private void Results_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Video video)
        {
            var nav = App.Services.GetRequiredService<INavigationService>();
            nav.Navigate(typeof(WatchPage), video.Id);
        }
    }
}
