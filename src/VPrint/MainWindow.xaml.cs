using System.ComponentModel;
using System.Windows;
using VPrint.Models;
using VPrint.Services;
using VPrint.ViewModels;
using VPrint.Views;

namespace VPrint;

public partial class MainWindow : Window
{
    private readonly PrinterManager _printerManager;
    private readonly JobRepository _repository;
    private readonly PrintEventMonitor _monitor;
    private readonly MainViewModel _viewModel = new();

    public MainWindow(PrinterManager printerManager, JobRepository repository, PrintEventMonitor monitor)
    {
        _printerManager = printerManager;
        _repository = repository;
        _monitor = monitor;
        InitializeComponent();
        DataContext = _viewModel;

        Loaded += OnLoaded;
        Closed += OnClosed;
        _monitor.JobReceived += OnJobReceived;
        _monitor.MonitorError += OnMonitorError;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await ExecuteAsync(async () =>
        {
            _viewModel.IsHistoryEnabled = _monitor.IsLoggingEnabled();
            if (_viewModel.IsHistoryEnabled)
            {
                try
                {
                    _monitor.Start();
                    await ImportRecentJobsAsync();
                }
                catch (Exception ex)
                {
                    _viewModel.StatusMessage = $"Print history cannot start: {ex.Message}";
                }
            }
            UpdateStatus();
            _viewModel.LoadJobs(await Task.Run(() => _repository.GetRecent()));
            await RefreshPrintersAsync();
        });
    }

    private async Task ImportRecentJobsAsync()
    {
        await Task.Run(() =>
        {
            foreach (var job in _monitor.ReadRecent())
                _repository.Insert(job);
        });
    }

    private void OnJobReceived(PrintJob job)
    {
        try
        {
            if (!_repository.Insert(job)) return;
            if (!Dispatcher.HasShutdownStarted)
                Dispatcher.BeginInvoke(() => _viewModel.AddJob(job));
        }
        catch (Exception ex)
        {
            OnMonitorError(ex.Message);
        }
    }

    private void OnMonitorError(string error)
    {
        if (!Dispatcher.HasShutdownStarted)
            Dispatcher.BeginInvoke(() => _viewModel.StatusMessage = $"Print history warning: {error}");
    }

    private void UpdateStatus()
    {
        _viewModel.StatusMessage = _viewModel.IsHistoryEnabled
            ? "Monitoring completed print jobs · Stored locally"
            : "Windows print history is disabled · Enable it to start recording jobs";
    }

    private async Task RefreshPrintersAsync()
    {
        var printers = await _printerManager.ListAsync();
        _viewModel.ReplacePrinters(printers);
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
        => await ExecuteAsync(RefreshPrintersAsync);

    private void AllJobs_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.SelectedPrinter = null;
        PrinterList.SelectedItem = null;
    }

    private async void AddPrinter_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsBusy) return;
        var dialog = new NewPrinterDialog { Owner = this };
        if (dialog.ShowDialog() != true) return;
        await ExecuteAsync(async () =>
        {
            await _printerManager.CreateAsync(dialog.PrinterLabel);
            await RefreshPrintersAsync();
            _viewModel.SelectedPrinter = _viewModel.Printers.FirstOrDefault(p =>
                p.Label.Equals(dialog.PrinterLabel, StringComparison.OrdinalIgnoreCase));
        });
    }

    private async void RemovePrinter_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsBusy || _viewModel.SelectedPrinter is null) return;
        var printer = _viewModel.SelectedPrinter;
        var confirm = MessageBox.Show(
            $"Remove '{printer.Label}' from Windows?\n\nSaved job history will be kept.",
            "Remove virtual printer", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;
        await ExecuteAsync(async () =>
        {
            await _printerManager.RemoveAsync(printer);
            await RefreshPrintersAsync();
        });
    }

    private async void EnableHistory_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteAsync(async () =>
        {
            await _printerManager.EnablePrintHistoryAsync();
            _viewModel.IsHistoryEnabled = _monitor.IsLoggingEnabled();
            if (!_viewModel.IsHistoryEnabled)
                throw new InvalidOperationException("The PrintService event log was not enabled.");
            _monitor.Start();
            await ImportRecentJobsAsync();
            _viewModel.LoadJobs(await Task.Run(() => _repository.GetRecent()));
            UpdateStatus();
        });
    }

    private async Task ExecuteAsync(Func<Task> action)
    {
        if (_viewModel.IsBusy) return;
        _viewModel.IsBusy = true;
        try { await action(); }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            _viewModel.StatusMessage = "Administrator permission was cancelled.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "VPrint", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { _viewModel.IsBusy = false; }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _monitor.JobReceived -= OnJobReceived;
        _monitor.MonitorError -= OnMonitorError;
        _monitor.Dispose();
    }
}
