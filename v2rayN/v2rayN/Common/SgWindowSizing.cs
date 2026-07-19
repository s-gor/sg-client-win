using System.Runtime.InteropServices;
using System.Windows.Interop;

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

    public static void AttachMain(Window window)
    {
        Attach(window, MainWidth, MainHeight, enforcePhysicalMinimum: true);
    }

    public static void AttachLarge(Window window)
    {
        Attach(window, LargeWidth, LargeHeight);
    }

    public static void AttachConnections(Window window)
    {
        Attach(window, ConnectionsWidth, ConnectionsHeight);
    }

    private static void Attach(Window window, double preferredPhysicalWidth, double preferredPhysicalHeight, bool enforcePhysicalMinimum = false)
    {
        ArgumentNullException.ThrowIfNull(window);

        var appliedAfterLoad = false;

        window.SourceInitialized += (_, _) =>
        {
            Apply(window, preferredPhysicalWidth, preferredPhysicalHeight, enforcePhysicalMinimum);
        };

        // WindowBase may restore an older saved WPF size during Loaded.
        // Re-apply once afterwards so stale settings cannot make the window
        // appear maximized. Do not interfere with intentional auto-hide.
        window.Loaded += (_, _) =>
        {
            if (appliedAfterLoad || !window.IsVisible || window.WindowState == WindowState.Minimized)
            {
                return;
            }

            appliedAfterLoad = true;
            Apply(window, preferredPhysicalWidth, preferredPhysicalHeight, enforcePhysicalMinimum);
        };
    }

    private static void Apply(Window window, double preferredPhysicalWidth, double preferredPhysicalHeight, bool enforcePhysicalMinimum)
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
            else
            {
                // Other large windows still fit inside the available monitor work area.
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
