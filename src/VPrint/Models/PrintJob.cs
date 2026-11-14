using System.Globalization;

namespace VPrint.Models;

public sealed record PrintJob(
    string Id,
    string PrinterName,
    string DocumentName,
    string UserName,
    int JobNumber,
    int Pages,
    long SizeBytes,
    DateTimeOffset PrintedAt)
{
    public string PrinterLabel => PrinterName.StartsWith(PrinterManager.PrinterPrefix, StringComparison.OrdinalIgnoreCase)
        ? PrinterName[PrinterManager.PrinterPrefix.Length..]
        : PrinterName;

    public string PrintedLabel => PrintedAt.ToLocalTime().ToString("dd MMM yyyy, HH:mm", CultureInfo.CurrentCulture);
    public string PagesLabel => Pages > 0 ? Pages.ToString(CultureInfo.CurrentCulture) : "—";
    public string SizeLabel => SizeBytes switch
    {
        < 1_024 => $"{SizeBytes} B",
        < 1_048_576 => $"{SizeBytes / 1_024d:0.#} KB",
        _ => $"{SizeBytes / 1_048_576d:0.#} MB"
    };
}
