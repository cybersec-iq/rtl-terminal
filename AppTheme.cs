using System.Windows;
using System.Windows.Media;

namespace RtlTerminal;

public enum AppThemeKind
{
    Dark,
    Light
}

public static class AppTheme
{
    private static readonly TerminalColor[] LightForegrounds =
    [
        new(36, 41, 47),
        new(207, 34, 46),
        new(17, 99, 41),
        new(125, 78, 0),
        new(5, 80, 174),
        new(130, 80, 223),
        new(27, 124, 131),
        new(87, 96, 106),
        new(110, 119, 129),
        new(164, 14, 38),
        new(26, 127, 55),
        new(99, 60, 1),
        new(9, 105, 218),
        new(130, 80, 223),
        new(49, 146, 170),
        new(36, 41, 47)
    ];

    // Applications pick colors assuming a dark screen, so explicit
    // backgrounds become light tints that keep the remapped text readable.
    private static readonly TerminalColor[] LightBackgrounds =
    [
        new(246, 248, 250),
        new(255, 235, 233),
        new(218, 251, 225),
        new(255, 248, 197),
        new(221, 244, 255),
        new(251, 239, 255),
        new(216, 243, 246),
        new(208, 215, 222),
        new(208, 215, 222),
        new(255, 206, 203),
        new(172, 238, 187),
        new(255, 236, 153),
        new(182, 227, 255),
        new(236, 215, 255),
        new(179, 233, 239),
        new(234, 238, 242)
    ];

    public static AppThemeKind Current { get; private set; } = AppThemeKind.Dark;

    // The selected color scheme, or null for the built-in colors of the theme.
    public static ColorScheme? Scheme { get; private set; }

    public static bool IsLight => Current == AppThemeKind.Light;
    private static bool IsPhosphor => Scheme?.Name == "AryanIQ Phosphor";
    private static bool IsWindowsTerminal => Scheme?.Name == "Windows Terminal";
    private static bool IsCampbell => Scheme?.Name == "Campbell";
    private static DerivedChrome? _derivedChrome;

    private readonly record struct DerivedChrome(
        Color TitleBar, Color Toolbar, Color Border, Color Foreground, Color StrongForeground,
        Color MutedForeground, Color Accent, Color MenuBackground, Color MenuHighlight,
        Color MenuOpen, Color Separator, Color CaptionHover, Color CaptionPressed,
        Color ButtonPressed, Color RoundHover, Color RoundPressed, Color RoundFocus,
        Color ActiveTab, Color ActiveTabText, Color HoverTab, Color InactiveTabText, Color TabSeparator,
        Color ScrollTrack, Color ScrollThumb, Color ScrollThumbHover, Color ScrollThumbPressed);

    public static TerminalColor DefaultForeground =>
        Scheme?.DefaultForeground ?? (IsLight ? new(36, 41, 47) : new(230, 230, 230));

    public static TerminalColor DefaultBackground =>
        Scheme?.DefaultBackground ?? (IsLight ? new(255, 255, 255) : new(12, 12, 12));

    public static TerminalColor MapForeground(TerminalColor color, int? paletteIndex = null) =>
        paletteIndex is not (>= 0 and < 16) ? color
        : Scheme is not null ? Scheme.Text(paletteIndex.Value)
        : IsLight ? LightForegrounds[paletteIndex.Value]
        : color;

    public static TerminalColor MapBackground(TerminalColor color, int? paletteIndex = null) =>
        paletteIndex is not (>= 0 and < 16) ? color
        : Scheme is not null ? Scheme.Fill(paletteIndex.Value)
        : IsLight ? LightBackgrounds[paletteIndex.Value]
        : color;

    internal static TerminalColor DefaultAnsiForeground(AppThemeKind theme, int index) =>
        theme == AppThemeKind.Light ? LightForegrounds[index] : ColorScheme.Find("Campbell")!.Text(index);

    public static Color TabAccent => IsPhosphor ? Rgb(57, 255, 20) : IsWindowsTerminal ? Rgb(0, 120, 212) : _derivedChrome?.Accent ?? (IsLight ? Rgb(15, 118, 110) : Rgb(114, 214, 197));
    public static Color TabIcon => _derivedChrome?.MutedForeground ?? (IsLight ? Rgb(101, 109, 118) : IsWindowsTerminal ? Rgb(160, 160, 160) : Rgb(139, 148, 158));
    public static Color ActiveTabText => _derivedChrome?.ActiveTabText ?? (IsLight ? Rgb(31, 35, 40) : Rgb(240, 244, 248));
    public static Color InactiveTabText => _derivedChrome?.InactiveTabText ?? (IsLight ? Rgb(87, 96, 106) : Rgb(165, 174, 184));
    public static Color ActiveTab => _derivedChrome?.ActiveTab ?? (IsLight ? Rgb(246, 248, 250) : IsWindowsTerminal ? Rgb(45, 45, 45) : Rgb(48, 49, 52));
    public static Color HoverTab => _derivedChrome?.HoverTab ?? (IsLight ? Rgb(215, 220, 226) : IsWindowsTerminal ? Rgb(58, 58, 58) : Rgb(42, 43, 47));
    public static Color TabSeparator => _derivedChrome?.TabSeparator ?? (IsLight ? Rgb(175, 184, 193) : IsWindowsTerminal ? Rgb(63, 63, 63) : Rgb(78, 80, 84));
    public static Color CursorFill => IsPhosphor ? Color.FromArgb(96, 57, 255, 20) : IsLight ? Color.FromArgb(90, 36, 41, 47) : Color.FromArgb(90, 230, 230, 230);
    public static Color ScrollTrack => _derivedChrome?.ScrollTrack ?? (IsLight ? Rgb(246, 248, 250) : Rgb(17, 17, 17));
    public static Color ScrollThumb => _derivedChrome?.ScrollThumb ?? (IsLight ? Rgb(190, 196, 203) : Rgb(85, 85, 85));
    public static Color ScrollThumbHover => _derivedChrome?.ScrollThumbHover ?? (IsLight ? Rgb(150, 158, 167) : Rgb(136, 136, 136));
    public static Color ScrollThumbPressed => _derivedChrome?.ScrollThumbPressed ?? (IsLight ? Rgb(110, 119, 129) : Rgb(170, 170, 170));
    public static Color CursorBorder => IsLight ? Rgb(110, 119, 129) : Colors.LightGray;

    // A scheme switches to its own dark or light theme.
    public static void Apply(AppThemeKind theme, ColorScheme? scheme = null)
    {
        Current = scheme?.Theme ?? theme;
        Scheme = scheme;
        _derivedChrome = scheme is not null && !IsPhosphor && !IsWindowsTerminal && !IsCampbell
            ? CreateDerivedChrome(scheme)
            : null;
        theme = Current;
        TerminalBuffer.DefaultForeground = DefaultForeground;
        TerminalBuffer.DefaultBackground = DefaultBackground;
        var light = theme == AppThemeKind.Light;
        var resources = Application.Current.Resources;

        void Set(string key, byte r, byte g, byte b, byte lr, byte lg, byte lb)
        {
            resources[key] = Frozen(light ? Rgb(lr, lg, lb) : Rgb(r, g, b));
        }

        Set("TerminalBackgroundBrush", 12, 12, 12, 255, 255, 255);
        Set("TerminalForegroundBrush", 230, 230, 230, 36, 41, 47);
        Set("TitleBarBrush", 32, 33, 36, 222, 226, 231);
        Set("ToolbarBrush", 48, 49, 52, 246, 248, 250);
        Set("ToolbarBorderBrush", 60, 64, 67, 208, 215, 222);
        Set("ChromeForegroundBrush", 240, 240, 240, 31, 35, 40);
        Set("ChromeStrongForegroundBrush", 255, 255, 255, 0, 0, 0);
        Set("MutedForegroundBrush", 139, 148, 158, 87, 96, 106);
        Set("AccentBrush", 114, 214, 197, 15, 118, 110);
        Set("FocusBrush", 168, 199, 250, 11, 87, 208);
        Set("MenuBackgroundBrush", 37, 37, 38, 255, 255, 255);
        Set("MenuBorderBrush", 63, 63, 70, 208, 215, 222);
        Set("MenuHighlightBrush", 58, 58, 58, 234, 238, 242);
        Set("MenuOpenBrush", 65, 65, 65, 220, 225, 230);
        Set("SeparatorBrush", 69, 70, 74, 216, 222, 228);
        Set("CaptionHoverBrush", 64, 64, 64, 205, 210, 216);
        Set("CaptionPressedBrush", 80, 80, 80, 190, 196, 203);
        Set("ButtonPressedBrush", 70, 70, 70, 208, 215, 222);
        Set("RoundButtonHoverBrush", 64, 65, 69, 205, 210, 216);
        Set("RoundButtonPressedBrush", 81, 82, 86, 190, 196, 203);
        Set("RoundButtonFocusBrush", 69, 79, 96, 196, 214, 240);

        if (IsWindowsTerminal)
        {
            resources["TitleBarBrush"] = Frozen(Rgb(32, 32, 32));
            resources["ToolbarBrush"] = Frozen(Rgb(45, 45, 45));
            resources["ToolbarBorderBrush"] = Frozen(Rgb(63, 63, 63));
            resources["ChromeForegroundBrush"] = Frozen(Rgb(242, 242, 242));
            resources["ChromeStrongForegroundBrush"] = Frozen(Rgb(255, 255, 255));
            resources["MutedForegroundBrush"] = Frozen(Rgb(160, 160, 160));
            resources["AccentBrush"] = Frozen(Rgb(0, 120, 212));
            resources["FocusBrush"] = Frozen(Rgb(76, 194, 255));
            resources["MenuBackgroundBrush"] = Frozen(Rgb(45, 45, 45));
            resources["MenuBorderBrush"] = Frozen(Rgb(63, 63, 63));
            resources["MenuHighlightBrush"] = Frozen(Rgb(58, 58, 58));
            resources["MenuOpenBrush"] = Frozen(Rgb(66, 66, 66));
            resources["SeparatorBrush"] = Frozen(Rgb(63, 63, 63));
            resources["CaptionHoverBrush"] = Frozen(Rgb(56, 56, 56));
            resources["CaptionPressedBrush"] = Frozen(Rgb(69, 69, 69));
            resources["ButtonPressedBrush"] = Frozen(Rgb(69, 69, 69));
            resources["RoundButtonHoverBrush"] = Frozen(Rgb(58, 58, 58));
            resources["RoundButtonPressedBrush"] = Frozen(Rgb(76, 76, 76));
            resources["RoundButtonFocusBrush"] = Frozen(Rgb(0, 120, 212));
        }

        if (IsPhosphor)
        {
            resources["AccentBrush"] = Frozen(Rgb(57, 255, 20));
            resources["FocusBrush"] = Frozen(Rgb(57, 255, 20));
            resources["TerminalBackgroundBrush"] = Frozen(Rgb(0, 0, 0));
            resources["TerminalForegroundBrush"] = Frozen(Rgb(57, 255, 20));
        }

        if (_derivedChrome is { } chrome)
        {
            resources["TitleBarBrush"] = Frozen(chrome.TitleBar);
            resources["ToolbarBrush"] = Frozen(chrome.Toolbar);
            resources["ToolbarBorderBrush"] = Frozen(chrome.Border);
            resources["ChromeForegroundBrush"] = Frozen(chrome.Foreground);
            resources["ChromeStrongForegroundBrush"] = Frozen(chrome.StrongForeground);
            resources["MutedForegroundBrush"] = Frozen(chrome.MutedForeground);
            resources["AccentBrush"] = Frozen(chrome.Accent);
            resources["FocusBrush"] = Frozen(chrome.Accent);
            resources["MenuBackgroundBrush"] = Frozen(chrome.MenuBackground);
            resources["MenuBorderBrush"] = Frozen(chrome.Border);
            resources["MenuHighlightBrush"] = Frozen(chrome.MenuHighlight);
            resources["MenuOpenBrush"] = Frozen(chrome.MenuOpen);
            resources["SeparatorBrush"] = Frozen(chrome.Separator);
            resources["CaptionHoverBrush"] = Frozen(chrome.CaptionHover);
            resources["CaptionPressedBrush"] = Frozen(chrome.CaptionPressed);
            resources["ButtonPressedBrush"] = Frozen(chrome.ButtonPressed);
            resources["RoundButtonHoverBrush"] = Frozen(chrome.RoundHover);
            resources["RoundButtonPressedBrush"] = Frozen(chrome.RoundPressed);
            resources["RoundButtonFocusBrush"] = Frozen(chrome.RoundFocus);
        }

        if (scheme is not null)
        {
            resources["TerminalBackgroundBrush"] = Frozen(ToColor(scheme.DefaultBackground));
            resources["TerminalForegroundBrush"] = Frozen(ToColor(scheme.DefaultForeground));
        }

        // Menus in the window share the terminal scrollbar style.
        resources["ScrollTrackBrush"] = Frozen(ScrollTrack);
        resources["ScrollThumbBrush"] = Frozen(ScrollThumb);
        resources["ScrollThumbHoverBrush"] = Frozen(ScrollThumbHover);
        resources["ScrollThumbPressedBrush"] = Frozen(ScrollThumbPressed);
        resources["DialogBackgroundBrush"] = resources["TitleBarBrush"];
        resources["DialogSurfaceBrush"] = resources["MenuBackgroundBrush"];
        resources["DialogInputBrush"] = resources["TerminalBackgroundBrush"];
        resources["DialogForegroundBrush"] = resources["ChromeForegroundBrush"];
        resources["DialogMutedBrush"] = resources["MutedForegroundBrush"];
        resources["DialogBorderBrush"] = resources["MenuBorderBrush"];
        resources["DialogSelectionBrush"] = resources["MenuHighlightBrush"];
        resources["DialogAccentBrush"] = resources["AccentBrush"];
        var accentColor = resources["AccentBrush"] is SolidColorBrush accentBrush
            ? accentBrush.Color
            : Rgb(15, 118, 110);
        resources["DialogAccentForegroundBrush"] = Frozen(ContrastForeground(accentColor));
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Color ToColor(TerminalColor color) =>
        Color.FromRgb(color.Red, color.Green, color.Blue);

    private static Color Rgb(byte red, byte green, byte blue) =>
        Color.FromRgb(red, green, blue);

    private static DerivedChrome CreateDerivedChrome(ColorScheme scheme)
    {
        var background = ToColor(scheme.DefaultBackground);
        var foreground = ToColor(scheme.DefaultForeground);
        var light = scheme.Theme == AppThemeKind.Light;
        var title = Blend(foreground, background, light ? .035 : .075);
        var toolbar = Blend(foreground, background, light ? .075 : .14);
        var menu = Blend(foreground, background, light ? .06 : .12);
        var border = Blend(foreground, background, light ? .28 : .24);
        var readableForeground = EnsureContrast(foreground, toolbar, 4.5);
        var highlight = EnsureContrast(Blend(foreground, toolbar, light ? .13 : .18), readableForeground, 4.5);
        var open = EnsureContrast(Blend(foreground, toolbar, light ? .2 : .26), readableForeground, 4.5);
        var mutedCandidate = Blend(background, foreground, .34);
        var muted = EnsureContrast(mutedCandidate, toolbar, 4.5);
        var accent = EnsureContrast(ToColor(scheme.Text(4)), toolbar, 4.5);
        var activeTab = Blend(foreground, background, light ? .04 : .09);
        var activeTabText = EnsureContrast(foreground, activeTab, 4.5);
        var hoverTab = Blend(foreground, toolbar, light ? .15 : .2);
        var inactiveTab = EnsureContrast(muted, title, 4.5);
        var pressed = Blend(foreground, toolbar, light ? .26 : .32);
        var thumb = Blend(foreground, background, light ? .3 : .25);
        return new(
            title, toolbar, border, readableForeground, EnsureContrast(foreground, toolbar, 4.5),
            muted, accent, menu, highlight, open, border, highlight, open, pressed,
            highlight, pressed, accent, activeTab, activeTabText, hoverTab, inactiveTab, border,
            menu, thumb, Blend(foreground, thumb, .22), Blend(foreground, thumb, .4));
    }

    private static Color Blend(Color foreground, Color background, double amount) => Color.FromRgb(
        MixChannel(background.R, foreground.R, amount),
        MixChannel(background.G, foreground.G, amount),
        MixChannel(background.B, foreground.B, amount));

    private static byte MixChannel(byte from, byte to, double amount) =>
        (byte)Math.Clamp(Math.Round(from + (to - from) * amount), 0, 255);

    private static Color EnsureContrast(Color color, Color background, double minimumRatio)
    {
        if (ContrastRatio(color, background) >= minimumRatio) return color;
        var black = ContrastCandidate(color, Colors.Black, background, minimumRatio);
        var white = ContrastCandidate(color, Colors.White, background, minimumRatio);
        if (black is null) return white?.Color ?? Colors.White;
        if (white is null) return black.Value.Color;
        return black.Value.Amount <= white.Value.Amount ? black.Value.Color : white.Value.Color;
    }

    private static (Color Color, double Amount)? ContrastCandidate(Color start, Color end, Color background, double minimumRatio)
    {
        if (ContrastRatio(end, background) < minimumRatio) return null;
        var low = 0d;
        var high = 1d;
        for (var i = 0; i < 20; i++)
        {
            var middle = (low + high) / 2;
            if (ContrastRatio(Blend(end, start, middle), background) >= minimumRatio) high = middle;
            else low = middle;
        }
        return (Blend(end, start, high), high);
    }

    private static double ContrastRatio(Color first, Color second)
    {
        var a = RelativeLuminance(first);
        var b = RelativeLuminance(second);
        return (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05);
    }

    private static Color ContrastForeground(Color background) =>
        ContrastRatio(Colors.White, background) >= ContrastRatio(Colors.Black, background)
            ? Colors.White
            : Colors.Black;

    private static double RelativeLuminance(Color color)
    {
        static double Linear(byte value)
        {
            var channel = value / 255d;
            return channel <= .04045 ? channel / 12.92 : Math.Pow((channel + .055) / 1.055, 2.4);
        }
        return .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
    }
}
