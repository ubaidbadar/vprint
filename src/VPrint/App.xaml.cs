using System.IO;
using System.Windows;
using VPrint.Services;

namespace VPrint;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var dataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VPrint", "jobs.sqlite3");
        try
        {
            var window = new MainWindow(new PrinterManager(), new JobRepository(dataPath), new PrintEventMonitor());
            MainWindow = window;
            window.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"VPrint could not start:\n\n{ex.Message}", "VPrint", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
}
