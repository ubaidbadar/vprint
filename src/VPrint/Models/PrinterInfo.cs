using System.ComponentModel;
using System.Runtime.CompilerServices;
using VPrint.Services;

namespace VPrint.Models;

public sealed class PrinterInfo(string name, string driverName, string portName) : INotifyPropertyChanged
{
    public string Name { get; } = name;
    public string DriverName { get; } = driverName;
    public string PortName { get; } = portName;
    public string Label => Name.StartsWith(PrinterManager.PrinterPrefix, StringComparison.OrdinalIgnoreCase)
        ? Name[PrinterManager.PrinterPrefix.Length..] : Name;
    private int _jobCount;
    public int JobCount
    {
        get => _jobCount;
        set { if (_jobCount == value) return; _jobCount = value; OnPropertyChanged(); }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
