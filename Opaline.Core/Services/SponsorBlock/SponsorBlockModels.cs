namespace Opaline.Core.Services.SponsorBlock;

public enum SbCategory
{
    Sponsor,
    SelfPromo,
    ExclusiveAccess,
    Interaction,
    Highlight,
    Intro,
    Outro,
    Preview,
    Filler,
    MusicOfftopic,
    Chapter
}

public enum SbSkipBehavior
{
    Disabled,
    ShowButton,
    AutoSkip
}

public sealed class SponsorBlockSegment
{
    public string Uuid { get; init; }
    public SbCategory Category { get; init; }
    public double StartTime { get; init; }
    public double EndTime { get; init; }
    /// <summary>skip | poi | chapter | full</summary>
    public string ActionType { get; init; }
}

public static class SbCategoryExtensions
{
    public static string ToApiString(this SbCategory c) => c switch
    {
        SbCategory.Sponsor => "sponsor",
        SbCategory.SelfPromo => "selfpromo",
        SbCategory.ExclusiveAccess => "exclusive_access",
        SbCategory.Interaction => "interaction",
        SbCategory.Highlight => "highlight",
        SbCategory.Intro => "intro",
        SbCategory.Outro => "outro",
        SbCategory.Preview => "preview",
        SbCategory.Filler => "filler",
        SbCategory.MusicOfftopic => "music_offtopic",
        SbCategory.Chapter => "chapter",
        _ => "sponsor"
    };

    public static SbCategory? FromApiString(string? s) => s switch
    {
        "sponsor" => SbCategory.Sponsor,
        "selfpromo" => SbCategory.SelfPromo,
        "exclusive_access" => SbCategory.ExclusiveAccess,
        "interaction" => SbCategory.Interaction,
        "highlight" => SbCategory.Highlight,
        "intro" => SbCategory.Intro,
        "outro" => SbCategory.Outro,
        "preview" => SbCategory.Preview,
        "filler" => SbCategory.Filler,
        "music_offtopic" => SbCategory.MusicOfftopic,
        "chapter" => SbCategory.Chapter,
        _ => null
    };

    public static SbSkipBehavior DefaultBehavior(this SbCategory c) => c switch
    {
        SbCategory.Sponsor => SbSkipBehavior.AutoSkip,
        SbCategory.Chapter or SbCategory.ExclusiveAccess => SbSkipBehavior.Disabled,
        _ => SbSkipBehavior.ShowButton
    };

    public static string DisplayName(this SbCategory c) => c switch
    {
        SbCategory.Sponsor => "Sponsor",
        SbCategory.SelfPromo => "Self promo",
        SbCategory.ExclusiveAccess => "Exclusive access",
        SbCategory.Interaction => "Interaction reminder",
        SbCategory.Highlight => "Highlight",
        SbCategory.Intro => "Intro",
        SbCategory.Outro => "Outro",
        SbCategory.Preview => "Preview / Recap",
        SbCategory.Filler => "Filler",
        SbCategory.MusicOfftopic => "Music: non-music",
        SbCategory.Chapter => "Chapter",
        _ => c.ToString()
    };
}
