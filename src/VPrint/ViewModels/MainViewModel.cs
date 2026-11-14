using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using VPrint.Models;

namespace VPrint.ViewModels;

/// <summary>Only the visible rows and changed counters are notified to WPF.</summary>
public sealed class MainViewModel : INotifyPropertyChanged
{
    public ObservableCollection<PrinterInfo> Printers { get; } = [];
    public ObservableCollection<PrintJob> Jobs { get; } = [];
    public ICollectionView VisibleJobs { get; }

    private string _searchText = "";
    private PrinterInfo? _selectedPrinter;
    private bool _isBusy;
    private bool _isHistoryEnabled;
    private string _statusMessage = "Checking Windows print service…";

    public MainViewModel()
    {
        VisibleJobs = CollectionViewSource.GetDefaultView(Jobs);
        VisibleJobs.Filter = FilterJob;
    }

    public string SearchText
    {
        get => _searchText;
        set { if (!Set(ref _searchText, value)) return; RefreshFilter(); }
    }
    public PrinterInfo? SelectedPrinter
    {
        get => _selectedPrinter;
        set { if (!Set(ref _selectedPrinter, value)) return; RefreshFilter(); OnPropertyChanged(nameof(CurrentScope)); OnPropertyChanged(nameof(HasSelectedPrinter)); }
    }
    public bool IsBusy
    {
        get => _isBusy;
        set => Set(ref _isBusy, value);
    }
    public bool IsHistoryEnabled
    {
        get => _isHistoryEnabled;
        set
        {
            if (!Set(ref _isHistoryEnabled, value)) return;
            OnPropertyChanged(nameof(NeedsHistorySetup));
        }
    }
    public bool NeedsHistorySetup => !IsHistoryEnabled;
    public string StatusMessage
    {
        get => _statusMessage;
        set => Set(ref _statusMessage, value);
    }
    public bool HasSelectedPrinter => SelectedPrinter is not null;
    public string CurrentScope => SelectedPrinter?.Label ?? "All print jobs";
    public int PrinterCount => Printers.Count;
    public int JobCount => Jobs.Count;
    public int TodayCount => Jobs.Count(job => job.PrintedAt.ToLocalTime().Date == DateTime.Today);
    public bool IsVisibleHistoryEmpty => VisibleJobs.IsEmpty;
    public string VisibleCountLabel => $"{VisibleJobs.Cast<PrintJob>().Count():N0} jobs";

    public void ReplacePrinters(IReadOnlyList<PrinterInfo> items)
    {
        var selected = SelectedPrinter?.Name;
        // Printer refreshes are explicit, rather than polling/re-rendering on every frame.
        Printers.Clear();
        foreach (var item in items)
        {
            item.JobCount = Jobs.Count(j => j.PrinterName.Equals(item.Name, StringComparison.OrdinalIgnoreCase));
            Printers.Add(item);
        }
        SelectedPrinter = Printers.FirstOrDefault(p => p.Name.Equals(selected, StringComparison.OrdinalIgnoreCase));
        OnPropertyChanged(nameof(PrinterCount));
        RefreshFilter();
    }

    public void LoadJobs(IReadOnlyList<PrintJob> items)
    {
        Jobs.Clear();
        foreach (var item in items) Jobs.Add(item);
        UpdateCounters();
        RefreshFilter();
    }

    public void AddJob(PrintJob job)
    {
        if (Jobs.Any(j => j.Id == job.Id)) return;
        Jobs.Insert(0, job);
        if (Jobs.Count > 750) Jobs.RemoveAt(Jobs.Count - 1);
        var printer = Printers.FirstOrDefault(p => p.Name.Equals(job.PrinterName, StringComparison.OrdinalIgnoreCase));
        if (printer is not null) printer.JobCount++;
        UpdateCounters();
        RefreshFilter();
    }

    private bool FilterJob(object value)
    {
        if (value is not PrintJob job) return false;
        if (SelectedPrinter is not null &&
            !string.Equals(SelectedPrinter.Name, job.PrinterName, StringComparison.OrdinalIgnoreCase))
            return false;
        if (string.IsNullOrWhiteSpace(SearchText)) return true;
        var text = SearchText.Trim();
        return job.DocumentName.Contains(text, StringComparison.OrdinalIgnoreCase)
            || job.PrinterName.Contains(text, StringComparison.OrdinalIgnoreCase)
            || job.UserName.Contains(text, StringComparison.OrdinalIgnoreCase);
    }

    private void RefreshFilter()
    {
        VisibleJobs.Refresh();
        OnPropertyChanged(nameof(IsVisibleHistoryEmpty));
        OnPropertyChanged(nameof(VisibleCountLabel));
    }

    private void UpdateCounters()
    {
        OnPropertyChanged(nameof(JobCount));
        OnPropertyChanged(nameof(TodayCount));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
    private void OnPropertyChanged(string? name)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
