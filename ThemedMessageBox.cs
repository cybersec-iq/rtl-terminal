using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace RtlTerminal;

internal static class ThemedMessageBox
{
    public static MessageBoxResult Show(
        Window? owner,
        string message,
        string caption,
        MessageBoxButton buttons,
        MessageBoxImage image)
    {
        var dialog = new Window
        {
            Title = caption,
            Icon = Application.Current?.MainWindow?.Icon,
            Width = 520,
            MinWidth = 420,
            MaxWidth = 720,
            SizeToContent = SizeToContent.Height,
            MinHeight = 190,
            MaxHeight = 520,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            Owner = owner,
            Background = Brush("DialogBackgroundBrush", Color.FromRgb(32, 33, 36)),
            Foreground = Brush("DialogForegroundBrush", Color.FromRgb(240, 240, 240)),
            ShowInTaskbar = false
        };
        DialogTheme.ApplyDarkCaption(dialog);

        var root = new Grid { Margin = new Thickness(20) };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });

        var body = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 12, 0, 18) };
        var symbol = new TextBlock
        {
            Text = Symbol(image),
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush(image == MessageBoxImage.Error ? "ErrorBrush" : "DialogAccentBrush", Color.FromRgb(76, 194, 255)),
            Margin = new Thickness(0, 0, 14, 0),
            VerticalAlignment = VerticalAlignment.Top
        };
        DockPanel.SetDock(symbol, Dock.Left);
        body.Children.Add(symbol);
        var text = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("DialogForegroundBrush", Color.FromRgb(240, 240, 240)),
            VerticalAlignment = VerticalAlignment.Center
        };
        body.Children.Add(text);
        Grid.SetRow(body, 1);
        root.Children.Add(body);

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        MessageBoxResult defaultResult = DefaultResult(buttons);
        var selectedResult = defaultResult;
        foreach (var (label, result) in ButtonSet(buttons))
        {
            var button = new Button
            {
                Content = label,
                MinWidth = 88,
                Margin = new Thickness(8, 0, 0, 0),
                Padding = new Thickness(12, 6, 12, 6),
                Background = Brush("DialogSurfaceBrush", Color.FromRgb(45, 45, 45)),
                Foreground = Brush("DialogForegroundBrush", Color.FromRgb(240, 240, 240)),
                BorderBrush = Brush("DialogBorderBrush", Color.FromRgb(63, 63, 70)),
                IsDefault = result == defaultResult,
                IsCancel = result == MessageBoxResult.Cancel
            };
            button.Click += (_, _) =>
            {
                selectedResult = result;
                dialog.DialogResult = true;
            };
            buttonPanel.Children.Add(button);
        }
        Grid.SetRow(buttonPanel, 2);
        root.Children.Add(buttonPanel);
        dialog.Content = root;

        return dialog.ShowDialog() == true
            ? selectedResult
            : CancelResult(buttons);
    }

    private static Brush Brush(string key, Color fallback) =>
        Application.Current?.TryFindResource(key) as Brush ?? new SolidColorBrush(fallback);

    private static string Symbol(MessageBoxImage image) => image switch
    {
        MessageBoxImage.Error => "!",
        MessageBoxImage.Warning => "⚠",
        MessageBoxImage.Question => "?",
        _ => "i"
    };

    private static MessageBoxResult DefaultResult(MessageBoxButton buttons) => buttons switch
    {
        MessageBoxButton.YesNo or MessageBoxButton.YesNoCancel => MessageBoxResult.No,
        MessageBoxButton.OKCancel => MessageBoxResult.OK,
        _ => MessageBoxResult.OK
    };

    private static MessageBoxResult CancelResult(MessageBoxButton buttons) => buttons switch
    {
        MessageBoxButton.YesNo => MessageBoxResult.No,
        MessageBoxButton.OK => MessageBoxResult.OK,
        _ => MessageBoxResult.Cancel
    };

    private static IEnumerable<(string Label, MessageBoxResult Result)> ButtonSet(MessageBoxButton buttons) => buttons switch
    {
        MessageBoxButton.OKCancel => [("OK", MessageBoxResult.OK), ("Cancel", MessageBoxResult.Cancel)],
        MessageBoxButton.YesNo => [("Yes", MessageBoxResult.Yes), ("No", MessageBoxResult.No)],
        MessageBoxButton.YesNoCancel => [("Yes", MessageBoxResult.Yes), ("No", MessageBoxResult.No), ("Cancel", MessageBoxResult.Cancel)],
        _ => [("OK", MessageBoxResult.OK)]
    };
}

internal static class DialogTheme
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeLegacy = 19;

    public static void ApplyDarkCaption(Window window)
    {
        window.SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(window).Handle;
            var enabled = AppTheme.IsLight ? 0 : 1;
            try
            {
                _ = DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref enabled, sizeof(int));
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
            if (enabled == 1)
            {
                try { _ = DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkModeLegacy, ref enabled, sizeof(int)); }
                catch (DllNotFoundException) { }
                catch (EntryPointNotFoundException) { }
            }
        };
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int valueSize);
}
