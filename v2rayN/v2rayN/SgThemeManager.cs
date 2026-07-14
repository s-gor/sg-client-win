using MaterialDesignColors;
using MaterialDesignColors.ColorManipulation;
using MaterialDesignThemes.Wpf;
using System.Windows.Media;

namespace v2rayN;

public sealed record SgThemeOption(string Id, string Name);

public static class SgThemeManager
{
    public const string Graphite = "SgGraphite";
    public const string Light = "SgLight";
    public const string Northern = "SgNorthern";

    public static event Action<string>? ThemeChanged;

    public static IReadOnlyList<SgThemeOption> Options { get; } =
    [
        new(Graphite, "Графит"),
        new(Light, "Латте"),
        new(Northern, "Северная")
    ];

    public static string Current => Normalize(AppManager.Instance.Config.UiItem.CurrentTheme);

    public static void Initialize()
    {
        var config = AppManager.Instance.Config;
        var normalized = Normalize(config.UiItem.CurrentTheme);
        config.UiItem.CurrentTheme = normalized;
        Apply(normalized);
    }

    public static async Task ApplyAndSaveAsync(string? theme)
    {
        var normalized = Normalize(theme);
        var config = AppManager.Instance.Config;
        config.UiItem.CurrentTheme = normalized;
        Apply(normalized);
        await ConfigHandler.SaveConfig(config);
        ThemeChanged?.Invoke(normalized);
    }

    public static string GetDisplayName(string? theme)
    {
        var normalized = Normalize(theme);
        return Options.First(item => item.Id == normalized).Name;
    }

    public static string Normalize(string? theme)
    {
        return theme switch
        {
            Light or nameof(ETheme.Light) => Light,
            Northern => Northern,
            Graphite or nameof(ETheme.Dark) or nameof(ETheme.FollowSystem) => Graphite,
            _ => Graphite
        };
    }

    private static void Apply(string themeName)
    {
        var palette = themeName switch
        {
            Light => CreateLightPalette(),
            Northern => CreateNorthernPalette(),
            _ => CreateGraphitePalette()
        };

        foreach (var item in palette.Brushes)
        {
            Application.Current.Resources[item.Key] = CreateBrush(item.Value);
        }

        Application.Current.Resources["SgPrimaryActionBrush"] = CreateVerticalGradient(
            palette.Brushes["SgPrimaryActionTopColor"],
            palette.Brushes["SgPrimaryActionBottomColor"]);
        Application.Current.Resources["SgPrimaryActionHoverBrush"] = CreateVerticalGradient(
            palette.Brushes["SgPrimaryActionHoverTopColor"],
            palette.Brushes["SgPrimaryActionHoverBottomColor"]);
        Application.Current.Resources["SgPrimaryActionPressedBrush"] = CreateVerticalGradient(
            palette.Brushes["SgPrimaryActionPressedTopColor"],
            palette.Brushes["SgPrimaryActionPressedBottomColor"]);

        var helper = new PaletteHelper();
        var materialTheme = helper.GetTheme();
        materialTheme.SetBaseTheme(palette.IsLight ? BaseTheme.Light : BaseTheme.Dark);
        var primary = ParseColor(palette.Primary);
        materialTheme.PrimaryLight = new ColorPair(primary.Lighten());
        materialTheme.PrimaryMid = new ColorPair(primary);
        materialTheme.PrimaryDark = new ColorPair(primary.Darken());
        helper.SetTheme(materialTheme);

        // MaterialDesign templates still use these legacy/new aliases for popup, menu and input surfaces.
        // Re-apply them after PaletteHelper so the SG light theme never falls back to pure white.
        Application.Current.Resources["MaterialDesignPaper"] = CreateBrush(palette.Brushes["SgSurfaceBrush"]);
        Application.Current.Resources["MaterialDesignBody"] = CreateBrush(palette.Brushes["SgTextBrush"]);
        Application.Current.Resources["MaterialDesignBodyLight"] = CreateBrush(palette.Brushes["SgMutedBrush"]);
        Application.Current.Resources["MaterialDesignDivider"] = CreateBrush(palette.Brushes["SgBorderBrush"]);
        Application.Current.Resources["MaterialDesign.Brush.Background"] = CreateBrush(palette.Brushes["SgSurfaceBrush"]);
        Application.Current.Resources["MaterialDesign.Brush.Foreground"] = CreateBrush(palette.Brushes["SgTextBrush"]);

        foreach (Window window in Application.Current.Windows)
        {
            WindowsUtils.SetDarkBorder(window, palette.IsLight ? nameof(ETheme.Light) : nameof(ETheme.Dark));
        }
    }

    private static ThemePalette CreateGraphitePalette() => new(false, "#35D69A", new Dictionary<string, string>
    {
        ["SgBackgroundBrush"] = "#0B121C",
        ["SgHeaderBrush"] = "#0E1723",
        ["SgSidebarBrush"] = "#101B29",
        ["SgSurfaceBrush"] = "#111D2B",
        ["SgSurfaceSoftBrush"] = "#0F1A27",
        ["SgSurfaceRaisedBrush"] = "#162438",
        ["SgBorderBrush"] = "#24364B",
        ["SgBorderStrongBrush"] = "#34506B",
        ["SgTextBrush"] = "#F4F7FA",
        ["SgMutedBrush"] = "#8A9AAF",
        ["SgAccentBrush"] = "#35D69A",
        ["SgTrafficDownloadBrush"] = "#4DB8FF",
        ["SgTrafficUploadBrush"] = "#39D6A0",
        ["SgTunHeroBrush"] = "#10251F",
        ["SgTunCardBrush"] = "#112A25",
        ["SgTunCardBorderBrush"] = "#245C4A",
        ["SgSystemProxyBrush"] = "#E9C46A",
        ["SgSystemProxySoftBrush"] = "#3A321A",
        ["SgSystemProxyBorderBrush"] = "#E9C46A",
        ["SgSystemProxyHeroBrush"] = "#2B2618",
        ["SgSystemProxyCardBrush"] = "#342D1B",
        ["SgSystemProxyCardBorderBrush"] = "#6F5B24",
        ["SgLocalProxyBrush"] = "#A78BFA",
        ["SgLocalProxySoftBrush"] = "#2B2345",
        ["SgLocalProxyBorderBrush"] = "#7059B0",
        ["SgLocalProxyHeroBrush"] = "#1B1930",
        ["SgLocalProxyCardBrush"] = "#211D39",
        ["SgLocalProxyCardBorderBrush"] = "#4B3F77",
        ["SgAccentSoftBrush"] = "#14372D",
        ["SgAccentBorderBrush"] = "#2A7A5C",
        ["SgSuccessBrush"] = "#35D69A",
        ["SgSuccessSoftBrush"] = "#14372D",
        ["SgWarningBrush"] = "#E9C46A",
        ["SgWarningSoftBrush"] = "#3A321A",
        ["SgErrorBrush"] = "#F08BA4",
        ["SgErrorSoftBrush"] = "#3B1E2A",
        ["SgOffBrush"] = "#65758A",
        ["SgHoverBrush"] = "#17263A",
        ["SgPressedBrush"] = "#20324A",
        ["SgSelectedBrush"] = "#182A3E",
        ["SgInputBrush"] = "#0E1926",
        ["SgHeroBrush"] = "#111D2B",
        ["SgHeroBusyBrush"] = "#211F18",
        ["SgHeroErrorBrush"] = "#251820",
        ["SgOnButtonBrush"] = "#EDF8F4",
        ["SgOnButtonTextBrush"] = "#12392C",
        ["SgNeutralActionBrush"] = "#102238",
        ["SgNeutralActionHoverBrush"] = "#162D49",
        ["SgNeutralActionPressedBrush"] = "#0C1B2D",
        ["SgNeutralActionBorderBrush"] = "#31516F",
        ["SgNeutralActionTextBrush"] = "#F4F7FA",
        ["SgSubtleTextBrush"] = "#718198",
        ["SgSecondaryActionBrush"] = "#162438",
        ["SgSecondaryActionHoverBrush"] = "#17263A",
        ["SgSecondaryActionPressedBrush"] = "#20324A",
        ["SgSecondaryActionBorderBrush"] = "#34506B",
        ["SgSecondaryActionTextBrush"] = "#F4F7FA",
        ["SgIconButtonBrush"] = "#162438",
        ["SgIconButtonHoverBrush"] = "#20324A",
        ["SgIconButtonPressedBrush"] = "#263B56",
        ["SgDisabledBrush"] = "#0F1A27",
        ["SgDisabledBorderBrush"] = "#24364B",
        ["SgDisabledTextBrush"] = "#65758A",
        ["SgTileBrush"] = "#0F1A27",
        ["SgTileHoverBrush"] = "#17263A",
        ["SgTileActiveBrush"] = "#182A3E",
        ["SgTileIconBrush"] = "#162438",
        ["SgTrafficCardBrush"] = "#0F1A27",
        ["SgTrafficSectionBrush"] = "#162438",
        ["SgDangerSoftBrush"] = "#3B1E2A",
        ["SgPrimaryActionTopColor"] = "#163B36",
        ["SgPrimaryActionBottomColor"] = "#102E2A",
        ["SgPrimaryActionHoverTopColor"] = "#1B4B43",
        ["SgPrimaryActionHoverBottomColor"] = "#143C36",
        ["SgPrimaryActionPressedTopColor"] = "#102E2A",
        ["SgPrimaryActionPressedBottomColor"] = "#0C2421",
        ["SgPrimaryActionBorderBrush"] = "#2A7A5C",
        ["SgPrimaryActionTextBrush"] = "#EDF8F4",
        ["SgLogoFillBrush"] = "#10242B"
    });

    private static ThemePalette CreateLightPalette() => new(true, "#31566F", new Dictionary<string, string>
    {
        // Latte Graphite: warm coffee-and-milk surfaces with a restrained steel-blue accent.
        ["SgBackgroundBrush"] = "#D8CEC1",
        ["SgHeaderBrush"] = "#CDBEAD",
        ["SgSidebarBrush"] = "#D2C4B4",
        ["SgSurfaceBrush"] = "#DDD2C5",
        ["SgSurfaceSoftBrush"] = "#CFC2B3",
        ["SgSurfaceRaisedBrush"] = "#E1D7CB",
        ["SgBorderBrush"] = "#7C6E5F",
        ["SgBorderStrongBrush"] = "#66584C",
        ["SgTextBrush"] = "#2B2521",
        ["SgMutedBrush"] = "#62584F",
        ["SgSubtleTextBrush"] = "#6E6052",
        ["SgAccentBrush"] = "#31566F",
        ["SgTrafficDownloadBrush"] = "#31566F",
        ["SgTrafficUploadBrush"] = "#4E7B48",

        // Connection-state scenes stay warm and calm instead of recolouring the whole application.
        ["SgTunHeroBrush"] = "#D9DFC9",
        ["SgTunCardBrush"] = "#CFD8BC",
        ["SgTunCardBorderBrush"] = "#7D986A",
        ["SgSystemProxyBrush"] = "#9B6C24",
        ["SgSystemProxySoftBrush"] = "#E2D1AD",
        ["SgSystemProxyBorderBrush"] = "#B48A47",
        ["SgSystemProxyHeroBrush"] = "#E4D2AD",
        ["SgSystemProxyCardBrush"] = "#D8C59E",
        ["SgSystemProxyCardBorderBrush"] = "#A97C38",
        ["SgLocalProxyBrush"] = "#31566F",
        ["SgLocalProxySoftBrush"] = "#C8D3D8",
        ["SgLocalProxyBorderBrush"] = "#7890A0",
        ["SgLocalProxyHeroBrush"] = "#D0D8D8",
        ["SgLocalProxyCardBrush"] = "#C2CFD2",
        ["SgLocalProxyCardBorderBrush"] = "#6F8999",

        ["SgAccentSoftBrush"] = "#C3D0D4",
        ["SgAccentBorderBrush"] = "#31566F",
        ["SgSuccessBrush"] = "#47793A",
        ["SgSuccessSoftBrush"] = "#D7E3CB",
        ["SgWarningBrush"] = "#A86F1D",
        ["SgWarningSoftBrush"] = "#EAD8B6",
        ["SgErrorBrush"] = "#C43C32",
        ["SgErrorSoftBrush"] = "#ECD0C9",
        ["SgOffBrush"] = "#85796E",
        ["SgHoverBrush"] = "#C4B29F",
        ["SgPressedBrush"] = "#B7A490",
        ["SgSelectedBrush"] = "#C1CDD2",
        ["SgInputBrush"] = "#E1D7CB",
        ["SgHeroBrush"] = "#DDD2C5",
        ["SgHeroBusyBrush"] = "#E6D9BC",
        ["SgHeroErrorBrush"] = "#E8CFC8",
        ["SgOnButtonBrush"] = "#31566F",
        ["SgOnButtonTextBrush"] = "#FFFFFF",

        // Steel-blue actions are reserved for primary/important commands.
        ["SgNeutralActionBrush"] = "#31566F",
        ["SgNeutralActionHoverBrush"] = "#3B6682",
        ["SgNeutralActionPressedBrush"] = "#294A61",
        ["SgNeutralActionBorderBrush"] = "#27475C",
        ["SgNeutralActionTextBrush"] = "#FFFFFF",

        // Ordinary actions stay in the coffee family.
        ["SgSecondaryActionBrush"] = "#786858",
        ["SgSecondaryActionHoverBrush"] = "#877565",
        ["SgSecondaryActionPressedBrush"] = "#66584B",
        ["SgSecondaryActionBorderBrush"] = "#66584C",
        ["SgSecondaryActionTextBrush"] = "#FFFFFF",
        ["SgIconButtonBrush"] = "#CFC2B3",
        ["SgIconButtonHoverBrush"] = "#C4B29F",
        ["SgIconButtonPressedBrush"] = "#B7A490",
        ["SgDisabledBrush"] = "#D1C7BC",
        ["SgDisabledBorderBrush"] = "#AA9C8D",
        ["SgDisabledTextBrush"] = "#85796E",

        ["SgTileBrush"] = "#DDD2C5",
        ["SgTileHoverBrush"] = "#CFC2B3",
        ["SgTileActiveBrush"] = "#C1CDD2",
        ["SgTileIconBrush"] = "#C8D2D5",
        ["SgTrafficCardBrush"] = "#DDD2C5",
        ["SgTrafficSectionBrush"] = "#E1D7CB",
        ["SgDangerSoftBrush"] = "#ECD0C9",

        ["SgPrimaryActionTopColor"] = "#3A647F",
        ["SgPrimaryActionBottomColor"] = "#31566F",
        ["SgPrimaryActionHoverTopColor"] = "#46738F",
        ["SgPrimaryActionHoverBottomColor"] = "#39647F",
        ["SgPrimaryActionPressedTopColor"] = "#294A61",
        ["SgPrimaryActionPressedBottomColor"] = "#223F53",
        ["SgPrimaryActionBorderBrush"] = "#27475C",
        ["SgPrimaryActionTextBrush"] = "#FFFFFF",
        ["SgLogoFillBrush"] = "#D2C4B4"
    });


    private static ThemePalette CreateNorthernPalette() => new(false, "#4BA3FF", new Dictionary<string, string>
    {
        ["SgBackgroundBrush"] = "#091523",
        ["SgHeaderBrush"] = "#0D1B2B",
        ["SgSidebarBrush"] = "#0E2033",
        ["SgSurfaceBrush"] = "#10243A",
        ["SgSurfaceSoftBrush"] = "#0C1E31",
        ["SgSurfaceRaisedBrush"] = "#17314D",
        ["SgBorderBrush"] = "#26445F",
        ["SgBorderStrongBrush"] = "#386788",
        ["SgTextBrush"] = "#F3F8FC",
        ["SgMutedBrush"] = "#89A2B9",
        ["SgAccentBrush"] = "#4BA3FF",
        ["SgTrafficDownloadBrush"] = "#67B8FF",
        ["SgTrafficUploadBrush"] = "#39D98A",
        ["SgTunHeroBrush"] = "#102C28",
        ["SgTunCardBrush"] = "#12362F",
        ["SgTunCardBorderBrush"] = "#245F50",
        ["SgSystemProxyBrush"] = "#F0BE5A",
        ["SgSystemProxySoftBrush"] = "#3A301A",
        ["SgSystemProxyBorderBrush"] = "#F0BE5A",
        ["SgSystemProxyHeroBrush"] = "#2A2418",
        ["SgSystemProxyCardBrush"] = "#342B19",
        ["SgSystemProxyCardBorderBrush"] = "#735C23",
        ["SgLocalProxyBrush"] = "#B19CFF",
        ["SgLocalProxySoftBrush"] = "#2C2850",
        ["SgLocalProxyBorderBrush"] = "#7165B7",
        ["SgLocalProxyHeroBrush"] = "#1B1B38",
        ["SgLocalProxyCardBrush"] = "#232144",
        ["SgLocalProxyCardBorderBrush"] = "#4B467C",
        ["SgAccentSoftBrush"] = "#153A5D",
        ["SgAccentBorderBrush"] = "#337FBD",
        ["SgSuccessBrush"] = "#39D98A",
        ["SgSuccessSoftBrush"] = "#123A2B",
        ["SgWarningBrush"] = "#F0BE5A",
        ["SgWarningSoftBrush"] = "#3A301A",
        ["SgErrorBrush"] = "#F07F9A",
        ["SgErrorSoftBrush"] = "#3A1D2A",
        ["SgOffBrush"] = "#68839B",
        ["SgHoverBrush"] = "#17314D",
        ["SgPressedBrush"] = "#1E4164",
        ["SgSelectedBrush"] = "#193C5B",
        ["SgInputBrush"] = "#0C1D2E",
        ["SgHeroBrush"] = "#10243A",
        ["SgHeroBusyBrush"] = "#26261E",
        ["SgHeroErrorBrush"] = "#281A24",
        ["SgOnButtonBrush"] = "#EAF8F1",
        ["SgOnButtonTextBrush"] = "#123A2B",
        ["SgNeutralActionBrush"] = "#163B62",
        ["SgNeutralActionHoverBrush"] = "#1D4C7B",
        ["SgNeutralActionPressedBrush"] = "#102E4D",
        ["SgNeutralActionBorderBrush"] = "#3D75A8",
        ["SgNeutralActionTextBrush"] = "#F3F8FC",
        ["SgSubtleTextBrush"] = "#708BA5",
        ["SgSecondaryActionBrush"] = "#17314D",
        ["SgSecondaryActionHoverBrush"] = "#1E4164",
        ["SgSecondaryActionPressedBrush"] = "#102E4D",
        ["SgSecondaryActionBorderBrush"] = "#386788",
        ["SgSecondaryActionTextBrush"] = "#F3F8FC",
        ["SgIconButtonBrush"] = "#17314D",
        ["SgIconButtonHoverBrush"] = "#1E4164",
        ["SgIconButtonPressedBrush"] = "#255179",
        ["SgDisabledBrush"] = "#0C1E31",
        ["SgDisabledBorderBrush"] = "#26445F",
        ["SgDisabledTextBrush"] = "#68839B",
        ["SgTileBrush"] = "#0C1E31",
        ["SgTileHoverBrush"] = "#17314D",
        ["SgTileActiveBrush"] = "#193C5B",
        ["SgTileIconBrush"] = "#17314D",
        ["SgTrafficCardBrush"] = "#0C1E31",
        ["SgTrafficSectionBrush"] = "#17314D",
        ["SgDangerSoftBrush"] = "#3A1D2A",
        ["SgPrimaryActionTopColor"] = "#1D4C7B",
        ["SgPrimaryActionBottomColor"] = "#163B62",
        ["SgPrimaryActionHoverTopColor"] = "#245C91",
        ["SgPrimaryActionHoverBottomColor"] = "#1D4C7B",
        ["SgPrimaryActionPressedTopColor"] = "#163B62",
        ["SgPrimaryActionPressedBottomColor"] = "#102E4D",
        ["SgPrimaryActionBorderBrush"] = "#3D75A8",
        ["SgPrimaryActionTextBrush"] = "#F3F8FC",
        ["SgLogoFillBrush"] = "#102D46"
    });

    private static SolidColorBrush CreateBrush(string color)
    {
        var brush = new SolidColorBrush(ParseColor(color));
        brush.Freeze();
        return brush;
    }

    private static LinearGradientBrush CreateVerticalGradient(string top, string bottom)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new System.Windows.Point(0.5, 0),
            EndPoint = new System.Windows.Point(0.5, 1),
            GradientStops = new GradientStopCollection
            {
                new(ParseColor(top), 0),
                new(ParseColor(bottom), 1)
            }
        };
        brush.Freeze();
        return brush;
    }

    private static Color ParseColor(string value) => (Color)ColorConverter.ConvertFromString(value);

    private sealed record ThemePalette(bool IsLight, string Primary, IReadOnlyDictionary<string, string> Brushes);
}
