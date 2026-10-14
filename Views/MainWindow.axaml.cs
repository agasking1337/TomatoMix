using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using TomatoMix.ViewModels;

namespace TomatoMix.Views;

public partial class MainWindow : Window
{
    private AppSessionViewModel? _draggedApp;

    public bool IsExplicitExitRequested { get; set; }

    public MainWindow()
    {
        InitializeComponent();
        AddHandler(DragDrop.DragOverEvent, OnChannelDragOver);
        AddHandler(DragDrop.DropEvent, OnChannelDrop);
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

    private async void OnAppCardPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control control || control.DataContext is not AppSessionViewModel appVm)
            return;

        var point = e.GetCurrentPoint(control);
        if (!point.Properties.IsLeftButtonPressed)
            return;

        // Walk the visual tree so template children inside Slider / Thumb / Track / Button are properly detected
        for (Visual? v = e.Source as Visual; v != null && v != control; v = v.GetVisualParent())
        {
            if (v is Button || v is ToggleButton || v is Slider || v is Track || v is Thumb)
                return;
        }

        _draggedApp = appVm;
        var dragData = new DataObject();
        dragData.Set("TomatoMixAppKey", appVm.ProcessKey);
        await DragDrop.DoDragDrop(e, dragData, DragDropEffects.Move);
        _draggedApp = null;
    }

    private void OnChannelDragOver(object? sender, DragEventArgs e)
    {
        if (_draggedApp != null && e.Data.Contains("TomatoMixAppKey"))
        {
            e.DragEffects = DragDropEffects.Move;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
    }

    private void OnChannelDrop(object? sender, DragEventArgs e)
    {
        if (_draggedApp == null || DataContext is not MainWindowViewModel mainVm)
            return;

        if (e.Source is Control sourceControl)
        {
            Control? current = sourceControl;
            while (current != null)
            {
                if (current.DataContext is ChannelViewModel targetChannel)
                {
                    mainVm.MoveAppToChannelInternal(_draggedApp, targetChannel.Id);
                    break;
                }
                current = current.Parent as Control;
            }
        }

        _draggedApp = null;
    }

    private void OnRouteAppButtonClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn
            || btn.DataContext is not AppSessionViewModel appVm
            || DataContext is not MainWindowViewModel mainVm)
            return;

        var menu = new MenuFlyout
        {
            Placement = PlacementMode.BottomEdgeAlignedRight
        };

        var headerItem = new MenuItem
        {
            Header = $"ROUTE {appVm.DisplayName.ToUpperInvariant()} TO:",
            IsEnabled = false
        };
        menu.Items.Add(headerItem);
        menu.Items.Add(new Separator());

        foreach (var ch in mainVm.Channels)
        {
            bool isCurrent = string.Equals(ch.Id, appVm.ChannelId, StringComparison.OrdinalIgnoreCase);
            var item = new MenuItem
            {
                Header = isCurrent ? $"✓ {ch.Name} (Current)" : $"→ {ch.Name}",
                IsEnabled = !isCurrent
            };
            var targetId = ch.Id;
            item.Click += (_, _) => mainVm.MoveAppToChannelInternal(appVm, targetId);
            menu.Items.Add(item);
        }

        menu.ShowAt(btn);
    }
}
