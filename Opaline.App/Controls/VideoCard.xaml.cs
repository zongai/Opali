using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Opaline.Core.Models;
using Opaline.Core.Services;

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

    /// <summary>Raised when feedback removes the card from a list.</summary>
    public event EventHandler<Video>? FeedbackApplied;
    /// <summary>Global: video id hidden after feedback (lists subscribe).</summary>
    public static event Action<string>? VideoHiddenByFeedback;


    private Brush? _defaultBorderBrush;

    public VideoCard()
    {
        InitializeComponent();
        Loaded += (_, _) => { _defaultBorderBrush = RootBorder.BorderBrush; };
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
        DurationBadge.Visibility = string.IsNullOrEmpty(v.FormattedDuration)
            ? Visibility.Collapsed : Visibility.Visible;
        AutomationProperties.SetName(this, v.Title);
        if (!string.IsNullOrEmpty(v.ThumbnailUrl))
        {
            try { ThumbImage.Source = new BitmapImage(new Uri(v.ThumbnailUrl)); }
            catch { ThumbImage.Source = null; }
        }
        else ThumbImage.Source = null;
    }

    private async void MenuButton_Click(object sender, RoutedEventArgs e)
    {
        if (Video is null) return;
        var flyout = new MenuFlyout();

        var notInt = Video.FeedbackActions.FirstOrDefault(a => a.Kind == "not_interested")
            ?? Video.FeedbackActions.FirstOrDefault(a => a.Label.Contains("不感兴趣") || a.Label.Contains("Not interested", StringComparison.OrdinalIgnoreCase));
        var noCh = Video.FeedbackActions.FirstOrDefault(a => a.Kind == "dont_recommend_channel")
            ?? Video.FeedbackActions.FirstOrDefault(a => a.Label.Contains("不推荐") || a.Label.Contains("channel", StringComparison.OrdinalIgnoreCase));

        var mi1 = new MenuFlyoutItem { Text = notInt?.Label ?? "不感兴趣" };
        mi1.Click += async (_, _) => await SendFeedbackAsync(notInt, "不感兴趣");
        flyout.Items.Add(mi1);

        var mi2 = new MenuFlyoutItem { Text = noCh?.Label ?? "不推荐该频道" };
        mi2.Click += async (_, _) => await SendFeedbackAsync(noCh, "不推荐该频道");
        flyout.Items.Add(mi2);

        foreach (var a in Video.FeedbackActions)
        {
            if (a == notInt || a == noCh) continue;
            if (string.IsNullOrEmpty(a.Token)) continue;
            var mi = new MenuFlyoutItem { Text = a.Label };
            var token = a.Token;
            var label = a.Label;
            mi.Click += async (_, _) => await SendFeedbackAsync(a, label);
            flyout.Items.Add(mi);
        }

        flyout.ShowAt(MenuButton);
    }

    private async Task SendFeedbackAsync(FeedbackAction? action, string fallbackLabel)
    {
        if (Video is null) return;
        if (action is null || string.IsNullOrEmpty(action.Token))
        {
            // No token from feed — still remove locally and hint login
            FeedbackApplied?.Invoke(this, Video);
            VideoHiddenByFeedback?.Invoke(Video.Id);
            return;
        }
        try
        {
            var yt = App.Services.GetRequiredService<IYouTubeService>();
            var ok = await yt.SendFeedbackAsync(action.Token);
            if (ok)
            {
                FeedbackApplied?.Invoke(this, Video);
                VideoHiddenByFeedback?.Invoke(Video.Id);
            }
        }
        catch
        {
            // swallow — toast optional
        }
    }

    private void Root_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (Application.Current.Resources.TryGetValue("OpalineAccentBrush", out var brush)
            && brush is Brush b)
            RootBorder.BorderBrush = b;
    }

    private void Root_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        RootBorder.BorderBrush = _defaultBorderBrush
            ?? (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"];
    }
}
