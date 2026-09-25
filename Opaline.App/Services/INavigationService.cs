using Microsoft.UI.Xaml.Controls;

namespace Opaline.App.Services;

public interface INavigationService
{
    void Initialize(Frame frame);
    void Navigate(Type pageType, object? parameter = null);
    bool CanGoBack { get; }
    void GoBack();
}
