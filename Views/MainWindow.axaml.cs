using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using TomatoMix.ViewModels;

namespace TomatoMix.Views;

public partial class MainWindow : Window
{
    public bool IsExplicitExitRequested { get; set; }

    public MainWindow()
    {
        InitializeComponent();
        AddHandler(PointerWheelChangedEvent, OnPointerWheelChanged, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);
    }

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (e.Handled || Math.Abs(e.Delta.Y) < 0.001)
            return;

        var slider = FindHoveredFaderSlider(e.Source as Visual);
        if (slider == null || !slider.IsEnabled)
            return;

        double range = slider.Maximum - slider.Minimum;
        if (range <= 0)
            return;

        double baseStep = Math.Abs(range - 24.0) < 0.1
            ? 0.5
            : Math.Abs(range - 200.0) < 0.1
                ? 4.0
                : 2.0;

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            baseStep *= 0.5;
        }
        else if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            baseStep *= 2.5;
        }

        double nextValue = Math.Clamp(slider.Value + (e.Delta.Y * baseStep), slider.Minimum, slider.Maximum);
        if (Math.Abs(range - 24.0) < 0.1)
        {
            nextValue = Math.Round(nextValue, 1);
        }
        else
        {
            nextValue = Math.Round(nextValue);
        }

        slider.Value = nextValue;
        e.Handled = true;
    }

    private static Slider? FindHoveredFaderSlider(Visual? source)
    {
        if (source == null)
            return null;

        // First check if the pointer is directly over a Slider or any of its template parts (Thumb, Track, RepeatButtons)
        if (source.FindAncestorOfType<Slider>(includeSelf: true) is { } directSlider)
            return directSlider;

        for (Visual? v = source; v != null; v = v.GetVisualParent())
        {
            if (v is Button || v is ComboBox || v is TextBox)
                return null;

            if (v is ScrollViewer)
                return null;

            if (v is Border border && border.Classes.Contains("app-card"))
                return null;

            if (v is Control container && !container.GetVisualDescendants().OfType<ScrollViewer>().Any())
            {
                var sliders = container.GetVisualDescendants().OfType<Slider>().ToList();
                if (sliders.Count == 1)
                    return sliders[0];
            }
        }

        return null;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == WindowStateProperty
            && change.NewValue is WindowState newState
            && newState == WindowState.Minimized
            && DataContext is MainWindowViewModel vm
            && vm.MinimizeToTray)
        {
            HideToTray();
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!IsExplicitExitRequested
            && e.CloseReason == WindowCloseReason.WindowClosing
            && DataContext is MainWindowViewModel vm
            && vm.CloseToTray)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }

        base.OnClosing(e);
    }

    public void HideToTray()
    {
        ShowInTaskbar = false;
        Hide();
        if (DataContext is MainWindowViewModel vm)
        {
            vm.SetBackgroundMode(true);
        }
    }

    public void RestoreFromTray()
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.SetBackgroundMode(false);
        }

        ShowInTaskbar = true;
        if (!IsVisible)
        {
            Show();
        }

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        Focus();
    }
}
