using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ServiceLib.Handler.Builder;

namespace v2rayN.Views;

public partial class SgProfileEditorWindow
{
    private sealed record EditorChoice(string Name, string Value)
    {
        public override string ToString() => Name;
    }

    private readonly Config _config;
    private readonly Dictionary<string, TextBox> _texts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ComboBox> _combos = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CheckBox> _checks = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FrameworkElement> _fieldHosts = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _previewTimer;

    private ProfileItem? _profile;
    private AwgProfile? _awgProfile;
    private string _awgOriginalContent = string.Empty;
    private TextBox? _generatedConfig;
    private bool _validated;
    private bool _loadingEditor;
    private bool _updatingPreview;

    public SgProfileEditorWindow()
    {
        InitializeComponent();
        SgWindowSizing.AttachLarge(this);
        _config = AppManager.Instance.Config;
        _previewTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(350),
        };
        _previewTimer.Tick += async (_, _) =>
        {
            _previewTimer.Stop();
            await RefreshGeneratedConfigPreviewAsync();
        };
        Loaded += async (_, _) => await LoadEditorAsync();
        WindowsUtils.SetDarkBorder(this, _config.UiItem.CurrentTheme);
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void Help_Click(object sender, RoutedEventArgs e)
    {
        new SgHelpWindow("expert") { Owner = this }.ShowDialog();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void Reload_Click(object sender, RoutedEventArgs e)
    {
        await LoadEditorAsync();
    }

    private async void Validate_Click(object sender, RoutedEventArgs e)
    {
        await ValidateAsync(showSuccess: true);
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        await SaveAsync(asCopy: false);
    }

    private async void Copy_Click(object sender, RoutedEventArgs e)
    {
        await SaveAsync(asCopy: true);
    }

    private async Task LoadEditorAsync()
    {
        _loadingEditor = true;
        _previewTimer.Stop();
        _validated = false;
        _texts.Clear();
        _combos.Clear();
        _checks.Clear();
        _fieldHosts.Clear();
        _generatedConfig = null;
        editorHost.Children.Clear();

        _awgProfile = AmneziaWgManager.Instance.GetSelectedProfile();
        if (_awgProfile != null)
        {
            _profile = null;
            await BuildAwgEditorAsync(_awgProfile);
            _loadingEditor = false;
            return;
        }

        _profile = await AppManager.Instance.GetProfileItem(_config.IndexId);
        if (_profile == null)
        {
            txtWindowTitle.Text = "Редактор профиля";
            txtWindowSubtitle.Text = "Профиль не выбран";
            editorHost.Children.Add(CreateNoticeCard(
                "Профиль не выбран",
                "Выберите профиль в главном окне и снова откройте редактор.",
                "SgWarningBrush"));
            btnSave.IsEnabled = false;
            btnCopy.IsEnabled = false;
            SetStatus("Нет профиля для редактирования.", "SgWarningBrush");
            _loadingEditor = false;
            return;
        }

        var isManagedProfile = _profile.Subid.IsNotEmpty();
        btnSave.IsEnabled = !isManagedProfile;
        btnSave.ToolTip = isManagedProfile
            ? "Профиль получен из подписки. Для ручной правки создайте локальную копию."
            : "Проверить и сохранить изменения в текущем локальном профиле.";
        btnCopy.IsEnabled = true;

        switch (_profile.ConfigType)
        {
            case EConfigType.VLESS:
                BuildVlessEditor(_profile);
                break;

            case EConfigType.Hysteria2:
                BuildHysteriaEditor(_profile);
                break;

            default:
                BuildUnsupportedEditor(_profile);
                break;
        }

        _loadingEditor = false;
        SetStatus(
            isManagedProfile
                ? "Профиль управляется подпиской. Для ручной правки используйте «Создать копию»."
                : "Изменения ещё не проверялись. Сохранение всегда запускает семантическую проверку и тест выбранным ядром.",
            isManagedProfile ? "SgWarningBrush" : "SgMutedBrush");
        await RefreshGeneratedConfigPreviewAsync();
    }

    private Border CreateNoticeCard(string title, string message, string brushKey)
    {
        var card = CreateCard(title, null);
        card.Panel.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11.5,
            LineHeight = 18,
            Foreground = (Brush)FindResource(brushKey),
        });
        return card.Border;
    }

    private (Border Border, StackPanel Panel) CreateCard(string title, string? description)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("SgTextBrush"),
        });

        if (description.IsNotEmpty())
        {
            panel.Children.Add(new TextBlock
            {
                Text = description,
                Margin = new Thickness(0, 5, 0, 13),
                FontSize = 10.5,
                LineHeight = 17,
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)FindResource("SgMutedBrush"),
            });
        }
        else
        {
            panel.Children.Add(new Border { Height = 12 });
        }

        var border = new Border { Child = panel };
        border.Style = (Style)FindResource("EditorCard");
        return (border, panel);
    }

    private WrapPanel AddFieldWrap(StackPanel panel)
    {
        var wrap = new WrapPanel
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        panel.Children.Add(wrap);
        return wrap;
    }

    private StackPanel CreateFieldBox(string label, double width = 444)
    {
        var box = new StackPanel
        {
            Width = width,
            Margin = new Thickness(0, 0, 12, 12),
        };
        var caption = new TextBlock { Text = label };
        caption.Style = (Style)FindResource("EditorLabel");
        box.Children.Add(caption);
        return box;
    }

    private TextBox AddText(
        Panel host,
        string key,
        string label,
        string? value,
        bool multiline = false,
        bool readOnly = false,
        double width = 444,
        double height = 0)
    {
        var box = CreateFieldBox(label, width);
        var text = new TextBox
        {
            Text = value ?? string.Empty,
            IsReadOnly = readOnly,
        };
        text.Style = (Style)FindResource(multiline ? "EditorCodeTextBox" : "EditorTextBox");
        if (multiline)
        {
            text.Height = height > 0 ? height : 125;
        }
        if (!readOnly)
        {
            text.TextChanged += (_, _) => MarkEditorChanged();
        }
        box.Children.Add(text);
        host.Children.Add(box);
        _texts[key] = text;
        _fieldHosts[key] = box;
        return text;
    }

    private ComboBox AddCombo(
        Panel host,
        string key,
        string label,
        IEnumerable<EditorChoice> values,
        string? selectedValue,
        double width = 444,
        bool editable = false)
    {
        var box = CreateFieldBox(label, width);
        var choices = values.ToList();
        var combo = new ComboBox
        {
            ItemsSource = choices,
            DisplayMemberPath = nameof(EditorChoice.Name),
            SelectedValuePath = nameof(EditorChoice.Value),
            SelectedValue = selectedValue ?? string.Empty,
            IsEditable = editable,
            IsTextSearchEnabled = true,
            StaysOpenOnEdit = true,
        };
        if (editable
            && combo.SelectedItem == null
            && selectedValue.IsNotEmpty())
        {
            combo.Text = selectedValue;
        }
        combo.Style = (Style)FindResource("EditorComboBox");
        TextSearch.SetTextPath(combo, nameof(EditorChoice.Name));
        combo.SelectionChanged += (_, _) => MarkEditorChanged();
        box.Children.Add(combo);
        host.Children.Add(box);
        _combos[key] = combo;
        _fieldHosts[key] = box;
        return combo;
    }

    private CheckBox AddCheck(
        Panel host,
        string key,
        string label,
        bool value)
    {
        var check = new CheckBox
        {
            Content = label,
            IsChecked = value,
        };
        check.Style = (Style)FindResource("EditorCheckBox");
        check.Checked += (_, _) => MarkEditorChanged();
        check.Unchecked += (_, _) => MarkEditorChanged();
        host.Children.Add(check);
        _checks[key] = check;
        _fieldHosts[key] = check;
        return check;
    }

    private void MarkEditorChanged()
    {
        if (_loadingEditor || _updatingPreview)
        {
            return;
        }

        _validated = false;
        if (_generatedConfig != null)
        {
            _previewTimer.Stop();
            _previewTimer.Start();
        }
    }

    private void SetVisible(string key, bool visible)
    {
        if (_fieldHosts.TryGetValue(key, out var host))
        {
            host.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void SetTaggedCardVisibility(string tag, bool visible)
    {
        foreach (var border in editorHost.Children.OfType<Border>())
        {
            if (Equals(border.Tag, tag))
            {
                border.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }

    private bool IsFieldVisible(string key)
    {
        return !_fieldHosts.TryGetValue(key, out var host)
            || host.Visibility == Visibility.Visible;
    }

    private void AddOptionalParametersInfo(
        StackPanel panel,
        ProfileItem profile,
        bool includeFinalMask)
    {
        var lines = new List<string>();
        if (includeFinalMask)
        {
            lines.Add(profile.Finalmask.IsNotEmpty()
                ? "FinalMask: загружен из профиля и будет передан ядру."
                : "FinalMask: не настроен. Пустое значение означает, что дополнительная маскировка выключена. Формат — JSON-объект streamSettings.finalmask.");
        }
        lines.Add(profile.EchConfigList.IsNotEmpty()
            ? "ECH: конфигурация загружена из профиля."
            : "ECH: не настроен. Используйте только конфигурацию, выданную сервером или его DNS-инфраструктурой.");
        lines.Add(profile.CertSha.IsNotEmpty()
            ? "Certificate pinning: задан SHA-256 закреплённого сертификата."
            : "Certificate pinning: выключен. Для включения нужен SHA-256 из 64 шестнадцатеричных символов.");
        lines.Add(profile.Cert.IsNotEmpty()
            ? "PEM-цепочка: собственный сертификат загружен из профиля."
            : "PEM-цепочка: не задана; используется системное хранилище доверия Windows.");

        var card = new Border
        {
            Margin = new Thickness(0, 4, 0, 12),
            Padding = new Thickness(12),
            Background = (Brush)FindResource("SgSurfaceSoftBrush"),
            BorderBrush = (Brush)FindResource("SgBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Child = new TextBlock
            {
                Text = string.Join(Environment.NewLine, lines.Select(line => "• " + line)),
                TextWrapping = TextWrapping.Wrap,
                FontSize = 10.5,
                LineHeight = 17,
                Foreground = (Brush)FindResource("SgMutedBrush"),
            },
        };
        panel.Children.Add(card);
    }

    private TextBlock AddInfo(StackPanel panel, string text, string brush = "SgAccentBrush")
    {
        var block = new TextBlock
        {
            Text = text,
            Margin = new Thickness(0, 0, 0, 12),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource(brush),
            TextWrapping = TextWrapping.Wrap,
        };
        panel.Children.Add(block);
        return block;
    }

    private static IEnumerable<EditorChoice> CoreChoices(
        bool includeSingBox,
        EConfigType configType)
    {
        yield return new EditorChoice(
            configType == EConfigType.Hysteria2
                ? "Xray — экспериментально, поддерживает FinalMask"
                : "Xray",
            nameof(ECoreType.Xray));

        if (includeSingBox)
        {
            yield return new EditorChoice(
                configType == EConfigType.Hysteria2
                    ? "sing-box — основной режим Hysteria2"
                    : "sing-box",
                nameof(ECoreType.sing_box));
        }
    }

    private void BuildVlessEditor(ProfileItem profile)
    {
        txtWindowTitle.Text = "Редактор VLESS";
        txtWindowSubtitle.Text = profile.GetNetwork() == nameof(ETransport.xhttp)
            ? "VLESS XHTTP + REALITY/TLS"
            : "VLESS REALITY/TLS · raw";

        var protocolExtra = profile.GetProtocolExtra();
        var transport = profile.GetTransportExtra();

        var common = CreateCard(
            "Основные параметры",
            "Название, сервер и учётные данные профиля.");
        AddInfo(
            common.Panel,
            profile.Subid.IsNotEmpty()
                ? "Источник: подписка. Для постоянной правки лучше создать независимую копию."
                : "Источник: локальный профиль.");
        var commonWrap = AddFieldWrap(common.Panel);
        AddText(commonWrap, "remarks", "Название профиля", profile.Remarks);
        AddCombo(
            commonWrap,
            "core",
            "Ядро",
            CoreChoices(includeSingBox: true, configType: EConfigType.VLESS),
            (profile.CoreType ?? ECoreType.Xray).ToString());
        AddText(commonWrap, "address", "Адрес сервера", profile.Address);
        AddText(commonWrap, "port", "Порт", profile.Port.ToString());
        AddText(commonWrap, "password", "UUID / идентификатор пользователя", profile.Password);
        AddText(commonWrap, "encryption", "Шифрование VLESS", protocolExtra.VlessEncryption ?? Global.None);
        AddCombo(
            commonWrap,
            "flow",
            "Режим Flow",
            Global.Flows.Select(value => new EditorChoice(
                value.IsNullOrEmpty() ? "Не задан" : value,
                value)),
            protocolExtra.Flow ?? string.Empty,
            editable: true);
        AddCheck(common.Panel, "mux", "Использовать Mux", profile.MuxEnabled == true);
        editorHost.Children.Add(common.Border);

        var transportCard = CreateCard(
            "Транспорт",
            "SG Client гарантированно обслуживает профили raw и XHTTP. XHTTP проверяется только ядром Xray.");
        var transportWrap = AddFieldWrap(transportCard.Panel);
        var network = AddCombo(
            transportWrap,
            "network",
            "Транспорт",
            new[]
            {
                new EditorChoice("raw / TCP", nameof(ETransport.raw)),
                new EditorChoice("XHTTP", nameof(ETransport.xhttp)),
            },
            profile.GetNetwork());
        var rawHeader = AddCombo(
            transportWrap,
            "rawHeader",
            "Заголовок raw",
            new[]
            {
                new EditorChoice("Без HTTP-заголовка", Global.None),
                new EditorChoice("HTTP", Global.RawHeaderHttp),
            },
            transport.RawHeaderType ?? Global.None);
        AddText(transportWrap, "host", "Заголовок Host", transport.Host);
        AddText(transportWrap, "path", "Путь", transport.Path);
        AddCombo(
            transportWrap,
            "xhttpMode",
            "Режим XHTTP",
            Global.XhttpMode.Select(value => new EditorChoice(value, value)),
            transport.XhttpMode ?? Global.DefaultXhttpMode,
            editable: true);
        AddText(
            transportCard.Panel,
            "xhttpExtra",
            "XHTTP Extra · JSON-объект",
            PrettyJson(transport.XhttpExtra),
            multiline: true,
            width: 900,
            height: 140);
        network.SelectionChanged += (_, _) => RefreshVlessTransportState();
        rawHeader.SelectionChanged += (_, _) => RefreshVlessTransportState();
        editorHost.Children.Add(transportCard.Border);

        var security = CreateCard(
            "Безопасность TLS / REALITY",
            "Поля сохраняются в профиль, а затем полный конфиг проверяется выбранным ядром.");
        var securityWrap = AddFieldWrap(security.Panel);
        var securityCombo = AddCombo(
            securityWrap,
            "streamSecurity",
            "Режим безопасности",
            new[]
            {
                new EditorChoice("Без TLS", string.Empty),
                new EditorChoice("TLS", Global.StreamSecurity),
                new EditorChoice("REALITY", Global.StreamSecurityReality),
            },
            profile.StreamSecurity);
        AddText(securityWrap, "sni", "SNI / имя сервера", profile.Sni);
        AddCombo(
            securityWrap,
            "fingerprint",
            "Отпечаток TLS (Fingerprint)",
            Global.Fingerprints.Select(value => new EditorChoice(
                value.IsNullOrEmpty() ? "По умолчанию" : value,
                value)),
            profile.Fingerprint,
            editable: true);
        AddCombo(
            securityWrap,
            "alpn",
            "Протоколы ALPN",
            Global.Alpns.Select(value => new EditorChoice(
                value.IsNullOrEmpty() ? "Не задан" : value,
                value)),
            profile.Alpn,
            editable: true);
        AddCheck(security.Panel, "allowInsecure", "Разрешить небезопасный сертификат", profile.GetAllowInsecure());
        securityCombo.SelectionChanged += (_, _) => RefreshSecurityState();
        editorHost.Children.Add(security.Border);

        var reality = CreateCard(
            "Параметры REALITY",
            "Используются только при выборе REALITY.");
        var realityWrap = AddFieldWrap(reality.Panel);
        AddText(realityWrap, "publicKey", "Публичный ключ", profile.PublicKey);
        AddText(realityWrap, "shortId", "Короткий идентификатор (Short ID)", profile.ShortId);
        AddText(realityWrap, "spiderX", "Путь SpiderX", profile.SpiderX);
        AddText(realityWrap, "mldsa65", "Проверочный ключ ML-DSA-65", profile.Mldsa65Verify);
        reality.Border.Tag = "reality-card";
        editorHost.Children.Add(reality.Border);

        var finalMask = CreateCard(
            "FinalMask",
            "Показывается только для режимов, в которых параметр может попасть в фактический конфиг. Используйте значение из профиля, SG-Panel или проверенный JSON администратора.");
        finalMask.Border.Tag = "finalmask-card";
        AddText(
            finalMask.Panel,
            "finalmask",
            "FinalMask · JSON-объект",
            PrettyJson(profile.Finalmask),
            multiline: true,
            width: 900,
            height: 140);
        AddInfo(
            finalMask.Panel,
            profile.Finalmask.IsNotEmpty()
                ? "Источник: значение уже присутствует в профиле."
                : "Не настроено. Пустое значение означает, что дополнительная маскировка выключена.",
            profile.Finalmask.IsNotEmpty() ? "SgAccentBrush" : "SgMutedBrush");
        editorHost.Children.Add(finalMask.Border);

        var tlsAdvanced = CreateCard(
            "Дополнительная проверка TLS",
            "Параметры обычного TLS. Для REALITY этот блок полностью скрыт, потому что ECH, PEM и закрепление обычного сертификата там не используются.");
        tlsAdvanced.Border.Tag = "tls-advanced-card";
        AddInfo(
            tlsAdvanced.Panel,
            "По умолчанию не требуется — используется системное хранилище доверия Windows.",
            "SgMutedBrush");
        AddText(
            tlsAdvanced.Panel,
            "ech",
            "Конфигурация ECH",
            profile.EchConfigList,
            multiline: true,
            width: 900,
            height: 95);
        var advWrap = AddFieldWrap(tlsAdvanced.Panel);
        AddText(advWrap, "verifyPeer", "Проверять сертификат по имени", profile.VerifyPeerCertByName);
        AddText(advWrap, "certSha", "SHA-256 закреплённого сертификата", profile.CertSha);
        AddText(
            tlsAdvanced.Panel,
            "cert",
            "Сертификат PEM / цепочка",
            profile.Cert,
            multiline: true,
            width: 900,
            height: 135);
        AddOptionalParametersInfo(tlsAdvanced.Panel, profile, includeFinalMask: false);
        AddInfo(tlsAdvanced.Panel, "Значения должны приходить из профиля, подписки, DNS или от администратора. Не подбирайте их случайно.", "SgWarningBrush");
        editorHost.Children.Add(tlsAdvanced.Border);

        var generated = CreateCard(
            "Текущий эффективный config.json",
            "Показывает фактически сформированную конфигурацию выбранного ядра. Поля, которые текущий режим не использует, сюда не попадают.");
        _generatedConfig = AddText(
            generated.Panel,
            "generated",
            "Предварительный config.json",
            "Формирование текущей конфигурации…",
            multiline: true,
            readOnly: true,
            width: 900,
            height: 240);
        editorHost.Children.Add(generated.Border);

        RefreshVlessTransportState();
        RefreshSecurityState();
    }

    private void BuildHysteriaEditor(ProfileItem profile)
    {
        txtWindowTitle.Text = "Редактор Hysteria2";
        txtWindowSubtitle.Text = "Полный профиль Hysteria2 · все поддерживаемые параметры";

        var protocolExtra = profile.GetProtocolExtra();

        var common = CreateCard(
            "Основные параметры",
            "Название, сервер, порт и данные аутентификации.");
        AddInfo(
            common.Panel,
            profile.Subid.IsNotEmpty()
                ? "Источник: подписка. Управляемый профиль доступен только для чтения; для ручной правки создайте независимую копию."
                : "Источник: локальный профиль.");
        var commonWrap = AddFieldWrap(common.Panel);
        AddText(commonWrap, "remarks", "Название профиля", profile.Remarks);
        var hysteriaCore = AddCombo(
            commonWrap,
            "core",
            "Ядро",
            CoreChoices(
                includeSingBox: true,
                configType: EConfigType.Hysteria2),
            (profile.CoreType ?? ECoreType.sing_box).ToString());
        hysteriaCore.SelectionChanged += (_, _) => RefreshHysteriaCoreState();
        AddText(commonWrap, "address", "Адрес сервера", profile.Address);
        AddText(commonWrap, "port", "Порт", profile.Port.ToString());
        AddText(commonWrap, "password", "Пароль / токен аутентификации", profile.Password);
        editorHost.Children.Add(common.Border);

        var disguise = CreateCard(
            "Маскировка и смена портов",
            "Параметры obfs и port hopping. Пустой диапазон означает один основной порт.");
        var disguiseWrap = AddFieldWrap(disguise.Panel);
        var obfsCombo = AddCombo(
            disguiseWrap,
            "obfs",
            "Тип маскировки",
            new[]
            {
                new EditorChoice("Выключена", string.Empty),
                new EditorChoice("Salamander", "salamander"),
            },
            protocolExtra.SalamanderPass.IsNotEmpty() ? "salamander" : string.Empty);
        AddText(disguiseWrap, "salamanderPass", "Пароль маскировки", protocolExtra.SalamanderPass);
        AddText(disguiseWrap, "ports", "Диапазон портов", protocolExtra.Ports);
        AddText(disguiseWrap, "hopInterval", "Интервал смены портов, сек.", protocolExtra.HopInterval);
        obfsCombo.SelectionChanged += (_, _) => RefreshHysteriaObfsState();
        editorHost.Children.Add(disguise.Border);

        var performance = CreateCard(
            "Скорость и FinalMask",
            "Ограничения скорости необязательны. Пустые значения означают автоматический режим.");
        var speedWrap = AddFieldWrap(performance.Panel);
        AddText(speedWrap, "upMbps", "Максимальная отдача, Мбит/с", protocolExtra.UpMbps?.ToString());
        AddText(speedWrap, "downMbps", "Максимальная загрузка, Мбит/с", protocolExtra.DownMbps?.ToString());
        AddText(
            performance.Panel,
            "finalmask",
            "FinalMask · JSON-объект",
            PrettyJson(profile.Finalmask),
            multiline: true,
            width: 900,
            height: 145);
        AddInfo(
            performance.Panel,
            "FinalMask передаётся и проверяется при выборе Xray. Для sing-box поле должно оставаться пустым.",
            "SgWarningBrush");
        editorHost.Children.Add(performance.Border);

        var tls = CreateCard(
            "TLS и проверка сервера",
            "Hysteria2 всегда использует TLS. Здесь доступны все параметры, которые SG Client сохраняет и передаёт ядру.");
        var tlsWrap = AddFieldWrap(tls.Panel);
        AddText(tlsWrap, "tlsMode", "TLS", "Включён — обязателен для Hysteria2", readOnly: true);
        AddText(tlsWrap, "sni", "SNI / имя сервера", profile.Sni);
        AddCombo(
            tlsWrap,
            "fingerprint",
            "Отпечаток TLS (Fingerprint)",
            Global.Fingerprints.Select(value => new EditorChoice(
                value.IsNullOrEmpty() ? "По умолчанию" : value,
                value)),
            profile.Fingerprint,
            editable: true);
        AddCombo(
            tlsWrap,
            "alpn",
            "Протоколы ALPN",
            Global.Alpns.Select(value => new EditorChoice(
                value.IsNullOrEmpty() ? "Не задано" : value,
                value)),
            profile.Alpn,
            editable: true);
        AddCheck(tls.Panel, "allowInsecure", "Разрешить небезопасный сертификат", profile.GetAllowInsecure());
        AddInfo(
            tls.Panel,
            "Опасно: при включении подлинность сертификата сервера не гарантируется.",
            "SgWarningBrush");
        editorHost.Children.Add(tls.Border);

        var certificate = CreateCard(
            "ECH и закрепление сертификата",
            "Расширенные параметры не удалены: ECH, проверка имени, SHA-256 и собственная цепочка сертификатов.");
        AddText(
            certificate.Panel,
            "ech",
            "Конфигурация ECH",
            profile.EchConfigList,
            multiline: true,
            width: 900,
            height: 100);
        var certificateWrap = AddFieldWrap(certificate.Panel);
        AddText(certificateWrap, "verifyPeer", "Проверять сертификат по имени", profile.VerifyPeerCertByName);
        AddText(certificateWrap, "certSha", "SHA-256 закреплённого сертификата", profile.CertSha);
        AddText(
            certificate.Panel,
            "cert",
            "Сертификат PEM / цепочка сертификатов",
            profile.Cert,
            multiline: true,
            width: 900,
            height: 150);
        AddOptionalParametersInfo(certificate.Panel, profile, includeFinalMask: false);
        AddInfo(certificate.Panel, "Примеры ECH, SHA-256 и PEM приведены в docs/EXPERT-SETTINGS-GUIDE-RU.md.", "SgAccentBrush");
        editorHost.Children.Add(certificate.Border);

        var generated = CreateCard(
            "Текущий эффективный config.json",
            "Показывает фактически сформированную конфигурацию выбранного ядра и обновляется после изменения используемых полей.");
        _generatedConfig = AddText(
            generated.Panel,
            "generated",
            "Предварительный config.json",
            "Формирование текущей конфигурации…",
            multiline: true,
            readOnly: true,
            width: 900,
            height: 240);
        editorHost.Children.Add(generated.Border);

        RefreshHysteriaObfsState();
        RefreshHysteriaCoreState();
    }

    private async Task BuildAwgEditorAsync(AwgProfile profile)
    {
        txtWindowTitle.Text = "Редактор AmneziaWG";
        txtWindowSubtitle.Text = "Все параметры профиля и полный .conf";
        btnCopy.IsEnabled = false;

        if (!File.Exists(profile.ConfigPath))
        {
            editorHost.Children.Add(CreateNoticeCard(
                "Файл профиля не найден",
                profile.ConfigPath,
                "SgErrorBrush"));
            btnSave.IsEnabled = false;
            SetStatus("Файл AmneziaWG отсутствует.", "SgErrorBrush");
            return;
        }

        _awgOriginalContent = await File.ReadAllTextAsync(profile.ConfigPath);

        var common = CreateCard(
            "Основные параметры",
            "Поля редактируют существующий .conf без удаления неизвестных параметров.");
        AddInfo(common.Panel, "Ядро: AmneziaWG", "SgAccentBrush");
        var commonWrap = AddFieldWrap(common.Panel);
        AddText(commonWrap, "awgName", "Название профиля", profile.Name);
        AddText(commonWrap, "awgAddress", "Адрес интерфейса", ReadIniValue(_awgOriginalContent, "Interface", "Address"));
        AddText(commonWrap, "awgDns", "DNS-серверы", ReadIniValue(_awgOriginalContent, "Interface", "DNS"));
        AddText(commonWrap, "awgMtu", "MTU", ReadIniValue(_awgOriginalContent, "Interface", "MTU"));
        AddText(commonWrap, "awgPrivateKey", "Закрытый ключ (PrivateKey)", ReadIniValue(_awgOriginalContent, "Interface", "PrivateKey"));
        editorHost.Children.Add(common.Border);

        var mask = CreateCard(
            "Параметры AmneziaWG",
            "Jc/Jmin/Jmax, S1–S4 и H1–H4 передаются в конфигурацию без упрощения.");
        var maskWrap = AddFieldWrap(mask.Panel);
        foreach (var key in new[] { "Jc", "Jmin", "Jmax", "S1", "S2", "S3", "S4", "H1", "H2", "H3", "H4" })
        {
            AddText(
                maskWrap,
                "awg" + key,
                key,
                ReadIniValue(_awgOriginalContent, "Interface", key),
                width: 286);
        }
        editorHost.Children.Add(mask.Border);

        var peer = CreateCard(
            "Сервер и маршруты",
            "Поля секции Peer.");
        var peerWrap = AddFieldWrap(peer.Panel);
        AddText(peerWrap, "awgPublicKey", "Открытый ключ сервера (PublicKey)", ReadIniValue(_awgOriginalContent, "Peer", "PublicKey"));
        AddText(peerWrap, "awgPresharedKey", "Предварительный ключ (PresharedKey)", ReadIniValue(_awgOriginalContent, "Peer", "PresharedKey"));
        AddText(peerWrap, "awgEndpoint", "Сервер и порт (Endpoint)", ReadIniValue(_awgOriginalContent, "Peer", "Endpoint"));
        AddText(peerWrap, "awgAllowedIps", "Разрешённые сети (AllowedIPs)", ReadIniValue(_awgOriginalContent, "Peer", "AllowedIPs"));
        AddText(peerWrap, "awgKeepalive", "Интервал поддержания соединения", ReadIniValue(_awgOriginalContent, "Peer", "PersistentKeepalive"));
        editorHost.Children.Add(peer.Border);

        var raw = CreateCard(
            "Полная конфигурация",
            "Неизвестные и будущие параметры сохраняются. Перед записью файл проверяется парсером SG Client.");
        AddText(
            raw.Panel,
            "awgConfig",
            "Полный .conf",
            _awgOriginalContent,
            multiline: true,
            width: 900,
            height: 330);
        editorHost.Children.Add(raw.Border);

        SetStatus("Изменения ещё не проверялись.", "SgMutedBrush");
    }

    private void BuildUnsupportedEditor(ProfileItem profile)
    {
        txtWindowTitle.Text = $"Редактор {profile.ConfigType}";
        txtWindowSubtitle.Text = "Импортированный профиль вне набора наших панелей";
        btnSave.IsEnabled = false;
        btnCopy.IsEnabled = false;

        editorHost.Children.Add(CreateNoticeCard(
            "Для этого протокола фирменная форма пока не готова",
            "SG Client не будет показывать поля, которые не может надёжно сохранить и проверить. "
                + "Фирменный редактор 070 поддерживает VLESS raw, VLESS XHTTP, Hysteria2 и AmneziaWG.",
            "SgWarningBrush"));
        SetStatus("Редактирование заблокировано, чтобы не повредить профиль.", "SgWarningBrush");
    }

    private void RefreshVlessTransportState()
    {
        if (!_combos.TryGetValue("network", out var combo))
        {
            return;
        }

        var xhttp = string.Equals(
            combo.SelectedValue?.ToString(),
            nameof(ETransport.xhttp),
            StringComparison.OrdinalIgnoreCase);
        var rawHttp = !xhttp
            && string.Equals(
                ComboValue("rawHeader"),
                Global.RawHeaderHttp,
                StringComparison.OrdinalIgnoreCase);

        SetVisible("rawHeader", !xhttp);
        SetVisible("host", xhttp || rawHttp);
        SetVisible("path", xhttp || rawHttp);
        SetVisible("xhttpMode", xhttp);
        SetVisible("xhttpExtra", xhttp);

        if (_combos.TryGetValue("core", out var core))
        {
            if (xhttp)
            {
                core.SelectedValue = nameof(ECoreType.Xray);
            }
            core.IsEnabled = !xhttp;
            core.ToolTip = xhttp
                ? "XHTTP поддерживается в SG Client только ядром Xray."
                : "Для raw доступны Xray и sing-box; выбранный вариант будет проверен ядром.";
        }

        RefreshSecurityState();
        MarkEditorChanged();
    }

    private void RefreshSecurityState()
    {
        if (!_combos.TryGetValue("streamSecurity", out var security))
        {
            return;
        }

        var selected = security.SelectedValue?.ToString() ?? string.Empty;
        var reality = string.Equals(
            selected,
            Global.StreamSecurityReality,
            StringComparison.OrdinalIgnoreCase);
        var tls = string.Equals(
            selected,
            Global.StreamSecurity,
            StringComparison.OrdinalIgnoreCase);

        var xhttp = string.Equals(
            ComboValue("network"),
            nameof(ETransport.xhttp),
            StringComparison.OrdinalIgnoreCase);

        SetVisible("flow", !xhttp && (tls || reality));
        SetVisible("sni", tls || reality);
        SetVisible("fingerprint", tls || reality);
        SetVisible("alpn", tls);
        SetVisible("allowInsecure", tls);
        SetTaggedCardVisibility("reality-card", reality);
        SetTaggedCardVisibility("tls-advanced-card", tls);
        SetTaggedCardVisibility("finalmask-card", tls || reality);

        MarkEditorChanged();
    }

    private void RefreshHysteriaCoreState()
    {
        var isXray = ComboValue("core") == nameof(ECoreType.Xray);
        foreach (var key in new[] { "finalmask", "verifyPeer", "certSha" })
        {
            if (_texts.TryGetValue(key, out var text))
            {
                text.ToolTip = isXray
                    ? "Параметр будет передан Xray и проверен командой xray run -test."
                    : "Этот параметр передаётся только Xray. Для использования выберите Xray.";
                text.Opacity = isXray ? 1.0 : 0.72;
            }
        }
    }

    private void RefreshHysteriaObfsState()
    {
        var enabled = _combos.TryGetValue("obfs", out var combo)
            && string.Equals(
                combo.SelectedValue?.ToString(),
                "salamander",
                StringComparison.OrdinalIgnoreCase);
        SetEnabled("salamanderPass", enabled);
    }

    private void SetEnabled(string key, bool enabled)
    {
        if (_texts.TryGetValue(key, out var text))
        {
            text.IsEnabled = enabled;
        }
        if (_combos.TryGetValue(key, out var combo))
        {
            combo.IsEnabled = enabled;
        }
    }

    private string TextValue(string key)
    {
        return _texts.TryGetValue(key, out var text)
            ? text.Text.Trim()
            : string.Empty;
    }

    private string ComboValue(string key)
    {
        if (!_combos.TryGetValue(key, out var combo))
        {
            return string.Empty;
        }

        if (combo.IsEditable)
        {
            var entered = combo.Text?.Trim() ?? string.Empty;
            var known = combo.Items
                .OfType<EditorChoice>()
                .FirstOrDefault(item =>
                    item.Name.Equals(
                        entered,
                        StringComparison.OrdinalIgnoreCase)
                    || item.Value.Equals(
                        entered,
                        StringComparison.OrdinalIgnoreCase));
            return known?.Value ?? entered;
        }

        return combo.SelectedValue?.ToString() ?? string.Empty;
    }

    private bool CheckValue(string key)
    {
        return _checks.TryGetValue(key, out var check)
            && check.IsChecked == true;
    }

    private ProfileItem? BuildCandidateProfile()
    {
        if (_profile == null)
        {
            return null;
        }

        var candidate = JsonUtils.DeepCopy(_profile);
        if (candidate == null)
        {
            return null;
        }

        candidate.Remarks = TextValue("remarks");
        candidate.Address = TextValue("address");
        candidate.Port = int.TryParse(TextValue("port"), out var port) ? port : 0;
        candidate.Password = TextValue("password");
        candidate.CoreType = Enum.TryParse<ECoreType>(ComboValue("core"), out var core)
            ? core
            : candidate.CoreType;

        candidate.Sni = TextValue("sni");
        candidate.Fingerprint = ComboValue("fingerprint");
        candidate.Alpn = ComboValue("alpn");
        candidate.AllowInsecure = CheckValue("allowInsecure")
            ? Global.StringTrue
            : Global.StringFalse;
        candidate.EchConfigList = TextValue("ech");
        candidate.VerifyPeerCertByName = TextValue("verifyPeer");
        candidate.CertSha = TextValue("certSha");
        candidate.Cert = TextValue("cert");
        candidate.Finalmask = TextValue("finalmask");

        if (candidate.ConfigType == EConfigType.VLESS)
        {
            candidate.Network = ComboValue("network");
            candidate.StreamSecurity = ComboValue("streamSecurity");
            candidate.MuxEnabled = CheckValue("mux");

            var xhttp = string.Equals(
                candidate.Network,
                nameof(ETransport.xhttp),
                StringComparison.OrdinalIgnoreCase);

            candidate.SetProtocolExtra(candidate.GetProtocolExtra() with
            {
                Flow = xhttp ? null : ComboValue("flow").NullIfEmpty(),
                VlessEncryption = TextValue("encryption").NullIfEmpty(),
            });
            var rawHeader = xhttp ? null : ComboValue("rawHeader").NullIfEmpty();
            var rawHttp = string.Equals(
                rawHeader,
                Global.RawHeaderHttp,
                StringComparison.OrdinalIgnoreCase);
            candidate.SetTransportExtra(candidate.GetTransportExtra() with
            {
                RawHeaderType = rawHeader,
                Host = (xhttp || rawHttp) ? TextValue("host").NullIfEmpty() : null,
                Path = (xhttp || rawHttp) ? TextValue("path").NullIfEmpty() : null,
                XhttpMode = xhttp ? ComboValue("xhttpMode").NullIfEmpty() : null,
                XhttpExtra = xhttp ? TextValue("xhttpExtra").NullIfEmpty() : null,
            });

            var reality = string.Equals(
                candidate.StreamSecurity,
                Global.StreamSecurityReality,
                StringComparison.OrdinalIgnoreCase);
            var tls = string.Equals(
                candidate.StreamSecurity,
                Global.StreamSecurity,
                StringComparison.OrdinalIgnoreCase);

            if (reality)
            {
                candidate.Sni = TextValue("sni");
                candidate.Fingerprint = ComboValue("fingerprint");
                candidate.Alpn = null;
                candidate.AllowInsecure = Global.StringFalse;
                candidate.PublicKey = TextValue("publicKey");
                candidate.ShortId = TextValue("shortId");
                candidate.SpiderX = TextValue("spiderX");
                candidate.Mldsa65Verify = TextValue("mldsa65");
                candidate.EchConfigList = null;
                candidate.VerifyPeerCertByName = null;
                candidate.CertSha = null;
                candidate.Cert = null;
            }
            else if (tls)
            {
                candidate.Sni = TextValue("sni");
                candidate.Fingerprint = ComboValue("fingerprint");
                candidate.Alpn = ComboValue("alpn");
                candidate.AllowInsecure = CheckValue("allowInsecure")
                    ? Global.StringTrue
                    : Global.StringFalse;
                candidate.PublicKey = null;
                candidate.ShortId = null;
                candidate.SpiderX = null;
                candidate.Mldsa65Verify = null;
                candidate.EchConfigList = TextValue("ech").NullIfEmpty();
                candidate.VerifyPeerCertByName = TextValue("verifyPeer").NullIfEmpty();
                candidate.CertSha = TextValue("certSha").NullIfEmpty();
                candidate.Cert = TextValue("cert").NullIfEmpty();
            }
            else
            {
                candidate.Sni = null;
                candidate.Fingerprint = null;
                candidate.Alpn = null;
                candidate.AllowInsecure = Global.StringFalse;
                candidate.PublicKey = null;
                candidate.ShortId = null;
                candidate.SpiderX = null;
                candidate.Mldsa65Verify = null;
                candidate.EchConfigList = null;
                candidate.VerifyPeerCertByName = null;
                candidate.CertSha = null;
                candidate.Cert = null;
                candidate.Finalmask = null;
            }
        }
        else if (candidate.ConfigType == EConfigType.Hysteria2)
        {
            candidate.Network = nameof(ETransport.raw);
            candidate.StreamSecurity = Global.StreamSecurity;

            var obfsEnabled = ComboValue("obfs") == "salamander";
            candidate.SetProtocolExtra(candidate.GetProtocolExtra() with
            {
                SalamanderPass = obfsEnabled
                    ? TextValue("salamanderPass").NullIfEmpty()
                    : null,
                Ports = TextValue("ports").NullIfEmpty(),
                HopInterval = TextValue("hopInterval").NullIfEmpty(),
                UpMbps = ParseNullablePositiveInt(TextValue("upMbps")),
                DownMbps = ParseNullablePositiveInt(TextValue("downMbps")),
            });
        }

        return candidate;
    }

    private static int? ParseNullablePositiveInt(string value)
    {
        return int.TryParse(value, out var parsed) && parsed >= 0
            ? parsed
            : null;
    }

    private bool TextFieldChanged(string key, string? original)
    {
        return _texts.ContainsKey(key)
            && !string.Equals(
                TextValue(key),
                original?.Trim() ?? string.Empty,
                StringComparison.Ordinal);
    }

    private bool ComboFieldChanged(string key, string? original)
    {
        return _combos.ContainsKey(key)
            && !string.Equals(
                ComboValue(key),
                original?.Trim() ?? string.Empty,
                StringComparison.OrdinalIgnoreCase);
    }

    private bool ValidateContextualFields(out string error)
    {
        error = string.Empty;
        if (_profile?.ConfigType != EConfigType.VLESS)
        {
            return true;
        }

        var originalTransport = _profile.GetTransportExtra();
        var xhttp = string.Equals(
            ComboValue("network"),
            nameof(ETransport.xhttp),
            StringComparison.OrdinalIgnoreCase);
        var rawHttp = !xhttp
            && string.Equals(
                ComboValue("rawHeader"),
                Global.RawHeaderHttp,
                StringComparison.OrdinalIgnoreCase);

        if (!xhttp && !rawHttp
            && (TextFieldChanged("host", originalTransport.Host)
                || TextFieldChanged("path", originalTransport.Path)))
        {
            error = "Host и Path не используются при raw/TCP без HTTP-заголовка. "
                + "Верните несохранённые значения либо выберите HTTP-заголовок.";
            return false;
        }

        if (!xhttp
            && (TextFieldChanged("xhttpExtra", PrettyJson(originalTransport.XhttpExtra))
                || ComboFieldChanged(
                    "xhttpMode",
                    originalTransport.XhttpMode ?? Global.DefaultXhttpMode)))
        {
            error = "Параметры XHTTP не используются при raw/TCP. "
                + "Верните несохранённые значения либо выберите транспорт XHTTP.";
            return false;
        }

        if (xhttp && ComboFieldChanged(
                "rawHeader",
                originalTransport.RawHeaderType ?? Global.None))
        {
            error = "Заголовок raw не используется транспортом XHTTP. "
                + "Верните несохранённое значение либо выберите raw/TCP.";
            return false;
        }

        if (xhttp
            && ComboFieldChanged(
                "flow",
                _profile.GetProtocolExtra().Flow ?? string.Empty))
        {
            error = "Flow/Vision не используется транспортом XHTTP. "
                + "Верните несохранённое значение либо выберите raw/TCP.";
            return false;
        }

        if (xhttp || rawHttp)
        {
            var host = TextValue("host");
            if (host.IsNotEmpty() && host.Any(char.IsWhiteSpace))
            {
                error = "Host не должен содержать пробелы.";
                return false;
            }

            var path = TextValue("path");
            if (path.IsNotEmpty() && !path.StartsWith("/", StringComparison.Ordinal))
            {
                error = "Путь должен начинаться с символа /.";
                return false;
            }
        }

        var security = ComboValue("streamSecurity");
        var reality = string.Equals(
            security,
            Global.StreamSecurityReality,
            StringComparison.OrdinalIgnoreCase);
        var tls = string.Equals(
            security,
            Global.StreamSecurity,
            StringComparison.OrdinalIgnoreCase);

        if (reality)
        {
            if (TextValue("sni").IsNullOrEmpty()
                || TextValue("publicKey").IsNullOrEmpty())
            {
                error = "Для REALITY требуются serverName/SNI и публичный ключ.";
                return false;
            }

            if (TextFieldChanged("ech", _profile.EchConfigList)
                || TextFieldChanged("verifyPeer", _profile.VerifyPeerCertByName)
                || TextFieldChanged("certSha", _profile.CertSha)
                || TextFieldChanged("cert", _profile.Cert))
            {
                error = "ECH, SHA-256, PEM и проверка обычного TLS-сертификата "
                    + "не используются профилем REALITY. Верните несохранённые изменения.";
                return false;
            }
        }
        else if (tls)
        {
            if (TextFieldChanged("publicKey", _profile.PublicKey)
                || TextFieldChanged("shortId", _profile.ShortId)
                || TextFieldChanged("spiderX", _profile.SpiderX)
                || TextFieldChanged("mldsa65", _profile.Mldsa65Verify))
            {
                error = "Параметры REALITY не используются обычным TLS. "
                    + "Верните несохранённые изменения либо выберите REALITY.";
                return false;
            }

            var certSha = TextValue("certSha");
            if (certSha.IsNotEmpty()
                && !Regex.IsMatch(certSha, "^[0-9A-Fa-f]{64}$"))
            {
                error = "SHA-256 сертификата должен содержать ровно 64 шестнадцатеричных символа.";
                return false;
            }
        }
        else if (TextFieldChanged("sni", _profile.Sni)
            || ComboFieldChanged("fingerprint", _profile.Fingerprint)
            || ComboFieldChanged("alpn", _profile.Alpn)
            || TextFieldChanged("publicKey", _profile.PublicKey)
            || TextFieldChanged("shortId", _profile.ShortId)
            || TextFieldChanged("spiderX", _profile.SpiderX)
            || TextFieldChanged("mldsa65", _profile.Mldsa65Verify)
            || TextFieldChanged("ech", _profile.EchConfigList)
            || TextFieldChanged("verifyPeer", _profile.VerifyPeerCertByName)
            || TextFieldChanged("certSha", _profile.CertSha)
            || TextFieldChanged("cert", _profile.Cert))
        {
            error = "Параметры TLS/REALITY не используются, когда безопасность отключена. "
                + "Верните несохранённые изменения либо выберите TLS или REALITY.";
            return false;
        }

        return true;
    }

    private async Task RefreshGeneratedConfigPreviewAsync()
    {
        if (_generatedConfig == null || _profile == null || _awgProfile != null || _updatingPreview)
        {
            return;
        }

        _updatingPreview = true;
        var tempPath = Path.Combine(
            Path.GetTempPath(),
            $"sg-client-profile-preview-{Guid.NewGuid():N}.json");
        try
        {
            var candidate = BuildCandidateProfile();
            if (candidate == null)
            {
                _generatedConfig.Text = "Предварительный config.json пока не сформирован.";
                return;
            }

            var previewConfig = JsonUtils.DeepCopy(_config);
            if (previewConfig == null)
            {
                _generatedConfig.Text = "Не удалось создать копию настроек для предварительного просмотра.";
                return;
            }
            previewConfig.TunModeItem.EnableTun = false;

            var builder = await CoreConfigContextBuilder.Build(previewConfig, candidate);
            if (!builder.Success)
            {
                var errors = builder.ValidatorResult.Errors.Count > 0
                    ? string.Join(Environment.NewLine, builder.ValidatorResult.Errors)
                    : "Профиль пока не готов к формированию config.json.";
                _generatedConfig.Text = "Предварительный config.json не сформирован:"
                    + Environment.NewLine + errors;
                return;
            }

            var generated = await CoreConfigHandler.GenerateClientConfig(
                builder.Context,
                tempPath);
            if (generated.Success != true || !File.Exists(tempPath))
            {
                _generatedConfig.Text = generated.Msg.IsNotEmpty()
                    ? generated.Msg
                    : "Не удалось сформировать предварительный config.json.";
                return;
            }

            _generatedConfig.Text = await File.ReadAllTextAsync(tempPath);
        }
        catch (Exception ex)
        {
            Logging.SaveLog("SgProfileEditorWindow.Preview", ex);
            _generatedConfig.Text = "Предварительный config.json не сформирован:"
                + Environment.NewLine + ex.Message;
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch
            {
            }
            _updatingPreview = false;
        }
    }

    private async Task<bool> ValidateAsync(bool showSuccess)
    {
        _validated = false;

        if (_awgProfile != null)
        {
            return await ValidateAwgAsync(showSuccess);
        }

        if (!ValidateContextualFields(out var contextualError))
        {
            SetStatus(contextualError, "SgErrorBrush");
            return false;
        }

        var candidate = BuildCandidateProfile();
        if (candidate == null)
        {
            SetStatus("Не удалось подготовить профиль.", "SgErrorBrush");
            return false;
        }

        if (candidate.Remarks.IsNullOrEmpty()
            || candidate.Address.IsNullOrEmpty()
            || candidate.Port is <= 0 or > 65535
            || candidate.Password.IsNullOrEmpty())
        {
            SetStatus(
                "Заполните название, адрес, корректный порт и данные аутентификации.",
                "SgErrorBrush");
            return false;
        }

        if (!ValidateJsonObject(candidate.Finalmask, "FinalMask", out var jsonError))
        {
            SetStatus(jsonError, "SgErrorBrush");
            return false;
        }

        if (candidate.ConfigType == EConfigType.VLESS
            && candidate.GetNetwork() == nameof(ETransport.xhttp)
            && !ValidateJsonObject(
                candidate.GetTransportExtra().XhttpExtra,
                "XHTTP Extra",
                out jsonError))
        {
            SetStatus(jsonError, "SgErrorBrush");
            return false;
        }

        if (candidate.ConfigType == EConfigType.Hysteria2
            && ComboValue("obfs") == "salamander"
            && TextValue("salamanderPass").IsNullOrEmpty())
        {
            SetStatus("Для маскировки Salamander требуется пароль.", "SgErrorBrush");
            return false;
        }

        if (candidate.ConfigType == EConfigType.Hysteria2)
        {
            foreach (var (key, title) in new[]
            {
                ("upMbps", "Скорость отдачи"),
                ("downMbps", "Скорость загрузки"),
            })
            {
                var value = TextValue(key);
                if (value.IsNotEmpty()
                    && (!int.TryParse(value, out var parsed) || parsed < 0))
                {
                    SetStatus(
                        $"{title}: укажите целое число не меньше нуля либо оставьте поле пустым.",
                        "SgErrorBrush");
                    return false;
                }
            }
        }

        if (candidate.CoreType == ECoreType.sing_box
            && (candidate.Finalmask.IsNotEmpty()
                || candidate.VerifyPeerCertByName.IsNotEmpty()
                || candidate.CertSha.IsNotEmpty()))
        {
            SetStatus(
                "FinalMask, закрепление SHA-256 и проверка имени сертификата "
                    + "не передаются текущей конфигурацией sing-box. "
                    + "Выберите Xray либо очистите эти поля.",
                "SgErrorBrush");
            return false;
        }

        var validationConfig = JsonUtils.DeepCopy(_config);
        if (validationConfig == null)
        {
            SetStatus("Не удалось создать копию настроек.", "SgErrorBrush");
            return false;
        }
        validationConfig.TunModeItem.EnableTun = false;

        SetStatus("Создание полного временного config.json…", "SgMutedBrush");
        var builder = await CoreConfigContextBuilder.Build(validationConfig, candidate);
        if (!builder.Success)
        {
            var errors = builder.ValidatorResult.Errors.Count > 0
                ? string.Join(Environment.NewLine, builder.ValidatorResult.Errors)
                : "Профиль не прошёл внутреннюю проверку.";
            SetStatus(errors, "SgErrorBrush");
            return false;
        }

        var tempPath = Path.Combine(
            Path.GetTempPath(),
            $"sg-client-profile-check-{Guid.NewGuid():N}.json");

        try
        {
            var generated = await CoreConfigHandler.GenerateClientConfig(
                builder.Context,
                tempPath);

            if (generated.Success != true || !File.Exists(tempPath))
            {
                SetStatus(
                    generated.Msg.IsNotEmpty()
                        ? generated.Msg
                        : "Не удалось создать полный config.json.",
                    "SgErrorBrush");
                return false;
            }

            var fullConfig = await File.ReadAllTextAsync(tempPath);
            if (_generatedConfig != null)
            {
                _generatedConfig.Text = fullConfig;
            }

            var (success, output) = builder.Context.RunCoreType == ECoreType.sing_box
                ? await RunSingBoxConfigTestAsync(tempPath)
                : await RunXrayConfigTestAsync(tempPath);

            if (!success)
            {
                SetStatus(
                    $"{builder.Context.RunCoreType} отклонил конфигурацию:"
                        + Environment.NewLine + output,
                    "SgErrorBrush");
                return false;
            }

            _validated = true;
            if (showSuccess)
            {
                var warning = builder.ValidatorResult.Warnings.Count > 0
                    ? Environment.NewLine
                        + "Предупреждения: "
                        + string.Join(" · ", builder.ValidatorResult.Warnings)
                    : string.Empty;
                SetStatus(
                    "Семантическая проверка пройдена: все изменённые поля используются текущим режимом, полный config.json принят выбранным ядром."
                        + warning,
                    "SgSuccessBrush");
            }
            return true;
        }
        catch (Exception ex)
        {
            Logging.SaveLog("SgProfileEditorWindow.Validate", ex);
            SetStatus(ex.Message, "SgErrorBrush");
            return false;
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch
            {
            }
        }
    }

    private async Task<bool> ValidateAwgAsync(bool showSuccess)
    {
        if (_awgProfile == null)
        {
            return false;
        }

        try
        {
            var content = BuildAwgContent();
            var preview = AmneziaWgManager.Instance.InspectConfig(content);
            if (preview.Endpoint.IsNullOrEmpty()
                || preview.Address.IsNullOrEmpty())
            {
                SetStatus(
                    "В конфигурации AmneziaWG отсутствуют Address или Endpoint.",
                    "SgErrorBrush");
                return false;
            }

            _validated = true;
            if (showSuccess)
            {
                SetStatus(
                    $"Проверка пройдена: {preview.Endpoint} · {preview.AllowedIPs}",
                    "SgSuccessBrush");
            }
            return true;
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, "SgErrorBrush");
            return false;
        }
    }

    private async Task SaveAsync(bool asCopy)
    {
        if (!asCopy && _profile?.Subid.IsNotEmpty() == true)
        {
            SetStatus(
                "Профиль управляется подпиской. Для ручной правки используйте «Создать копию».",
                "SgWarningBrush");
            return;
        }

        if (!await ValidateAsync(showSuccess: false))
        {
            return;
        }

        if (_awgProfile != null)
        {
            if (asCopy)
            {
                SetStatus(
                    "Для AmneziaWG создайте копию через импорт полного .conf.",
                    "SgWarningBrush");
                return;
            }

            try
            {
                await AmneziaWgManager.Instance.UpdateProfileAsync(
                    _awgProfile.Id,
                    TextValue("awgName"),
                    BuildAwgContent());
                AppEvents.ProfilesRefreshRequested.Publish();
                AppEvents.ReloadRequested.Publish();
                SetStatus(
                    "Профиль AmneziaWG сохранён. Создана резервная копия старого .conf.",
                    "SgSuccessBrush");
                NoticeManager.Instance.Enqueue("Профиль AmneziaWG сохранён.");
                await LoadEditorAsync();
            }
            catch (Exception ex)
            {
                Logging.SaveLog("SgProfileEditorWindow.SaveAwg", ex);
                SetStatus(ex.Message, "SgErrorBrush");
            }
            return;
        }

        var candidate = BuildCandidateProfile();
        if (candidate == null || _profile == null)
        {
            return;
        }

        if (!asCopy && _profile.Subid.IsNotEmpty())
        {
            var answer = MessageBox.Show(
                this,
                "Профиль принадлежит подписке и может быть заменён при обновлении."
                    + Environment.NewLine + Environment.NewLine
                    + "Сохранить изменение в исходном профиле?",
                "Профиль подписки",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes)
            {
                return;
            }
        }

        try
        {
            if (asCopy)
            {
                candidate.IndexId = string.Empty;
                candidate.Subid = string.Empty;
                candidate.IsSub = false;
                candidate.Remarks =
                    $"{candidate.Remarks} · локальная копия";
            }
            else
            {
                SaveProfileBackup(_profile);
            }

            var result = await ConfigHandler.AddServer(_config, candidate);
            if (result != 0)
            {
                SetStatus("Не удалось сохранить профиль.", "SgErrorBrush");
                return;
            }

            AppEvents.ProfilesRefreshRequested.Publish();
            if (!asCopy && candidate.IndexId == _config.IndexId)
            {
                AppEvents.ReloadRequested.Publish();
            }

            SetStatus(
                asCopy
                    ? "Независимая локальная копия создана."
                    : "Профиль сохранён и передан активному ядру.",
                "SgSuccessBrush");
            NoticeManager.Instance.Enqueue(
                asCopy
                    ? "Создана локальная копия профиля."
                    : "Профиль сохранён.");

            if (!asCopy)
            {
                _profile = candidate;
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog("SgProfileEditorWindow.SaveProfile", ex);
            SetStatus(ex.Message, "SgErrorBrush");
        }
    }

    private string BuildAwgContent()
    {
        var content = TextValue("awgConfig");
        if (content.IsNullOrEmpty())
        {
            content = _awgOriginalContent;
        }

        content = SetIniValue(content, "Interface", "Address", TextValue("awgAddress"));
        content = SetIniValue(content, "Interface", "DNS", TextValue("awgDns"));
        content = SetIniValue(content, "Interface", "MTU", TextValue("awgMtu"));
        content = SetIniValue(content, "Interface", "PrivateKey", TextValue("awgPrivateKey"));

        foreach (var key in new[] { "Jc", "Jmin", "Jmax", "S1", "S2", "S3", "S4", "H1", "H2", "H3", "H4" })
        {
            content = SetIniValue(
                content,
                "Interface",
                key,
                TextValue("awg" + key));
        }

        content = SetIniValue(content, "Peer", "PublicKey", TextValue("awgPublicKey"));
        content = SetIniValue(content, "Peer", "PresharedKey", TextValue("awgPresharedKey"));
        content = SetIniValue(content, "Peer", "Endpoint", TextValue("awgEndpoint"));
        content = SetIniValue(content, "Peer", "AllowedIPs", TextValue("awgAllowedIps"));
        content = SetIniValue(content, "Peer", "PersistentKeepalive", TextValue("awgKeepalive"));
        return content;
    }

    private static string ReadIniValue(string content, string section, string key)
    {
        var current = string.Empty;
        foreach (var rawLine in content.Replace("\r\n", "\n").Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.StartsWith("[") && line.EndsWith("]"))
            {
                current = line[1..^1].Trim();
                continue;
            }

            if (!current.Equals(section, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            if (line[..separator].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                return line[(separator + 1)..].Trim();
            }
        }
        return string.Empty;
    }

    private static string SetIniValue(
        string content,
        string section,
        string key,
        string value)
    {
        var lines = content.Replace("\r\n", "\n").Split('\n').ToList();
        var sectionStart = -1;
        var sectionEnd = lines.Count;

        for (var index = 0; index < lines.Count; index++)
        {
            var trimmed = lines[index].Trim();
            if (trimmed.Equals($"[{section}]", StringComparison.OrdinalIgnoreCase))
            {
                sectionStart = index;
                for (var next = index + 1; next < lines.Count; next++)
                {
                    var candidate = lines[next].Trim();
                    if (candidate.StartsWith("[") && candidate.EndsWith("]"))
                    {
                        sectionEnd = next;
                        break;
                    }
                }
                break;
            }
        }

        if (sectionStart < 0)
        {
            if (value.IsNullOrEmpty())
            {
                return string.Join(Environment.NewLine, lines);
            }
            lines.Add(string.Empty);
            lines.Add($"[{section}]");
            lines.Add($"{key} = {value}");
            return string.Join(Environment.NewLine, lines);
        }

        for (var index = sectionStart + 1; index < sectionEnd; index++)
        {
            var line = lines[index];
            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            if (!line[..separator].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (value.IsNullOrEmpty())
            {
                lines.RemoveAt(index);
            }
            else
            {
                lines[index] = $"{key} = {value}";
            }
            return string.Join(Environment.NewLine, lines);
        }

        if (value.IsNotEmpty())
        {
            lines.Insert(sectionEnd, $"{key} = {value}");
        }
        return string.Join(Environment.NewLine, lines);
    }

    private static bool ValidateJsonObject(
        string? text,
        string fieldName,
        out string error)
    {
        error = string.Empty;
        if (text.IsNullOrEmpty())
        {
            return true;
        }

        try
        {
            if (JsonNode.Parse(text) is not JsonObject)
            {
                error = $"{fieldName}: ожидается JSON-объект {{ ... }}.";
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            error = $"{fieldName}: {ex.Message}";
            return false;
        }
    }

    private static string PrettyJson(string? text)
    {
        if (text.IsNullOrEmpty())
        {
            return string.Empty;
        }

        try
        {
            var node = JsonNode.Parse(text);
            return node?.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true,
            }) ?? text;
        }
        catch
        {
            return text;
        }
    }

    private static void SaveProfileBackup(ProfileItem profile)
    {
        var directory = Path.Combine(
            Utils.GetConfigPath(),
            "profile-backups");
        Directory.CreateDirectory(directory);

        var safeName = Regex.Replace(
            profile.Remarks.IsNotEmpty() ? profile.Remarks : "profile",
            @"[^a-zA-Z0-9а-яА-ЯёЁ._-]+",
            "_");

        File.WriteAllText(
            Path.Combine(
                directory,
                $"{DateTime.Now:yyyyMMdd-HHmmss}-{safeName}-{profile.IndexId}.json"),
            JsonUtils.Serialize(profile, true));
    }

    private static async Task<(bool Success, string Output)> RunXrayConfigTestAsync(
        string configPath)
    {
        return await RunCoreCheckAsync(
            ECoreType.Xray,
            new[] { "run", "-test", "-config", configPath },
            configPath);
    }

    private static async Task<(bool Success, string Output)> RunSingBoxConfigTestAsync(
        string configPath)
    {
        return await RunCoreCheckAsync(
            ECoreType.sing_box,
            new[] { "check", "-c", configPath },
            configPath);
    }

    private static async Task<(bool Success, string Output)> RunCoreCheckAsync(
        ECoreType coreType,
        IEnumerable<string> arguments,
        string configPath)
    {
        var coreInfo = CoreInfoManager.Instance.GetCoreInfo(coreType);
        var executable = CoreInfoManager.Instance.GetCoreExecFile(
            coreInfo,
            out var message);

        if (coreInfo == null || executable.IsNullOrEmpty())
        {
            return (
                false,
                message.IsNotEmpty()
                    ? message
                    : $"Ядро {coreType} не найдено.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = Path.GetDirectoryName(executable)
                ?? Utils.StartupPath(),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (var pair in coreInfo.Environment)
        {
            if (pair.Value != null)
            {
                startInfo.Environment[pair.Key] = string.Format(
                    pair.Value,
                    configPath);
            }
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        using var timeout = new CancellationTokenSource(
            TimeSpan.FromSeconds(20));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
            }
            return (false, $"Проверка {coreType} превысила 20 секунд.");
        }

        var output = string.Join(
            Environment.NewLine,
            new[]
            {
                (await stdoutTask).Trim(),
                (await stderrTask).Trim(),
            }.Where(value => value.IsNotEmpty()));

        return (
            process.ExitCode == 0,
            output.IsNotEmpty()
                ? output
                : $"Код завершения: {process.ExitCode}");
    }

    private void SetStatus(string text, string brushKey)
    {
        txtStatus.Text = text;
        txtStatus.Foreground = (Brush)FindResource(brushKey);
    }
}
