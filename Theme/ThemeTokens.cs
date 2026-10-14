using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace TomatoMix.Theme;

/// <summary>
/// Centralized semantic color token keys matching the monochrome Light/Dark design system.
/// Actual color values live exclusively in App.axaml ThemeDictionaries.
/// </summary>
public static class ThemeTokens
{
    // Core semantic tokens (--bg, --surface, --surface-raised, --surface-inset, --text, --text-muted, --text-faint, --border, --border-strong, --accent, --accent-fg)
    public const string Bg = "TokenBg";
    public const string Surface = "TokenSurface";
    public const string SurfaceRaised = "TokenSurfaceRaised";
    public const string SurfaceInset = "TokenSurfaceInset";
    public const string Text = "TokenText";
    public const string TextMuted = "TokenTextMuted";
    public const string TextFaint = "TokenTextFaint";
    public const string Border = "TokenBorder";
    public const string BorderStrong = "TokenBorderStrong";
    public const string Accent = "TokenAccent";
    public const string AccentFg = "TokenAccentFg";

    // Derived monochrome surface/accent helpers defined in App.axaml
    public const string AccentSubtle = "TokenAccentSubtle";
    public const string Backdrop = "TokenBackdrop";

    // Semantic status tokens
    public const string StatusOk = "TokenStatusOk";
    public const string StatusWarning = "TokenStatusWarning";
    public const string StatusCritical = "TokenStatusCritical";
    public const string StatusOffline = "TokenStatusOffline";
    public const string StatusUnknown = "TokenTextFaint";

    public const string StatusOkSubtle = "TokenStatusOkSubtle";
    public const string StatusWarningSubtle = "TokenStatusWarningSubtle";
    public const string StatusCriticalSubtle = "TokenStatusCriticalSubtle";
    public const string StatusOfflineSubtle = "TokenStatusOfflineSubtle";

    public static IBrush ResolveBrush(string tokenKey)
    {
        var app = Application.Current;
        if (app != null)
        {
            var theme = app.ActualThemeVariant;
            if (app.TryFindResource(tokenKey, theme, out var resource) && resource is IBrush brush)
            {
                return brush;
            }
        }

        return Brushes.Transparent;
    }

    public static void SetTheme(bool isDark)
    {
        if (Application.Current != null)
        {
            Application.Current.RequestedThemeVariant = isDark ? ThemeVariant.Dark : ThemeVariant.Light;
        }
    }
}
