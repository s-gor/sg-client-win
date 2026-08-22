using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;

namespace v2rayN.Common;

/// <summary>
/// Opens SG Client windows at the agreed physical-pixel sizes.
/// WPF uses device-independent units, so the requested pixel dimensions are
/// converted with the DPI of the monitor where the window is opened.
/// Compact legacy dialogs keep their existing dimensions.
/// </summary>
public static class SgWindowSizing
{
    // Physical pixels measured by the user on the target layout.
    public const double MainWidth = 1730d;
    public const double MainHeight = 1182d;
    public const double LargeWidth = 1460d;
    public const double LargeHeight = 1071d;
    public const double ConnectionsWidth = LargeWidth;
    public const double ConnectionsHeight = LargeHeight;

    private const uint MonitorDefaultToNearest = 0x00000002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const int EdgeMarginPx = 16;
    // Content scaling is a low-resolution fallback, not a general zoom feature.
    // Normal desktop work areas remain pixel-for-pixel identical to the classic SG layout.
    private const int LowResolutionWidthPx = 1600;
    private const int LowResolutionHeightPx = 900;
    private const double MinResponsiveScale = 0.50d;
    private const double ScaleEpsilon = 0.995d;

    public static void AttachMain(Window window)
    {
        // Main window: choose a safe first-launch size for the current monitor,
        // but never impose a minimum. After WindowBase restores the user's saved
        // size, keep that size and only clamp it when it no longer fits the monitor.
        Attach(
            window,
            MainWidth,
            MainHeight,
            enforcePhysicalMinimum: false,
            preserveLoadedSize: true,
            keepFreeMaximum: true);
    }

    public static void AttachLarge(Window window)
    {
        Attach(window, LargeWidth, LargeHeight);
    }

    public static void AttachConnections(Window window)
    {
        Attach(window, ConnectionsWidth, ConnectionsHeight);
    }

    /// <summary>
    /// Keeps compact SG dialogs inside the current monitor work area and scales
    /// their content only when the monitor is smaller than the dialog's XAML
    /// design size. Normal monitors stay at 100%.
    /// </summary>
    public static void AttachCompact(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        var designWidthDip = GetDesignDimension(window.Width, 800d);
        var designHeightDip = GetDesignDimension(window.Height, 600d);
        var appliedAfterLoad = false;

        window.SourceInitialized += (_, _) =>
        {
            ApplyCompact(window, designWidthDip, designHeightDip);
        };

        window.Loaded += (_, _) =>
        {
            if (appliedAfterLoad || !window.IsVisible || window.WindowState == WindowState.Minimized)
            {
                return;
            }

            appliedAfterLoad = true;
            ApplyCompact(window, designWidthDip, designHeightDip);
        };

        AttachResponsiveMonitorTracking(window, designWidthDip, designHeightDip, dimensionsAreDip: true);
    }

    private static void Attach(
        Window window,
        double preferredPhysicalWidth,
        double preferredPhysicalHeight,
        bool enforcePhysicalMinimum = false,
        bool preserveLoadedSize = false,
        bool keepFreeMaximum = false)
    {
        ArgumentNullException.ThrowIfNull(window);

        var designWidthDip = GetDesignDimension(window.Width, preferredPhysicalWidth);
        var designHeightDip = GetDesignDimension(window.Height, preferredPhysicalHeight);
        var appliedAfterLoad = false;

        window.SourceInitialized += (_, _) =>
        {
            Apply(window, preferredPhysicalWidth, preferredPhysicalHeight, enforcePhysicalMinimum, keepFreeMaximum);
            ApplyResponsiveScale(window, preferredPhysicalWidth, preferredPhysicalHeight, dimensionsAreDip: false);
        };

        // WindowBase may restore a saved WPF size during Loaded. Main preserves
        // that user size and only clamps it to the current monitor; other windows
        // keep their existing preferred-size behavior. Do not interfere with auto-hide.
        window.Loaded += (_, _) =>
        {
            if (appliedAfterLoad || !window.IsVisible || window.WindowState == WindowState.Minimized)
            {
                return;
            }

            appliedAfterLoad = true;
            if (preserveLoadedSize)
            {
                ClampCurrentSizeToMonitor(window);
            }
            else
            {
                Apply(window, preferredPhysicalWidth, preferredPhysicalHeight, enforcePhysicalMinimum, keepFreeMaximum);
            }

            ApplyResponsiveScale(window, preferredPhysicalWidth, preferredPhysicalHeight, dimensionsAreDip: false);
        };

        AttachResponsiveMonitorTracking(window, preferredPhysicalWidth, preferredPhysicalHeight, dimensionsAreDip: false);
    }

    private static double GetDesignDimension(double value, double fallback)
    {
        return double.IsFinite(value) && value > 0 ? value : fallback;
    }

    private static void AttachResponsiveMonitorTracking(
        Window window,
        double requiredWidth,
        double requiredHeight,
        bool dimensionsAreDip)
    {
        // Moving between monitors or changing DPI may cross the low-resolution threshold.
        // Deliberately do NOT react to SizeChanged: resizing a window on a normal monitor
        // must never zoom the SG interface.
        window.LocationChanged += (_, _) => ApplyResponsiveScale(window, requiredWidth, requiredHeight, dimensionsAreDip);
        window.DpiChanged += (_, _) => ApplyResponsiveScale(window, requiredWidth, requiredHeight, dimensionsAreDip);
    }

    private static void ApplyResponsiveScale(
        Window window,
        double requiredWidth,
        double requiredHeight,
        bool dimensionsAreDip)
    {
        try
        {
            if (window.Content is not FrameworkElement root || requiredWidth <= 0 || requiredHeight <= 0)
            {
                return;
            }

            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
            if (monitor == IntPtr.Zero)
            {
                return;
            }

            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(monitor, ref info))
            {
                return;
            }

            var workWidthPx = info.Work.Right - info.Work.Left;
            var workHeightPx = info.Work.Bottom - info.Work.Top;

            // Normal monitors stay exactly as before, regardless of window size or Windows DPI.
            if (workWidthPx >= LowResolutionWidthPx && workHeightPx >= LowResolutionHeightPx)
            {
                root.LayoutTransform = Transform.Identity;
                return;
            }

            var dpi = GetDpiForWindow(hwnd);
            if (dpi == 0)
            {
                dpi = 96;
            }

            var dpiScale = dpi / 96d;
            var requiredWidthPx = dimensionsAreDip ? requiredWidth * dpiScale : requiredWidth;
            var requiredHeightPx = dimensionsAreDip ? requiredHeight * dpiScale : requiredHeight;
            var availableWidthPx = Math.Max(1d, workWidthPx - (EdgeMarginPx * 2));
            var availableHeightPx = Math.Max(1d, workHeightPx - (EdgeMarginPx * 2));

            var responsiveScale = Math.Min(1d, Math.Min(availableWidthPx / requiredWidthPx, availableHeightPx / requiredHeightPx));
            responsiveScale = Math.Max(MinResponsiveScale, responsiveScale);

            root.LayoutTransform = responsiveScale < ScaleEpsilon
                ? new ScaleTransform(responsiveScale, responsiveScale)
                : Transform.Identity;
        }
        catch
        {
            // Scaling is a low-resolution enhancement only. Keep the ordinary layout on failure.
        }
    }

    private static void ApplyCompact(Window window, double designWidthDip, double designHeightDip)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
            if (monitor == IntPtr.Zero)
            {
                return;
            }

            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(monitor, ref info))
            {
                return;
            }

            var dpi = GetDpiForWindow(hwnd);
            if (dpi == 0)
            {
                dpi = 96;
            }

            var dpiScale = dpi / 96d;
            var workWidthPx = info.Work.Right - info.Work.Left;
            var workHeightPx = info.Work.Bottom - info.Work.Top;
            var maxWidthDip = Math.Max(1d, (workWidthPx - (EdgeMarginPx * 2)) / dpiScale);
            var maxHeightDip = Math.Max(1d, (workHeightPx - (EdgeMarginPx * 2)) / dpiScale);
            var targetWidthDip = Math.Min(designWidthDip, maxWidthDip);
            var targetHeightDip = Math.Min(designHeightDip, maxHeightDip);

            window.SizeToContent = SizeToContent.Manual;
            if (window.WindowState != WindowState.Minimized)
            {
                window.WindowState = WindowState.Normal;
            }

            window.MinWidth = Math.Min(window.MinWidth, targetWidthDip);
            window.MinHeight = Math.Min(window.MinHeight, targetHeightDip);
            window.MaxWidth = maxWidthDip;
            window.MaxHeight = maxHeightDip;
            window.Width = targetWidthDip;
            window.Height = targetHeightDip;

            var targetWidthPx = (int)Math.Round(targetWidthDip * dpiScale);
            var targetHeightPx = (int)Math.Round(targetHeightDip * dpiScale);
            var leftPx = info.Work.Left + ((workWidthPx - targetWidthPx) / 2);
            var topPx = info.Work.Top + ((workHeightPx - targetHeightPx) / 2);

            SetWindowPos(
                hwnd,
                IntPtr.Zero,
                leftPx,
                topPx,
                targetWidthPx,
                targetHeightPx,
                SwpNoZOrder | SwpNoActivate);

            ApplyResponsiveScale(window, designWidthDip, designHeightDip, dimensionsAreDip: true);
        }
        catch
        {
            // Keep the XAML dimensions if monitor probing fails.
        }
    }

    private static void ClampCurrentSizeToMonitor(Window window)
    {
        try
        {
            var helper = new WindowInteropHelper(window);
            var hwnd = helper.Handle;
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
            if (monitor == IntPtr.Zero)
            {
                return;
            }

            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(monitor, ref info))
            {
                return;
            }

            var dpi = GetDpiForWindow(hwnd);
            if (dpi == 0)
            {
                dpi = 96;
            }

            var dpiScale = dpi / 96d;
            var workWidthPx = info.Work.Right - info.Work.Left;
            var workHeightPx = info.Work.Bottom - info.Work.Top;
            var maxWidthPx = Math.Max(1, workWidthPx - (EdgeMarginPx * 2));
            var maxHeightPx = Math.Max(1, workHeightPx - (EdgeMarginPx * 2));

            var requestedWidthDip = double.IsFinite(window.Width) && window.Width > 0
                ? window.Width
                : MainWidth / dpiScale;
            var requestedHeightDip = double.IsFinite(window.Height) && window.Height > 0
                ? window.Height
                : MainHeight / dpiScale;

            var targetWidthPx = Math.Min((int)Math.Round(requestedWidthDip * dpiScale), maxWidthPx);
            var targetHeightPx = Math.Min((int)Math.Round(requestedHeightDip * dpiScale), maxHeightPx);

            window.MinWidth = 0;
            window.MinHeight = 0;
            window.MaxWidth = double.PositiveInfinity;
            window.MaxHeight = double.PositiveInfinity;
            window.Width = targetWidthPx / dpiScale;
            window.Height = targetHeightPx / dpiScale;

            var leftPx = info.Work.Left + ((workWidthPx - targetWidthPx) / 2);
            var topPx = info.Work.Top + ((workHeightPx - targetHeightPx) / 2);

            SetWindowPos(
                hwnd,
                IntPtr.Zero,
                leftPx,
                topPx,
                targetWidthPx,
                targetHeightPx,
                SwpNoZOrder | SwpNoActivate);
        }
        catch
        {
            // Keep the size restored by WindowBase if monitor probing fails.
        }
    }

    private static void Apply(
        Window window,
        double preferredPhysicalWidth,
        double preferredPhysicalHeight,
        bool enforcePhysicalMinimum,
        bool keepFreeMaximum)
    {
        try
        {
            var helper = new WindowInteropHelper(window);
            var hwnd = helper.Handle;
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            var ownerHwnd = window.Owner is { IsVisible: true }
                ? new WindowInteropHelper(window.Owner).Handle
                : IntPtr.Zero;

            var monitorSource = ownerHwnd != IntPtr.Zero ? ownerHwnd : hwnd;
            var monitor = MonitorFromWindow(monitorSource, MonitorDefaultToNearest);
            if (monitor == IntPtr.Zero)
            {
                return;
            }

            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(monitor, ref info))
            {
                return;
            }

            var dpi = GetDpiForWindow(hwnd);
            if (dpi == 0)
            {
                dpi = 96;
            }

            var dpiScale = dpi / 96d;
            var workWidthPx = info.Work.Right - info.Work.Left;
            var workHeightPx = info.Work.Bottom - info.Work.Top;

            var maxWidthPx = Math.Max(1, workWidthPx - (EdgeMarginPx * 2));
            var maxHeightPx = Math.Max(1, workHeightPx - (EdgeMarginPx * 2));
            var targetWidthPx = enforcePhysicalMinimum
                ? (int)Math.Round(preferredPhysicalWidth)
                : (int)Math.Round(Math.Min(preferredPhysicalWidth, maxWidthPx));
            var targetHeightPx = enforcePhysicalMinimum
                ? (int)Math.Round(preferredPhysicalHeight)
                : (int)Math.Round(Math.Min(preferredPhysicalHeight, maxHeightPx));

            var targetWidthDip = targetWidthPx / dpiScale;
            var targetHeightDip = targetHeightPx / dpiScale;

            window.SizeToContent = SizeToContent.Manual;
            if (window.WindowState != WindowState.Minimized)
            {
                window.WindowState = WindowState.Normal;
            }

            if (enforcePhysicalMinimum)
            {
                // The main window uses a true physical-pixel minimum requested by the user.
                // Keep MaxWidth/MaxHeight at least as large as that minimum so WPF never
                // silently reduces it on a smaller work area or at high DPI.
                window.MinWidth = targetWidthDip;
                window.MinHeight = targetHeightDip;
                window.MaxWidth = Math.Max(workWidthPx, targetWidthPx) / dpiScale;
                window.MaxHeight = Math.Max(workHeightPx, targetHeightPx) / dpiScale;
            }
            else if (keepFreeMaximum)
            {
                // Main window: fit the initial size to the current work area, but keep
                // later resizing free, including after moving to a larger monitor.
                window.MinWidth = 0;
                window.MinHeight = 0;
                window.MaxWidth = double.PositiveInfinity;
                window.MaxHeight = double.PositiveInfinity;
            }
            else
            {
                // Other large windows keep their previous work-area bounds.
                window.MinWidth = Math.Min(window.MinWidth, targetWidthDip);
                window.MinHeight = Math.Min(window.MinHeight, targetHeightDip);
                window.MaxWidth = workWidthPx / dpiScale;
                window.MaxHeight = workHeightPx / dpiScale;
            }

            window.Width = targetWidthDip;
            window.Height = targetHeightDip;

            var leftPx = info.Work.Left + ((workWidthPx - targetWidthPx) / 2);
            var topPx = info.Work.Top + ((workHeightPx - targetHeightPx) / 2);

            if (ownerHwnd != IntPtr.Zero && GetWindowRect(ownerHwnd, out var ownerRect))
            {
                leftPx = ownerRect.Left + (((ownerRect.Right - ownerRect.Left) - targetWidthPx) / 2);
                topPx = ownerRect.Top + (((ownerRect.Bottom - ownerRect.Top) - targetHeightPx) / 2);
            }

            var minLeftPx = info.Work.Left + EdgeMarginPx;
            var maxLeftPx = info.Work.Right - EdgeMarginPx - targetWidthPx;
            var minTopPx = info.Work.Top + EdgeMarginPx;
            var maxTopPx = info.Work.Bottom - EdgeMarginPx - targetHeightPx;

            // Math.Clamp requires min <= max. When the requested main-window minimum is
            // larger than the work area, keep it centred instead of reducing its size.
            if (minLeftPx <= maxLeftPx)
            {
                leftPx = Math.Clamp(leftPx, minLeftPx, maxLeftPx);
            }
            if (minTopPx <= maxTopPx)
            {
                topPx = Math.Clamp(topPx, minTopPx, maxTopPx);
            }

            SetWindowPos(
                hwnd,
                IntPtr.Zero,
                leftPx,
                topPx,
                targetWidthPx,
                targetHeightPx,
                SwpNoZOrder | SwpNoActivate);
        }
        catch
        {
            // The moderate XAML dimensions remain the safe fallback.
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo monitorInfo);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr hwnd,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
    }
}
