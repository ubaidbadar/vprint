using VPrint.Services;

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception($"Check failed: {message}");
    Console.WriteLine($"PASS: {message}");
}

Check(PrinterManager.ValidateLabel("Kitchen - 1") == "Kitchen - 1", "valid name");
Check(PrinterManager.ValidateLabel("  Counter  ") == "Counter", "name trimming");
foreach (var invalid in new[] { "", "   ", "Room/West", "A|B", new string('x', 55) })
{
    try
    {
        PrinterManager.ValidateLabel(invalid);
        throw new Exception($"Rejected name accepted: {invalid}");
    }
    catch (ArgumentException) { }
}
Console.WriteLine("PASS: invalid printer names rejected");

const string xml = """
    <Event>
      <UserData>
        <DocumentPrinted xmlns="http://manifests.microsoft.com/win/2005/08/windows/printing/spooler/core/events">
          <Param1>42</Param1><Param2>Invoice #42</Param2><Param3>Cashier</Param3>
          <Param4>LOCALHOST</Param4><Param5>VPrint - Kitchen</Param5>
          <Param6>PORTPROMPT:</Param6><Param7>4096</Param7><Param8>2</Param8>
        </DocumentPrinted>
      </UserData>
    </Event>
    """;

var timestamp = DateTimeOffset.UtcNow;
var job = PrintEventParser.TryParse(xml, 12, timestamp);
Check(job is not null, "event parsed");
Check(job!.PrinterLabel == "Kitchen", "printer extracted");
Check(job.DocumentName == "Invoice #42" && job.UserName == "Cashier", "document/user extracted");
Check(job.Pages == 2 && job.SizeBytes == 4096, "page count and bytes");
Check(PrintEventParser.TryParse(xml.Replace("VPrint - Kitchen", "Other Laser"), 12, timestamp) is null,
    "other printers excluded");
Check(PrintEventParser.TryParse("<invalid xml", 12, timestamp) is null, "invalid XML rejected");

var file = Path.Combine(Path.GetTempPath(), "vprint-check-" + Guid.NewGuid().ToString("N"), "jobs.sqlite3");
try
{
    var repository = new JobRepository(file);
    Check(repository.Insert(job), "first event persisted");
    Check(!repository.Insert(job), "duplicate event ignored");
    var saved = repository.GetRecent();
    Check(saved.Count == 1 && saved[0].Id == job.Id, "SQLite round-trip");
}
finally
{
    try { Directory.Delete(Path.GetDirectoryName(file)!, recursive: true); }
    catch (IOException) { }
    catch (UnauthorizedAccessException) { }
}
Console.WriteLine("All VPrint checks passed.");
