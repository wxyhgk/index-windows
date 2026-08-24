using System.Globalization;
using Index.Storage;

namespace Index.UI.Gallery;

/// <summary>
/// Semantic color roles understood by a gallery theme. The presentation model deliberately
/// carries no RGB values or WinUI brushes.
/// </summary>
public enum CardHeaderColorRole
{
    Screenshot,
    Recording,
    Note,
    Document,
    Code,
    Data,
    Neutral
}

[Flags]
public enum CardBadgeFlags
{
    None = 0,
    Favorite = 1 << 0,
    Category = 1 << 1,
    Annotated = 1 << 2,
    Recording = 1 << 3
}

/// <summary>
/// Type-specific defaults used to build a card. Additional content types can supply another
/// descriptor without teaching the card view about that type.
/// </summary>
public sealed record CardContentPresentation(
    string HeaderLabel,
    CardHeaderColorRole HeaderColorRole,
    string FallbackTitle,
    string? FixedSubtitle = null,
    bool UsesAppNameFallback = true)
{
    public static CardContentPresentation Screenshot { get; } = new(
        "截图",
        CardHeaderColorRole.Screenshot,
        "截图");

    public static CardContentPresentation Recording { get; } = new(
        "录屏",
        CardHeaderColorRole.Recording,
        "录屏",
        UsesAppNameFallback: false);
}

/// <summary>
/// Pure presentation model consumed by a gallery card. It resolves labels and badge state but
/// does not know about controls, brushes, files, or database access.
/// </summary>
public sealed record CardAppearance
{
    private CardAppearance(
        string headerLabel,
        CardHeaderColorRole headerColorRole,
        string captionTitle,
        string captionSubtitle,
        CardBadgeFlags badges,
        string? categoryLabel)
    {
        HeaderLabel = headerLabel;
        HeaderColorRole = headerColorRole;
        CaptionTitle = captionTitle;
        CaptionSubtitle = captionSubtitle;
        Badges = badges;
        CategoryLabel = categoryLabel;
    }

    public string HeaderLabel { get; }
    public CardHeaderColorRole HeaderColorRole { get; }
    public string CaptionTitle { get; }
    public string CaptionSubtitle { get; }
    public CardBadgeFlags Badges { get; }

    /// <summary>Normalized category text when the Category badge is present.</summary>
    public string? CategoryLabel { get; }

    public bool HasBadge(CardBadgeFlags badge) =>
        badge != CardBadgeFlags.None && (Badges & badge) == badge;

    /// <summary>Builds the default screenshot appearance, with recording as an override.</summary>
    public static CardAppearance ForShot(
        ShotRecord shot,
        bool isFavorite = false,
        string? category = null,
        bool isAnnotated = false,
        bool isRecording = false)
    {
        ArgumentNullException.ThrowIfNull(shot);
        var presentation = isRecording
            ? CardContentPresentation.Recording
            : CardContentPresentation.Screenshot;
        return ForContent(
            shot,
            presentation,
            isFavorite,
            category,
            isAnnotated,
            isRecording);
    }

    /// <summary>
    /// Extension point for future content modes. A caller supplies type-specific semantic
    /// defaults while favorite/category/annotation/recording remain orthogonal card metadata.
    /// Recording always takes precedence, matching the macOS card contract.
    /// </summary>
    public static CardAppearance ForContent(
        ShotRecord shot,
        CardContentPresentation content,
        bool isFavorite = false,
        string? category = null,
        bool isAnnotated = false,
        bool isRecording = false)
    {
        ArgumentNullException.ThrowIfNull(shot);
        ArgumentNullException.ThrowIfNull(content);

        var presentation = isRecording ? CardContentPresentation.Recording : content;
        string? categoryLabel = NormalizeCategory(category);
        CardBadgeFlags badges = CardBadgeFlags.None;
        if (isFavorite) badges |= CardBadgeFlags.Favorite;
        if (categoryLabel is not null) badges |= CardBadgeFlags.Category;
        if (isAnnotated) badges |= CardBadgeFlags.Annotated;
        if (isRecording) badges |= CardBadgeFlags.Recording;

        return new CardAppearance(
            presentation.HeaderLabel,
            presentation.HeaderColorRole,
            ResolveTitle(shot, presentation),
            presentation.FixedSubtitle ?? FormatSubtitle(shot),
            badges,
            categoryLabel);
    }

    private static string ResolveTitle(ShotRecord shot, CardContentPresentation presentation)
    {
        string?[] candidates = presentation.UsesAppNameFallback
            ? new[] { shot.WindowTitle, shot.AppName, presentation.FallbackTitle }
            : new[] { shot.WindowTitle, presentation.FallbackTitle };
        foreach (string? candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate))
                return candidate.Trim();
        }
        return "截图";
    }

    private static string FormatCapturedAt(DateTimeOffset capturedAt) =>
        capturedAt.ToLocalTime().ToString("MM-dd HH:mm", CultureInfo.InvariantCulture);

    private static string FormatSubtitle(ShotRecord shot)
    {
        var capturedAt = FormatCapturedAt(shot.CapturedAt);
        if (Uri.TryCreate(shot.SourceUrl, UriKind.Absolute, out var source)
            && !string.IsNullOrWhiteSpace(source.Host))
            return $"{source.Host} · {capturedAt}";
        return string.IsNullOrWhiteSpace(shot.AppName)
            ? capturedAt
            : $"{shot.AppName.Trim()} · {capturedAt}";
    }

    private static string? NormalizeCategory(string? category)
    {
        if (string.IsNullOrWhiteSpace(category)) return null;
        return category.Trim();
    }
}
