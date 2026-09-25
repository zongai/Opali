using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Opaline.App.Services;
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

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.Load();
    }

    private void Selector_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        var tag = (sender.SelectedItem as SelectorBarItem)?.Tag as string;
        HistoryGrid.Visibility = tag == "later" ? Visibility.Collapsed : Visibility.Visible;
        LaterGrid.Visibility = tag == "later" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Grid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Video video)
        {
            var nav = App.Services.GetRequiredService<INavigationService>();
            nav.Navigate(typeof(WatchPage), video.Id);
        }
    }
}
