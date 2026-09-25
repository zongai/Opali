using Microsoft.UI.Xaml;

namespace Opaline.App.Services;

public sealed class ThemeService : IThemeService
{
    private Window? _window;

    public ElementTheme CurrentTheme { get; private set; } = ElementTheme.Default;

    public void Attach(Window window) => _window = window;

    public void SetTheme(ElementTheme theme)
    {
        CurrentTheme = theme;
        if (_window?.Content is FrameworkElement root)
            root.RequestedTheme = theme;
    }
}
