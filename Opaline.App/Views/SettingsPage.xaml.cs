using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Opaline.App.ViewModels;

namespace Opaline.App.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; }
    public AuthViewModel Auth { get; }

    public SettingsPage()
    {
        ViewModel = App.Services.GetRequiredService<SettingsViewModel>();
        Auth = App.Services.GetRequiredService<AuthViewModel>();
        InitializeComponent();
    }

    private async void SignIn_Click(object sender, RoutedEventArgs e)
        => await Auth.StartSignInAsync();

    private void SignOut_Click(object sender, RoutedEventArgs e)
        => Auth.SignOut();
}
