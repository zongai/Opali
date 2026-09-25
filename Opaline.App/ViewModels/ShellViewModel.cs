using CommunityToolkit.Mvvm.ComponentModel;

namespace Opaline.App.ViewModels;

public partial class ShellViewModel : ObservableObject
{
    [ObservableProperty]
    private bool canGoBack;
}
