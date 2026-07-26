using MaterialDesignColors;
using MaterialDesignColors.ColorManipulation;
using MaterialDesignThemes.Wpf;
using System.Windows.Media;
using System.Windows.Media.Effects;

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
        new(Northern, "Север")
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

        // One exact outline brush for enabled and disabled buttons in every theme.
        // Disabled state is shown by fill/text only; the border color must not change.
        var buttonOutlineBrush = CreateBrush(palette.Brushes["SgBorderStrongBrush"]);
        Application.Current.Resources["SgButtonOutlineBrush"] = buttonOutlineBrush;
        Application.Current.Resources["SgButtonDisabledOutlineBrush"] = buttonOutlineBrush;

        if (themeName == Light)
        {
            Application.Current.Resources["SgBackgroundBrush"] = CreateLatteBackground();
            Application.Current.Resources["SgHeaderBrush"] = CreateVerticalGradient("#F1F3EF", "#DCE4DD");
            Application.Current.Resources["SgSidebarBrush"] = CreateVerticalGradient("#E8EFF2", "#D8E1E4");
            Application.Current.Resources["SgSurfaceBrush"] = CreateVerticalGradient("#FEFCF7", "#F2ECE1");
            Application.Current.Resources["SgSurfaceSoftBrush"] = CreateVerticalGradient("#F1EADE", "#E1D8CA");
            Application.Current.Resources["SgSurfaceRaisedBrush"] = CreateVerticalGradient("#F8F0E4", "#E8D9C3");
            Application.Current.Resources["SgInputBrush"] = CreateVerticalGradient("#FFFDFC", "#F6F1E7");
            Application.Current.Resources["SgHeroBrush"] = CreateVerticalGradient("#FAF6EE", "#EEE5D7");
            Application.Current.Resources["SgTunHeroBrush"] = CreateVerticalGradient("#FAF6EE", "#EEE5D7");
            Application.Current.Resources["SgSystemProxyHeroBrush"] = CreateVerticalGradient("#FAF6EE", "#EEE5D7");
            Application.Current.Resources["SgLocalProxyHeroBrush"] = CreateVerticalGradient("#FAF6EE", "#EEE5D7");
            Application.Current.Resources["SgDisabledBrush"] = CreateVerticalGradient("#F6F0E6", "#E7DCCB");
            Application.Current.Resources["SgTrafficCardBrush"] = CreateVerticalGradient("#FCFAF5", "#EFE9DE");
            Application.Current.Resources["SgTrafficSectionBrush"] = CreateVerticalGradient("#FAF4EA", "#ECE2D2");
            Application.Current.Resources["SgSecondaryActionBrush"] = CreateVerticalGradient("#FCF8F1", "#EEE5D7");
            Application.Current.Resources["SgSecondaryActionHoverBrush"] = CreateVerticalGradient("#FFF9F2", "#F2E6D5");
            Application.Current.Resources["SgSecondaryActionPressedBrush"] = CreateVerticalGradient("#E8DCCB", "#DCCDB8");
            Application.Current.Resources["SgIconButtonBrush"] = CreateVerticalGradient("#FBF6EE", "#ECE2D4");
            Application.Current.Resources["SgIconButtonHoverBrush"] = CreateVerticalGradient("#FFF9F1", "#F3E6D4");
            Application.Current.Resources["SgIconButtonPressedBrush"] = CreateVerticalGradient("#E8DBC7", "#D9CAB5");
            Application.Current.Resources["SgTileBrush"] = CreateVerticalGradient("#FBF8F2", "#EFE7DA");
            Application.Current.Resources["SgTileHoverBrush"] = CreateVerticalGradient("#FFF9F3", "#F2E6D3");
            Application.Current.Resources["SgTileActiveBrush"] = CreateVerticalGradient("#739E88", "#4E7965");
            Application.Current.Resources["SgNeutralActionBrush"] = CreateVerticalGradient("#709A84", "#4D7864");
            Application.Current.Resources["SgNeutralActionHoverBrush"] = CreateVerticalGradient("#7EAA93", "#5B866F");
            Application.Current.Resources["SgNeutralActionPressedBrush"] = CreateVerticalGradient("#4F7764", "#3E6050");
            Application.Current.Resources["SgToolbarButtonBrush"] = CreateVerticalGradient("#FDF9F2", "#EEE5D7");
            Application.Current.Resources["SgToolbarButtonHoverBrush"] = CreateVerticalGradient("#FFF9F1", "#F4E7D5");
            Application.Current.Resources["SgToolbarButtonPressedBrush"] = CreateVerticalGradient("#E8DCC8", "#DCCCB6");
            Application.Current.Resources["SgToolbarIconButtonBrush"] = CreateVerticalGradient("#FCF8F1", "#EEE5D8");
            Application.Current.Resources["SgToolbarIconButtonHoverBrush"] = CreateVerticalGradient("#FFF9F1", "#F4E7D6");
            Application.Current.Resources["SgToolbarIconButtonPressedBrush"] = CreateVerticalGradient("#E7DBC7", "#D9CAB5");
            Application.Current.Resources["SgButtonShadowEffect"] = CreateShadowEffect("#2B342E", 12, 2, 0.20);
            Application.Current.Resources["SgCardShadowEffect"] = CreateShadowEffect("#2B342E", 18, 4, 0.17);
        }
        else
        {
            Application.Current.Resources["SgToolbarButtonBrush"] = CreateBrush("#00FFFFFF");
            Application.Current.Resources["SgToolbarButtonHoverBrush"] = CreateBrush("#17263A");
            Application.Current.Resources["SgToolbarButtonPressedBrush"] = CreateBrush("#20324A");
            Application.Current.Resources["SgToolbarIconButtonBrush"] = CreateBrush("#00FFFFFF");
            Application.Current.Resources["SgToolbarIconButtonHoverBrush"] = CreateBrush("#20324A");
            Application.Current.Resources["SgToolbarIconButtonPressedBrush"] = CreateBrush("#263B56");
            Application.Current.Resources["SgButtonShadowEffect"] = CreateShadowEffect("#000000", 0, 0, 0);
            Application.Current.Resources["SgCardShadowEffect"] = CreateShadowEffect("#000000", 0, 0, 0);
        }

        ApplyLegacy060CompatibilityAliases();

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


    // SG Client 095: Graphite and Northern use the exact SG Client 060/GitHub palette.
    // Newer controls receive aliases to those original brushes instead of introducing new colours.
    private static void ApplyLegacy060CompatibilityAliases()
    {
        SetAlias("SgSuccessBorderBrush", "SgAccentBorderBrush");
        SetAlias("SgSuccessTextBrush", "SgSuccessBrush");
        SetAlias("SgSuccessDotBrush", "SgSuccessBrush");
        SetAlias("SgSuccessHoverSoftBrush", "SgAccentSoftBrush");
        SetAlias("SgWarningButtonBrush", "SgWarningBrush");
        SetAlias("SgWarningButtonBorderBrush", "SgWarningBrush");
        SetAlias("SgDangerButtonBrush", "SgErrorBrush");
        SetAlias("SgDangerButtonBorderBrush", "SgErrorBrush");
        SetAlias("SgDangerButtonTextBrush", "SgPrimaryActionTextBrush");

        SetAlias("SgConnectionsWindowBrush", "SgBackgroundBrush");
        SetAlias("SgConnectionsPanelBrush", "SgSurfaceBrush");
        SetAlias("SgConnectionsPanelRaisedBrush", "SgSurfaceRaisedBrush");
        SetAlias("SgConnectionsTableBrush", "SgSurfaceSoftBrush");
        SetAlias("SgConnectionsTableHeaderBrush", "SgSurfaceRaisedBrush");
        SetAlias("SgConnectionsTableAltBrush", "SgSurfaceBrush");
        SetAlias("SgConnectionsTableBorderBrush", "SgBorderBrush");
        SetAlias("SgConnectionsTableHoverBrush", "SgHoverBrush");
        SetAlias("SgConnectionsTableSelectedBrush", "SgSelectedBrush");

    }

    private static void SetAlias(string target, string source)
    {
        if (Application.Current.Resources.Contains(source))
        {
            Application.Current.Resources[target] = Application.Current.Resources[source];
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
        ["SgConnectionsVpnBadgeBrush"] = "#132B43",
        ["SgConnectionsVpnBadgeBorderBrush"] = "#31516F",
        ["SgConnectionsVpnBadgeTextBrush"] = "#74C0FF",
        ["SgConnectionsDirectBadgeBrush"] = "#15362D",
        ["SgConnectionsDirectBadgeBorderBrush"] = "#2A7A5C",
        ["SgConnectionsDirectBadgeTextBrush"] = "#54DFA8",
        ["SgConnectionsBlockBadgeBrush"] = "#332229",
        ["SgConnectionsBlockBadgeBorderBrush"] = "#7A4652",
        ["SgConnectionsBlockBadgeTextBrush"] = "#D98A98",
        ["SgConnectionsOtherBadgeBrush"] = "#1A2431",
        ["SgConnectionsOtherBadgeBorderBrush"] = "#3A4A5C",
        ["SgConnectionsOtherBadgeTextBrush"] = "#9AA8B8",

        ["SgPrimaryActionTopColor"] = "#1A493F",
        ["SgPrimaryActionBottomColor"] = "#143A33",
        ["SgPrimaryActionHoverTopColor"] = "#20564A",
        ["SgPrimaryActionHoverBottomColor"] = "#19453D",
        ["SgPrimaryActionPressedTopColor"] = "#102E2A",
        ["SgPrimaryActionPressedBottomColor"] = "#0C2421",
        ["SgPrimaryActionBorderBrush"] = "#2A7A5C",
        ["SgPrimaryActionTextBrush"] = "#EDF8F4",
        ["SgLogoFillBrush"] = "#10242B"
    });

    private static ThemePalette CreateLightPalette() => new(true, "#456F5C", new Dictionary<string, string>
    {
        // Luxury Jade — ivory, warm stone, jade and restrained champagne-gold accents.
        ["SgBackgroundBrush"] = "#E5ECE7",
        ["SgHeaderBrush"] = "#D9E5DE",
        ["SgSidebarBrush"] = "#DCE5E0",
        ["SgSurfaceBrush"] = "#F8F5EE",
        ["SgSurfaceSoftBrush"] = "#DDDAD2",
        ["SgSurfaceRaisedBrush"] = "#EFE5D5",
        ["SgBorderBrush"] = "#89968A",
        ["SgBorderStrongBrush"] = "#68786C",
        ["SgTextBrush"] = "#29312C",
        ["SgMutedBrush"] = "#66716A",
        ["SgSubtleTextBrush"] = "#7C857E",
        ["SgAccentBrush"] = "#456F5C",
        ["SgTrafficDownloadBrush"] = "#356B56",
        ["SgTrafficUploadBrush"] = "#B88A45",

        // Mode scenes: refined jade and champagne rather than flat grey slabs.
        ["SgTunHeroBrush"] = "#F8F5EE",
        ["SgTunCardBrush"] = "#B9C8B9",
        ["SgTunCardBorderBrush"] = "#6E8876",
        ["SgSystemProxyBrush"] = "#93672E",
        ["SgSystemProxySoftBrush"] = "#E8D0AA",
        ["SgSystemProxyBorderBrush"] = "#B88A45",
        ["SgSystemProxyHeroBrush"] = "#F8F5EE",
        ["SgSystemProxyCardBrush"] = "#E2C394",
        ["SgSystemProxyCardBorderBrush"] = "#B88A45",
        ["SgLocalProxyBrush"] = "#456F5C",
        ["SgLocalProxySoftBrush"] = "#D6E0D8",
        ["SgLocalProxyBorderBrush"] = "#78927F",
        ["SgLocalProxyHeroBrush"] = "#F8F5EE",
        ["SgLocalProxyCardBrush"] = "#AFC0B0",
        ["SgLocalProxyCardBorderBrush"] = "#6E8876",

        ["SgAccentSoftBrush"] = "#24456F5C",
        ["SgAccentBorderBrush"] = "#456F5C",
        ["SgSuccessBrush"] = "#456F5C",
        ["SgSuccessSoftBrush"] = "#C9D6CB",
        ["SgWarningBrush"] = "#93672E",
        ["SgWarningSoftBrush"] = "#E8D0AA",
        ["SgErrorBrush"] = "#934D54",
        ["SgErrorSoftBrush"] = "#E9D4D6",
        ["SgSuccessBorderBrush"] = "#78927F",
        ["SgSuccessTextBrush"] = "#304E40",
        ["SgSuccessDotBrush"] = "#456F5C",
        ["SgSuccessHoverSoftBrush"] = "#BBCBBB",
        ["SgWarningButtonBrush"] = "#B88A45",
        ["SgWarningButtonBorderBrush"] = "#93672E",
        ["SgDangerButtonBrush"] = "#A75A62",
        ["SgDangerButtonBorderBrush"] = "#934D54",
        ["SgDangerButtonTextBrush"] = "#FFFFFF",
        ["SgOffBrush"] = "#7C857E",
        ["SgHoverBrush"] = "#1F456F5C",
        ["SgPressedBrush"] = "#345A49",
        ["SgSelectedBrush"] = "#2B456F5C",
        ["SgInputBrush"] = "#FBFAF6",
        ["SgHeroBrush"] = "#F8F5EE",
        ["SgHeroBusyBrush"] = "#F2E6D2",
        ["SgHeroErrorBrush"] = "#E9D4D6",
        ["SgOnButtonBrush"] = "#456F5C",
        ["SgOnButtonTextBrush"] = "#FFFFFF",

        // Main actions: deep jade gradient, white text and a restrained champagne edge.
        ["SgNeutralActionBrush"] = "#456F5C",
        ["SgNeutralActionHoverBrush"] = "#567F6B",
        ["SgNeutralActionPressedBrush"] = "#345A49",
        ["SgNeutralActionBorderBrush"] = "#B88A45",
        ["SgNeutralActionTextBrush"] = "#FFFFFF",

        // Secondary controls: ivory/stone surfaces with neutral jade-grey borders.
        ["SgSecondaryActionBrush"] = "#E8E3DA",
        ["SgSecondaryActionHoverBrush"] = "#F3E7D3",
        ["SgSecondaryActionPressedBrush"] = "#D6CDBD",
        ["SgSecondaryActionBorderBrush"] = "#89968A",
        ["SgSecondaryActionTextBrush"] = "#29312C",
        ["SgIconButtonBrush"] = "#E6E1D8",
        ["SgIconButtonHoverBrush"] = "#F1E5D0",
        ["SgIconButtonPressedBrush"] = "#D3C9B8",
        ["SgDisabledBrush"] = "#DDDAD2",
        ["SgDisabledBorderBrush"] = "#B7B7AE",
        ["SgDisabledTextBrush"] = "#8A908B",

        ["SgTileBrush"] = "#F8F5EE",
        ["SgTileHoverBrush"] = "#F2E8D8",
        ["SgTileActiveBrush"] = "#A8B9AA",
        ["SgTileIconBrush"] = "#D6E0D8",
        ["SgTrafficCardBrush"] = "#F8F5EE",
        ["SgTrafficSectionBrush"] = "#E5E2DB",
        ["SgDangerSoftBrush"] = "#E9D4D6",

        // Connections: ivory table on a cool jade-grey frame, with richer badges.
        ["SgConnectionsWindowBrush"] = "#E5ECE7",
        ["SgConnectionsPanelBrush"] = "#EEF1EC",
        ["SgConnectionsPanelRaisedBrush"] = "#DED8CC",
        ["SgConnectionsTableBrush"] = "#FBF9F3",
        ["SgConnectionsTableHeaderBrush"] = "#D1D9D3",
        ["SgConnectionsTableAltBrush"] = "#EAECE7",
        ["SgConnectionsTableBorderBrush"] = "#87988B",
        ["SgConnectionsTableHoverBrush"] = "#E7E0D4",
        ["SgConnectionsTableSelectedBrush"] = "#C9D6CC",
        ["SgConnectionsVpnBadgeBrush"] = "#C9D6CB",
        ["SgConnectionsVpnBadgeBorderBrush"] = "#78927F",
        ["SgConnectionsVpnBadgeTextBrush"] = "#304E40",
        ["SgConnectionsDirectBadgeBrush"] = "#DCE6DD",
        ["SgConnectionsDirectBadgeBorderBrush"] = "#7F9A84",
        ["SgConnectionsDirectBadgeTextBrush"] = "#365A45",
        ["SgConnectionsBlockBadgeBrush"] = "#E9D4D6",
        ["SgConnectionsBlockBadgeBorderBrush"] = "#C68A91",
        ["SgConnectionsBlockBadgeTextBrush"] = "#934D54",
        ["SgConnectionsOtherBadgeBrush"] = "#E8D0AA",
        ["SgConnectionsOtherBadgeBorderBrush"] = "#B88A45",
        ["SgConnectionsOtherBadgeTextBrush"] = "#755124",

        ["SgPrimaryActionTopColor"] = "#5F8874",
        ["SgPrimaryActionBottomColor"] = "#3E6B57",
        ["SgPrimaryActionHoverTopColor"] = "#6E967F",
        ["SgPrimaryActionHoverBottomColor"] = "#4F7B66",
        ["SgPrimaryActionPressedTopColor"] = "#3D6754",
        ["SgPrimaryActionPressedBottomColor"] = "#2F5544",
        ["SgPrimaryActionBorderBrush"] = "#B88A45",
        ["SgPrimaryActionTextBrush"] = "#FFFFFF",
        ["SgLogoFillBrush"] = "#D8C6A5"
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
        ["SgConnectionsVpnBadgeBrush"] = "#163552",
        ["SgConnectionsVpnBadgeBorderBrush"] = "#3D75A8",
        ["SgConnectionsVpnBadgeTextBrush"] = "#79C3FF",
        ["SgConnectionsDirectBadgeBrush"] = "#14372B",
        ["SgConnectionsDirectBadgeBorderBrush"] = "#2E7E5A",
        ["SgConnectionsDirectBadgeTextBrush"] = "#55D99A",
        ["SgConnectionsBlockBadgeBrush"] = "#342229",
        ["SgConnectionsBlockBadgeBorderBrush"] = "#824856",
        ["SgConnectionsBlockBadgeTextBrush"] = "#DC8C9B",
        ["SgConnectionsOtherBadgeBrush"] = "#17283A",
        ["SgConnectionsOtherBadgeBorderBrush"] = "#41627D",
        ["SgConnectionsOtherBadgeTextBrush"] = "#9CB0C2",

        // Connections window: deeper northern surfaces and clearer route badges.
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


    private static DropShadowEffect CreateShadowEffect(string color, double blurRadius, double shadowDepth, double opacity)
    {
        var effect = new DropShadowEffect
        {
            Color = ParseColor(color),
            BlurRadius = blurRadius,
            ShadowDepth = shadowDepth,
            Direction = 270,
            Opacity = opacity,
            RenderingBias = RenderingBias.Quality
        };
        if (effect.CanFreeze)
        {
            effect.Freeze();
        }
        return effect;
    }

    private static RadialGradientBrush CreateLatteBackground()
    {
        // Luxury Jade background: pearl light with a soft champagne glow and a pale jade outer field.
        var brush = new RadialGradientBrush
        {
            MappingMode = BrushMappingMode.RelativeToBoundingBox,
            Center = new System.Windows.Point(0.78, -0.08),
            GradientOrigin = new System.Windows.Point(0.78, -0.08),
            RadiusX = 0.68,
            RadiusY = 0.68,
            GradientStops = new GradientStopCollection
            {
                new(ParseColor("#FBF4E8"), 0),
                new(ParseColor("#F1EEE6"), 0.22),
                new(ParseColor("#E5ECE7"), 0.58),
                new(ParseColor("#E5ECE7"), 1)
            }
        };
        brush.Freeze();
        return brush;
    }

    private static Color ParseColor(string value) => (Color)ColorConverter.ConvertFromString(value);

    private sealed record ThemePalette(bool IsLight, string Primary, IReadOnlyDictionary<string, string> Brushes);
}
