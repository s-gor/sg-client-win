using System.Collections.Concurrent;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace v2rayN.Converters;

/// <summary>
/// Loads a bundled flag directly from the profile's structured country code or
/// visible Remarks marker. This intentionally avoids relying on a separately
/// populated UI field: [US] / [ᴜs] / 【FR】 is resolved at the binding point.
/// </summary>
public sealed class ProfileCountryFlagConverter : IMultiValueConverter
{
    private static readonly ConcurrentDictionary<string, ImageSource> Cache = new(StringComparer.OrdinalIgnoreCase);

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var structuredCode = values.Length > 0 && values[0] is string code ? code : string.Empty;
        var remarks = values.Length > 1 && values[1] is string text ? text : string.Empty;
        var resolved = SgCountryHelper.ResolveCode(structuredCode, remarks);
        return GetImage(resolved.Length == 2 ? resolved : "ZZ");
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        targetTypes.Select(_ => Binding.DoNothing).ToArray();

    private static ImageSource GetImage(string code)
    {
        return Cache.GetOrAdd(code, static value =>
        {
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.UriSource = new Uri($"pack://application:,,,/Assets/Flags/{value}.png", UriKind.Absolute);
                image.EndInit();
                image.Freeze();
                return image;
            }
            catch
            {
                if (!string.Equals(value, "ZZ", StringComparison.OrdinalIgnoreCase))
                {
                    return GetImage("ZZ");
                }
                return new DrawingImage();
            }
        });
    }
}

/// <summary>
/// Removes an explicit country marker only after it has been successfully
/// resolved. Names such as sg-admin/Primary are returned unchanged.
/// </summary>
public sealed class ProfileDisplayRemarksConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var structuredCode = values.Length > 0 && values[0] is string code ? code : string.Empty;
        var remarks = values.Length > 1 && values[1] is string text ? text : string.Empty;
        return SgCountryHelper.CleanRemarks(remarks, structuredCode);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        targetTypes.Select(_ => Binding.DoNothing).ToArray();
}
