using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Opaline.App.ViewModels;
using Opaline.Core.Models;

namespace Opaline.App.Views;

public sealed partial class LibraryPage : Page
{
    public LibraryViewModel ViewModel { get; }

    public LibraryPage()
    {
        ViewModel = App.Services.GetRequiredService<LibraryViewModel>();
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.LoadAsync();
    }

    private void Video_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Video v)
            Frame.Navigate(typeof(WatchPage), v.Id);
    }

    private void Playlist_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Playlist p)
            Frame.Navigate(typeof(PlaylistPage), p.Id);
    }
}
