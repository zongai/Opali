using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Opaline.Core.Models;

namespace Opaline.App.Controls;

public sealed partial class VideoCard : UserControl
{
    public static readonly DependencyProperty VideoProperty =
        DependencyProperty.Register(
            nameof(Video),
            typeof(Video),
            typeof(VideoCard),
            new PropertyMetadata(null, OnVideoChanged));

    public Video? Video
    {
        get => (Video?)GetValue(VideoProperty);
        set => SetValue(VideoProperty, value);
    }

    public VideoCard()
    {
        InitializeComponent();
    }

    private static void OnVideoChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is VideoCard card && e.NewValue is Video v)
            card.Bind(v);
    }

    private void Bind(Video v)
    {
        TitleText.Text = v.Title;
        ChannelText.Text = v.ChannelTitle ?? string.Empty;
        ViewsText.Text = v.FormattedViewCount;
        DurationText.Text = v.FormattedDuration;
        DurationText.Visibility = string.IsNullOrEmpty(v.FormattedDuration)
            ? Visibility.Collapsed
            : Visibility.Visible;

        if (!string.IsNullOrEmpty(v.ThumbnailUrl))
        {
            try
            {
                ThumbImage.Source = new BitmapImage(new Uri(v.ThumbnailUrl));
            }
            catch
            {
                ThumbImage.Source = null;
            }
        }
    }

    private void Root_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border b)
            b.BorderBrush = (Brush)Application.Current.Resources["OpalineAccentBrush"];
    }

    private void Root_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border b)
            b.BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"];
    }
}
