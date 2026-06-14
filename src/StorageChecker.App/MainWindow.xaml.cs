using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace StorageChecker.App;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        LoadWindowIcon();
    }

    private void LoadWindowIcon()
    {
        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
            if (File.Exists(iconPath))
            {
                Icon = BitmapFrame.Create(
                    new Uri(iconPath, UriKind.Absolute),
                    BitmapCreateOptions.None,
                    BitmapCacheOption.OnLoad);
            }
        }
        catch
        {
            // Abaikan — icon jendela boleh kosong, aplikasi tetap jalan.
        }
    }
}