using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RtlTerminal;

internal static partial class Program
{
    private static void CheckThemes()
    {
        AssertScheme("Tokyo Night", "1A1B26", "C0CAF5", "F7768E");
        AssertScheme("Catppuccin Frappe", "303446", "C6D0F5", "E78284");
        AssertScheme("Rosé Pine", "191724", "E0DEF4", "EB6F92");
        if (Application.Current is null)
            _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        AssertSchemeChrome();
        AppTheme.Apply(AppThemeKind.Dark);
        var view = new TerminalView
        {
            Width = 800, Height = 420, FontFamily = new FontFamily("Consolas"),
            FontSize = 18, Background = new SolidColorBrush(Color.FromRgb(12, 12, 12))
        };
        var buffer = new TerminalBuffer(70, 16);
        var snapshot = buffer.Process("\x1b[?25l\x1b[31m█\x1b[38;2;197;15;31m█" +
            "\x1b[38;5;1m█\x1b[0m\r\n\x1b[41m \x1b[48;2;197;15;31m " +
            "\x1b[48;5;1m \x1b[0m\r\n\x1b[38;5;243m█\x1b[48;5;243m \x1b[0m\r\nسلام English");
        byte[] Pixels(RenderTargetBitmap bitmap)
        {
            var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
            bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
            return pixels;
        }
        void AssertPixel(byte[] pixels, int x, int y, TerminalColor expected)
        {
            var offset = (y * 800 + x) * 4;
            if (pixels[offset] != expected.Blue || pixels[offset + 1] != expected.Green || pixels[offset + 2] != expected.Red)
                throw new Exception($"Unexpected themed color at {x},{y}: expected {expected}");
        }
        try
        {
            var dark = Pixels(Render(view, snapshot, "theme-dark.png"));
            AppTheme.Apply(AppThemeKind.Light);
            view.Background = Brushes.White;
            view.RefreshTheme();
            var light = Pixels(Render(view, snapshot, "theme-light.png"));
            AssertPixel(light, 5, 12, new(207, 34, 46));
            AssertPixel(light, 16, 12, new(197, 15, 31));
            AssertPixel(light, 27, 12, new(207, 34, 46));
            AssertPixel(light, 5, 37, new(255, 235, 233));
            AssertPixel(light, 16, 37, new(197, 15, 31));
            AssertPixel(light, 27, 37, new(255, 235, 233));
            AssertPixel(light, 5, 62, new(118, 118, 118));
            AssertPixel(light, 16, 62, new(118, 118, 118));
            var replies = buffer.Process("\x1b]10;?\a\x1b]11;?\a\x1b]12;?\a").Responses;
            if (!replies.Contains("\x1b]10;rgb:2424/2929/2f2f\x1b\\") ||
                !replies.Contains("\x1b]11;rgb:ffff/ffff/ffff\x1b\\") ||
                !replies.Contains("\x1b]12;rgb:2424/2929/2f2f\x1b\\"))
                throw new Exception("OSC queries did not report the active light default colors");
            AppTheme.Apply(AppThemeKind.Dark, ColorScheme.Find("Dracula"));
            view.RefreshTheme();
            var dracula = Pixels(Render(view, snapshot, "theme-dracula.png"));
            AssertPixel(dracula, 5, 12, new(255, 85, 85));
            AssertPixel(dracula, 16, 12, new(197, 15, 31));
            AssertPixel(dracula, 27, 12, new(255, 85, 85));
            AssertPixel(dracula, 5, 37, new(255, 85, 85));
            if (AppTheme.DefaultBackground != new TerminalColor(40, 42, 54))
                throw new Exception("The scheme did not set the default background");
            AppTheme.Apply(AppThemeKind.Dark, ColorScheme.Find("One Half Light"));
            if (!AppTheme.IsLight)
                throw new Exception("A light scheme did not switch to the light theme");
            view.RefreshTheme();
            var halfLight = Pixels(Render(view, snapshot, "theme-one-half-light.png"));
            AssertPixel(halfLight, 5, 12, new(228, 86, 73));
            AssertPixel(halfLight, 5, 37, new(245, 217, 214));
            AppTheme.Apply(AppThemeKind.Dark);
            view.Background = new SolidColorBrush(Color.FromRgb(12, 12, 12));
            view.RefreshTheme();
            var restored = Pixels(Render(view, snapshot, "theme-dark-restored.png"));
            // Compare the terminal drawing area only; the WPF scrollbar thumb can still be in a
            // theme-transition frame at the right edge when the bitmap is captured.
            for (var y = 0; y < 420; y++)
                for (var x = 0; x < 760; x++)
                    for (var channel = 0; channel < 4; channel++)
                    {
                        var offset = (y * 800 + x) * 4 + channel;
                        if (dark[offset] != restored[offset])
                            throw new Exception($"Switching back to dark retained a changed terminal pixel at ({x},{y})");
                    }
            Console.WriteLine("PASS live theme switching, ANSI/indexed remapping, color schemes, exact RGB preservation and OSC defaults");
        }
        finally { AppTheme.Apply(AppThemeKind.Dark); }
    }

    private static void AssertScheme(string name, string background, string foreground, string red)
    {
        var scheme = ColorScheme.Find(name) ?? throw new Exception($"Missing color scheme '{name}'");
        if (scheme.Ansi.Length != 16 || scheme.Background != background || scheme.Foreground != foreground || scheme.Ansi[1] != red)
            throw new Exception($"Color scheme '{name}' has an incomplete or incorrect ANSI palette");
    }

    private static void AssertSchemeChrome()
    {
        var resources = Application.Current.Resources;
        Color Brush(string key) => ((SolidColorBrush)resources[key]).Color;
        void AssertReadable(Color text, Color background, string label)
        {
            static double Channel(byte value)
            {
                var c = value / 255d;
                return c <= .04045 ? c / 12.92 : Math.Pow((c + .055) / 1.055, 2.4);
            }
            static double Luminance(Color color) => .2126 * Channel(color.R) + .7152 * Channel(color.G) + .0722 * Channel(color.B);
            var a = Luminance(text);
            var b = Luminance(background);
            var ratio = (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05);
            if (ratio < 4.5) throw new Exception($"{label} contrast is too low: {ratio:F2}:1");
        }
        void CheckGenericScheme(string name)
        {
            AppTheme.Apply(AppThemeKind.Dark, ColorScheme.Find(name));
            var toolbar = Brush("ToolbarBrush");
            var title = Brush("TitleBarBrush");
            var menu = Brush("MenuBackgroundBrush");
            var terminalBackground = AppTheme.DefaultBackground;
            var terminalSurface = Color.FromRgb(terminalBackground.Red, terminalBackground.Green, terminalBackground.Blue);
            if (toolbar == terminalSurface || title == toolbar || menu == terminalSurface)
                throw new Exception($"{name} did not derive distinct chrome surfaces from its terminal background");
            AssertReadable(Brush("ChromeForegroundBrush"), toolbar, $"{name} chrome text");
            AssertReadable(Brush("ChromeForegroundBrush"), menu, $"{name} menu text");
            AssertReadable(Brush("ChromeStrongForegroundBrush"), Brush("MenuHighlightBrush"), $"{name} highlighted menu text");
            AssertReadable(Brush("ChromeStrongForegroundBrush"), Brush("MenuOpenBrush"), $"{name} open menu text");
            AssertReadable(AppTheme.TabAccent, toolbar, $"{name} accent");
            AssertReadable(Brush("FocusBrush"), toolbar, $"{name} keyboard focus");
            AssertReadable(AppTheme.ActiveTabText, AppTheme.ActiveTab, $"{name} active tab text");
            AssertReadable(AppTheme.InactiveTabText, title, $"{name} inactive tab text");
        }

        CheckGenericScheme("Dracula");
        CheckGenericScheme("Catppuccin Latte");

        AppTheme.Apply(AppThemeKind.Dark, ColorScheme.Find("Campbell"));
        if (Brush("TitleBarBrush") != Color.FromRgb(32, 33, 36) || AppTheme.TabAccent != Color.FromRgb(114, 214, 197))
            throw new Exception("Campbell's existing application chrome changed");
        AppTheme.Apply(AppThemeKind.Dark, ColorScheme.Find("Windows Terminal"));
        if (Brush("TitleBarBrush") != Color.FromRgb(32, 32, 32) || AppTheme.TabAccent != Color.FromRgb(0, 120, 212))
            throw new Exception("Windows Terminal's established chrome changed");
        AppTheme.Apply(AppThemeKind.Dark, ColorScheme.Find("AryanIQ Phosphor"));
        if (AppTheme.TabAccent != Color.FromRgb(57, 255, 20) || Brush("TerminalBackgroundBrush") != Colors.Black)
            throw new Exception("Phosphor's established accent or terminal surface changed");
        AppTheme.Apply(AppThemeKind.Dark);
    }
}
