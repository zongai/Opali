using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Opaline.Core.Models;
using Opaline.Core.Services;

namespace Opaline.App.Views;

public sealed partial class PlaylistPage : Page
{
    public PlaylistPage() => InitializeComponent();

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is not string id) return;
        try
        {
            var yt = App.Services.GetRequiredService<IYouTubeService>();
            var page = await yt.GetPlaylistAsync(id);
            TitleBlock.Text = page.Playlist.Title;
            VideosGrid.ItemsSource = page.Videos;
        }
        catch (Exception ex)
        {
            TitleBlock.Text = $"Error: {ex.Message}";
        }
    }

    private void Grid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Video v)
            Frame.Navigate(typeof(WatchPage), v.Id);
    }
}
