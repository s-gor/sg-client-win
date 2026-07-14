namespace v2rayN.Views;

public partial class SgGeoFilesWindow
{
    public SgGeoFilesWindow()
    {
        InitializeComponent();
        SgWindowSizing.AttachLarge(this);
        WindowsUtils.SetDarkBorder(this, AppManager.Instance.Config.UiItem.CurrentTheme);
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
