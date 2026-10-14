using Avalonia;
using Avalonia.Controls;

namespace TomatoMix.Theme;

/// <summary>
/// Typed vector icon control backed by <see cref="IconRegistry"/>.
/// Compile-time checking prevents invalid icon names.
/// </summary>
public class ThemeIcon : PathIcon
{
    public static readonly StyledProperty<IconName> IconProperty =
        AvaloniaProperty.Register<ThemeIcon, IconName>(nameof(Icon), IconName.Settings);

    protected override Type StyleKeyOverride => typeof(PathIcon);

    static ThemeIcon()
    {
        IconProperty.Changed.AddClassHandler<ThemeIcon>((control, _) => control.UpdateGeometry());
    }

    public ThemeIcon()
    {
        Width = 13;
        Height = 13;
        UpdateGeometry();
    }

    public IconName Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    private void UpdateGeometry()
    {
        Data = IconRegistry.GetGeometry(Icon);
    }
}
