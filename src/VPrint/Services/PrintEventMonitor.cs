using System.Diagnostics.Eventing.Reader;
using VPrint.Models;

namespace VPrint.Services;

/// <summary>Monitors job metadata (not the document bytes) written by Windows itself.</summary>
public sealed class PrintEventMonitor : IDisposable
{
    private const string LogName = "Microsoft-Windows-PrintService/Operational";
    private const string XPath = "*[System[(EventID=307)]]";
    private EventLogWatcher? _watcher;
    public event Action<PrintJob>? JobReceived;
    public event Action<string>? MonitorError;

    public bool IsLoggingEnabled()
    {
        try
        {
            using var config = new EventLogConfiguration(LogName);
            return config.IsEnabled;
        }
        catch (EventLogException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    public IReadOnlyList<PrintJob> ReadRecent(int maxRecords = 3000)
    {
        var jobs = new List<PrintJob>();
        try
        {
            var query = new EventLogQuery(LogName, PathType.LogName, XPath) { ReverseDirection = true };
            using var reader = new EventLogReader(query);
            for (var i = 0; i < maxRecords; i++)
            {
                using var record = reader.ReadEvent();
                if (record is null) break;
                var job = Convert(record);
                if (job is not null) jobs.Add(job);
            }
        }
        catch (EventLogException ex) { MonitorError?.Invoke(ex.Message); }
        catch (UnauthorizedAccessException ex) { MonitorError?.Invoke(ex.Message); }
        return jobs;
    }

    public void Start()
    {
        if (_watcher is not null) return;
        var query = new EventLogQuery(LogName, PathType.LogName, XPath);
        var watcher = new EventLogWatcher(query);
        watcher.EventRecordWritten += OnEventWritten;
        try
        {
            watcher.Enabled = true;
            _watcher = watcher;
        }
        catch
        {
            watcher.EventRecordWritten -= OnEventWritten;
            watcher.Dispose();
            throw;
        }
    }

    private void OnEventWritten(object? sender, EventRecordWrittenEventArgs args)
    {
        if (args.EventException is not null)
        {
            MonitorError?.Invoke(args.EventException.Message);
            return;
        }
        if (args.EventRecord is null) return;
        try
        {
            using var record = args.EventRecord;
            var job = Convert(record);
            if (job is not null) JobReceived?.Invoke(job);
        }
        catch (Exception ex) when (ex is EventLogException or System.Xml.XmlException or InvalidOperationException)
        {
            MonitorError?.Invoke(ex.Message);
        }
    }

    private static PrintJob? Convert(EventRecord record)
        => PrintEventParser.TryParse(record.ToXml(), record.RecordId ?? 0,
            new DateTimeOffset(record.TimeCreated ?? DateTime.Now));

    public void Dispose()
    {
        if (_watcher is null) return;
        _watcher.EventRecordWritten -= OnEventWritten;
        _watcher.Enabled = false;
        _watcher.Dispose();
        _watcher = null;
    }
}
