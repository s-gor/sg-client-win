namespace v2rayN.Views;

public partial class SgAwgTextImportWindow : Window
{
    private AwgConfigPreview? _preview;
    private readonly string _sourceFileName;
    private bool _settingSuggestedName;
    private bool _profileNameEdited;

    public AwgProfile? ImportedProfile { get; private set; }

    public SgAwgTextImportWindow()
        : this(null, "AmneziaWG.conf")
    {
    }

    public SgAwgTextImportWindow(string? content, string? sourceFileName)
    {
        _sourceFileName = sourceFileName.IsNotEmpty() ? sourceFileName! : "AmneziaWG.conf";
        InitializeComponent();
        SourceInitialized += (_, _) => WindowsUtils.SetSgBorderlessFrame(this);
        Owner = Application.Current.MainWindow;
        WindowsUtils.SetDarkBorder(this, SgThemeManager.Current == SgThemeManager.Light ? nameof(ETheme.Light) : nameof(ETheme.Dark));

        var initialContent = content;
        if (initialContent.IsNullOrEmpty())
        {
            var clipboard = WindowsUtils.GetClipboardData();
            if (LooksLikeAwgConfig(clipboard))
            {
                initialContent = clipboard;
            }
        }

        if (LooksLikeAwgConfig(initialContent))
        {
            txtConfig.Text = initialContent!;
            ApplySuggestedName(force: true);
        }
        else
        {
            SetProfileName("AmneziaWG");
        }

        txtProfileName.TextChanged += ProfileName_TextChanged;
    }

    private static bool LooksLikeAwgConfig(string? content)
    {
        return AmneziaWgManager.LooksLikeWireGuardConfig(content);
    }

    private void ProfileName_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (!_settingSuggestedName)
        {
            _profileNameEdited = true;
        }
    }

    private void ApplySuggestedName(bool force = false)
    {
        if (!force && _profileNameEdited)
        {
            return;
        }

        var suggested = AmneziaWgManager.GetSuggestedProfileName(_sourceFileName, txtConfig.Text);
        SetProfileName(suggested);
    }

    private void SetProfileName(string name)
    {
        _settingSuggestedName = true;
        try
        {
            txtProfileName.Text = name.IsNotEmpty() ? name : "AmneziaWG";
        }
        finally
        {
            _settingSuggestedName = false;
        }
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Paste_Click(object sender, RoutedEventArgs e)
    {
        var clipboard = WindowsUtils.GetClipboardData();
        if (clipboard.IsNullOrEmpty())
        {
            ShowError("Буфер обмена пуст.");
            return;
        }
        txtConfig.Text = clipboard;
        txtConfig.CaretIndex = txtConfig.Text.Length;
        txtConfig.Focus();
    }

    private void Config_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        _preview = null;
        btnAdd.IsEnabled = false;
        txtValidationTitle.Text = "Конфигурация изменена — выполните проверку";
        txtValidationTitle.Foreground = (System.Windows.Media.Brush)FindResource("SgTextBrush");
        txtPreviewType.Text = string.Empty;
        txtPreviewEndpoint.Text = string.Empty;
        txtPreviewAddress.Text = string.Empty;
        txtPreviewRoute.Text = string.Empty;
        ApplySuggestedName();
    }

    private void Validate_Click(object sender, RoutedEventArgs e)
    {
        ValidateContent();
    }

    private bool ValidateContent()
    {
        try
        {
            _preview = AmneziaWgManager.Instance.InspectConfig(txtConfig.Text);
            btnAdd.IsEnabled = true;
            txtValidationTitle.Text = _preview.DuplicateProfileName.IsNotEmpty()
                ? $"Профиль уже добавлен: {_preview.DuplicateProfileName}"
                : "Конфигурация корректна";
            txtValidationTitle.Foreground = (System.Windows.Media.Brush)FindResource("SgAccentBrush");
            txtPreviewType.Text = $"Тип: {_preview.Protocol}";
            txtPreviewEndpoint.Text = $"Сервер: {_preview.Endpoint}";
            txtPreviewAddress.Text = $"Адрес клиента: {_preview.Address}" + (_preview.DNS.IsNotEmpty() ? $" · DNS: {_preview.DNS}" : string.Empty);
            txtPreviewRoute.Text = $"Маршрут: {_preview.AllowedIPs}";
            if (!_profileNameEdited && _preview.SuggestedName.IsNotEmpty())
            {
                SetProfileName(_preview.SuggestedName);
            }
            return true;
        }
        catch (Exception ex)
        {
            ShowError(ex.Message.IsNullOrEmpty() ? "Конфигурация AmneziaWG некорректна." : ex.Message);
            return false;
        }
    }

    private async void Add_Click(object sender, RoutedEventArgs e)
    {
        if (_preview == null && !ValidateContent())
        {
            return;
        }

        var profileName = txtProfileName.Text.Trim();
        if (profileName.IsNullOrEmpty())
        {
            txtValidationTitle.Text = "Введите имя профиля.";
            txtValidationTitle.Foreground = System.Windows.Media.Brushes.IndianRed;
            btnAdd.IsEnabled = true;
            txtProfileName.Focus();
            return;
        }

        btnAdd.IsEnabled = false;
        try
        {
            ImportedProfile = await AmneziaWgManager.Instance.ImportProfileAsync(
                _sourceFileName,
                txtConfig.Text,
                profileName);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            Logging.SaveLog("Import AmneziaWG text configuration", ex);
            ShowError(ex.Message.IsNullOrEmpty() ? "Не удалось добавить профиль AmneziaWG." : ex.Message);
            btnAdd.IsEnabled = true;
        }
    }

    private void ShowError(string message)
    {
        _preview = null;
        btnAdd.IsEnabled = false;
        txtValidationTitle.Text = message;
        txtValidationTitle.Foreground = System.Windows.Media.Brushes.IndianRed;
        txtPreviewType.Text = string.Empty;
        txtPreviewEndpoint.Text = string.Empty;
        txtPreviewAddress.Text = string.Empty;
        txtPreviewRoute.Text = string.Empty;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
