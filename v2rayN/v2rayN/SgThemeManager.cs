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
            Application.Current.Resources["SgSidebarBrush"] = CreateVerticalGradient("#DCE4E9", "#D3DDE3");
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

    private static ThemePalette CreateLightPalette() => new(true, "#31536F", new Dictionary<string, string>
    {
        // Latte Graphite — exact SG-AWG-Panel RC6 palette.
        ["SgBackgroundBrush"] = "#E3E9EE",
        ["SgHeaderBrush"] = "#E3E9EE",
        ["SgSidebarBrush"] = "#D7E0E5",
        ["SgSurfaceBrush"] = "#EEF2F4",
        ["SgSurfaceSoftBrush"] = "#D9E2E8",
        ["SgSurfaceRaisedBrush"] = "#E4EBEE",
        ["SgBorderBrush"] = "#AEBCC7",
        ["SgBorderStrongBrush"] = "#7E91A0",
        ["SgTextBrush"] = "#192530",
        ["SgMutedBrush"] = "#556672",
        ["SgSubtleTextBrush"] = "#71808B",
        ["SgAccentBrush"] = "#31536F",
        ["SgTrafficDownloadBrush"] = "#31536F",
        ["SgTrafficUploadBrush"] = "#2F805A",

        // Mode scenes use the same neutral graphite surfaces; state is shown by accent, border and icon.
        ["SgTunHeroBrush"] = "#EEF2F4",
        ["SgTunCardBrush"] = "#D8EADF",
        ["SgTunCardBorderBrush"] = "#8DBFA2",
        ["SgSystemProxyBrush"] = "#7A5318",
        ["SgSystemProxySoftBrush"] = "#ECE2CF",
        ["SgSystemProxyBorderBrush"] = "#57956820",
        ["SgSystemProxyHeroBrush"] = "#EEF2F4",
        ["SgSystemProxyCardBrush"] = "#ECE2CF",
        ["SgSystemProxyCardBorderBrush"] = "#57956820",
        ["SgLocalProxyBrush"] = "#31536F",
        ["SgLocalProxySoftBrush"] = "#D9E2E8",
        ["SgLocalProxyBorderBrush"] = "#AEBCC7",
        ["SgLocalProxyHeroBrush"] = "#EEF2F4",
        ["SgLocalProxyCardBrush"] = "#D9E2E8",
        ["SgLocalProxyCardBorderBrush"] = "#C5D0D7",

        ["SgAccentSoftBrush"] = "#1F31536F",
        ["SgAccentBorderBrush"] = "#31536F",
        ["SgSuccessBrush"] = "#17623F",
        ["SgSuccessSoftBrush"] = "#D8EADF",
        ["SgWarningBrush"] = "#7A5318",
        ["SgWarningSoftBrush"] = "#ECE2CF",
        ["SgErrorBrush"] = "#973E49",
        ["SgErrorSoftBrush"] = "#ECD9DC",
        ["SgSuccessBorderBrush"] = "#8DBFA2",
        ["SgSuccessTextBrush"] = "#17623F",
        ["SgSuccessDotBrush"] = "#2F805A",
        ["SgSuccessHoverSoftBrush"] = "#CCE3D5",
        ["SgWarningButtonBrush"] = "#956820",
        ["SgWarningButtonBorderBrush"] = "#835A1C",
        ["SgDangerButtonBrush"] = "#B34F5A",
        ["SgDangerButtonBorderBrush"] = "#A8424D",
        ["SgDangerButtonTextBrush"] = "#FFFFFF",
        ["SgOffBrush"] = "#71808B",
        ["SgHoverBrush"] = "#1731536F",
        ["SgPressedBrush"] = "#27465F",
        ["SgSelectedBrush"] = "#1C31536F",
        ["SgInputBrush"] = "#EAF0F3",
        ["SgHeroBrush"] = "#EEF2F4",
        ["SgHeroBusyBrush"] = "#ECE2CF",
        ["SgHeroErrorBrush"] = "#ECD9DC",
        ["SgOnButtonBrush"] = "#31536F",
        ["SgOnButtonTextBrush"] = "#FFFFFF",

        // Primary actions: exact accent/hover/pressed values with white SemiBold text.
        ["SgNeutralActionBrush"] = "#31536F",
        ["SgNeutralActionHoverBrush"] = "#3A607F",
        ["SgNeutralActionPressedBrush"] = "#27465F",
        ["SgNeutralActionBorderBrush"] = "#31536F",
        ["SgNeutralActionTextBrush"] = "#FFFFFF",

        // Secondary actions keep the neutral graphite hierarchy.
        ["SgSecondaryActionBrush"] = "#D9E2E8",
        ["SgSecondaryActionHoverBrush"] = "#EAF0F3",
        ["SgSecondaryActionPressedBrush"] = "#C5D0D7",
        ["SgSecondaryActionBorderBrush"] = "#AEBCC7",
        ["SgSecondaryActionTextBrush"] = "#192530",
        ["SgIconButtonBrush"] = "#D9E2E8",
        ["SgIconButtonHoverBrush"] = "#EAF0F3",
        ["SgIconButtonPressedBrush"] = "#C5D0D7",
        ["SgDisabledBrush"] = "#D9E2E8",
        ["SgDisabledBorderBrush"] = "#C5D0D7",
        ["SgDisabledTextBrush"] = "#71808B",

        ["SgTileBrush"] = "#EEF2F4",
        ["SgTileHoverBrush"] = "#EAF0F3",
        ["SgTileActiveBrush"] = "#1F31536F",
        ["SgTileIconBrush"] = "#D9E2E8",
        ["SgTrafficCardBrush"] = "#EEF2F4",
        ["SgTrafficSectionBrush"] = "#D9E2E8",
        ["SgDangerSoftBrush"] = "#ECD9DC",

        // Connections window: stronger graphite hierarchy while keeping Latte RC6 tokens.
        ["SgConnectionsWindowBrush"] = "#E3E9EE",
        ["SgConnectionsPanelBrush"] = "#E6ECEF",
        ["SgConnectionsPanelRaisedBrush"] = "#D9E2E8",
        ["SgConnectionsTableBrush"] = "#EEF2F4",
        ["SgConnectionsTableHeaderBrush"] = "#CBD7DE",
        ["SgConnectionsTableAltBrush"] = "#E3EAEE",
        ["SgConnectionsTableBorderBrush"] = "#8FA2AF",
        ["SgConnectionsTableHoverBrush"] = "#D9E4E9",
        ["SgConnectionsTableSelectedBrush"] = "#CEDCE3",
        ["SgConnectionsVpnBadgeBrush"] = "#D8E4ED",
        ["SgConnectionsVpnBadgeBorderBrush"] = "#6F8DA4",
        ["SgConnectionsVpnBadgeTextBrush"] = "#274D69",
        ["SgConnectionsDirectBadgeBrush"] = "#D8EADF",
        ["SgConnectionsDirectBadgeBorderBrush"] = "#8DBFA2",
        ["SgConnectionsDirectBadgeTextBrush"] = "#17623F",
        ["SgConnectionsBlockBadgeBrush"] = "#ECD9DC",
        ["SgConnectionsBlockBadgeBorderBrush"] = "#C78891",
        ["SgConnectionsBlockBadgeTextBrush"] = "#973E49",
        ["SgConnectionsOtherBadgeBrush"] = "#E5E1DC",
        ["SgConnectionsOtherBadgeBorderBrush"] = "#B2A79C",
        ["SgConnectionsOtherBadgeTextBrush"] = "#62594F",

        ["SgPrimaryActionTopColor"] = "#31536F",
        ["SgPrimaryActionBottomColor"] = "#31536F",
        ["SgPrimaryActionHoverTopColor"] = "#3A607F",
        ["SgPrimaryActionHoverBottomColor"] = "#3A607F",
        ["SgPrimaryActionPressedTopColor"] = "#27465F",
        ["SgPrimaryActionPressedBottomColor"] = "#27465F",
        ["SgPrimaryActionBorderBrush"] = "#31536F",
        ["SgPrimaryActionTextBrush"] = "#FFFFFF",
        ["SgLogoFillBrush"] = "#D7E0E5"
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

    private static RadialGradientBrush CreateLatteBackground()
    {
        // CSS equivalent: radial-gradient(circle at 75% -10%,
        // rgba(49,83,111,.11), transparent 36%), #E3E9EE.
        var brush = new RadialGradientBrush
        {
            MappingMode = BrushMappingMode.RelativeToBoundingBox,
            Center = new System.Windows.Point(0.75, -0.10),
            GradientOrigin = new System.Windows.Point(0.75, -0.10),
            RadiusX = 0.55,
            RadiusY = 0.55,
            GradientStops = new GradientStopCollection
            {
                new(ParseColor("#CFD8E0"), 0),
                new(ParseColor("#E3E9EE"), 0.36),
                new(ParseColor("#E3E9EE"), 1)
            }
        };
        brush.Freeze();
        return brush;
    }

    private static Color ParseColor(string value) => (Color)ColorConverter.ConvertFromString(value);

    private sealed record ThemePalette(bool IsLight, string Primary, IReadOnlyDictionary<string, string> Brushes);
}
