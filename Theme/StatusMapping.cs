using Avalonia.Media;

namespace TomatoMix.Theme;

/// <summary>
/// Semantic status states mapped to theme-aware status color tokens.
/// </summary>
public enum StatusLevel
{
    Ok,
    Warning,
    Critical,
    Offline,
    Unknown
}

/// <summary>
/// Translates status values into semantic color tokens through a single mapping function.
/// Unknown/connecting states stay neutral (--text-faint) rather than appearing healthy or offline.
/// </summary>
public static class StatusMapping
{
    public static string ToTokenKey(StatusLevel status) => status switch
    {
        StatusLevel.Ok => ThemeTokens.StatusOk,
        StatusLevel.Warning => ThemeTokens.StatusWarning,
        StatusLevel.Critical => ThemeTokens.StatusCritical,
        StatusLevel.Offline => ThemeTokens.StatusOffline,
        StatusLevel.Unknown => ThemeTokens.StatusUnknown,
        _ => ThemeTokens.StatusUnknown
    };

    public static string ToSubtleTokenKey(StatusLevel status) => status switch
    {
        StatusLevel.Ok => ThemeTokens.StatusOkSubtle,
        StatusLevel.Warning => ThemeTokens.StatusWarningSubtle,
        StatusLevel.Critical => ThemeTokens.StatusCriticalSubtle,
        StatusLevel.Offline => ThemeTokens.StatusOfflineSubtle,
        StatusLevel.Unknown => ThemeTokens.SurfaceInset,
        _ => ThemeTokens.SurfaceInset
    };

    public static IBrush ResolveStatusBrush(StatusLevel status)
    {
        return ThemeTokens.ResolveBrush(ToTokenKey(status));
    }

    public static IBrush ResolveStatusSubtleBrush(StatusLevel status)
    {
        return ThemeTokens.ResolveBrush(ToSubtleTokenKey(status));
    }
}
