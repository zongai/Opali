using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace Opaline.App.Services;

public sealed class NavigationService : INavigationService
{
    private Frame? _frame;

    public void Initialize(Frame frame) => _frame = frame;

    public bool CanGoBack => _frame?.CanGoBack ?? false;

    public void Navigate(Type pageType, object? parameter = null)
    {
        _frame?.Navigate(pageType, parameter, new EntranceNavigationTransitionInfo());
    }

    public void GoBack()
    {
        if (_frame?.CanGoBack == true)
            _frame.GoBack();
    }
}
