using System.Globalization;

namespace ServiceLib.Manager;

public sealed class SgTrafficSnapshot
{
    public string ProfileId { get; init; } = string.Empty;
    public string ProfileName { get; init; } = "Профиль не выбран";
    public long CurrentUploadBytesPerSecond { get; init; }
    public long CurrentDownloadBytesPerSecond { get; init; }
    public long SessionUploadBytes { get; init; }
    public long SessionDownloadBytes { get; init; }
    public long TodayUploadBytes { get; init; }
    public long TodayDownloadBytes { get; init; }
    public long MonthUploadBytes { get; init; }
    public long MonthDownloadBytes { get; init; }
    public long TotalUploadBytes { get; init; }
    public long TotalDownloadBytes { get; init; }
}

public sealed class SgProfileTrafficSnapshot
{
    public string ProfileId { get; init; } = string.Empty;
    public string ProfileName { get; init; } = string.Empty;
    public long SessionUploadBytes { get; init; }
    public long SessionDownloadBytes { get; init; }
    public long TodayUploadBytes { get; init; }
    public long TodayDownloadBytes { get; init; }
    public long MonthUploadBytes { get; init; }
    public long MonthDownloadBytes { get; init; }
    public long TotalUploadBytes { get; init; }
    public long TotalDownloadBytes { get; init; }
    public DateTime LastUsedUtc { get; init; }
}

public sealed class SgTrafficStatisticsManager
{
    private const string ObsoleteDirectProfileId = "__sg_direct__";

    private sealed class PersistedProfileState
    {
        public string Name { get; set; } = string.Empty;
        public long TodayUploadBytes { get; set; }
        public long TodayDownloadBytes { get; set; }
        public long MonthUploadBytes { get; set; }
        public long MonthDownloadBytes { get; set; }
        public long TotalUploadBytes { get; set; }
        public long TotalDownloadBytes { get; set; }
        public DateTime LastUsedUtc { get; set; }
    }

    private sealed class PersistedState
    {
        public int Version { get; set; } = 2;
        public string Day { get; set; } = string.Empty;
        public string Month { get; set; } = string.Empty;

        // Kept for backward compatibility with sg-traffic.json from 070/071.
        public long TodayUploadBytes { get; set; }
        public long TodayDownloadBytes { get; set; }
        public long MonthUploadBytes { get; set; }
        public long MonthDownloadBytes { get; set; }
        public long TotalUploadBytes { get; set; }
        public long TotalDownloadBytes { get; set; }

        public Dictionary<string, PersistedProfileState> Profiles { get; set; } = new();
    }

    private sealed class SessionState
    {
        public long UploadBytes { get; set; }
        public long DownloadBytes { get; set; }
    }

    private static readonly Lazy<SgTrafficStatisticsManager> LazyInstance =
        new(() => new SgTrafficStatisticsManager());

    private readonly object _syncRoot = new();
    private readonly Stopwatch _deltaClock = Stopwatch.StartNew();
    private readonly Stopwatch _cumulativeClock = Stopwatch.StartNew();
    private readonly string _filePath;
    private readonly Dictionary<string, SessionState> _sessions =
        new(StringComparer.OrdinalIgnoreCase);

    private PersistedState _state;
    private string _activeProfileId = string.Empty;
    private string _activeProfileName = "Профиль не выбран";
    private long _currentUploadBytesPerSecond;
    private long _currentDownloadBytesPerSecond;
    private long _lastCumulativeUploadBytes = -1;
    private long _lastCumulativeDownloadBytes = -1;
    private DateTime _lastSaveUtc = DateTime.MinValue;

    public static SgTrafficStatisticsManager Instance => LazyInstance.Value;

    private SgTrafficStatisticsManager()
    {
        _filePath = Utils.GetConfigPath("sg-traffic.json");
        _state = LoadState();

        // Build Fix 4/5 briefly stored Xray's direct outbound as a synthetic
        // profile. That outbound also contains the tunnel engine's own outer
        // connection and must never be mixed with end-user VPN traffic.
        if (_state.Profiles.Remove(ObsoleteDirectProfileId))
        {
            RecalculateAggregateTotals();
            SaveState();
        }

        EnsureCurrentPeriods(saveImmediately: false);
    }

    public SgTrafficSnapshot Current
    {
        get
        {
            lock (_syncRoot)
            {
                EnsureCurrentPeriods(saveImmediately: true);
                return CreateSnapshot();
            }
        }
    }

    public SgTrafficSnapshot SetActiveProfile(string? profileId, string? profileName)
    {
        lock (_syncRoot)
        {
            EnsureCurrentPeriods(saveImmediately: true);
            EnsureProfileContext(profileId, profileName, createPersistedState: false);
            return CreateSnapshot();
        }
    }

    public IReadOnlyList<SgProfileTrafficSnapshot> GetProfiles()
    {
        lock (_syncRoot)
        {
            EnsureCurrentPeriods(saveImmediately: true);
            return _state.Profiles
                .Select(pair =>
                {
                    _sessions.TryGetValue(pair.Key, out var session);
                    return new SgProfileTrafficSnapshot
                    {
                        ProfileId = pair.Key,
                        ProfileName = DisplayName(pair.Value.Name, pair.Key),
                        SessionUploadBytes = session?.UploadBytes ?? 0,
                        SessionDownloadBytes = session?.DownloadBytes ?? 0,
                        TodayUploadBytes = pair.Value.TodayUploadBytes,
                        TodayDownloadBytes = pair.Value.TodayDownloadBytes,
                        MonthUploadBytes = pair.Value.MonthUploadBytes,
                        MonthDownloadBytes = pair.Value.MonthDownloadBytes,
                        TotalUploadBytes = pair.Value.TotalUploadBytes,
                        TotalDownloadBytes = pair.Value.TotalDownloadBytes,
                        LastUsedUtc = pair.Value.LastUsedUtc,
                    };
                })
                .OrderByDescending(item => SaturatingAdd(
                    item.MonthDownloadBytes,
                    item.MonthUploadBytes))
                .ThenByDescending(item => item.LastUsedUtc)
                .ToList();
        }
    }

    public SgTrafficSnapshot AddDelta(
        string? profileId,
        string? profileName,
        long uploadUnits,
        long downloadUnits,
        long bytesPerUnit)
    {
        lock (_syncRoot)
        {
            EnsureCurrentPeriods(saveImmediately: true);
            var profile = EnsureProfileContext(
                profileId,
                profileName,
                createPersistedState: true);

            var elapsedSeconds = _deltaClock.Elapsed.TotalSeconds;
            _deltaClock.Restart();

            var uploadBytes = ToBytes(uploadUnits, bytesPerUnit);
            var downloadBytes = ToBytes(downloadUnits, bytesPerUnit);

            _currentUploadBytesPerSecond = CalculateRate(uploadBytes, elapsedSeconds);
            _currentDownloadBytesPerSecond = CalculateRate(downloadBytes, elapsedSeconds);

            AddTraffic(profile, uploadBytes, downloadBytes);
            SaveWhenDue();
            return CreateSnapshot();
        }
    }

    public SgTrafficSnapshot UpdateCumulative(
        string? profileId,
        string? profileName,
        long uploadBytes,
        long downloadBytes)
    {
        lock (_syncRoot)
        {
            EnsureCurrentPeriods(saveImmediately: true);
            var profile = EnsureProfileContext(
                profileId,
                profileName,
                createPersistedState: true);

            var elapsedSeconds = _cumulativeClock.Elapsed.TotalSeconds;
            _cumulativeClock.Restart();

            if (_lastCumulativeUploadBytes < 0
                || _lastCumulativeDownloadBytes < 0
                || uploadBytes < _lastCumulativeUploadBytes
                || downloadBytes < _lastCumulativeDownloadBytes)
            {
                _lastCumulativeUploadBytes = Math.Max(0, uploadBytes);
                _lastCumulativeDownloadBytes = Math.Max(0, downloadBytes);
                _currentUploadBytesPerSecond = 0;
                _currentDownloadBytesPerSecond = 0;
                return CreateSnapshot();
            }

            var uploadDelta = Math.Max(0, uploadBytes - _lastCumulativeUploadBytes);
            var downloadDelta = Math.Max(0, downloadBytes - _lastCumulativeDownloadBytes);

            _lastCumulativeUploadBytes = uploadBytes;
            _lastCumulativeDownloadBytes = downloadBytes;

            _currentUploadBytesPerSecond = CalculateRate(uploadDelta, elapsedSeconds);
            _currentDownloadBytesPerSecond = CalculateRate(downloadDelta, elapsedSeconds);

            AddTraffic(profile, uploadDelta, downloadDelta);
            SaveWhenDue();
            return CreateSnapshot();
        }
    }

    public SgTrafficSnapshot ResetSession()
    {
        lock (_syncRoot)
        {
            if (_activeProfileId.IsNotEmpty())
            {
                _sessions.Remove(_activeProfileId);
            }

            ResetLiveState();
            return CreateSnapshot();
        }
    }

    public SgTrafficSnapshot ResetAll()
    {
        lock (_syncRoot)
        {
            if (_activeProfileId.IsNotEmpty())
            {
                _sessions.Remove(_activeProfileId);
                _state.Profiles.Remove(_activeProfileId);
            }

            ResetLiveState();
            RecalculateAggregateTotals();
            SaveState();
            return CreateSnapshot();
        }
    }

    public SgTrafficSnapshot SetIdle()
    {
        lock (_syncRoot)
        {
            _currentUploadBytesPerSecond = 0;
            _currentDownloadBytesPerSecond = 0;
            _deltaClock.Restart();
            _cumulativeClock.Restart();
            SaveState();
            return CreateSnapshot();
        }
    }

    private PersistedProfileState? EnsureProfileContext(
        string? profileId,
        string? profileName,
        bool createPersistedState)
    {
        var normalizedId = NormalizeProfileId(profileId);
        var normalizedName = DisplayName(profileName, normalizedId);

        if (!string.Equals(_activeProfileId, normalizedId, StringComparison.OrdinalIgnoreCase))
        {
            _activeProfileId = normalizedId;
            _activeProfileName = normalizedName;
            ResetLiveState();
        }
        else if (normalizedName.IsNotEmpty())
        {
            _activeProfileName = normalizedName;
        }

        if (normalizedId.IsNullOrEmpty())
        {
            return null;
        }

        if (!_state.Profiles.TryGetValue(normalizedId, out var profile))
        {
            if (!createPersistedState)
            {
                return null;
            }

            profile = new PersistedProfileState
            {
                Name = normalizedName,
                LastUsedUtc = DateTime.UtcNow,
            };
            _state.Profiles[normalizedId] = profile;
        }

        if (normalizedName.IsNotEmpty())
        {
            profile.Name = normalizedName;
        }

        return profile;
    }

    private void ResetLiveState()
    {
        _currentUploadBytesPerSecond = 0;
        _currentDownloadBytesPerSecond = 0;
        _lastCumulativeUploadBytes = -1;
        _lastCumulativeDownloadBytes = -1;
        _deltaClock.Restart();
        _cumulativeClock.Restart();
    }

    private void AddTraffic(
        PersistedProfileState? profile,
        long uploadBytes,
        long downloadBytes)
    {
        if (profile == null || _activeProfileId.IsNullOrEmpty())
        {
            return;
        }

        if (!_sessions.TryGetValue(_activeProfileId, out var session))
        {
            session = new SessionState();
            _sessions[_activeProfileId] = session;
        }

        session.UploadBytes = SaturatingAdd(session.UploadBytes, uploadBytes);
        session.DownloadBytes = SaturatingAdd(session.DownloadBytes, downloadBytes);

        profile.TodayUploadBytes = SaturatingAdd(profile.TodayUploadBytes, uploadBytes);
        profile.TodayDownloadBytes = SaturatingAdd(profile.TodayDownloadBytes, downloadBytes);
        profile.MonthUploadBytes = SaturatingAdd(profile.MonthUploadBytes, uploadBytes);
        profile.MonthDownloadBytes = SaturatingAdd(profile.MonthDownloadBytes, downloadBytes);
        profile.TotalUploadBytes = SaturatingAdd(profile.TotalUploadBytes, uploadBytes);
        profile.TotalDownloadBytes = SaturatingAdd(profile.TotalDownloadBytes, downloadBytes);
        profile.LastUsedUtc = DateTime.UtcNow;

        _state.TodayUploadBytes = SaturatingAdd(_state.TodayUploadBytes, uploadBytes);
        _state.TodayDownloadBytes = SaturatingAdd(_state.TodayDownloadBytes, downloadBytes);
        _state.MonthUploadBytes = SaturatingAdd(_state.MonthUploadBytes, uploadBytes);
        _state.MonthDownloadBytes = SaturatingAdd(_state.MonthDownloadBytes, downloadBytes);
        _state.TotalUploadBytes = SaturatingAdd(_state.TotalUploadBytes, uploadBytes);
        _state.TotalDownloadBytes = SaturatingAdd(_state.TotalDownloadBytes, downloadBytes);
    }

    private PersistedState LoadState()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return NewState();
            }

            var json = File.ReadAllText(_filePath);
            var state = JsonSerializer.Deserialize<PersistedState>(json) ?? NewState();
            state.Profiles ??= new Dictionary<string, PersistedProfileState>();
            return state;
        }
        catch
        {
            return NewState();
        }
    }

    private static PersistedState NewState()
    {
        return new PersistedState
        {
            Day = CurrentDay(),
            Month = CurrentMonth(),
        };
    }

    private void EnsureCurrentPeriods(bool saveImmediately)
    {
        var changed = false;
        var currentDay = CurrentDay();
        if (!string.Equals(_state.Day, currentDay, StringComparison.Ordinal))
        {
            _state.Day = currentDay;
            _state.TodayUploadBytes = 0;
            _state.TodayDownloadBytes = 0;
            foreach (var profile in _state.Profiles.Values)
            {
                profile.TodayUploadBytes = 0;
                profile.TodayDownloadBytes = 0;
            }
            changed = true;
        }

        var currentMonth = CurrentMonth();
        if (!string.Equals(_state.Month, currentMonth, StringComparison.Ordinal))
        {
            _state.Month = currentMonth;
            _state.MonthUploadBytes = 0;
            _state.MonthDownloadBytes = 0;
            foreach (var profile in _state.Profiles.Values)
            {
                profile.MonthUploadBytes = 0;
                profile.MonthDownloadBytes = 0;
            }
            changed = true;
        }

        if (changed && saveImmediately)
        {
            SaveState();
        }
    }

    private void RecalculateAggregateTotals()
    {
        _state.TodayUploadBytes = _state.Profiles.Values.Aggregate(
            0L,
            (total, item) => SaturatingAdd(total, item.TodayUploadBytes));
        _state.TodayDownloadBytes = _state.Profiles.Values.Aggregate(
            0L,
            (total, item) => SaturatingAdd(total, item.TodayDownloadBytes));
        _state.MonthUploadBytes = _state.Profiles.Values.Aggregate(
            0L,
            (total, item) => SaturatingAdd(total, item.MonthUploadBytes));
        _state.MonthDownloadBytes = _state.Profiles.Values.Aggregate(
            0L,
            (total, item) => SaturatingAdd(total, item.MonthDownloadBytes));
        _state.TotalUploadBytes = _state.Profiles.Values.Aggregate(
            0L,
            (total, item) => SaturatingAdd(total, item.TotalUploadBytes));
        _state.TotalDownloadBytes = _state.Profiles.Values.Aggregate(
            0L,
            (total, item) => SaturatingAdd(total, item.TotalDownloadBytes));
    }

    private void SaveWhenDue()
    {
        if ((DateTime.UtcNow - _lastSaveUtc).TotalSeconds >= 5)
        {
            SaveState();
        }
    }

    private void SaveState()
    {
        try
        {
            var temporaryPath = _filePath + ".tmp";
            var json = JsonSerializer.Serialize(
                _state,
                new JsonSerializerOptions { WriteIndented = true });

            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, _filePath, overwrite: true);
            _lastSaveUtc = DateTime.UtcNow;
        }
        catch
        {
            // Traffic statistics must never interrupt VPN operation.
        }
    }

    private SgTrafficSnapshot CreateSnapshot()
    {
        PersistedProfileState? profile = null;
        SessionState? session = null;

        if (_activeProfileId.IsNotEmpty())
        {
            _state.Profiles.TryGetValue(_activeProfileId, out profile);
            _sessions.TryGetValue(_activeProfileId, out session);
        }

        return new SgTrafficSnapshot
        {
            ProfileId = _activeProfileId,
            ProfileName = DisplayName(profile?.Name ?? _activeProfileName, _activeProfileId),
            CurrentUploadBytesPerSecond = _currentUploadBytesPerSecond,
            CurrentDownloadBytesPerSecond = _currentDownloadBytesPerSecond,
            SessionUploadBytes = session?.UploadBytes ?? 0,
            SessionDownloadBytes = session?.DownloadBytes ?? 0,
            TodayUploadBytes = profile?.TodayUploadBytes ?? 0,
            TodayDownloadBytes = profile?.TodayDownloadBytes ?? 0,
            MonthUploadBytes = profile?.MonthUploadBytes ?? 0,
            MonthDownloadBytes = profile?.MonthDownloadBytes ?? 0,
            TotalUploadBytes = profile?.TotalUploadBytes ?? 0,
            TotalDownloadBytes = profile?.TotalDownloadBytes ?? 0,
        };
    }

    private static string NormalizeProfileId(string? profileId)
    {
        return profileId?.Trim() ?? string.Empty;
    }

    private static string DisplayName(string? profileName, string? profileId)
    {
        var name = profileName?.Trim();
        if (name.IsNotEmpty())
        {
            return name!;
        }

        return profileId.IsNotEmpty()
            ? "Профиль"
            : "Профиль не выбран";
    }

    private static long ToBytes(long units, long bytesPerUnit)
    {
        if (units <= 0 || bytesPerUnit <= 0)
        {
            return 0;
        }

        return units > long.MaxValue / bytesPerUnit
            ? long.MaxValue
            : units * bytesPerUnit;
    }

    private static long CalculateRate(long bytes, double elapsedSeconds)
    {
        if (bytes <= 0)
        {
            return 0;
        }

        if (elapsedSeconds < 0.20 || elapsedSeconds > 15)
        {
            return bytes;
        }

        var rate = bytes / elapsedSeconds;
        return rate >= long.MaxValue
            ? long.MaxValue
            : Math.Max(0, (long)Math.Round(rate));
    }

    private static long SaturatingAdd(long left, long right)
    {
        if (right <= 0)
        {
            return left;
        }

        return left > long.MaxValue - right
            ? long.MaxValue
            : left + right;
    }

    private static string CurrentDay()
    {
        return DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static string CurrentMonth()
    {
        return DateTime.Now.ToString("yyyy-MM", CultureInfo.InvariantCulture);
    }
}
