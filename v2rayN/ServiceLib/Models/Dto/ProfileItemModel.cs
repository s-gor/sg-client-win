using ServiceLib.Common;

namespace ServiceLib.Models.Dto;

[Serializable]
public class ProfileItemModel : ReactiveObject
{
    [Reactive]
    public bool IsActive { get; set; }
    public string IndexId { get; set; }
    public EConfigType ConfigType { get; set; }

    public string Remarks { get; set; } = string.Empty;
    [Reactive]
    public string CountryCode { get; set; } = string.Empty;

    [Reactive]
    public string CountrySource { get; set; } = string.Empty;
    public string ResolvedCountryCode => SgCountryHelper.ResolveCode(CountryCode, Remarks);
    public string DisplayRemarks => SgCountryHelper.CleanRemarks(Remarks, ResolvedCountryCode);
    public string CountryBadge => ResolvedCountryCode.Length == 2 ? ResolvedCountryCode : "—";
    public string CountryFlagUri => $"pack://application:,,,/Assets/Flags/{(ResolvedCountryCode.Length == 2 ? ResolvedCountryCode : "ZZ")}.png";
    public string CountryName => SgCountryHelper.GetRussianName(ResolvedCountryCode);
    public string CountryToolTip => ResolvedCountryCode.Length == 2
        ? $"{CountryName} · {ResolvedCountryCode}\nИсточник: {(CountrySource.IsNotEmpty() ? CountrySource : "профиль")}\nАдрес: {Address}"
        : $"Страна не определена\nАдрес: {Address}";

    public void ApplyCountry(string countryCode, string source)
    {
        CountryCode = SgCountryHelper.NormalizeCode(countryCode);
        CountrySource = source ?? string.Empty;
        this.RaisePropertyChanged(nameof(ResolvedCountryCode));
        this.RaisePropertyChanged(nameof(DisplayRemarks));
        this.RaisePropertyChanged(nameof(CountryBadge));
        this.RaisePropertyChanged(nameof(CountryFlagUri));
        this.RaisePropertyChanged(nameof(CountryName));
        this.RaisePropertyChanged(nameof(CountryToolTip));
    }
    public string Address { get; set; }
    public int Port { get; set; }
    public string Network { get; set; }
    public string StreamSecurity { get; set; }
    public string Subid { get; set; }
    public bool IsSub { get; set; }
    public string SubRemarks { get; set; }
    public int Sort { get; set; }

    // SG Client virtual profile fields. AmneziaWG profiles are stored separately
    // from the upstream v2rayN database but are rendered in the same profile list.
    public bool IsAmneziaWG { get; set; }
    public string ProtocolDisplay { get; set; } = string.Empty;
    public string SourceDisplay { get; set; } = "Локальный профиль";

    [Reactive]
    public int Delay { get; set; }

    public decimal Speed { get; set; }

    [Reactive]
    public string DelayVal { get; set; }

    [Reactive]
    public string SpeedVal { get; set; }

    [Reactive]
    public string IpInfo { get; set; }

    [Reactive]
    public string TodayUp { get; set; }

    [Reactive]
    public string TodayDown { get; set; }

    [Reactive]
    public string TotalUp { get; set; }

    [Reactive]
    public string TotalDown { get; set; }

    public string GetSummary()
    {
        var summary = $"[{ConfigType}] {Remarks}";
        if (!ConfigType.IsComplexType())
        {
            summary += $"({Address}:{Port})";
        }

        return summary;
    }
}
