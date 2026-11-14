using System.Text.Json;
using VPrint.Models;

namespace VPrint.Services;

/// <summary>
/// Uses the inbox Microsoft Print to PDF driver. These queues appear in ordinary
/// Windows print dialogs; document saving still uses Windows' Save As prompt.
/// This is not (yet) an unattended document capture engine.
/// </summary>
public sealed class PrinterManager
{
    public const string PrinterPrefix = "VPrint - ";
    public const string DriverName = "Microsoft Print To PDF";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<IReadOnlyList<PrinterInfo>> ListAsync(CancellationToken cancellationToken = default)
    {
        const string script = """
            $ErrorActionPreference = 'Stop'
            $printers = @(Get-Printer | Where-Object { $_.Name -like 'VPrint - *' } |
                Select-Object Name, DriverName, PortName)
            ConvertTo-Json -InputObject $printers -Compress
            """;
        var json = await PowerShellRunner.RunAsync(script, cancellationToken: cancellationToken);
        if (string.IsNullOrWhiteSpace(json)) return [];
        var entries = JsonSerializer.Deserialize<List<PrinterEntry>>(json, Options) ?? [];
        return entries
            .Where(x => x.Name is not null &&
                        string.Equals(x.DriverName, DriverName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(x => new PrinterInfo(x.Name!, x.DriverName!, x.PortName ?? string.Empty))
            .ToList();
    }

    public static string ValidateLabel(string? input)
    {
        var label = (input ?? "").Trim();
        if (label.Length is < 1 or > 54)
            throw new ArgumentException("Use a printer name between 1 and 54 characters.");
        if (label.Any(c => char.IsControl(c) || "\\/:*?\"<>|".Contains(c)))
            throw new ArgumentException("That name contains characters Windows printers cannot use.");
        return label;
    }

    public async Task CreateAsync(string label)
    {
        label = ValidateLabel(label);
        var name = Quote(PrinterPrefix + label);
        var script = $@"
            $ErrorActionPreference = 'Stop'
            if (-not (Get-PrinterDriver -Name '{DriverName}' -ErrorAction SilentlyContinue)) {{
                throw 'Microsoft Print to PDF is unavailable. Enable it in Windows Optional Features first.'
            }}
            if (Get-Printer -Name {name} -ErrorAction SilentlyContinue) {{
                throw 'A printer with this name already exists.'
            }}
            Add-Printer -Name {name} -DriverName '{DriverName}' -PortName 'PORTPROMPT:'
            ";
        await PowerShellRunner.RunAsync(script, elevated: true);
    }

    public async Task RemoveAsync(PrinterInfo printer)
    {
        if (!printer.Name.StartsWith(PrinterPrefix, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(printer.DriverName, DriverName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("VPrint can remove only its own PDF queues.");

        var name = Quote(printer.Name);
        var script = $@"
            $ErrorActionPreference = 'Stop'
            $printer = Get-Printer -Name {name} -ErrorAction Stop
            if ($printer.DriverName -ne '{DriverName}') {{ throw 'Printer driver changed. Removal refused.' }}
            Remove-Printer -Name {name} -ErrorAction Stop
            ";
        await PowerShellRunner.RunAsync(script, elevated: true);
    }

    public Task EnablePrintHistoryAsync() => PowerShellRunner.RunAsync(
        "$ErrorActionPreference='Stop'; wevtutil.exe sl 'Microsoft-Windows-PrintService/Operational' /e:true; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }",
        elevated: true);

    private static string Quote(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    private sealed record PrinterEntry(string? Name, string? DriverName, string? PortName);
}
