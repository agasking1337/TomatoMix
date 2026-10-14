using Avalonia.Media;

namespace TomatoMix.Theme;

/// <summary>
/// Strongly-typed icon registry grouped by purpose.
/// Invalid icon names are caught at compile time.
/// </summary>
public enum IconName
{
    // Theme and search
    Sun,
    Moon,
    Command,
    Search,
    Eye,

    // Main navigation
    Dashboard,
    Snippets,
    Sftp,
    Terminal,
    Settings,

    // Actions
    Plus,
    Edit,
    Trash,
    Check,
    Play,
    Close,

    // Files
    Folder,
    File,
    Upload,
    Download,
    Refresh,

    // Security
    Key,
    Shield,

    // Layout
    Collapse,
    Expand,
    Chevron,
    Grip,
    Layers,

    // Other
    Telegram,
    Star,
    Tunnel
}

/// <summary>
/// Maps typed IconName values to 24x24 vector StreamGeometry artwork.
/// </summary>
public static class IconRegistry
{
    private static readonly IReadOnlyDictionary<string, IconName> NameLookup =
        new Dictionary<string, IconName>(StringComparer.OrdinalIgnoreCase)
        {
            // Theme and search
            ["sun"] = IconName.Sun,
            ["moon"] = IconName.Moon,
            ["command"] = IconName.Command,
            ["search"] = IconName.Search,
            ["eye"] = IconName.Eye,

            // Main navigation
            ["dashboard"] = IconName.Dashboard,
            ["snippets"] = IconName.Snippets,
            ["sftp"] = IconName.Sftp,
            ["terminal"] = IconName.Terminal,
            ["settings"] = IconName.Settings,

            // Actions
            ["plus"] = IconName.Plus,
            ["edit"] = IconName.Edit,
            ["trash"] = IconName.Trash,
            ["check"] = IconName.Check,
            ["play"] = IconName.Play,
            ["close"] = IconName.Close,

            // Files
            ["folder"] = IconName.Folder,
            ["file"] = IconName.File,
            ["upload"] = IconName.Upload,
            ["download"] = IconName.Download,
            ["refresh"] = IconName.Refresh,

            // Security
            ["key"] = IconName.Key,
            ["shield"] = IconName.Shield,

            // Layout
            ["collapse"] = IconName.Collapse,
            ["expand"] = IconName.Expand,
            ["chevron"] = IconName.Chevron,
            ["grip"] = IconName.Grip,
            ["layers"] = IconName.Layers,

            // Other
            ["telegram"] = IconName.Telegram,
            ["star"] = IconName.Star,
            ["tunnel"] = IconName.Tunnel
        };

    private static readonly Dictionary<IconName, StreamGeometry> GeometryCache = new();

    public static StreamGeometry GetGeometry(IconName icon)
    {
        if (GeometryCache.TryGetValue(icon, out var cached))
        {
            return cached;
        }

        string path = icon switch
        {
            // Theme and search
            IconName.Sun => "M12,7 A5,5 0 1,0 12,17 A5,5 0 1,0 12,7 Z M11,1 H13 V4 H11 V1 Z M11,20 H13 V23 H11 V20 Z M3.51,4.92 L4.92,3.51 L7.05,5.63 L5.63,7.05 L3.51,4.92 Z M16.95,18.36 L18.36,16.95 L20.49,19.07 L19.07,20.49 L16.95,18.36 Z M1,11 H4 V13 H1 V11 Z M20,11 H23 V13 H20 V11 Z M5.63,16.95 L7.05,18.36 L4.92,20.49 L3.51,19.07 L5.63,16.95 Z M19.07,3.51 L20.49,4.92 L18.36,7.05 L16.95,5.63 L19.07,3.51 Z",
            IconName.Moon => "M12.3,4.9 C9.1,5.4 6.6,8 6.1,11.2 C5.4,15.6 8.8,19.5 13.1,19.5 C15.8,19.5 18.1,18 19.3,15.8 C15.1,16.4 11.4,12.8 12,8.5 C12.1,7.2 12.6,6 13.3,5 C13,4.9 12.6,4.9 12.3,4.9 Z M12,3 C13.8,3 15.5,3.5 16.9,4.5 C14.6,5.8 13.3,8.5 13.8,11.3 C14.3,14.1 16.7,16.1 19.5,16.1 C20.1,16.1 20.6,16 21.1,15.9 C19.6,18.9 16.5,21 13,21 C8,21 4,17 4,12 C4,7 8,3 12,3 Z",
            IconName.Command => "M6.5,4 A2.5,2.5 0 0,0 4,6.5 A2.5,2.5 0 0,0 6.5,9 H8 V6.5 A2.5,2.5 0 0,0 6.5,4 Z M10,6.5 V9 H14 V6.5 A4.5,4.5 0 1,1 18.5,11 H16 V13 H18.5 A4.5,4.5 0 1,1 14,17.5 V15 H10 V17.5 A4.5,4.5 0 1,1 5.5,13 H8 V11 H5.5 A4.5,4.5 0 1,1 10,6.5 Z M16,9 H17.5 A2.5,2.5 0 1,0 16,6.5 V9 Z M10,11 V13 H14 V11 H10 Z M8,15 H6.5 A2.5,2.5 0 1,0 8,17.5 V15 Z M16,15 V17.5 A2.5,2.5 0 1,0 17.5,15 H16 Z",
            IconName.Search => "M15.5,14 H14.71 L14.43,13.73 C15.41,12.59 16,11.11 16,9.5 A6.5,6.5 0 1,0 9.5,16 C11.11,16 12.59,15.41 13.73,14.43 L14,14.71 V15.5 L19,20.49 L20.49,19 L15.5,14 Z M9.5,14 C7.01,14 5,11.99 5,9.5 C5,7.01 7.01,5 9.5,5 C11.99,5 14,7.01 14,9.5 C14,11.99 11.99,14 9.5,14 Z",
            IconName.Eye => "M12,4.5 C7,4.5 2.73,7.61 1,12 C2.73,16.39 7,19.5 12,19.5 C17,19.5 21.27,16.39 23,12 C21.27,7.61 17,4.5 12,4.5 Z M12,17 C9.24,17 7,14.76 7,12 C7,9.24 9.24,7 12,7 C14.76,7 17,9.24 17,12 C17,14.76 14.76,17 12,17 Z M12,9 C10.34,9 9,10.34 9,12 C9,13.66 10.34,15 12,15 C13.66,15 15,13.66 15,12 C15,10.34 13.66,9 12,9 Z",

            // Main navigation
            IconName.Dashboard => "M3,13 H11 V3 H3 V13 Z M3,21 H11 V15 H3 V21 Z M13,21 H21 V11 H13 V21 Z M13,3 V9 H21 V3 H13 Z",
            IconName.Snippets => "M9.4,16.6 L4.8,12 L9.4,7.4 L8,6 L2,12 L8,18 L9.4,16.6 Z M14.6,16.6 L19.2,12 L14.6,7.4 L16,6 L22,12 L16,18 L14.6,16.6 Z",
            IconName.Sftp => "M6.99,11 L3,15 L6.99,19 V16 H14 V14 H6.99 V11 Z M21,9 L17.01,5 V8 H10 V10 H17.01 V13 L21,9 Z",
            IconName.Terminal => "M20,4 H4 C2.9,4 2,4.9 2,6 V18 C2,19.1 2.9,20 4,20 H20 C21.1,20 22,19.1 22,18 V6 C22,4.9 21.1,4 20,4 Z M20,18 H4 V8 H20 V18 Z M7.5,16.5 L6.09,15.09 L9.17,12 L6.09,8.91 L7.5,7.5 L12,12 L7.5,16.5 Z M12,15 H18 V17 H12 V15 Z",
            IconName.Settings => "M3,17 V19 H9 V17 H3 Z M3,5 V7 H13 V5 H3 Z M13,21 V19 H21 V17 H13 V15 H11 V21 H13 Z M7,9 V11 H3 V13 H7 V15 H9 V9 H7 Z M21,13 V11 H11 V13 H21 Z M15,9 H17 V7 H21 V5 H17 V3 H15 V9 Z",

            // Actions
            IconName.Plus => "M19,13 H13 V19 H11 V13 H5 V11 H11 V5 H13 V11 H19 V13 Z",
            IconName.Edit => "M3,17.25 V21 H6.75 L17.81,9.94 L14.06,6.19 L3,17.25 Z M20.71,7.04 C21.1,6.65 21.1,6.02 20.71,5.63 L18.37,3.29 C17.98,2.9 17.35,2.9 16.96,3.29 L15.13,5.12 L18.88,8.87 L20.71,7.04 Z",
            IconName.Trash => "M6,19 C6,20.1 6.9,21 8,21 H16 C17.1,21 18,20.1 18,19 V7 H6 V19 Z M19,4 H15.5 L14.5,3 H9.5 L8.5,4 H5 V6 H19 V4 Z",
            IconName.Check => "M9,16.17 L4.83,12 L3.41,13.41 L9,19 L21,7 L19.59,5.59 L9,16.17 Z",
            IconName.Play => "M8,5 V19 L19,12 L8,5 Z",
            IconName.Close => "M19,6.41 L17.59,5 L12,10.59 L6.41,5 L5,6.41 L10.59,12 L5,17.59 L6.41,19 L12,13.41 L17.59,19 L19,17.59 L13.41,12 L19,6.41 Z",

            // Files
            IconName.Folder => "M10,4 H4 C2.9,4 2.01,4.9 2.01,6 L2,18 C2,19.1 2.9,20 4,20 H20 C21.1,20 22,19.1 22,18 V8 C22,6.9 21.1,6 20,6 H12 L10,4 Z",
            IconName.File => "M14,2 H6 C4.9,2 4,2.9 4,4 V20 C4,21.1 4.9,22 6,22 H18 C19.1,22 20,21.1 20,20 V8 L14,2 Z M13,9 V3.5 L18.5,9 H13 Z",
            IconName.Upload => "M9,16 H15 V10 H19 L12,3 L5,10 H9 V16 Z M5,18 H19 V20 H5 V18 Z",
            IconName.Download => "M19,9 H15 V3 H9 V9 H5 L12,16 L19,9 Z M5,18 V20 H19 V18 H5 Z",
            IconName.Refresh => "M17.65,6.35 C16.2,4.9 14.21,4 12,4 A8,8 0 0,0 4,12 A8,8 0 0,0 12,20 C15.73,20 18.84,17.45 19.73,14 H17.65 C16.83,16.33 14.61,18 12,18 A6,6 0 0,1 6,12 A6,6 0 0,1 12,6 C13.66,6 15.14,6.69 16.22,7.78 L13,11 H20 V4 L17.65,6.35 Z",

            // Security
            IconName.Key => "M12.65,10 C11.83,7.67 9.61,6 7,6 C3.69,6 1,8.69 1,12 C1,15.31 3.69,18 7,18 C9.61,18 11.83,16.33 12.65,14 H17 V18 H21 V14 H23 V10 H12.65 Z M7,14 C5.9,14 5,13.1 5,12 C5,10.9 5.9,10 7,10 C8.1,10 9,10.9 9,12 C9,13.1 8.1,14 7,14 Z",
            IconName.Shield => "M12,1 L3,5 V11 C3,16.55 6.84,21.74 12,23 C17.16,21.74 21,16.55 21,11 V5 L12,1 Z",

            // Layout
            IconName.Collapse => "M5,16 H8 V19 H10 V14 H5 V16 Z M8,8 H5 V10 H10 V5 H8 V8 Z M14,19 H16 V16 H19 V14 H14 V19 Z M16,8 V5 H14 V10 H19 V8 H16 Z",
            IconName.Expand => "M7,14 H5 V19 H10 V17 H7 V14 Z M5,10 H7 V7 H10 V5 H5 V10 Z M17,17 H14 V19 H19 V14 H17 V17 Z M14,5 V7 H17 V10 H19 V5 H14 Z",
            IconName.Chevron => "M10,6 L8.59,7.41 L13.17,12 L8.59,16.59 L10,18 L16,12 L10,6 Z",
            IconName.Grip => "M9,4 A1.5,1.5 0 1,0 9,7 A1.5,1.5 0 1,0 9,4 Z M15,4 A1.5,1.5 0 1,0 15,7 A1.5,1.5 0 1,0 15,4 Z M9,10.5 A1.5,1.5 0 1,0 9,13.5 A1.5,1.5 0 1,0 9,10.5 Z M15,10.5 A1.5,1.5 0 1,0 15,13.5 A1.5,1.5 0 1,0 15,10.5 Z M9,17 A1.5,1.5 0 1,0 9,20 A1.5,1.5 0 1,0 9,17 Z M15,17 A1.5,1.5 0 1,0 15,20 A1.5,1.5 0 1,0 15,17 Z",
            IconName.Layers => "M11.99,18.54 L4.62,12.81 L3,14.07 L12,21.07 L21,14.07 L19.37,12.8 L11.99,18.54 Z M12,16 L19.36,10.27 L21,9 L12,2 L3,9 L4.63,10.27 L12,16 Z",

            // Other
            IconName.Telegram => "M9.78,18.65 L10.06,14.42 L17.74,7.5 C18.08,7.19 17.67,7.04 17.22,7.31 L7.74,13.3 L3.64,12.02 C2.76,11.75 2.75,11.14 3.84,10.7 L19.81,4.54 C20.54,4.21 21.24,4.72 20.96,5.84 L18.24,18.65 C18.05,19.56 17.5,19.78 16.74,19.36 L12.6,16.3 L10.61,18.23 C10.38,18.46 10.19,18.65 9.78,18.65 Z",
            IconName.Star => "M12,17.27 L18.18,21 L16.54,13.97 L22,9.24 L14.81,8.63 L12,2 L9.19,8.63 L2,9.24 L7.46,13.97 L5.82,21 L12,17.27 Z",
            IconName.Tunnel => "M3,17 H5 V7 H3 V17 Z M7,21 H9 V3 H7 V21 Z M11,15 H13 V9 H11 V15 Z M15,19 H17 V5 H15 V19 Z M19,14 H21 V10 H19 V14 Z",

            _ => throw new ArgumentOutOfRangeException(nameof(icon), icon, "Unregistered icon name")
        };

        var geometry = StreamGeometry.Parse(path);
        GeometryCache[icon] = geometry;
        return geometry;
    }

    public static IconName ResolveIconName(string? name, IconName fallback = IconName.Settings)
    {
        if (!string.IsNullOrWhiteSpace(name) && NameLookup.TryGetValue(name.Trim(), out var resolved))
        {
            return resolved;
        }

        return fallback;
    }

    public static IconName ResolveChannelIcon(string? channelId, string? iconGlyph)
    {
        string key = (channelId ?? iconGlyph ?? string.Empty).Trim().ToLowerInvariant();
        if (NameLookup.TryGetValue(key, out var direct))
        {
            return direct;
        }

        return key switch
        {
            "game" => IconName.Dashboard,
            "chat" => IconName.Telegram,
            "media" => IconName.Play,
            "aux" => IconName.Layers,
            "mic" => IconName.Tunnel,
            _ => IconName.Settings
        };
    }
}
