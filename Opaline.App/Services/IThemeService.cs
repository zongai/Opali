using Microsoft.UI.Xaml;

namespace Opaline.App.Services;

public interface IThemeService
{
    ElementTheme CurrentTheme { get; }
    void Attach(Window window);
    void SetTheme(ElementTheme theme);
}
