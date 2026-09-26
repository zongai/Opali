using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Opaline.App.ViewModels;
using Opaline.Core.Models;

namespace Opaline.App.Views;

public sealed partial class ChannelPage : Page
{
    public ChannelViewModel ViewModel { get; }

    public ChannelPage()
    {
        ViewModel = App.Services.GetRequiredService<ChannelViewModel>();
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is string id)
            await ViewModel.LoadAsync(id);
    }

    private void Grid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Video v)
            Frame.Navigate(typeof(WatchPage), v.Id);
    }
}
